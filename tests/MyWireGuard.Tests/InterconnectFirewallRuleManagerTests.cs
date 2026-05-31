using MyWireGuard.Infrastructure.Services;

namespace MyWireGuard.Tests;

public sealed class InterconnectFirewallRuleManagerTests
{
    [Fact]
    public async Task EnsureInboundRuleAsync_ShouldAddRuleWhenSetCannotFindExistingRule()
    {
        var runner = new RecordingFirewallCommandRunner(
            new FirewallCommandResult(1, string.Empty, "No rules match the specified criteria."),
            new FirewallCommandResult(0, "Ok.", string.Empty));
        var manager = new InterconnectFirewallRuleManager(runner);

        await manager.EnsureInboundRuleAsync(7727, @"C:\Apps\MyWireGuard.exe", CancellationToken.None);

        Assert.Equal(2, runner.Commands.Count);
        Assert.Equal("set", runner.Commands[0][2]);
        Assert.Contains("localport=7727", runner.Commands[0]);
        Assert.Contains(@"program=C:\Apps\MyWireGuard.exe", runner.Commands[0]);
        Assert.Equal("add", runner.Commands[1][2]);
        Assert.Contains("localport=7727", runner.Commands[1]);
        Assert.Contains(@"program=C:\Apps\MyWireGuard.exe", runner.Commands[1]);
    }

    [Fact]
    public async Task EnsureInboundRuleAsync_ShouldNotAddRuleWhenSetUpdatesExistingRule()
    {
        var runner = new RecordingFirewallCommandRunner(new FirewallCommandResult(0, "Updated 1 rule(s).", string.Empty));
        var manager = new InterconnectFirewallRuleManager(runner);

        await manager.EnsureInboundRuleAsync(7727, @"C:\Apps\MyWireGuard.exe", CancellationToken.None);

        Assert.Single(runner.Commands);
        Assert.Equal("set", runner.Commands[0][2]);
    }

    private sealed class RecordingFirewallCommandRunner : IFirewallCommandRunner
    {
        private readonly Queue<FirewallCommandResult> results;

        public RecordingFirewallCommandRunner(params FirewallCommandResult[] results)
        {
            this.results = new Queue<FirewallCommandResult>(results);
        }

        public List<IReadOnlyList<string>> Commands { get; } = [];

        public Task<FirewallCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Commands.Add(arguments.ToArray());
            return Task.FromResult(results.Dequeue());
        }
    }
}
