using System.Windows;
using MyWireGuard.App.Services;
using MyWireGuard.App.ViewModels;
using MyWireGuard.Infrastructure.Config;
using MyWireGuard.Infrastructure.Interop;
using MyWireGuard.Infrastructure.Logging;
using MyWireGuard.Infrastructure.Runtime;
using MyWireGuard.Infrastructure.Services;

namespace MyWireGuard.App;

public partial class App : System.Windows.Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		var runtimePaths = new AppRuntimePaths();
		runtimePaths.EnsureCreated();
		var runtimeAssetLocator = new RuntimeAssetLocator(runtimePaths);
		runtimeAssetLocator.EnsureRuntimeAvailable();

		if (TryRunTunnelServiceHost(e.Args))
		{
			return;
		}

		var logService = new InMemoryLogService();
		var configStore = new FileConfigStore(runtimePaths, new WgQuickParser());
		var neighborMetadataStore = new FileNeighborMetadataStore(runtimePaths);
		var subnetCalculator = new IPv4SubnetCalculator();
		var privilegeService = new PrivilegeService();
		var keypairService = new TunnelDllKeypairService(runtimeAssetLocator);
		var tunnelServiceManager = new TunnelServiceManager(logService, runtimeAssetLocator);
		var tunnelNeighbors = new TunnelNeighborsViewModel(
			neighborMetadataStore,
			new NetworkNeighborScanner(subnetCalculator, logService),
			subnetCalculator,
			logService);
		var fileDialogService = new FileDialogService();
		var messageService = new MessageService();

		var mainWindowViewModel = new MainWindowViewModel(
			configStore,
			tunnelServiceManager,
			keypairService,
			logService,
			privilegeService,
			tunnelNeighbors,
			fileDialogService,
			messageService);

		var mainWindow = new MainWindow(mainWindowViewModel);
		mainWindow.Show();
	}

	private bool TryRunTunnelServiceHost(string[] args)
	{
		if (args.Length < 2 || !string.Equals(args[0], "/service", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var success = TunnelDllNative.RunTunnelService(args[1]);
		Shutdown(success ? 0 : 1);
		return true;
	}
}

