using System.Diagnostics;

namespace MyWireGuard.Infrastructure.Services;

public sealed class InterconnectFirewallRuleManager
{
    private const string RuleName = "MyWireGuard Interconnect TCP 7727";
    private readonly IFirewallCommandRunner commandRunner;

    public InterconnectFirewallRuleManager()
        : this(new NetshFirewallCommandRunner())
    {
    }

    public InterconnectFirewallRuleManager(IFirewallCommandRunner commandRunner)
    {
        this.commandRunner = commandRunner;
    }

    public async Task EnsureInboundRuleAsync(int port, string executablePath, CancellationToken cancellationToken)
    {
        if (port <= 0 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "端口必须在 1 到 65535 之间。");
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("互联防火墙规则缺少程序路径。");
        }

        var setResult = await commandRunner.RunAsync(BuildSetArguments(port, executablePath), cancellationToken).ConfigureAwait(false);
        if (setResult.ExitCode == 0)
        {
            return;
        }

        var addResult = await commandRunner.RunAsync(BuildAddArguments(port, executablePath), cancellationToken).ConfigureAwait(false);
        if (addResult.ExitCode != 0)
        {
            throw new InvalidOperationException($"配置互联防火墙入站规则失败: {addResult.GetCombinedOutput()}");
        }
    }

    private static string[] BuildSetArguments(int port, string executablePath)
    {
        return
        [
            "advfirewall",
            "firewall",
            "set",
            "rule",
            $"name={RuleName}",
            "new",
            "dir=in",
            "action=allow",
            "enable=yes",
            "profile=any",
            "protocol=TCP",
            $"localport={port}",
            $"program={executablePath}"
        ];
    }

    private static string[] BuildAddArguments(int port, string executablePath)
    {
        return
        [
            "advfirewall",
            "firewall",
            "add",
            "rule",
            $"name={RuleName}",
            "dir=in",
            "action=allow",
            "enable=yes",
            "profile=any",
            "protocol=TCP",
            $"localport={port}",
            $"program={executablePath}"
        ];
    }
}

public interface IFirewallCommandRunner
{
    Task<FirewallCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public sealed record FirewallCommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string GetCombinedOutput()
    {
        return string.Join(
            Environment.NewLine,
            new[] { StandardOutput, StandardError }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}

public sealed class NetshFirewallCommandRunner : IFirewallCommandRunner
{
    public async Task<FirewallCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new FirewallCommandResult(
            process.ExitCode,
            await standardOutputTask.ConfigureAwait(false),
            await standardErrorTask.ConfigureAwait(false));
    }
}
