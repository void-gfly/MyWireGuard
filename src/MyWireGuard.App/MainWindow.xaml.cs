using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MyWireGuard.App.ViewModels;
using Forms = System.Windows.Forms;
using System.Windows.Data;

namespace MyWireGuard.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly ContextMenu trayContextMenu;
    private readonly MenuItem trayStatusMenuItem;
    private bool hasDisposedExitResources;
    private bool isExitRequested;

    private bool isTunnelLeftPanelCollapsed;
    private bool wasTunnelLeftPanelAutoCollapsed;
    private const double TunnelLeftPanelExpandedWidth = 300;
    private const double AutoCollapseWidthThreshold = 1050;

    public MainWindow(MainWindowViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        Title = BuildWindowTitle();
        DataContext = viewModel;
        trayStatusMenuItem = new MenuItem
        {
            Header = viewModel.TrayStatusText,
            IsEnabled = false,
            Style = (Style)FindResource("TrayStatusMenuItemStyle")
        };
        trayContextMenu = CreateTrayContextMenu();
        notifyIcon = CreateNotifyIcon();

        Loaded += async (_, _) => await viewModel.InitializeAsync();
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        Closed += OnClosed;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private string BuildWindowTitle()
    {
        var informationalVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var displayVersion = informationalVersion?.Split('+', 2)[0];

        return string.IsNullOrWhiteSpace(displayVersion)
            ? Title
            : $"{Title} v{displayVersion}";
    }

    private ContextMenu CreateTrayContextMenu()
    {
        var contextMenu = new ContextMenu
        {
            Placement = PlacementMode.AbsolutePoint,
            Style = (Style)FindResource("TrayContextMenuStyle")
        };
        var showWindowMenuItem = new MenuItem
        {
            Header = "显示窗口",
            Style = (Style)FindResource("TrayMenuItemStyle")
        };
        var exitMenuItem = new MenuItem
        {
            Header = "退出程序",
            Style = (Style)FindResource("TrayMenuItemStyle")
        };
        var separator = new Separator
        {
            Style = (Style)FindResource("TraySeparatorStyle")
        };

        showWindowMenuItem.Click += (_, _) => ShowFromTray();
        exitMenuItem.Click += (_, _) => ExitApplication();

        contextMenu.Items.Add(trayStatusMenuItem);
        contextMenu.Items.Add(separator);
        contextMenu.Items.Add(showWindowMenuItem);
        contextMenu.Items.Add(exitMenuItem);

        return contextMenu;
    }

    private Forms.NotifyIcon CreateNotifyIcon()
    {
        var icon = new Forms.NotifyIcon
        {
            Text = "MyWireGuard",
            Icon = LoadTrayIcon(),
            Visible = true
        };

        icon.MouseUp += OnNotifyIconMouseUp;
        icon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);

        return icon;
    }

    private static Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "app-logo.ico");
        if (File.Exists(iconPath))
        {
            return new Icon(iconPath);
        }

        return SystemIcons.Application;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (isExitRequested)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        DisposeExitResources();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.TrayStatusText))
        {
            trayStatusMenuItem.Header = viewModel.TrayStatusText;
        }
    }

    private void OnNotifyIconMouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button != Forms.MouseButtons.Right)
        {
            return;
        }

        Dispatcher.Invoke(OpenTrayContextMenu);
    }

    private void OpenTrayContextMenu()
    {
        var cursorPosition = Forms.Control.MousePosition;
        trayStatusMenuItem.Header = viewModel.TrayStatusText;
        trayContextMenu.HorizontalOffset = cursorPosition.X;
        trayContextMenu.VerticalOffset = cursorPosition.Y;
        trayContextMenu.IsOpen = true;
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    public void BringToFrontFromExternalActivation()
    {
        var state = ComputeActivationState(IsVisible, ShowInTaskbar, WindowState);

        if (state.ShouldShowInTaskbar)
        {
            ShowInTaskbar = true;
        }

        if (state.ShouldShowWindow && !IsVisible)
        {
            Show();
        }

        WindowState = state.TargetWindowState;

        if (state.ShouldActivate)
        {
            if (state.ShouldTemporarilySetTopmost)
            {
                var originalTopmost = Topmost;
                Topmost = true;
                Topmost = originalTopmost;
            }

            Activate();
            Focus();
        }
    }

    internal static WindowActivationState ComputeActivationState(bool isVisible, bool showInTaskbar, WindowState windowState)
    {
        return new WindowActivationState(
            ShouldShowWindow: true,
            ShouldShowInTaskbar: true,
            TargetWindowState: WindowState.Normal,
            ShouldActivate: true,
            ShouldTemporarilySetTopmost: true);
    }

    private void ShowFromTray()
    {
        BringToFrontFromExternalActivation();
    }

    private async void ExitApplication()
    {
        var exitChoice = await viewModel.ConfirmExitAsync();
        if (exitChoice == Services.ExitConfirmationResult.Cancel)
        {
            return;
        }

        isExitRequested = true;
        trayContextMenu.IsOpen = false;

        if (exitChoice == Services.ExitConfirmationResult.StopTunnelsAndExit)
        {
            await viewModel.StopActiveTunnelsAsync();
        }

        DisposeExitResources();

        if (Application.Current.MainWindow == this)
        {
            Application.Current.MainWindow = null;
        }

        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Application.Current.Shutdown();
        Environment.Exit(0);
    }

    private void TunnelLeftPanelToggle_Click(object sender, RoutedEventArgs e)
    {
        wasTunnelLeftPanelAutoCollapsed = false;
        SetTunnelLeftPanelCollapsed(!isTunnelLeftPanelCollapsed);
    }

    private void SetTunnelLeftPanelCollapsed(bool collapsed)
    {
        isTunnelLeftPanelCollapsed = collapsed;
        TunnelLeftColumn.Width = collapsed ? new GridLength(0) : new GridLength(TunnelLeftPanelExpandedWidth);
        TunnelSpacerColumn.Width = collapsed ? new GridLength(0) : new GridLength(12);
        TunnelLeftPanelToggleIconScale.ScaleX = collapsed ? -1 : 1;
        TunnelLeftPanelToggleBtn.ToolTip = collapsed ? "展开面板" : "折叠面板";
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (!sizeInfo.WidthChanged)
            return;

        var newWidth = sizeInfo.NewSize.Width;
        if (newWidth < AutoCollapseWidthThreshold && !isTunnelLeftPanelCollapsed)
        {
            wasTunnelLeftPanelAutoCollapsed = true;
            SetTunnelLeftPanelCollapsed(true);
        }
        else if (newWidth >= AutoCollapseWidthThreshold && isTunnelLeftPanelCollapsed && wasTunnelLeftPanelAutoCollapsed)
        {
            wasTunnelLeftPanelAutoCollapsed = false;
            SetTunnelLeftPanelCollapsed(false);
        }
    }

    private void NeighborDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.EditingElement is TextBox textBox)
        {
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
    }

    private void NeighborDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (e.Column.Header is not string header || header != "备注名")
        {
            // 非备注名列：清除自定义排序，恢复默认行为
            var defaultCv = CollectionViewSource.GetDefaultView(((DataGrid)sender).ItemsSource);
            if (defaultCv is System.Windows.Data.ListCollectionView defaultLcv)
            {
                defaultLcv.CustomSort = null;
            }
            return;
        }

        e.Handled = true;
        var direction = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        e.Column.SortDirection = direction;

        var cv = CollectionViewSource.GetDefaultView(((DataGrid)sender).ItemsSource);
        if (cv is System.Windows.Data.ListCollectionView lcv)
        {
            lcv.CustomSort = new RemarkEmptyLastComparer(direction);
        }
    }

    private sealed class RemarkEmptyLastComparer : System.Collections.IComparer
    {
        private readonly ListSortDirection direction;

        public RemarkEmptyLastComparer(ListSortDirection direction)
        {
            this.direction = direction;
        }

        public int Compare(object? x, object? y)
        {
            var remarkX = (x as ViewModels.NeighborHostItemViewModel)?.Remark ?? string.Empty;
            var remarkY = (y as ViewModels.NeighborHostItemViewModel)?.Remark ?? string.Empty;

            var xEmpty = string.IsNullOrWhiteSpace(remarkX);
            var yEmpty = string.IsNullOrWhiteSpace(remarkY);

            if (xEmpty && yEmpty) return 0;
            if (xEmpty) return 1;   // 空值永远排最后
            if (yEmpty) return -1;  // 空值永远排最后

            var cmp = string.Compare(remarkX, remarkY, StringComparison.OrdinalIgnoreCase);
            return direction == ListSortDirection.Ascending ? cmp : -cmp;
        }
    }

    private void DisposeExitResources()
    {
        if (hasDisposedExitResources)
        {
            return;
        }

        hasDisposedExitResources = true;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.Shutdown();
        notifyIcon.MouseUp -= OnNotifyIconMouseUp;
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
    }
}

internal readonly record struct WindowActivationState(
    bool ShouldShowWindow,
    bool ShouldShowInTaskbar,
    WindowState TargetWindowState,
    bool ShouldActivate,
    bool ShouldTemporarilySetTopmost);
