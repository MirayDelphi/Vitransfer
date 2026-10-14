# Vitransfer

A Windows WPF application for copying Vita/PS TV base-game, DLC, update and save folders between FTP connections and/or local folders. Targets .NET 10.

Icons are read first from `ur0:appmeta/<TitleID>/icon0.png`, then from `ux0:app/<TitleID>/sce_sys/icon0.png`. Images must pass PNG signature and Windows decoder checks; missing, encrypted, or damaged artwork displays a placeholder. Artwork is only used for the list and does not change transferred game files.

Connecting loads games without calculating sizes or querying storage capacity. Each row shows only the available Save / DLC / Update / Patch labels, determined by matching title folders in savedata, addcont, patch and rePatch. Availability requires only top-level folder listings, not recursive size scans. If none are found, the detail row shows only the Title ID. Previously calculated values are loaded from local JSON; uncached sizes and storage show N/A. Click **Calculate Size** below either list to refresh that device's game sizes and ux0 storage information. Game sizes sum base-game, DLC and update files under app, addcont, patch and rePatch for each Title ID, excluding saves and filesystem allocation overhead. Capacity is available only when the server provides recognized storage replies. When total capacity is unavailable, Total calculated size sums every currently fetched title, including apps hidden by Hide apps. Missing sizes are shown as N/A or labelled partial; the sum is not device-wide used space.

Metadata is stored in `%LocalAppData%\Vitransfer\Metadata\<device-key>.json`, keyed by normalized FTP host and port or absolute local folder path, then by Title ID. Credentials are not saved. Each completed game's result is saved immediately, retaining completed work if calculation is cancelled. Old caches that omit rePatch are invalidated on loading; click Calculate Size once to replace them with combined sizes. Cached values do not expire automatically; click Calculate Size after changes to the device. Post-transfer refreshes use cached values without initiating another calculation.

## Use

1. For a Vita/PS TV, start FTP in VitaShell and use its address and port. For local files, select an existing folder representing ux0.
2. Enter `ftp://host:port` or an absolute local folder path in each column and click **Go**. Optional FTP authentication: `ftp://user:password@host:port`. FTP addresses must start with `ftp://`. Both address fields are editable dropdowns sharing the five most recent successful Go connections (newest first). Failed or cancelled connections are not added; selecting a suggestion still requires clicking Go. History persists in settings.json as RecentAddresses, supports local paths, and strips FTP credentials.
3. Select games in the source list (Ctrl/Shift supports multiple selections).
4. Leave **Include base game (+DLC)** unchecked for saves only, or check it for app folders, available DLC, available updates, rePatch folders and saves.
5. Click **>>** or **<<** and choose whether to skip existing folders or replace after backing them up.

Close the selected games on both devices before transferring. With the base-game option selected, this copies `ux0:app/<ID>`, `ux0:addcont/<ID>`, `ux0:patch/<ID>`, `ux0:rePatch/<ID>` and `ux0:user/00/savedata/<ID>`. Missing folders are skipped; conflict handling and backups apply independently to each folder. Updates stored within app files are copied with the app; standard updates are stored separately under patch. Licenses, trophies and PSP content are not included. DLC may require its associated licenses on the destination. Raw encrypted saves may need import/resigning using a Vita save manager. Copying app files does not install licenses; use VitaShell's LiveArea refresh where appropriate.

## Local folders

A path such as `D:\Vita\NoPayStation\Download` represents `ux0:`. Its structure is:

```text
Download/
  app/<TitleID>/
  addcont/<TitleID>/
  patch/<TitleID>/
  rePatch/<TitleID>/
  user/00/savedata/<TitleID>/
```

If app is absent, the list is empty; the local root can still receive games. A nonexistent root reports an error. Titles and artwork are read directly from disk, without an artwork cache copy. Calculate Size uses local filesystem sizes and reports the containing drive's used/total capacity when available. Size metadata is cached separately for each local root.

Local-to-FTP uploads from the original files; FTP-to-local downloads to the destination folder; local-to-local copies directly. No payload is staged in the cache or staging folders. Existing destination folders still follow the selected skip/replace policy, with recovery backups retained before replacement. Keep the source files unchanged during a transfer. Overlapping local roots, junctions and symbolic links are rejected.

## Game context menu

Right-click a game in either list. The menu acts only on the clicked title.

- **Open in explorer** opens its app/TitleID folder in Windows Explorer. Local roots use a filesystem path; FTP roots use an FTP URL with credentials omitted, so Explorer may prompt for authentication. FTP browsing depends on the Windows installation's Explorer support.
- **Delete** asks: Are you sure you want to delete this game, DLC and all related content?
  - **Keep saves** deletes that title's app, addcont, patch and rePatch folders, preserving user/00/savedata/TitleID.
  - **Delete** removes those folders and the title's save folder.
  - **Cancel** or closing the dialog changes nothing.

