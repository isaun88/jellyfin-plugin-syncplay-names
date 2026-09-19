using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.SyncPlay;
using MediaBrowser.Model.SyncPlay;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.SyncPlayNames.Configuration;

namespace Jellyfin.Plugin.SyncPlayNames;

internal sealed class ObservedRequest(IGroupPlaybackRequest inner, PartyTracker parties,
    IAnnouncementSink dispatcher, ILogger logger, Func<PluginConfiguration?>? readConfiguration = null) : IGroupPlaybackRequest
{
    public RequestType Type => inner.Type;
    public PlaybackRequestType Action => inner.Action;

    public void Apply(IGroupStateContext context, IGroupState state, SessionInfo session, CancellationToken cancellationToken)
    {
        (IGroupStateContext Context, ContextObserverProxy Observation) wrapped;
        try { wrapped = ContextObserverProxy.Wrap(context, session.Id, logger); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SyncPlay Names observation unavailable; continuing playback");
            inner.Apply(context, state, session, cancellationToken);
            return;
        }

        // Never catch/retry the original playback operation: preserve its exceptions and effects.
        inner.Apply(wrapped.Context, state, session, cancellationToken);
        try
        {
            var observation = wrapped.Observation;
            if (observation.ResetAllBuffering) parties.ResetBuffering(context.GroupId);
            var action = Action.ToString();
            bool accepted = action switch
            {
                "Buffer" => observation.ActorBuffering == true
                    && observation.StateUpdate?.Reason == Action
                    && observation.StateUpdate.State == GroupStateType.Waiting,
                "Ready" => observation.ActorBuffering == false,
                "Pause" or "Unpause" or "Seek" or "Stop" => observation.StateUpdate?.Reason == Action,
                "Play" or "NextItem" or "PreviousItem" or "SetPlaylistItem" => observation.QueueChanged,
                _ => false
            };
            var configuration = readConfiguration is null ? Plugin.Instance?.Configuration : readConfiguration();
            if (!accepted || configuration is null) return;
            var announcement = parties.Observe(context.GroupId, session.Id, session.UserName,
                action, context.PositionTicks, configuration, DateTimeOffset.UtcNow);
            if (announcement is not null) dispatcher.Enqueue(announcement);
        }
        catch (Exception ex) { logger.LogWarning(ex, "SyncPlay Names could not prepare a notification"); }
    }
}
