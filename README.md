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

- **Jellyfin Server 12.1.x / .NET 10.** Confirmed working by the maintainer on Jellyfin 12.1.0.
- Intended initially for Jellyfin Web in Chrome and Chromium browsers.
- Other players must support both SyncPlay and Jellyfin's `DisplayMessage` command. They have not been verified. External-player windows and picture-in-picture may behave differently.
- Jellyfin Web chooses its own toast duration. Other clients may honor the duration requested in the plugin settings.

## Install

1. Download `SyncPlayNames_0.1.0.0.zip` from the [latest release](https://github.com/isaun88/jellyfin-plugin-syncplay-names/releases/latest).
2. Extract the `SyncPlayNames_0.1.0.0` folder into your Jellyfin **plugins directory**. For Docker, this is normally `/config/plugins/` inside the container; use the corresponding directory in your host's configuration volume.
3. Ensure the Jellyfin process can read the extracted files, then restart Jellyfin.
4. Open **Dashboard → Plugins → My Plugins → SyncPlay Names** to configure nicknames and notification options.

The installed layout is:

```text
plugins/
└── SyncPlayNames_0.1.0.0/
    ├── Jellyfin.Plugin.SyncPlayNames.dll
    ├── meta.json
    ├── README.md
    └── LICENSE
```

Only install the release folder. Do not copy development dependencies or test references into Jellyfin. The plugin DLL is managed .NET code and can run on Ubuntu even when compiled on Windows.

To uninstall, stop Jellyfin, remove this plugin's directory, then restart. Jellyfin may retain the plugin's ordinary configuration XML so settings survive a later reinstall.

See [Jellyfin's plugin documentation](https://jellyfin.org/docs/general/server/plugins/) for other installation layouts.

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

Another plugin that replaces the SyncPlay service may conflict. This plugin checks the service implementation when it registers and disables its integration on unsupported server versions. Retest after upgrading beyond Jellyfin 12.1.

## Build and test

Requires the **.NET 10 SDK**. From the repository root, on Windows with PowerShell:

```powershell
.\Build.ps1
```

The script builds Release, runs the tests, and creates `artifacts\SyncPlayNames_0.1.0.0.zip`. It prefers a portable SDK at `.tools\dotnet\dotnet.exe`, otherwise uses `dotnet` on PATH. It does not install an SDK or contact a Jellyfin server. NuGet restore downloads the declared build dependencies into the project's `.packages` cache when needed.

For a direct build on other platforms:

```sh
dotnet build src/Jellyfin.Plugin.SyncPlayNames.csproj -c Release
dotnet run --project tests/SyncPlayNames.Tests.csproj -c Release
```

The standalone suite contains 25 tests covering attribution, group isolation, repeated actions, ignored/stale reports, recovery, configuration, concurrency, and failure handling. A 26th service-startup test is enabled if `.reference/Emby.Server.Implementations.dll` contains a Jellyfin 12.1 server assembly. That optional test reference is not distributed or installed with the plugin.

The initial release passed all 26 local tests and built without warnings or errors. Tests exercise Jellyfin's real playback-state classes with simulated sessions and transport; they do not substitute for checking the desired clients during a real party.

## Reporting problems

Open an [issue](https://github.com/isaun88/jellyfin-plugin-syncplay-names/issues) with the Jellyfin server version, client/app version, action involved, and expected versus observed notification. Include a minimal reproduction if possible.

## License

[GPL-3.0-only](LICENSE).
