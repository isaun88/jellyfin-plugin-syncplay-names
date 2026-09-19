using System.Threading.Channels;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SyncPlayNames;

internal interface IAnnouncementSink
{
    void Enqueue(Announcement announcement);
}

internal sealed class AnnouncementDispatcher(ISessionManager sessions, PartyTracker parties,
    ILogger<AnnouncementDispatcher> logger) : BackgroundService, IAnnouncementSink
{
    private readonly Channel<Announcement> _queue = Channel.CreateBounded<Announcement>(new BoundedChannelOptions(128)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropOldest
    });

    public void Enqueue(Announcement announcement) => _queue.Writer.TryWrite(announcement);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("SyncPlay Names: {IntegrationStatus}", Plugin.IntegrationStatus);
        try
        {
            await foreach (var announcement in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                if (DateTimeOffset.UtcNow - announcement.CreatedAt > TimeSpan.FromSeconds(8)
                    || Plugin.Instance?.Configuration.Enabled != true) continue;
                await Task.WhenAll(announcement.Recipients.Select(r => SendAsync(announcement, r, stoppingToken))).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task SendAsync(Announcement announcement, Recipient recipient, CancellationToken stoppingToken)
    {
        try
        {
            if (!parties.StillMember(announcement.GroupId, recipient)) return;
            var session = sessions.Sessions.FirstOrDefault(s => s.Id == recipient.SessionId);
            if (session is null || !session.SupportedCommands.Contains(GeneralCommandType.DisplayMessage)) return;
            var duration = Math.Clamp(Plugin.Instance?.Configuration.MessageDurationMs ?? 3500, 1000, 10000);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            // Server-originated message; no participant receives remote-control privileges.
            await sessions.SendMessageCommand(null!, session.Id, new MessageCommand
            {
                Header = "SyncPlay",
                Text = announcement.Text,
                TimeoutMs = duration
            }, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { logger.LogDebug(ex, "SyncPlay Names message could not reach a party member"); }
    }
}
