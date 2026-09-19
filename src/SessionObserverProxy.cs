using System.Reflection;
using System.Runtime.ExceptionServices;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.SyncPlay;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SyncPlayNames;

// This proxy is supplied only to Jellyfin's SyncPlay manager. Other server services
// retain the original ISessionManager. It forwards every call and event unchanged.
public class SessionObserverProxy : DispatchProxy
{
    private ISessionManager _inner = null!;
    private PartyTracker _parties = null!;
    private ILogger _logger = null!;

    internal static ISessionManager Wrap(ISessionManager inner, PartyTracker parties, ILogger logger)
    {
        var proxy = Create<ISessionManager, SessionObserverProxy>();
        var implementation = (SessionObserverProxy)proxy;
        implementation._inner = inner;
        implementation._parties = parties;
        implementation._logger = logger;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(method);
        try
        {
            if (method.Name == nameof(ISessionManager.SendSyncPlayGroupUpdate) && args is { Length: >= 2 }
                && args[0] is string sessionId)
            {
                if (args[1] is GroupUpdate<GroupInfoDto> joined && joined.Type == GroupUpdateType.GroupJoined)
                    _parties.Join(sessionId, joined.GroupId);
                else if (args[1] is GroupUpdate<string> left && left.Type == GroupUpdateType.GroupLeft)
                    _parties.Leave(sessionId, left.GroupId);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "SyncPlay Names could not observe membership"); }
        try { return method.Invoke(_inner, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
