# Architecture

PluginServiceRegistrator wraps the standard singleton SyncPlay service on Jellyfin 12.1.x and 12.2.x. Unsupported server versions and replacement services are left untouched and produce a startup warning with the actual server version. SessionObserverProxy observes confirmed GroupJoined and GroupLeft messages only on the session-manager interface passed to that service.

NamedSyncPlayManager wraps relevant playback requests. ObservedRequest lets the original request run exactly once; ContextObserverProxy forwards public state-context calls and records accepted effects. Notification errors do not cause the playback request to be retried or fail.

PartyTracker maintains session-level membership, membership generations, and buffering-notification state. It resolves usernames and configured nicknames into bounded plain text. AnnouncementDispatcher delivers native DisplayMessage commands from a bounded queue after checking membership and client capabilities.

The configuration page is embedded in the DLL and uses Jellyfin's standard plugin configuration API. No JavaScript is injected into the player. Other server services retain their original ISessionManager registration.

The test harness exercises Jellyfin's real playback state classes and the plugin's attribution, membership, and failure-handling logic. An optional test reference enables startup verification with the server implementation assembly. Test references and all build dependencies are excluded from release packages and source control.

The release targets the 12.1 API baseline. The same test suite is also compiled and run against 12.2 packages and its matching server implementation assembly. JellyfinVersion selects the dependency version for each test run. The released DLL is additionally tested unchanged with 12.2 dependencies.
