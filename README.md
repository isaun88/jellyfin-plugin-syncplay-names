# SyncPlay Names

A Jellyfin server plugin that shows who paused, resumed, seeked, or reported buffering in a SyncPlay party. It uses Jellyfin's existing on-screen notifications and installs in the plugins folder without modifying Jellyfin or its web player.

Examples:

- **JAKE paused playback**
- **JAKE resumed playback**
- **JAKE seeked to 12:34**
- **JAKE is buffering**
- **JAKE is ready again**

Stop, next/previous item, and starting or changing the playing item are also covered. Names default to Jellyfin usernames; administrators can assign nickname overrides in the plugin settings.

## Compatibility

- **Built for the Jellyfin 12.1 API baseline / .NET 10; tested with 12.1.0 and 12.2.0.** The revised 0.2.0 release has no server version allowlist or upper version limit. It checks the native SyncPlay service and its injectable session manager instead. A newer version number alone will not disable the integration. Future API or behavior changes may still require a plugin update; untested server releases are not guaranteed compatible. The original release was also confirmed working in a real party on 12.1.0.
- Intended initially for Jellyfin Web in Chrome and Chromium browsers.
- Other players must support both SyncPlay and Jellyfin's `DisplayMessage` command. They have not been verified. External-player windows and picture-in-picture may behave differently.
- Jellyfin Web chooses its own toast duration. Other clients may honor the duration requested in the plugin settings.

## Install

