using System.Windows;
using System.Windows.Threading;

namespace MyWireGuard.Tests;

internal static class WpfTestHost
{
    private static readonly Lazy<Dispatcher> SharedDispatcher = new(StartDispatcher);

    public static Dispatcher Dispatcher => SharedDispatcher.Value;

    private static Dispatcher StartDispatcher()
    {
        var dispatcherSource = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            _ = new Application();
            dispatcherSource.SetResult(dispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return dispatcherSource.Task.GetAwaiter().GetResult();
    }
}
