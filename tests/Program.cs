using System.Reflection;
using Jellyfin.Plugin.SyncPlayNames;
using Jellyfin.Plugin.SyncPlayNames.Configuration;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.SyncPlay;
using MediaBrowser.Controller.SyncPlay.GroupStates;
using MediaBrowser.Controller.SyncPlay.PlaybackRequests;
using MediaBrowser.Controller.SyncPlay.Queue;
using MediaBrowser.Model.SyncPlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.SyncPlay.Requests;

var tests = new (string, Action)[]
{
#if NATIVE_REFERENCE
    ("Plugin registration works with the installed server implementation", () => {
        var sessions = StubProxy.For<ISessionManager>((method, _) => method.ReturnType == typeof(Task) ? Task.CompletedTask : null);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(sessions);
        services.AddSingleton(StubProxy.For<IUserManager>((_, _) => null));
        services.AddSingleton(StubProxy.For<ILibraryManager>((_, _) => null));
        services.AddSingleton<ISyncPlayManager, Emby.Server.Implementations.SyncPlay.SyncPlayManager>();
        var host = StubProxy.For<IServerApplicationHost>((method, _) => method.Name == "get_ApplicationVersion" ? new Version(12, 1, 0) : null);
        new PluginServiceRegistrator().RegisterServices(services, host);
        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<ISyncPlayManager>();
        Check(manager is NamedSyncPlayManager);
        var actor = new SessionInfo(sessions, NullLogger.Instance) { Id = "native-actor", UserName = "JAKE", UserId = Guid.NewGuid() };
        var group = manager.NewGroup(actor, new NewGroupRequest("Native startup test"), default);
        var parties = provider.GetRequiredService<PartyTracker>();
        Check(parties.Observe(group.GroupId, actor.Id, actor.UserName, "Pause", 0, new(), DateTimeOffset.UtcNow) is not null);
        var other = new SessionInfo(sessions, NullLogger.Instance) { Id = "native-joiner", UserName = "SAM", UserId = Guid.NewGuid() };
        manager.JoinGroup(other, new JoinGroupRequest(Guid.NewGuid()), default);
        Check(parties.Observe(group.GroupId, other.Id, other.UserName, "Pause", 0, new(), DateTimeOffset.UtcNow) is null);
        manager.JoinGroup(other, new JoinGroupRequest(group.GroupId), default);
        Check(parties.Observe(group.GroupId, other.Id, other.UserName, "Pause", 0, new(), DateTimeOffset.UtcNow) is not null);
        manager.LeaveGroup(other, new LeaveGroupRequest(), default);
        Check(parties.Observe(group.GroupId, other.Id, other.UserName, "Pause", 0, new(), DateTimeOffset.UtcNow) is null);
    }),
#endif
    ("Settings page is embedded in the installable DLL", () => {
        Check(typeof(Plugin).Assembly.GetManifestResourceNames().Contains("Jellyfin.Plugin.SyncPlayNames.Configuration.configPage.html"));
    }),
    ("Nickname settings survive Jellyfin's XML configuration format", () => {
        var config = new PluginConfiguration(); config.Nicknames.Add(new() { Username = "jake", Nickname = "JAKE" });
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(PluginConfiguration));
        using var writer = new StringWriter(); serializer.Serialize(writer, config);
        using var reader = new StringReader(writer.ToString());
        var restored = (PluginConfiguration)serializer.Deserialize(reader)!;
        Check(restored.Nicknames.Single().Nickname == "JAKE");
    }),
    ("Messages stay within the actor's confirmed group", () => {
        var h = new Harness(); h.Parties.Join("outsider", Guid.NewGuid());
        h.Apply(new PauseGroupRequest());
        Check(h.Sink.Items.Single().Recipients.Select(r => r.SessionId).Order().SequenceEqual(new[] { "jake", "sam" }));
        Check(h.Sink.Items[0].Text == "JAKE paused playback");
    }),
    ("Repeated pause is not attributed twice", () => {
        var h = new Harness(); h.Apply(new PauseGroupRequest()); h.Apply(new PauseGroupRequest());
        Check(h.Sink.Items.Count == 1);
    }),
    ("Manual resume is attributed", () => {
        var h = new Harness(); h.Apply(new PauseGroupRequest()); h.Apply(new UnpauseGroupRequest());
        Check(h.Sink.Items.Last().Text == "JAKE resumed playback");
    }),
    ("Seek uses the server's accepted position", () => {
        var h = new Harness(); h.Apply(new SeekGroupRequest(TimeSpan.FromMinutes(99).Ticks));
        Check(h.Sink.Items.Single().Text == "JAKE seeked to 10:00");
    }),
    ("Buffering then Ready is recovery, not manual resume", () => {
        var h = new Harness(); h.Buffer(); h.Ready();
        Check(h.Sink.Items.Count == 2);
        Check(h.Sink.Items[0].Text == "JAKE is buffering");
        Check(h.Sink.Items[1].Text == "JAKE is ready again");
    }),
    ("Ignored buffering has no notification", () => {
        var h = new Harness { State = new PlayingGroupState(NullLoggerFactory.Instance) { IgnoreBuffering = true } };
        h.Buffer(); Check(h.Sink.Items.Count == 0);
    }),
    ("Wrong-item buffering has no false attribution", () => {
        var h = new Harness(); h.Apply(new BufferGroupRequest(DateTime.UtcNow, h.Position, true, Guid.NewGuid()));
        Check(h.Sink.Items.Count == 0);
    }),
    ("Wrong-item Ready does not claim recovery", () => {
        var h = new Harness(); h.Buffer();
        h.Apply(new ReadyGroupRequest(DateTime.UtcNow, h.Position, true, Guid.NewGuid()));
        Check(h.Sink.Items.Count == 1);
    }),
    ("A second buffering participant retains their own name", () => {
        var h = new Harness(); h.Buffer(); h.Buffer(h.Sam);
        Check(h.Sink.Items.Count == 2 && h.Sink.Items[1].Text == "SAM is buffering");
    }),
    ("Buffering reports are rate limited", () => {
        var h = new Harness(); h.Buffer(); h.Buffer(); h.Buffer(); Check(h.Sink.Items.Count == 1);
    }),
    ("Buffering can be announced again after cooldown", () => {
        var h = new Harness(); var now = DateTimeOffset.UtcNow;
        var first = h.Parties.Observe(h.GroupId, "jake", "JAKE", "Buffer", 0, h.Config, now);
        var next = h.Parties.Observe(h.GroupId, "jake", "JAKE", "Buffer", 0, h.Config, now.AddSeconds(16));
        Check(first is not null && next is not null);
    }),
    ("A seek clears the previous buffering recovery state", () => {
        var h = new Harness(); h.Buffer(); h.Apply(new SeekGroupRequest(h.Position)); h.Ready();
        Check(!h.Sink.Items.Any(a => a.Text.Contains("ready again")));
    }),
    ("Nickname override and username fallback", () => {
        var h = new Harness(); h.Config.Nicknames.Add(new() { Username = "jake", Nickname = "Jake the Great" });
        h.Apply(new PauseGroupRequest()); Check(h.Sink.Items.Single().Text == "Jake the Great paused playback");
        Check(PartyTracker.ResolveName("SAM", h.Config) == "SAM");
    }),
    ("Names are bounded plain text", () => {
        var c = new PluginConfiguration(); Check(PartyTracker.ResolveName("<JAKE>&\n\u202e", c) == "JAKE");
        Check(PartyTracker.ResolveName(new string('x', 100), c).Length == 64);
    }),
    ("Unconfirmed actors cannot emit party notices", () => {
        var h = new Harness(); h.Parties.Leave("jake", h.GroupId); h.Apply(new PauseGroupRequest());
        Check(h.Sink.Items.Count == 0);
    }),
    ("Departed and rejoined sessions reject stale queued notices", () => {
        var h = new Harness(); h.Apply(new PauseGroupRequest());
        var recipient = h.Sink.Items[0].Recipients.Single(r => r.SessionId == "sam");
        h.Parties.Leave("sam", h.GroupId); h.Parties.Join("sam", h.GroupId);
        Check(!h.Parties.StillMember(h.GroupId, recipient));
    }),
    ("Old-group leave cannot remove new-group membership", () => {
        var h = new Harness(); var next = Guid.NewGuid(); h.Parties.Join("sam", next); h.Parties.Leave("sam", h.GroupId);
        Check(h.Parties.Observe(next, "sam", "SAM", "Pause", 0, h.Config, DateTimeOffset.UtcNow) is not null);
    }),
    ("Disabled plugin leaves playback operational", () => {
        var h = new Harness(); h.Config.Enabled = false; h.Apply(new PauseGroupRequest());
        Check(h.State.Type == GroupStateType.Paused && h.Sink.Items.Count == 0);
    }),
    ("Notification failure does not fail playback", () => {
        var h = new Harness(); h.Sink.Throw = true; h.Apply(new PauseGroupRequest());
        Check(h.State.Type == GroupStateType.Paused);
    }),
    ("Original playback exceptions propagate, without retry", () => {
        var h = new Harness(); var request = new ThrowingRequest();
        try { h.Apply(request); throw new Exception("Expected original exception"); }
        catch (InvalidOperationException ex) { Check(ex.Message == "original failure"); }
        Check(request.Calls == 1 && h.Sink.Items.Count == 0);
    }),
    ("Confirmed membership messages are forwarded exactly once", () => {
        var parties = new PartyTracker(); var forwards = 0;
        var original = StubProxy.For<ISessionManager>((_, _) => { forwards++; return Task.CompletedTask; });
        var wrapped = SessionObserverProxy.Wrap(original, parties, NullLogger.Instance);
        var group = Guid.NewGuid(); var info = new GroupInfoDto(group, "Party", GroupStateType.Playing, ["JAKE"], DateTime.UtcNow);
        wrapped.SendSyncPlayGroupUpdate("jake", new SyncPlayGroupJoinedUpdate(group, info), default).GetAwaiter().GetResult();
        Check(forwards == 1 && parties.Observe(group, "jake", "JAKE", "Pause", 0, new(), DateTimeOffset.UtcNow) is not null);
        wrapped.SendSyncPlayGroupUpdate("jake", new SyncPlayGroupLeftUpdate(group, group.ToString()), default).GetAwaiter().GetResult();
        Check(forwards == 2 && parties.Observe(group, "jake", "JAKE", "Pause", 0, new(), DateTimeOffset.UtcNow) is null);
    }),
    ("Failed joins do not create membership", () => {
        var parties = new PartyTracker(); var original = StubProxy.For<ISessionManager>((_, _) => Task.CompletedTask);
        var wrapped = SessionObserverProxy.Wrap(original, parties, NullLogger.Instance); var group = Guid.NewGuid();
        wrapped.SendSyncPlayGroupUpdate("jake", new SyncPlayLibraryAccessDeniedUpdate(group, ""), default).GetAwaiter().GetResult();
        Check(parties.Observe(group, "jake", "JAKE", "Pause", 0, new(), DateTimeOffset.UtcNow) is null);
    }),
    ("Parallel parties remain isolated", () => {
        var parties = new PartyTracker(); var groups = Enumerable.Range(0, 40).Select(_ => Guid.NewGuid()).ToArray();
        Parallel.For(0, groups.Length, i => {
            var id = "member-" + i; parties.Join(id, groups[i]);
            for (var j = 0; j < 20; j++) {
                var a = parties.Observe(groups[i], id, "Shared Account", "Pause", 0, new(), DateTimeOffset.UtcNow);
                Check(a is not null && a.Recipients.Length == 1 && a.Recipients[0].SessionId == id);
            }
        });
    })
};
var failed = 0;
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + "\n" + ex); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
return failed == 0 ? 0 : 1;

