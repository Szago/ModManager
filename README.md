# Mod Manager

BepInEx plugin ID: `com.eros.modmanager`

Adds a **MOD MANAGER** button below the FPS controls in the game's settings
panel. The button opens a game-styled panel listing mods integrated with the
Mod Manager API and their restart-state toggles. Mods can also register owned
choice settings; those entries receive a gear button that opens a smaller
game-styled settings popup. Every entry also receives an info button cloned
from the game's Events panel; it opens the same style of popup with a short
description supplied by that mod.

## Behavior

- Toggling a mod never unloads or starts it in the current game session.
- Desired states are ordinary Boolean entries in
  `BepInEx/config/ModManager.states.cfg`.
- On the next launch, an integrated mod calls
  `ModManagerApi.IsEnabled(pluginGuid)` before installing patches, event
  handlers, coroutines, or UI.
- Disabled DLLs remain untouched and loaded by BepInEx, but their plugin
  initialization exits immediately.
- Mods declare a BepInEx dependency on `com.eros.modmanager`, which lets
  the manager list only plugins that actually support this API.
- Mod Manager itself is never listed and cannot be disabled.
- Choice settings remain owned and persisted by the integrating mod through its
  own BepInEx `ConfigEntry<string>`. Mod Manager only renders the registered
  choices and updates that entry.
- Descriptions are registered and owned by the integrating mod. Mod Manager
  keeps no separate description catalog.

Enabling/disabling still only writes config. Updates are a separate, explicit
download-and-restart operation; no external executable is used.

## GitHub updates (2.3.0)

Each mod owns its release source, registered before its enabled-state guard:

```csharp
public const string PluginReleasesUrl = ""; // Fill when the repository is published.
// Later: https://github.com/OWNER/REPOSITORY/releases
ModManagerApi.RegisterReleaseSource(PluginGuid, PluginReleasesUrl, "MyMod.dll");
```

Clients using this API require `[BepInDependency(ModManagerApi.ManagerGuid, "2.3.0")]`.
The empty URL intentionally displays `Not configured` and makes no request.
The asset name is exact and case-sensitive. Publish a public, stable GitHub
release tagged `v1.2.3` (or `1.2.3`), matching the DLL's BepInPlugin version,
and upload the named DLL. ZIP assets and prerelease tags are not supported.
Do not bundle multiple BepInEx plugins into one asset. Keep assembly names and
GUIDs stable. Release URLs cannot point to arbitrary download hosts.

Rows show name, then version / info-style refresh button / update status /
Update when newer. Mod Manager has equivalent controls in the header. The
restart-note bar has Update all, Check for updates, and Toggle all. Update all
checks before downloading and includes Mod Manager. Toggle all enables all
listed mods when any are off, otherwise disables all; it never toggles the
manager or BepInEx. No requests run automatically on opening the panel.

Checks use GitHub's unauthenticated latest-release API, skip prereleases, cache
successful checks for one minute, and report missing releases, bad assets,
network failures, and rate limits without claiming the mod is up to date.
Downloads run off the Unity thread and are capped at 64 MiB per DLL. Validate
the release size, SHA-256 digest when GitHub supplies it, assembly identity,
plugin GUID and plugin version before accepting a download.

`ModManager.dll` embeds `ModManager.Updater.dll`. On first load it installs this
small BepInEx startup component into `BepInEx/patchers/ModManager/`. Release
distribution still needs just ModManager.dll; the updater has no plugin entry
or toggle. The startup component has a stable v1 manifest contract and is not
itself remotely updated in this iteration.

Downloads go into `BepInEx/ModManagerUpdates/<unique-id>/` as payload.bin and
pending.xml. On the next launch, before plugin discovery, the startup component
checks hashes again and atomically replaces only the recorded installed plugin
DLL. The current DLL must still match the one seen at download time. A previous.bin
backup and applied.xml receipt remain in that folder. An interrupted replacement
can resume safely. Failed installs leave pending.xml and log an error, preserving
the installed DLL. For manual recovery, close the game, inspect the manifest's
RelativeTarget, and restore previous.bin to that exact plugins path; remove only
that update's pending.xml to cancel a blocked pending install. Backups are retained
until manually removed. Neither configs nor unrelated files are updated.

Publish compatible mod/manager versions together. The updater validates identity,
not game-version compatibility or arbitrary third-party dependency graphs.
Update all handles the manager before clients; do not publish a client requiring
a manager release that is not also available.

Offline checks (mock HTTP and real temporary DLL replacement; no live game writes):

```powershell
dotnet run --project mods/ModManager/tests/UpdateChecks.csproj -c Release
```

Version 2.3.4 is the user-confirmed working visual baseline. Release URLs remain
empty; a real GitHub release download/restart test is still needed once
repositories are published.

## Registering a description

Register the description before checking the enabled state so the info popup is
available even when the mod is disabled for the current session.

```csharp
ModManagerApi.RegisterDescription(
    PluginGuid,
    "Adds a concise explanation of this mod's player-facing behavior.");

if (!ModManagerApi.IsEnabled(PluginGuid))
{
    enabled = false;
    return;
}
```

Descriptions must be non-empty and should remain short enough for the popup.

## Registering a choice setting

Bind and register settings alongside the description before the enabled-state
guard so their gear remains available even when the mod is inactive for the
current session. Runtime hooks and UI still belong after the guard.

```csharp
ConfigEntry<string> preset = Config.Bind(
    "Keybinds", "Preset", "numbers", "Active key preset.");

ModManagerApi.RegisterChoiceSetting(
    PluginGuid,
    "keybind-preset",
    "KEYBIND PRESET",
    preset,
    new ModChoiceOption("numbers", "1 / 2 / 3 / 4 / 5"),
    new ModChoiceOption("letters", "Z / X / C / V / B"));

if (!ModManagerApi.IsEnabled(PluginGuid))
{
    enabled = false;
    return;
}
```

Setting keys and option values must be stable and non-empty. A choice setting
requires at least two uniquely valued options. Assigning a choice updates the
owning config entry immediately.

## Build and deploy

From the workspace root:

```powershell
.\tools\Build-Mod.ps1 -Mod ModManager -Configuration Release
```

Deploy the plugin with:

```powershell
.\tools\Deploy-Mod.ps1 -Mod ModManager -Configuration Release
```

The deploy target copies only `ModManager.dll` into `BepInEx/plugins`.

Runtime messages use the `[ModManager]` prefix in
`BepInEx/LogOutput.log`.
