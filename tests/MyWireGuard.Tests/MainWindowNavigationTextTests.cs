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

    [Fact]
    public void TopStatusBar_ShouldBindToMachineName()
    {
        var xaml = ReadMainWindowXaml();

        Assert.Contains("Text=\"{Binding MachineName}\"", xaml);
    }

    [Fact]
    public void SystemInfoPanel_ShouldContainInterconnectStatusAndPortBindings()
    {
        var xaml = ReadMainWindowXaml();
        var systemInfoSection = ExtractSection(xaml, "Text=\"系统信息\"", "</Grid>");

        Assert.Equal(4, CountOccurrences(systemInfoSection, "<RowDefinition Height=\"Auto\" />"));
        Assert.Contains("Text=\"监听状态\"", xaml);
        Assert.Contains("Text=\"监听端口\"", xaml);
        Assert.Contains("Text=\"{Binding InterconnectListenerStatus}\"", xaml);
        Assert.Contains("Text=\"{Binding InterconnectListenerPort}\"", xaml);
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

    private static string ExtractSection(string source, string startMarker, string endMarker)
    {
        var startIndex = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Could not find marker '{startMarker}'.");

        var endIndex = source.IndexOf(endMarker, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex >= 0, $"Could not find end marker '{endMarker}'.");

        return source[startIndex..endIndex];
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var searchIndex = 0;
        while (true)
        {
            var index = source.IndexOf(value, searchIndex, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            searchIndex = index + value.Length;
        }
    }
}
