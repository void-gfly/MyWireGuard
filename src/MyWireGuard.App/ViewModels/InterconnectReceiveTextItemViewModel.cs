using MyWireGuard.App.Services;
using MyWireGuard.Core.Models;

namespace MyWireGuard.App.ViewModels;

public sealed class InterconnectReceiveTextItemViewModel
{
    private readonly ISystemInteractionService systemInteractionService;

    public InterconnectReceiveTextItemViewModel(InterconnectReceiveTextRecord record, ISystemInteractionService systemInteractionService)
    {
        this.systemInteractionService = systemInteractionService;
        ReceivedAt = record.ReceivedAt;
        SourceIpAddress = record.SourceIpAddress;
        Text = record.Text;
        CopyTextCommand = new AsyncRelayCommand(CopyTextAsync);
    }

    public DateTimeOffset ReceivedAt { get; }

    public string SourceIpAddress { get; }

    public string Text { get; }

    public AsyncRelayCommand CopyTextCommand { get; }

    private Task CopyTextAsync()
    {
        systemInteractionService.CopyText(Text);
        return Task.CompletedTask;
    }
}
