using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;

namespace MyWireGuard.Infrastructure.Services;

public sealed class InterconnectService : IInterconnectService
{
    private const byte TextMessageType = 1;
    private const byte FileMessageType = 2;
    private const byte LocalInfoRequestMessageType = 3;
    private const int MaxPersistedReceiveRecords = 20;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    private readonly ILogService logService;
    private readonly string receiveDirectory;
    private readonly IInterconnectRecordStore? recordStore;
    private readonly int port;
    private readonly Lock recordsLock = new();
    private readonly List<InterconnectReceiveTextRecord> textRecords = [];
    private readonly List<InterconnectReceiveFileRecord> fileRecords = [];
    private readonly SemaphoreSlim lifecycleLock = new(1, 1);
    private TcpListener? listener;
    private CancellationTokenSource? listenerCancellationTokenSource;
    private Task? listenerTask;
    private string listenerStatusText = "已停止";

    public InterconnectService(ILogService logService, string receiveDirectory, int port = InterconnectLimits.DefaultPort)
        : this(logService, receiveDirectory, null, port)
    {
    }

    public InterconnectService(
        ILogService logService,
        string receiveDirectory,
        IInterconnectRecordStore? recordStore,
        int port = InterconnectLimits.DefaultPort)
    {
        this.logService = logService;
        this.receiveDirectory = receiveDirectory;
        this.recordStore = recordStore;
        this.port = port;

        LoadPersistedRecords();
    }

    public string ListenerStatusText => listenerStatusText;

    public int ListenerPort => port;

    public event EventHandler? ListenerStateChanged;

    public event EventHandler<InterconnectReceiveTextRecord>? TextReceived;

    public event EventHandler<InterconnectReceiveFileRecord>? FileReceived;

    public event EventHandler<InterconnectSendProgress>? SendProgressChanged;

    public IReadOnlyList<InterconnectReceiveTextRecord> GetReceivedTextRecords()
    {
        lock (recordsLock)
        {
            return textRecords.AsEnumerable().Reverse().ToArray();
        }
    }

