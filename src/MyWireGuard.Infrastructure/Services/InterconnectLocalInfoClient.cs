using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;

namespace MyWireGuard.Infrastructure.Services;

public sealed class InterconnectLocalInfoClient : IInterconnectLocalInfoClient
{
    private const byte LocalInfoRequestMessageType = 3;

    public async Task<InterconnectLocalInfo?> TryGetLocalInfoAsync(
        string ipAddress,
        int port,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Parse(ipAddress), port, timeoutCts.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();
            await stream.WriteAsync(new[] { LocalInfoRequestMessageType }, timeoutCts.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);

            var lengthBuffer = await ReadExactAsync(stream, sizeof(int), timeoutCts.Token).ConfigureAwait(false);
            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
            if (payloadLength <= 0)
            {
                return null;
            }

            var payload = await ReadExactAsync(stream, payloadLength, timeoutCts.Token).ConfigureAwait(false);
            return JsonSerializer.Deserialize<InterconnectLocalInfo>(payload, InterconnectJson.Options);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is SocketException
            or IOException
            or JsonException
            or FormatException)
        {
            return null;
        }
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("互联本机信息响应提前结束。");
            }

            offset += read;
        }

        return buffer;
    }
}
