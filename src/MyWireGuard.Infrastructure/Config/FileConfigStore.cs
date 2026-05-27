using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;

namespace MyWireGuard.Infrastructure.Config;

public sealed class FileConfigStore : IConfigStore
{
    private readonly AppRuntimePaths runtimePaths;
    private readonly WgQuickParser parser;

    public FileConfigStore(AppRuntimePaths runtimePaths, WgQuickParser parser)
    {
        this.runtimePaths = runtimePaths;
        this.parser = parser;
    }

    public async Task<IReadOnlyList<TunnelProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        runtimePaths.EnsureCreated();
        var profiles = new List<TunnelProfile>();

        foreach (var path in Directory.EnumerateFiles(runtimePaths.ConfigDirectory, "*.conf", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileNameWithoutExtension(path);
            var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var profile = parser.Parse(content, name);
            profile.ConfigPath = path;
            profiles.Add(profile);
        }

        return profiles.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<TunnelProfile?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = GetConfigPath(name);
        if (!File.Exists(path))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var profile = parser.Parse(content, name);
        profile.ConfigPath = path;
        return profile;
    }

    public async Task<TunnelProfile> SaveAsync(TunnelProfile profile, CancellationToken cancellationToken = default)
    {
        runtimePaths.EnsureCreated();
        var normalizedName = NormalizeName(profile.Name);
        var parsedProfile = parser.Parse(profile.ConfigText, normalizedName);
        parsedProfile.Name = normalizedName;
        parsedProfile.ConfigPath = GetConfigPath(normalizedName);
        await File.WriteAllTextAsync(parsedProfile.ConfigPath, parser.Serialize(parsedProfile), cancellationToken).ConfigureAwait(false);
        parsedProfile.ConfigText = await File.ReadAllTextAsync(parsedProfile.ConfigPath, cancellationToken).ConfigureAwait(false);
        return parsedProfile;
    }

    public async Task<TunnelProfile> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var sourceContent = await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var name = NormalizeName(Path.GetFileNameWithoutExtension(sourcePath));
        var profile = parser.Parse(sourceContent, name);
        profile.ConfigText = sourceContent;
        return await SaveAsync(profile, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportAsync(string name, string destinationPath, CancellationToken cancellationToken = default)
    {
        var profile = await GetAsync(name, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"Tunnel '{name}' was not found.");

        await File.WriteAllTextAsync(destinationPath, parser.Serialize(profile), cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = GetConfigPath(name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public string GetConfigPath(string name)
    {
        return Path.Combine(runtimePaths.ConfigDirectory, NormalizeName(name) + ".conf");
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Tunnel name cannot be empty.");
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Trim().Select(character => invalidChars.Contains(character) ? '_' : character).ToArray());
        return sanitized.Replace(' ', '_');
    }
}