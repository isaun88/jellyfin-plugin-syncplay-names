using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.SyncPlay;
using MediaBrowser.Controller.SyncPlay.Requests;
using MediaBrowser.Model.SyncPlay;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SyncPlayNames;

internal sealed class NamedSyncPlayManager(ISyncPlayManager inner, PartyTracker parties,
    AnnouncementDispatcher dispatcher, ILogger<NamedSyncPlayManager> logger) : ISyncPlayManager, IDisposable
{
    public GroupInfoDto NewGroup(SessionInfo session, NewGroupRequest request, CancellationToken ct) => inner.NewGroup(session, request, ct);
    public void JoinGroup(SessionInfo session, JoinGroupRequest request, CancellationToken ct) => inner.JoinGroup(session, request, ct);
    public void LeaveGroup(SessionInfo session, LeaveGroupRequest request, CancellationToken ct) => inner.LeaveGroup(session, request, ct);
    public List<GroupInfoDto> ListGroups(SessionInfo session, ListGroupsRequest request) => inner.ListGroups(session, request);
    public GroupInfoDto GetGroup(SessionInfo session, Guid groupId) => inner.GetGroup(session, groupId);
    public bool IsUserActive(Guid userId) => inner.IsUserActive(userId);

    public void HandleRequest(SessionInfo session, IGroupPlaybackRequest request, CancellationToken ct)
    {
        // Pings and unrelated requests remain on the original fast path.
        var action = request.Action.ToString();
        if (action is "Pause" or "Unpause" or "Seek" or "Buffer" or "Ready" or "Stop"
            or "Play" or "NextItem" or "PreviousItem" or "SetPlaylistItem")
            inner.HandleRequest(session, new ObservedRequest(request, parties, dispatcher, logger), ct);
        else inner.HandleRequest(session, request, ct);
    }

    public void Dispose() => (inner as IDisposable)?.Dispose();
}