static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }

public class StubProxy : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
    public static T For<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var value = Create<T, StubProxy>(); ((StubProxy)(object)value).Handler = handler; return value;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
}

internal sealed class Sink : IAnnouncementSink
{
    public List<Announcement> Items { get; } = [];
    public bool Throw { get; set; }
    public void Enqueue(Announcement announcement)
    {
        if (Throw) throw new InvalidOperationException("delivery unavailable");
        Items.Add(announcement);
    }
}

internal sealed class ThrowingRequest : IGroupPlaybackRequest
{
    public int Calls;
    public RequestType Type => RequestType.Playback;
    public PlaybackRequestType Action => PlaybackRequestType.Pause;
    public void Apply(IGroupStateContext context, IGroupState state, SessionInfo session, CancellationToken ct)
    { Calls++; throw new InvalidOperationException("original failure"); }
}

internal sealed class Harness
{
    public Guid GroupId { get; } = Guid.NewGuid();
    public PartyTracker Parties { get; } = new();
    public PluginConfiguration Config { get; } = new();
    public Sink Sink { get; } = new();
    public SessionInfo Jake { get; }
    public SessionInfo Sam { get; }
    public IGroupState State { get; set; } = new PlayingGroupState(NullLoggerFactory.Instance);
    public long Position { get; set; } = TimeSpan.FromSeconds(30).Ticks;
    private DateTime _lastActivity = DateTime.UtcNow;
    private readonly PlayQueueManager _queue = new();
    private readonly Dictionary<string, bool> _buffering = new() { ["jake"] = false, ["sam"] = false };
    private readonly IGroupStateContext _context;