1. Download `SyncPlayNames_0.2.0.0.zip` from the [latest release](https://github.com/isaun88/jellyfin-plugin-syncplay-names/releases/latest).
2. During a maintenance window, stop Jellyfin and create a `SyncPlayNames_0.2.0.0` folder in its **plugins directory**. Extract the four files from the ZIP directly into that folder. For Docker, the plugins directory is normally `/config/plugins/` inside the container; use the corresponding directory in your host's configuration volume.
3. When upgrading, move the old `SyncPlayNames_0.1.0.0` plugin folder out of the plugins directory. Keep `plugins/configurations/Jellyfin.Plugin.SyncPlayNames.xml` to preserve settings. Ensure the new files are readable by Jellyfin, then start it again. Existing running sessions continue using the old loaded plugin until this maintenance restart.
4. Open **Dashboard → Plugins → My Plugins → SyncPlay Names** to configure nicknames and notification options.

The installed layout is:

```text
plugins/
└── SyncPlayNames_0.2.0.0/
    ├── Jellyfin.Plugin.SyncPlayNames.dll
    ├── meta.json
    ├── README.md
    └── LICENSE
```

Only install the release folder. Do not copy development dependencies or test references into Jellyfin. The plugin DLL is managed .NET code and can run on Ubuntu even when compiled on Windows.

To uninstall, stop Jellyfin, remove this plugin's directory, then restart. Jellyfin may retain the plugin's ordinary configuration XML so settings survive a later reinstall.

See [Jellyfin's plugin documentation](https://jellyfin.org/docs/general/server/plugins/) for other installation layouts.

### Plugin catalog

The plugin catalog URL is:

```text
https://raw.githubusercontent.com/isaun88/jellyfin-plugin-syncplay-names/main/manifest.json
```

Add it under **Dashboard → Plugins → Repositories** to make the plugin's details and catalog installation available. A GitHub repository page or release URL is not a Jellyfin catalog URL. Adding the catalog alone does not activate a new DLL; installation and a maintenance restart are still required for an upgrade.

Without this catalog, a manually installed copy can show “An error occurred while getting the plugin details from the repository.” That warning is separate from the plugin's playback integration.

The catalog's `targetAbi: 12.1.0.0` is the minimum server requirement, not an exact-version restriction or upper limit. If you installed the earlier version-locked 0.2.0 build, download the revised ZIP and replace its plugin files during maintenance. Its plugin version remains 0.2.0.0, so automatic updates may not offer it as a newer version. Use the current `SHA256SUMS.txt` to identify the revised archive.

## Settings

- Enable or disable all notifications.
- Toggle playback, buffering, and recovery notices independently.
- Set a requested message duration, subject to client support.
- Set the minimum interval between buffering notices from one player; the default is 15 seconds.
- Add nickname overrides by account username. Matching is case-insensitive; update an override if the account is renamed.

An account used on several devices has the same displayed name on each. Sessions are still tracked separately for group membership and buffering state.

## What buffering messages mean

“JAKE is buffering” means that client sent a buffering report which Jellyfin accepted. It does not prove the cause is a poor internet connection: transcoding, decoding, seeking, or storage can also contribute. A completely disconnected client cannot send a report.

Messages identify the originating client. SyncPlay does not always distinguish human input from client automation, such as advancing to the next item. Automatic synchronization corrections are not labeled as manual pause/play actions. Ignored and wrong-item buffering reports do not produce notifications.

Notifications are short messages, not a persistent list of everyone still buffering.

## How it works

The plugin wraps Jellyfin's standard SyncPlay service through the plugin service-registration interface. Original requests still run through Jellyfin's own state machine exactly once. The plugin observes accepted effects and confirmed membership messages, then sends native notifications only to eligible sessions in the same party.

Delivery uses a bounded background queue. Failed deliveries do not block playback; stale queued messages are discarded and membership is checked again before sending. No browser scripts, web-file modifications, runtime method patches, database changes, polling requests, or external services are required.

Another plugin that replaces the SyncPlay service may conflict. Registration requires the standard singleton `ISyncPlayManager` implementation and a public constructor accepting `ISessionManager`. Missing or incompatible registrations are left untouched and produce a startup warning containing the actual server version. The version number is used for diagnostics only. These service checks cannot prove that every future API or playback behavior is compatible. Jellyfin's generic “Active” plugin status does not confirm that the integration registered.

## Build and test

Requires the **.NET 10 SDK**. From the repository root, on Windows with PowerShell:

```powershell
.\Build.ps1
```

The script builds Release against the 12.1 API baseline, runs the tests, and creates `artifacts\SyncPlayNames_0.2.0.0.zip`, `artifacts\SHA256SUMS.txt`, and a root `manifest.json` with the exact archive checksum. Publish the generated manifest only after its matching release ZIP is available. The ZIP contains the plugin files at its root so Jellyfin's catalog installer can use it.

The script prefers a portable SDK at `.tools\dotnet\dotnet.exe`, otherwise uses `dotnet` on PATH. It does not install an SDK, modify Jellyfin, or contact a running server. NuGet restore downloads the declared build dependencies into the project's `.packages` cache when needed. Release DLLs omit compiler debug paths, and packaging checks for build-machine paths.

For a direct build on other platforms:

```sh
dotnet build src/Jellyfin.Plugin.SyncPlayNames.csproj -c Release
dotnet run --project tests/SyncPlayNames.Tests.csproj -c Release
```

The standalone suite contains 27 tests covering attribution, group isolation, repeated actions, ignored/stale reports, recovery, configuration, concurrency, failure handling, missing services, and custom SyncPlay services. Three more registration tests are enabled when `.reference/<JellyfinVersion>/Emby.Server.Implementations.dll` contains the matching server assembly. They include simulated future version labels and incompatible service lifetimes. Those optional test references are not distributed or installed with the plugin.

To run against the 12.2 dependency set:

```sh
dotnet run --project tests/SyncPlayNames.Tests.csproj -c Release -p:JellyfinVersion=12.2.0
```

The revised 0.2.0 release passed all 30 tests with both 12.1.0 and 12.2.0 dependencies and their matching server implementations. The unchanged release DLL was also exercised with the 12.2 test runtime. Simulated version labels verify removal of the allowlist; they do not test future Jellyfin binaries. Tests use Jellyfin's real playback-state classes with simulated sessions and transport; they do not substitute for checking the desired clients during a real party.

## Reporting problems

Open an [issue](https://github.com/isaun88/jellyfin-plugin-syncplay-names/issues) with the Jellyfin server version, client/app version, action involved, and expected versus observed notification. Include a minimal reproduction if possible.

## License

[GPL-3.0-only](LICENSE).
