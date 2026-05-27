using MyWireGuard.Infrastructure.Config;

namespace MyWireGuard.Infrastructure.Runtime;

public sealed class RuntimeAssetLocator
{
    private static readonly string[] RequiredFiles = ["tunnel.dll", "wireguard.dll"];
    private readonly AppRuntimePaths runtimePaths;

    public RuntimeAssetLocator(AppRuntimePaths runtimePaths)
    {
        this.runtimePaths = runtimePaths;
    }

    public RuntimeResolutionResult EnsureRuntimeAvailable()
    {
        if (HasRequiredFiles(AppContext.BaseDirectory))
        {
            return new RuntimeResolutionResult(true, "Runtime ready", [AppContext.BaseDirectory]);
        }

        var searchedDirectories = new List<string>();
        foreach (var candidateDirectory in GetCandidateDirectories())
        {
            searchedDirectories.Add(candidateDirectory);
            if (!Directory.Exists(candidateDirectory) || !HasRequiredFiles(candidateDirectory))
            {
                continue;
            }

            CopyRuntimeFiles(candidateDirectory, AppContext.BaseDirectory);
            return new RuntimeResolutionResult(true, $"Runtime copied from '{candidateDirectory}'.", searchedDirectories);
        }

        var message = "Missing official embeddable runtime. Expected tunnel.dll and wireguard.dll in the application folder, the repository runtime folder, %LOCALAPPDATA%/MyWireGuard/Runtime, or the directory pointed to by MYWIREGUARD_RUNTIME_DIR.";
        return new RuntimeResolutionResult(false, message, searchedDirectories);
    }

    public string BuildMissingRuntimeMessage()
    {
        var result = EnsureRuntimeAvailable();
        if (result.IsAvailable)
        {
            return result.Message;
        }

        var searched = result.SearchedDirectories.Count == 0
            ? "No candidate runtime directories were found."
            : "Searched: " + string.Join("; ", result.SearchedDirectories);

        return result.Message + Environment.NewLine + searched;
    }

    private IEnumerable<string> GetCandidateDirectories()
    {
        yield return AppContext.BaseDirectory;

        var environmentDirectory = Environment.GetEnvironmentVariable("MYWIREGUARD_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(environmentDirectory))
        {
            yield return environmentDirectory;
        }

        if (!string.IsNullOrWhiteSpace(runtimePaths.WorkspaceRuntimeDirectory))
        {
            yield return runtimePaths.WorkspaceRuntimeDirectory;
        }

        yield return runtimePaths.RuntimeDirectory;
        yield return Path.Combine(AppContext.BaseDirectory, "runtime");
        yield return Directory.GetCurrentDirectory();
        yield return Path.Combine(Directory.GetCurrentDirectory(), "runtime");
    }

    private static bool HasRequiredFiles(string directory)
    {
        return RequiredFiles.All(fileName => File.Exists(Path.Combine(directory, fileName)));
    }

    private static void CopyRuntimeFiles(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }
}