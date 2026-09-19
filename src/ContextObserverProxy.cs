using System.Reflection;
using System.Runtime.ExceptionServices;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.SyncPlay;
using MediaBrowser.Model.SyncPlay;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SyncPlayNames;

// Observe effects through the public state-context interface. The original request
// and state machine still perform all playback work, exactly once.
public class ContextObserverProxy : DispatchProxy
{
    private IGroupStateContext _inner = null!;
    private string _actor = null!;
    private ILogger _logger = null!;
    internal GroupStateUpdate? StateUpdate { get; private set; }
    internal bool QueueChanged { get; private set; }
    internal bool? ActorBuffering { get; private set; }
    internal bool ResetAllBuffering { get; private set; }

    internal static (IGroupStateContext Context, ContextObserverProxy Observation) Wrap(
        IGroupStateContext inner, string actor, ILogger logger)
    {
        var context = Create<IGroupStateContext, ContextObserverProxy>();
        var observation = (ContextObserverProxy)context;
        observation._inner = inner;
        observation._actor = actor;
        observation._logger = logger;
        return (context, observation);
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(method);
        object? result;
        try { result = method.Invoke(_inner, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
        try
        {
            if (method.Name == nameof(IGroupStateContext.SetBuffering)
                && args is [SessionInfo member, bool buffering] && member.Id == _actor)
                ActorBuffering = buffering;
            else if (method.Name == nameof(IGroupStateContext.SetAllBuffering)) ResetAllBuffering = true;
            else if (method.Name == nameof(IGroupStateContext.SendGroupUpdate) && args is { Length: >= 3 }
                && args[1] is SyncPlayBroadcastType broadcast && broadcast != SyncPlayBroadcastType.CurrentSession)
            {
                if (args[2] is GroupUpdate<GroupStateUpdate> state) StateUpdate = state.Data;
                else if (args[2] is GroupUpdate<PlayQueueUpdate>) QueueChanged = true;
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "SyncPlay Names could not observe a playback effect"); }
        return result;
    }
}
