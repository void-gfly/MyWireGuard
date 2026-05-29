using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace MyWireGuard.App.Services;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private const string ShowMainWindowCommand = "show-main-window";
    private const string MutexName = @"Local\MyWireGuard.Gui.SingleInstance";
    private const string PipeName = "MyWireGuard.Gui.SingleInstance";

    private Mutex? instanceMutex;
    private CancellationTokenSource? listenerCancellation;
    private Task? listenerTask;
    private bool ownsPrimaryInstance;
    private bool disposed;

    public bool TryAcquirePrimaryInstance()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (instanceMutex is not null)
        {
            return ownsPrimaryInstance;
        }

        instanceMutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        ownsPrimaryInstance = createdNew;

        if (!createdNew)
        {
            instanceMutex.Dispose();
            instanceMutex = null;
        }

        return createdNew;
    }

    public void StartListening(Func<Task> onShowWindowRequested)
    {
        ArgumentNullException.ThrowIfNull(onShowWindowRequested);
        ObjectDisposedException.ThrowIf(disposed, this);

        if (!ownsPrimaryInstance)
        {
            throw new InvalidOperationException("Only the primary GUI instance can listen for activation requests.");
        }

        if (listenerTask is not null)
        {
            return;
        }

        listenerCancellation = new CancellationTokenSource();
        listenerTask = Task.Run(() => ListenAsync(onShowWindowRequested, listenerCancellation.Token));
    }

    public async Task NotifyPrimaryInstanceToShowWindowAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await client.ConnectAsync(1000, cancellationToken).ConfigureAwait(false);

        using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: false);
        await writer.WriteAsync(ShowMainWindowCommand.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static bool IsSupportedCommand(string? command)
    {
        return string.Equals(command, ShowMainWindowCommand, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ListenAsync(Func<Task> onShowWindowRequested, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: false);
                var command = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                if (IsSupportedCommand(command))
                {
                    await onShowWindowRequested().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException exception) when (cancellationToken.IsCancellationRequested)
            {
                Debug.WriteLine($"Single-instance listener stopped during shutdown: {exception}");
                break;
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (listenerCancellation is not null)
        {
            listenerCancellation.Cancel();
            try
            {
                listenerTask?.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }

            listenerCancellation.Dispose();
        }

        instanceMutex?.Dispose();
    }
}

internal enum AppStartupMode
{
    RunTunnelServiceHost,
    StartPrimaryGui,
    NotifyExistingGui
}

internal static class AppStartupDecider
{
    public static AppStartupMode DetermineStartupMode(string[] args, bool isPrimaryInstance)
    {
        return IsTunnelServiceHost(args)
            ? AppStartupMode.RunTunnelServiceHost
            : isPrimaryInstance
                ? AppStartupMode.StartPrimaryGui
                : AppStartupMode.NotifyExistingGui;
    }

    private static bool IsTunnelServiceHost(string[] args)
    {
        return args.Length >= 2
            && string.Equals(args[0], "/service", StringComparison.OrdinalIgnoreCase);
    }
}