Deletion is permanent and does not create a transfer backup. Missing folders are skipped, other titles are preserved, and all related folders are validated before deletion starts. The app refreshes matching connections afterward and invalidates the deleted title's size plus cached device capacity. Cancelling or losing a connection after deletion starts can leave only some folders removed.

## Hide apps and settings

**Hide apps** applies immediately to both lists without reconnecting. It retains folders beginning with `PCSA`, `PCSB`, `PCSC`, `PCSD`, `PCSE`, `PCSF`, `PCSG` or `PCSH`, plus configured exceptions. This is a title-prefix filter; an official application using one of these prefixes can remain visible. Calculate Size still refreshes all fetched games, including hidden entries.

Edit `%LocalAppData%\Vitransfer\settings.json` while the app is closed, then restart. Existing address-only settings migrate automatically. The default configuration is:

```json
{
  "Addresses": ["ftp://192.168.0.223:1337", "ftp://192.168.0.222:1337"],
  "HideApps": false,
  "HideAppsExceptions": ["MMFE*"]
}
```

Exception matches are case-insensitive. `MMFE*` keeps every folder beginning with MMFE; `MMFE21622` keeps only that exact folder. `*` matches any sequence of characters and patterns must match the entire folder name. Set the exceptions array to `[]` to disable exceptions.

Path reference: [NoNpDrm documentation](https://github.com/TheOfficialFloW/NoNpDrm#sharing-game-updates). Regional title IDs can also be seen in the [Libretro Vita metadata database](https://github.com/libretro/libretro-database/blob/master/metadat/no-intro/Sony%20-%20PlayStation%20Vita.dat).

## Build

Install the .NET 10 SDK, then:

```powershell
dotnet build Vitransfer.App/Vitransfer.App.csproj
dotnet run --project Vitransfer.Tests/Vitransfer.Tests.csproj
dotnet publish Vitransfer.App/Vitransfer.App.csproj -c Release -r win-x64 --self-contained true -o dist
```

Run `dist/Vitransfer.App.exe`. A self-contained publish does not require the user to install .NET.

## Transfer safety and recovery

For FTP-to-FTP transfers, all selected source folders are downloaded before destination changes begin. Transfers involving a local folder read/write the actual local files directly; only manifests and recovery logs are written to the Transfers folder. All source manifests are prepared before destination changes, but local source files are not staged. Files are verified by counts and byte sizes, not cryptographic hashes. Transfers are sequential. Existing folders are skipped unless replacement is explicitly selected. Replacement first downloads and verifies a local backup, then deletes the old folder and uploads the new one. FTP replacement is not atomic.

Local files are stored in `%LocalAppData%\Vitransfer`:

- `Cache`: game titles and icons.
- `Staging`: failed/cancelled jobs, source manifests, and `recovery.txt` identifying affected folders.
- `Backups`: retained destination backups with JSON identifying the device and original remote path.
- `Transfers`: failed/cancelled direct-transfer manifests and recovery logs (no staged payload files).
- `settings.json`: FTP addresses without credentials, local paths, Hide apps preference and configurable filter exceptions.

Successful jobs remove their staging files. Backups remain until manually removed. An interrupted upload can leave a partial destination folder: use the retained staged source, original local source, or backup to restore the exact folder; automatic rollback/resume is not provided in this version. The source is never deleted. Destination capacity is not available reliably through Vita FTP; insufficient destination space will fail and retain recovery files.

## Device validation

Icons are shared across all FTP addresses and local folders by Title ID under `%LocalAppData%\Vitransfer\Cache\Icons\<TitleID>\icon0.png`. Scans reuse a valid cached icon first. FTP scans fetch missing artwork from `ur0:appmeta`, falling back to the game icon; local scans can cache a readable game icon. Encrypted/unreadable artwork shows a placeholder until a readable icon is obtained. There are no cover downloads or external artwork services.

Right-click a game with DLC and choose **Show DLC** to list installed DLC folders in `ux0:addcont/<TitleID>` and `ux0:reAddcont/<TitleID>` (or the equivalent local folders). Names are read from each DLC's `sce_sys/param.sfo`, using `TITLE` then `STITLE`. The popup also shows its folder ID, location and SFO Content ID when available. Missing/unreadable names fall back to the folder ID. No TSV database or internet lookup is used. This browsing feature does not add `reAddcont` to transfers, size calculations or deletion.

Before large transfers, test one small folder in each direction. Check missing saves, skip conflicts, replacement backups, cancellation, and a disconnected device. Verify a copied game/save on the destination console. No real-device validation can be performed without access to your consoles.