    public Harness()
    {
        var sessionManager = StubProxy.For<ISessionManager>((_, _) => null);
        Jake = new SessionInfo(sessionManager, NullLogger.Instance) { Id = "jake", UserName = "JAKE" };
        Sam = new SessionInfo(sessionManager, NullLogger.Instance) { Id = "sam", UserName = "SAM" };
        Parties.Join("jake", GroupId); Parties.Join("sam", GroupId);
        _queue.SetPlaylist([Guid.NewGuid()]); _queue.SetPlayingItemByIndex(0);
        _context = StubProxy.For<IGroupStateContext>(Call);
    }
    public void Apply(IGroupPlaybackRequest request, SessionInfo? actor = null) =>
        new ObservedRequest(request, Parties, Sink, NullLogger.Instance, () => Config).Apply(_context, State, actor ?? Jake, default);
    public void Buffer(SessionInfo? actor = null) => Apply(new BufferGroupRequest(DateTime.UtcNow, Position, true, _queue.GetPlayingItemPlaylistId()), actor);
    public void Ready() => Apply(new ReadyGroupRequest(DateTime.UtcNow, Position, true, _queue.GetPlayingItemPlaylistId()));
    private object? Call(MethodInfo method, object?[]? args)
    {
        switch (method.Name)
        {
            case "get_GroupId": return GroupId;
            case "get_PlayQueue": return _queue;
            case "get_PositionTicks": return Position;
            case "set_PositionTicks": Position = (long)args![0]!; return null;
            case "get_LastActivity": return _lastActivity;
            case "set_LastActivity": _lastActivity = (DateTime)args![0]!; return null;
            case "get_DefaultPing": return 500L;
            case "get_TimeSyncOffset": return 2000L;
            case "get_MaxPlaybackOffset": return 500L;
            case "get_MaxCatchUpOffset": return 10000L;
            case "GetHighestPing": return 1L;
            case "SetState": State = (IGroupState)args![0]!; return null;
            case "SanitizePositionTicks": return Math.Clamp((long?)args![0] ?? 0, 0, TimeSpan.FromMinutes(10).Ticks);
            case "SetBuffering": _buffering[((SessionInfo)args![0]!).Id] = (bool)args[1]!; return null;
            case "SetAllBuffering": foreach (var id in _buffering.Keys.ToArray()) _buffering[id] = (bool)args![0]!; return null;
            case "IsBuffering": return _buffering.Values.Any(v => v);
            case "SendCommand": case "SendGroupUpdate": return Task.CompletedTask;
            case "NewSyncPlayCommand": return new SendCommand(GroupId, _queue.GetPlayingItemPlaylistId(), _lastActivity,
                (SendCommandType)args![0]!, Position, DateTime.UtcNow);
            case "GetPlayQueueUpdate": return null;
            default: throw new NotSupportedException(method.Name);
        }
    }
}
