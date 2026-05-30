using System.Text;

namespace MyWireGuard.Tests;

public sealed class MainWindowNavigationTextTests
{
    [Fact]
    public void TopNavigation_ShouldContainActivityLogLabel()
    {
        var xaml = ReadMainWindowXaml();

        Assert.Contains("<TextBlock Text=\"活动日志\" />", xaml);
    }

    [Fact]
    public void ActivityLogGrid_ShouldUseConsistentVerticalAlignmentStyles()
    {
        var xaml = ReadMainWindowXaml();

        Assert.Contains("x:Key=\"LogGridTextStyle\"", xaml);
        Assert.Contains("x:Key=\"LogGridCellStyle\"", xaml);
        Assert.Contains("ElementStyle=\"{StaticResource LogGridTextStyle}\"", xaml);
        Assert.Contains("CellStyle=\"{StaticResource LogGridCellStyle}\"", xaml);
    }

    private static string ReadMainWindowXaml()
    {
        var xamlPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "MyWireGuard.App",
            "MainWindow.xaml"));

        return File.ReadAllText(xamlPath, Encoding.UTF8);
    }
}