    public IReadOnlyList<InterconnectReceiveFileRecord> GetReceivedFileRecords()
    {
        lock (recordsLock)
        {
            return fileRecords.AsEnumerable().Reverse().ToArray();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (listener is not null)
            {
                return;
            }

            Directory.CreateDirectory(receiveDirectory);
            listenerCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            listener = new TcpListener(IPAddress.Any, port);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start();
            var startedListener = listener;
            var startedCancellationTokenSource = listenerCancellationTokenSource;
            listenerTask = Task.Run(() => AcceptLoopAsync(startedListener, startedCancellationTokenSource.Token), CancellationToken.None);
            SetListenerStatus("监听中");
            logService.WriteInfo($"Interconnect listener started on 0.0.0.0:{port}.");
        }
        catch
        {
            listenerCancellationTokenSource?.Dispose();
            listenerCancellationTokenSource = null;
            listener?.Stop();
            listener = null;
            SetListenerStatus("启动失败");
            throw;
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? taskToAwait = null;

        await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (listener is null)
            {
                return;
            }

            listenerCancellationTokenSource?.Cancel();
            listener.Stop();
            taskToAwait = listenerTask;
            listener = null;
            listenerTask = null;
            listenerCancellationTokenSource?.Dispose();
            listenerCancellationTokenSource = null;
            SetListenerStatus("已停止");
            logService.WriteInfo($"Interconnect listener stopped on 0.0.0.0:{port}.");
        }
        finally
        {
            lifecycleLock.Release();
        }

        if (taskToAwait is not null)
        {
            try
            {
                await taskToAwait.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public async Task SendTextAsync(string ipAddress, int port, string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("发送文本不能为空。");
        }

        var payload = Utf8.GetBytes(text);
        var header = new byte[1 + sizeof(int)];
        header[0] = TextMessageType;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1), payload.Length);

        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Parse(ipAddress), port, cancellationToken).ConfigureAwait(false);
        await using var stream = client.GetStream();
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        logService.WriteInfo($"Interconnect text sent to {ipAddress}:{port}.");
    }

    public async Task SendFileAsync(string ipAddress, int port, string filePath, IProgress<InterconnectSendProgress>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new InvalidOperationException("发送文件路径不能为空。");
        }

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("发送文件不存在。", filePath);
        }

        if (fileInfo.Length > InterconnectLimits.MaxFileSizeBytes)
        {
            throw new InvalidOperationException("发送文件不能超过 500MB。");
        }

        var fileNameBytes = Utf8.GetBytes(fileInfo.Name);
        var header = new byte[1 + sizeof(int) + fileNameBytes.Length + sizeof(long)];
        header[0] = FileMessageType;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1), fileNameBytes.Length);
        fileNameBytes.CopyTo(header.AsSpan(1 + sizeof(int)));
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(1 + sizeof(int) + fileNameBytes.Length), fileInfo.Length);

        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Parse(ipAddress), port, cancellationToken).ConfigureAwait(false);
        await using var networkStream = client.GetStream();
        await networkStream.WriteAsync(header, cancellationToken).ConfigureAwait(false);

        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        var buffer = new byte[81920];
        long totalWritten = 0;
        while (true)
        {
            var read = await fileStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await networkStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            totalWritten += read;
            var update = new InterconnectSendProgress(ipAddress, fileInfo.Name, totalWritten, fileInfo.Length);
            progress?.Report(update);
            SendProgressChanged?.Invoke(this, update);
        }

        await networkStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        logService.WriteInfo($"Interconnect file sent to {ipAddress}:{port}: {fileInfo.Name}.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        lifecycleLock.Dispose();
    }

    private void SetListenerStatus(string statusText)
    {
        if (string.Equals(listenerStatusText, statusText, StringComparison.Ordinal))
        {
            return;
        }

        listenerStatusText = statusText;
        ListenerStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task AcceptLoopAsync(TcpListener tcpListener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await tcpListener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception exception)
            {
                logService.WriteError($"Interconnect accept failed: {exception.Message}");
                client?.Dispose();
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        var remoteIpAddress = ((IPEndPoint?)client.Client.RemoteEndPoint)?.Address.ToString() ?? string.Empty;
        PipeReader? reader = null;

        try
        {
            await using var stream = client.GetStream();
            reader = PipeReader.Create(stream);
            var messageType = await ReadByteAsync(reader, cancellationToken).ConfigureAwait(false);
            switch (messageType)
            {
                case TextMessageType:
                    await ReceiveTextAsync(reader, remoteIpAddress, cancellationToken).ConfigureAwait(false);
                    break;
                case FileMessageType:
                    await ReceiveFileAsync(reader, remoteIpAddress, cancellationToken).ConfigureAwait(false);
                    break;
                case LocalInfoRequestMessageType:
                    await SendLocalInfoAsync(stream, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException($"收到未知互联消息类型: {messageType}。");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logService.WriteError($"Interconnect handle failed: {exception.Message}");
        }
        finally
        {
            if (reader is not null)
            {
                await reader.CompleteAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task ReceiveTextAsync(PipeReader reader, string sourceIpAddress, CancellationToken cancellationToken)
    {
        var payloadLength = await ReadInt32Async(reader, cancellationToken).ConfigureAwait(false);
        if (payloadLength <= 0 || payloadLength > InterconnectLimits.MaxTextMessageBytes)
        {
            throw new InvalidOperationException($"接收文本长度无效: {payloadLength}。");
        }

        var payload = await ReadBytesAsync(reader, payloadLength, cancellationToken).ConfigureAwait(false);
        var text = Utf8.GetString(payload);
        var record = new InterconnectReceiveTextRecord(DateTimeOffset.Now, sourceIpAddress, text);
        AddTextRecord(record);
        TextReceived?.Invoke(this, record);
        await TryPersistTextRecordsAsync(cancellationToken).ConfigureAwait(false);
        logService.WriteInfo($"Interconnect text received from {sourceIpAddress}.");
    }

    private static async Task SendLocalInfoAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            LocalNetworkInfoProvider.GetLocalInfo(),
            InterconnectJson.Options);
        var lengthPrefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, payload.Length);
        await stream.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveFileAsync(PipeReader reader, string sourceIpAddress, CancellationToken cancellationToken)
    {
        var fileNameLength = await ReadInt32Async(reader, cancellationToken).ConfigureAwait(false);
        if (fileNameLength <= 0 || fileNameLength > InterconnectLimits.MaxFileNameBytes)
        {
            throw new InvalidOperationException($"接收文件名长度无效: {fileNameLength}。");
        }

        var fileName = Utf8.GetString(await ReadBytesAsync(reader, fileNameLength, cancellationToken).ConfigureAwait(false));
        var fileLength = await ReadInt64Async(reader, cancellationToken).ConfigureAwait(false);
        if (fileLength < 0)
        {
            throw new InvalidOperationException($"接收文件大小无效: {fileLength}。");
        }

        if (fileLength > InterconnectLimits.MaxFileSizeBytes)
        {
            throw new InvalidOperationException($"接收文件超过 500MB 限制: {fileName}。");
        }

        Directory.CreateDirectory(receiveDirectory);
        string savedPath;
        await using (var fileStream = CreateUniqueFileStream(receiveDirectory, fileName, out savedPath))
        {
            await CopyExactToFileAsync(reader, fileStream, fileLength, cancellationToken).ConfigureAwait(false);
        }

        var record = new InterconnectReceiveFileRecord(DateTimeOffset.Now, sourceIpAddress, fileName, fileLength, savedPath);
        AddFileRecord(record);
        FileReceived?.Invoke(this, record);
        await TryPersistFileRecordsAsync(cancellationToken).ConfigureAwait(false);
        logService.WriteInfo($"Interconnect file received from {sourceIpAddress}: {fileName}.");
    }

    private void LoadPersistedRecords()
    {
        if (recordStore is null)
        {
            return;
        }

        try
        {
            var loadedTextRecords = recordStore.GetTextRecordsAsync(CancellationToken.None).GetAwaiter().GetResult();
            var loadedFileRecords = recordStore.GetFileRecordsAsync(CancellationToken.None).GetAwaiter().GetResult();

            lock (recordsLock)
            {
                textRecords.Clear();
                textRecords.AddRange(TakeNewest(loadedTextRecords));
                fileRecords.Clear();
                fileRecords.AddRange(TakeNewest(loadedFileRecords));
            }
        }
        catch (Exception exception)
        {
            logService.WriteError($"Failed to load persisted interconnect records: {exception.Message}");
        }
    }

    private void AddTextRecord(InterconnectReceiveTextRecord record)
    {
        lock (recordsLock)
        {
            textRecords.Add(record);
            TrimOldest(textRecords);
        }
    }

    private void AddFileRecord(InterconnectReceiveFileRecord record)
    {
        lock (recordsLock)
        {
            fileRecords.Add(record);
            TrimOldest(fileRecords);
        }
    }

    private async Task TryPersistTextRecordsAsync(CancellationToken cancellationToken)
    {
        if (recordStore is null)
        {
            return;
        }

        try
        {
            await recordStore.SaveTextRecordsAsync(GetTextSnapshot(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logService.WriteError($"Failed to persist interconnect text records: {exception.Message}");
        }
    }

    private async Task TryPersistFileRecordsAsync(CancellationToken cancellationToken)
    {
        if (recordStore is null)
        {
            return;
        }

        try
        {
            await recordStore.SaveFileRecordsAsync(GetFileSnapshot(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logService.WriteError($"Failed to persist interconnect file records: {exception.Message}");
        }
    }

    private IReadOnlyList<InterconnectReceiveTextRecord> GetTextSnapshot()
    {
        lock (recordsLock)
        {
            return textRecords.ToArray();
        }
    }

    private IReadOnlyList<InterconnectReceiveFileRecord> GetFileSnapshot()
    {
        lock (recordsLock)
        {
            return fileRecords.ToArray();
        }
    }

    private static void TrimOldest<TRecord>(List<TRecord> records)
    {
        var overflowCount = records.Count - MaxPersistedReceiveRecords;
        if (overflowCount <= 0)
        {
            return;
        }

        records.RemoveRange(0, overflowCount);
    }

    private static IReadOnlyList<TRecord> TakeNewest<TRecord>(IReadOnlyList<TRecord> records)
    {
        if (records.Count <= MaxPersistedReceiveRecords)
        {
            return records;
        }

        return records.Skip(records.Count - MaxPersistedReceiveRecords).ToArray();
    }

    private static async Task CopyExactToFileAsync(PipeReader reader, FileStream destination, long remainingBytes, CancellationToken cancellationToken)
    {
        while (remainingBytes > 0)
        {
            var readResult = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = readResult.Buffer;
            var consumed = buffer.Start;

            try
            {
                if (buffer.Length == 0 && readResult.IsCompleted)
                {
                    throw new EndOfStreamException("互联文件数据提前结束。");
                }

                var sliceLength = Math.Min(remainingBytes, buffer.Length);
                if (sliceLength == 0)
                {
                    continue;
                }

                var slice = buffer.Slice(0, sliceLength);
                foreach (var segment in slice)
                {
                    await destination.WriteAsync(segment, cancellationToken).ConfigureAwait(false);
                }

                remainingBytes -= sliceLength;
                consumed = slice.End;
            }
            finally
            {
                reader.AdvanceTo(consumed, buffer.End);
            }
        }
    }

    private static FileStream CreateUniqueFileStream(string directoryPath, string originalFileName, out string savedPath)
    {
        var sanitizedFileName = Path.GetFileName(originalFileName);
        var baseName = Path.GetFileNameWithoutExtension(sanitizedFileName);
        var extension = Path.GetExtension(sanitizedFileName);
        for (var index = 0; ; index++)
        {
            var candidateFileName = index == 0
                ? sanitizedFileName
                : $"{baseName} ({index}){extension}";
            var candidatePath = Path.Combine(directoryPath, candidateFileName);
            try
            {
                savedPath = candidatePath;
                return new FileStream(candidatePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            }
            catch (IOException) when (File.Exists(candidatePath))
            {
            }
        }
    }

    private static async Task<byte> ReadByteAsync(PipeReader reader, CancellationToken cancellationToken)
    {
        var data = await ReadBytesAsync(reader, 1, cancellationToken).ConfigureAwait(false);
        return data[0];
    }

    private static async Task<int> ReadInt32Async(PipeReader reader, CancellationToken cancellationToken)
    {
        var data = await ReadBytesAsync(reader, sizeof(int), cancellationToken).ConfigureAwait(false);
        return BinaryPrimitives.ReadInt32LittleEndian(data);
    }

    private static async Task<long> ReadInt64Async(PipeReader reader, CancellationToken cancellationToken)
    {
        var data = await ReadBytesAsync(reader, sizeof(long), cancellationToken).ConfigureAwait(false);
        return BinaryPrimitives.ReadInt64LittleEndian(data);
    }

    private static async Task<byte[]> ReadBytesAsync(PipeReader reader, int length, CancellationToken cancellationToken)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "读取长度不能为负数。");
        }

        if (length == 0)
        {
            return Array.Empty<byte>();
        }

        var resultBuffer = new byte[length];
        var written = 0;

        while (written < length)
        {
            var readResult = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = readResult.Buffer;
            var consumed = buffer.Start;

            try
            {
                if (buffer.Length == 0 && readResult.IsCompleted)
                {
                    throw new EndOfStreamException("互联消息数据提前结束。");
                }

                var remaining = length - written;
                var sliceLength = (int)Math.Min(remaining, buffer.Length);
                if (sliceLength == 0)
                {
                    continue;
                }

                var targetSpan = resultBuffer.AsSpan(written, sliceLength);
                var copied = 0;
                foreach (var segment in buffer.Slice(0, sliceLength))
                {
                    segment.Span.CopyTo(targetSpan[copied..]);
                    copied += segment.Length;
                }
                written += sliceLength;
                consumed = buffer.GetPosition(sliceLength);
            }
            finally
            {
                reader.AdvanceTo(consumed, buffer.End);
            }
        }

        return resultBuffer;
    }
}
