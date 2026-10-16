# Architecture

PluginServiceRegistrator wraps the standard singleton SyncPlay service without a server version allowlist. It checks the native implementation's identity, interface, lifetime, and public constructor accepting ISessionManager. Missing or incompatible service registrations are left untouched and produce a startup warning with the actual server version. The version number is diagnostic only. These structural checks do not guarantee compatibility with future API or behavior changes. SessionObserverProxy observes confirmed GroupJoined and GroupLeft messages only on the session-manager interface passed to that service.

NamedSyncPlayManager wraps relevant playback requests. ObservedRequest lets the original request run exactly once; ContextObserverProxy forwards public state-context calls and records accepted effects. Notification errors do not cause the playback request to be retried or fail.

PartyTracker maintains session-level membership, membership generations, and buffering-notification state. It resolves usernames and configured nicknames into bounded plain text. AnnouncementDispatcher delivers native DisplayMessage commands from a bounded queue after checking membership and client capabilities.

The configuration page is embedded in the DLL and uses Jellyfin's standard plugin configuration API. No JavaScript is injected into the player. Other server services retain their original ISessionManager registration.

The test harness exercises Jellyfin's real playback state classes and the plugin's attribution, membership, and failure-handling logic. An optional test reference enables startup verification with the server implementation assembly. Test references and all build dependencies are excluded from release packages and source control.

The release targets the 12.1 API baseline, which remains the catalog's minimum targetAbi rather than a maximum version. The same test suite is also compiled and run against 12.2 packages and its matching server implementation assembly. JellyfinVersion selects the dependency version for each test run. The released DLL is additionally tested unchanged with 12.2 dependencies. Simulated future version labels test the lack of a version gate, not compatibility with future server binaries.
