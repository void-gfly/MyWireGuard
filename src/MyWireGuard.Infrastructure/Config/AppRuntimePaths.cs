namespace MyWireGuard.Infrastructure.Config;

public sealed class AppRuntimePaths
{
    public AppRuntimePaths()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyWireGuard"))
    {
    }

    public AppRuntimePaths(string baseDirectory)
    {
        BaseDirectory = baseDirectory;
        ConfigDirectory = Path.Combine(baseDirectory, "Configs");
        RuntimeDirectory = Path.Combine(baseDirectory, "Runtime");
        WorkspaceRuntimeDirectory = ResolveWorkspaceRuntimeDirectory();
    }

    public string BaseDirectory { get; }

    public string ConfigDirectory { get; }

    public string RuntimeDirectory { get; }

    public string? WorkspaceRuntimeDirectory { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(RuntimeDirectory);
    }

    private static string? ResolveWorkspaceRuntimeDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "MyWireGuard.slnx");
            if (File.Exists(solutionPath))
            {
                return Path.Combine(directory.FullName, "runtime");
            }

            directory = directory.Parent;
        }

        return null;
    }
}
