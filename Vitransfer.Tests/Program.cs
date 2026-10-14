using System.Buffers.Binary;
using System.Text;
using Vitransfer.Core;
using FluentFTP;

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
Check(new[] { "PCSA", "PCSB", "PCSC", "PCSD", "PCSE", "PCSF", "PCSG", "PCSH" }.All(p => GameFilter.Matches(p + "00123", [])), "Regional Vita game prefixes remain visible");
Check(!GameFilter.Matches("VITASHELL", []) && !GameFilter.Matches("PCSX00123", []), "Hide unrelated apps and unknown prefixes");
Check(GameFilter.Matches("MMFE21622", ["MMFE*"]) && GameFilter.Matches("mmfe99999", ["MMFE*"]), "Wildcard homebrew exception matches prefix without case sensitivity");
Check(GameFilter.Matches("MMFE21622", ["MMFE21622"]) && !GameFilter.Matches("MMFE21623", ["MMFE21622"]), "Exact exception matches only the configured ID");
Check(!GameFilter.Matches("XMMFE21622", ["MMFE*"]) && !GameFilter.Matches("MMFE21622X", ["MMFE21622"]), "Exception patterns match the entire folder name");
Check(!GameFilter.Matches("ANYAPP", ["", " "]), "Empty exceptions do not expose apps");
var oldSettings = AppSettings.Parse("[\"ftp://192.168.0.223:1337\",\"ftp://192.168.0.222:1337\"]");
Check(oldSettings.Addresses.Length == 2 && oldSettings.HideAppsExceptions.SequenceEqual(new[] { "MMFE*" }), "Migrate legacy addresses and add default exception");
var configured = AppSettings.Parse("{\"HideApps\":true,\"HideAppsExceptions\":[\"MMFE21622\"],\"OtherSetting\":123}");
var restoredSettings = AppSettings.Parse(configured.ToJson());
Check(restoredSettings.IsGameVisible("MMFE21622") && !restoredSettings.IsGameVisible("MMFE21623") && restoredSettings.Extra!["OtherSetting"].GetInt32() == 123, "Settings round-trip retains filter and additional settings");
Check(new AppSettings().IsGameVisible("VITASHELL"), "Unchecked Hide apps shows every folder");
var recentSettings = new AppSettings();
for (int i = 1; i <= 6; i++) recentSettings.RememberSuccessfulAddress($"ftp://192.168.0.{i}:1337");
Check(recentSettings.RecentAddresses.Length == 5 && recentSettings.RecentAddresses.First().Contains("0.6:") && !recentSettings.RecentAddresses.Any(address => address.Contains("0.1:")), "Recent addresses retain the newest five successful entries");
recentSettings.RememberSuccessfulAddress("ftp://user:secret@192.168.0.3:1337/");
Check(recentSettings.RecentAddresses.First() == "ftp://192.168.0.3:1337/" && recentSettings.RecentAddresses.Length == 5 && !recentSettings.ToJson().Contains("secret"), "Reconnecting promotes existing address and never saves FTP credentials");
var recentLocalPath = Path.Combine(Path.GetTempPath(), "Vitransfer-recent");
recentSettings.RememberSuccessfulAddress(recentLocalPath + Path.DirectorySeparatorChar);
recentSettings.RememberSuccessfulAddress(recentLocalPath.ToUpperInvariant());
var recentRestored = AppSettings.Parse(recentSettings.ToJson());
Check(recentRestored.RecentAddresses.Length == 5 && recentRestored.RecentAddresses.Count(address => address.Equals(recentLocalPath, StringComparison.OrdinalIgnoreCase)) == 1, "Local paths deduplicate and recent history survives JSON round-trip");
Check(oldSettings.RecentAddresses.Length == 0, "Legacy settings do not invent successful connection history");
Check(StorageSpace.Parse("Total bytes: 1000; Free bytes: 400") is { TotalBytes: 1000, UsedBytes: 600, FreeBytes: 400 }, "Storage derives used bytes from total and free");
Check(StorageSpace.Parse("Total: 2 GiB; Used: 1 GiB") is { TotalBytes: 2147483648, FreeBytes: 1073741824 }, "Storage recognizes explicit binary units");
Check(StorageSpace.Parse("Total bytes: 10; Free bytes: 20") == null, "Reject impossible capacity");
Check(StorageSpace.Parse("211 Connected for 1000 seconds; transferred 400 bytes") == null, "FTP status counters are not capacity");
Check(StorageSpace.Parse("Total: 1000; Free: 400") == null, "Unspecified storage units are not guessed");
Check(ByteSize.Format(1073741824).EndsWith("GiB"), "Human-readable game sizes");
var summaryGames = new[] { new Game("PCSE00240", "Game", null, false, 500), new Game("VITASHELL", "App", null, false, 300) };
Check(DeviceStorageSummary.Display(null, summaryGames) == "Total calculated size: 800 B" && new AppSettings { HideApps = true }.IsGameVisible("VITASHELL") == false, "Calculated total includes apps hidden by the filter");
Check(DeviceStorageSummary.Display(new StorageSpace(FreeBytes: 42), summaryGames) == "Total calculated size: 800 B", "Free-only storage falls back to calculated total");
Check(DeviceStorageSummary.Display(new StorageSpace(1000, 600, 400), summaryGames).Contains("600 B used / 1000 B total"), "Reported device capacity takes priority");
Check(DeviceStorageSummary.Display(null, new[] { summaryGames[0] with { SizeBytes = null } }) == "Total calculated size: N/A", "Uncalculated total shows N/A");
Check(DeviceStorageSummary.Display(null, new[] { summaryGames[0], summaryGames[1] with { SizeBytes = null } }).Contains("500 B (partial; 1 size(s) unavailable)"), "Partial totals explicitly identify missing sizes");
var iconCache = Path.Combine(Path.GetTempPath(), "Vitransfer-icons-" + Guid.NewGuid().ToString("N"));
byte[] validPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
var artwork = new Dictionary<string, byte[]> { ["cached"] = validPng, ["game"] = validPng };
var artworkRequests = new List<string>();
async Task<bool> DownloadArtwork(string remote, string local, CancellationToken ct) {
    artworkRequests.Add(remote);
    if (!artwork.TryGetValue(remote, out var bytes)) return false;
    await File.WriteAllBytesAsync(local, bytes, ct); return true;
}
var iconResult = await IconArtwork.Resolve(new[] { "cached", "game" }, iconCache, DownloadArtwork, IconArtwork.HasPngSignature, default);
Check(iconResult != null && artworkRequests.SequenceEqual(new[] { "cached" }), "Cached artwork takes priority");
artwork["cached"] = [0x5D, 0xE8, 0x5C, 0x45]; artworkRequests.Clear();
iconResult = await IconArtwork.Resolve(new[] { "cached", "game" }, iconCache, DownloadArtwork, IconArtwork.HasPngSignature, default);
Check(iconResult != null && artworkRequests.SequenceEqual(new[] { "cached", "game" }), "Encrypted cached artwork falls back to game PNG");
artwork.Remove("cached"); artworkRequests.Clear();
iconResult = await IconArtwork.Resolve(new[] { "cached", "game" }, iconCache, DownloadArtwork, IconArtwork.HasPngSignature, default);
Check(iconResult != null && artworkRequests.Count == 2, "Missing cached artwork falls back to game PNG");
artwork["cached"] = validPng; int decodeAttempts = 0;
iconResult = await IconArtwork.Resolve(new[] { "cached", "game" }, iconCache, DownloadArtwork, _ => ++decodeAttempts > 1, default);
Check(iconResult != null && decodeAttempts == 2, "Decoder rejection tries next artwork source");
artwork["cached"] = artwork["game"] = [1, 2, 3];
iconResult = await IconArtwork.Resolve(new[] { "cached", "game" }, iconCache, DownloadArtwork, IconArtwork.HasPngSignature, default);
Check(iconResult == null, "Two invalid sources show placeholder instead of stale cached icon");
File.Delete(Path.Combine(iconCache, "icon0.png"));
artwork["cached"] = validPng; artworkRequests.Clear();
iconResult = await IconArtwork.ResolveShared(new[] { "cached" }, iconCache, DownloadArtwork, IconArtwork.HasPngSignature, default);
Check(iconResult != null && artworkRequests.Count == 1, "Shared icon cache stores missing artwork");
artworkRequests.Clear(); artwork.Remove("cached");
Check(await IconArtwork.ResolveShared(new[] { "cached" }, iconCache, DownloadArtwork, IconArtwork.HasPngSignature, default) == iconResult && artworkRequests.Count == 0, "Cached title icon is reused without contacting any source");
await File.WriteAllBytesAsync(Path.Combine(iconCache, "icon0.png"), new byte[] { 1, 2, 3 });
artwork["cached"] = validPng;
Check(await IconArtwork.ResolveShared(new[] { "cached" }, iconCache, DownloadArtwork, IconArtwork.HasPngSignature, default) != null && artworkRequests.Count == 1, "Corrupt shared icon is replaced from source");
Check(IconArtwork.SharedCache("pcse00001") == IconArtwork.SharedCache("PCSE00001"), "Shared icon identity is title-based and case-insensitive");
File.Delete(Path.Combine(iconCache, "icon0.png")); Directory.Delete(iconCache);
using (var server = new LimitedFtpServer()) {
    var metadataDirectory = Path.Combine(Path.GetTempPath(), "Vitransfer-metadata-" + Guid.NewGuid().ToString("N"));
    var metadataCache = new DeviceMetadataCache(metadataDirectory);
    using var ftp = new VitaConnection($"ftp://127.0.0.1:{server.Port}", metadataCache: metadataCache);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await ftp.Connect(timeout.Token);
    var staging = Path.Combine(Path.GetTempPath(), "Vitransfer-upload-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(staging);
    byte[] payload = [0, 1, 2, 255, 13, 10];
    await File.WriteAllBytesAsync(Path.Combine(staging, "save.dat"), payload);
    Directory.CreateDirectory(Path.Combine(staging, "nested"));
    await File.WriteAllBytesAsync(Path.Combine(staging, "nested", "second.dat"), payload);
    await ftp.Upload(staging, "/ux0:/app/TEST", new[] { new RemoteEntry("save.dat", false, payload.Length), new RemoteEntry("nested", true, 0), new RemoteEntry("nested/second.dat", false, payload.Length), new RemoteEntry("empty", true, 0) }, new Progress<TransferUpdate>(), timeout.Token);
    Check(server.Files["/ux0:/app/TEST/save.dat"].SequenceEqual(payload), "Binary upload to limited FTP server");
    Check(!server.Commands.Any(c => c is "NLST" or "SIZE" or "MDTM" or "EPSV" or "NOOP"), "Upload avoids unsupported Vita FTP commands");
    Check((await ftp.ReadStorage(timeout.Token)).TotalBytes == null, "Unsupported capacity commands do not fail connection");
    server.StorageReply = "Total bytes: 1000; Free bytes: 400";
    Check((await ftp.ReadStorage(timeout.Token)).UsedBytes == 600, "Read device capacity over FTP");
    int listings = server.Commands.Count(c => c == "LIST");
    var fetched = await ftp.Scan(new Progress<string>(), timeout.Token);
    Check(fetched.Single().SizeDisplay == "N/A" && fetched.Single().Detail == "TEST", "First fetch shows N/A and omits unavailable content labels");
    Check(server.Commands.Count(c => c == "LIST") == listings + 1, "Fetching games never recursively calculates sizes");
    await ftp.CalculateSizes(fetched, new Progress<string>(), timeout.Token);
    Check(ftp.CachedMetadata.Apply(fetched.Single()).SizeBytes == payload.Length * 2, "Missing optional DLC and updates contribute zero");
    var extraEntries = new[] { new RemoteEntry("save.dat", false, payload.Length) };
    await ftp.Upload(staging, ftp.AddcontRoot + "/TEST", extraEntries, new Progress<TransferUpdate>(), timeout.Token);
    await ftp.Upload(staging, ftp.PatchRoot + "/TEST", extraEntries, new Progress<TransferUpdate>(), timeout.Token);
    await ftp.Upload(staging, ftp.RePatchRoot + "/TEST", extraEntries, new Progress<TransferUpdate>(), timeout.Token);
    await ftp.CalculateSizes(fetched, new Progress<string>(), timeout.Token);
    Check(ftp.CachedMetadata.Apply(fetched.Single()).SizeBytes == payload.Length * 5, "Explicit calculation sums nested app files, DLC, updates and rePatch");
    Check(new DeviceMetadataCache(metadataDirectory).Load(ftp.DeviceKey).Storage?.UsedBytes == 600, "Device storage persists in JSON");
    listings = server.Commands.Count(c => c == "LIST");
    var refreshed = (await ftp.Scan(new Progress<string>(), timeout.Token)).Single();
    Check(refreshed.SizeBytes == payload.Length * 5, "Subsequent fetch restores cached size");
    Check(refreshed.Detail == "TEST · DLC / Update / Patch" && !refreshed.HasSave, "Fetch discovers DLC and updates without claiming a save");
    Check(server.Commands.Count(c => c == "LIST") == listings + 4, "Fetch lists only app, DLC, update and rePatch roots without recursion");
    await ftp.Upload(staging, ftp.SaveRoot + "/TEST", extraEntries, new Progress<TransferUpdate>(), timeout.Token);
    Check((await ftp.Scan(new Progress<string>(), timeout.Token)).Single().Detail == "TEST · Save / DLC / Update / Patch", "Refresh discovers newly available saves alongside DLC and updates");
    server.Files[ftp.AddcontRoot + "/TEST/save.dat"] = new byte[9];
    await ftp.CalculateSizes(fetched, new Progress<string>(), timeout.Token);
    Check(new DeviceMetadataCache(metadataDirectory).Load(ftp.DeviceKey).Apply(fetched.Single()).SizeBytes == 33, "Recalculation overwrites JSON with changed DLC size");
    server.Files[ftp.AddcontRoot + "/TEST/bad:stream"] = [1];
    await ftp.CalculateSizes(fetched, new Progress<string>(), timeout.Token);
    Check(ftp.CachedMetadata.Apply(fetched.Single()).SizeBytes == null, "Unreadable DLC prevents a misleading partial game size");
    server.Files.Remove(ftp.AddcontRoot + "/TEST/bad:stream");
    await ftp.CalculateSizes(fetched, new Progress<string>(), timeout.Token);
    var secondDevice = new DeviceMetadata { DeviceKey = "other-device", Address = "192.168.0.222:1337", Storage = new StorageSpace(2000, 1000, 1000) };
    secondDevice.Games["TEST"] = new("Test", 99, DateTimeOffset.UtcNow);
    metadataCache.Save(secondDevice);
    Check(metadataCache.Load(secondDevice.DeviceKey).Apply(fetched.Single()).SizeBytes == 99 && ftp.CachedMetadata.Apply(fetched.Single()).SizeBytes == 33, "Same Title ID has separate sizes per device");
    Check(metadataCache.Load(secondDevice.DeviceKey).Storage?.TotalBytes == 2000 && ftp.CachedMetadata.Storage?.TotalBytes == 1000, "Storage cache is separate per device");
    secondDevice.Version = 1;
    metadataCache.Save(secondDevice);
    Check(metadataCache.Load(secondDevice.DeviceKey).Apply(fetched.Single()).SizeBytes == null && metadataCache.Load(secondDevice.DeviceKey).Storage?.TotalBytes == 2000, "Legacy base-only cache invalidates sizes and preserves capacity");
    secondDevice.Version = 2;
    metadataCache.Save(secondDevice);
    Check(metadataCache.Load(secondDevice.DeviceKey).Apply(fetched.Single()).SizeBytes == null, "Cache omitting rePatch requires recalculation");
    File.WriteAllText(Path.Combine(metadataDirectory, "other-device.json"), "broken json");
    Check(metadataCache.Load("other-device").Apply(fetched.Single()).SizeBytes == null, "Corrupt cache falls back to N/A");
    File.Delete(Path.Combine(metadataDirectory, ftp.DeviceKey + ".json"));
    File.Delete(Path.Combine(metadataDirectory, "other-device.json")); Directory.Delete(metadataDirectory);
    File.Delete(Path.Combine(staging, "nested", "second.dat")); Directory.Delete(Path.Combine(staging, "nested"));
    File.Delete(Path.Combine(staging, "save.dat")); Directory.Delete(staging);
}
string cwd = "/";
var commands = new List<string>();
var listed = await VitaDirectoryListing.Read("/ux0:/app", (path, ct) => { commands.Add("CWD " + path); cwd = path; return Task.CompletedTask; }, ct => {
    commands.Add("LIST");
    return Task.FromResult(new[] { new FtpListItem { Name = cwd == "/ux0:/app" ? "PCSE00665" : "os0:", Type = FtpObjectType.Directory } });
}, default);
Check(commands.SequenceEqual(new[] { "CWD /ux0:/app", "LIST" }) && listed[0].Name == "PCSE00665", "Navigate to ux0 app before plain LIST");
bool partitionRejected = false;
try { await VitaDirectoryListing.Read("/ux0:/app", (path, ct) => Task.CompletedTask, ct => Task.FromResult(new[] { new FtpListItem { Name = "os0:" } }), default); }
catch (IOException) { partitionRejected = true; }
Check(partitionRejected, "Root partition listing is never treated as games");
byte[] sfo = new byte[64];
void Put(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(sfo.AsSpan(offset, 4), value);
Put(0, 0x46535000); Put(8, 36); Put(12, 44); Put(16, 1); Put(24, 11);
BinaryPrimitives.WriteUInt16LittleEndian(sfo.AsSpan(22, 2), 0x0204);
Encoding.UTF8.GetBytes("TITLE\0").CopyTo(sfo, 36);
Encoding.UTF8.GetBytes("Test Game!\0").CopyTo(sfo, 44);
Check(SfoReader.ReadTitle(sfo) == "Test Game!", "SFO title");
var dlcSfo = (byte[])sfo.Clone();
var dlcRoot = Path.Combine(Path.GetTempPath(), "Vitransfer-dlc-" + Guid.NewGuid().ToString("N"));
try {
    foreach (var folder in new[] { "app/TEST", "addcont/TEST/NAMED/sce_sys", "addcont/TEST/MISSING", "reAddcont/TEST/MOD/sce_sys" }) Directory.CreateDirectory(Path.Combine(dlcRoot, folder));
    await File.WriteAllBytesAsync(Path.Combine(dlcRoot, "addcont/TEST/NAMED/sce_sys/param.sfo"), dlcSfo);
    await File.WriteAllBytesAsync(Path.Combine(dlcRoot, "reAddcont/TEST/MOD/sce_sys/param.sfo"), [1, 2, 3]);
    using var localDlc = new LocalConnection(dlcRoot);
    await localDlc.Connect(default);
    var entries = await localDlc.ReadDlcs("TEST", default);
    Check(entries.Count == 3 && entries.Single(entry => entry.Id == "NAMED").Name == "Test Game!", "Local DLC list reads SFO title");
    Check(entries.Where(entry => entry.Id != "NAMED").All(entry => entry.Name == entry.Id && !entry.HasSfoName), "Missing and invalid DLC SFOs retain folder IDs");
    Check(entries.Single(entry => entry.Id == "MOD").Location.StartsWith("reAddcont/"), "Modified DLC list preserves its location");
    using var dlcServer = new LimitedFtpServer();
    using var ftpDlc = new VitaConnection($"ftp://127.0.0.1:{dlcServer.Port}");
    using var dlcTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await ftpDlc.Connect(dlcTimeout.Token);
    await ftpDlc.Upload(Path.Combine(dlcRoot, "addcont/TEST/NAMED"), "/ux0:/addcont/TEST/NAMED", new[] { new RemoteEntry("sce_sys", true, 0), new RemoteEntry("sce_sys/param.sfo", false, dlcSfo.Length) }, new Progress<TransferUpdate>(), dlcTimeout.Token);
    var ftpEntries = await ftpDlc.ReadDlcs("TEST", dlcTimeout.Token);
    Check(ftpEntries.Single().Name == "Test Game!", "Vita-style FTP DLC list reads SFO title");
    Check((await ftpDlc.ReadDlcs("OTHER", dlcTimeout.Token)).Count == 0, "Missing DLC folders return an empty list");
} finally {
    if (!Path.GetFullPath(dlcRoot).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe test cleanup");
    Directory.Delete(dlcRoot, true);
}
Check(SfoReader.ReadTitle(new byte[3]) == null, "Truncated header");
Put(16, uint.MaxValue); Check(SfoReader.ReadTitle(sfo) == null, "Invalid index count");
Put(16, 1); Put(32, uint.MaxValue); Check(SfoReader.ReadTitle(sfo) == null, "Invalid value offset");
var root = Path.Combine(Path.GetTempPath(), "Vitransfer-tests");
Check(AppPaths.SafeLocal(root, "sce_sys/icon0.png").StartsWith(root), "Nested safe path");
foreach (var path in new[] { "../escape", "/absolute", "a/../../escape", "a:stream", "a./b", "a//b", "a\\b", "CON.txt", "a/LPT1" }) {
    bool rejected = false; try { AppPaths.SafeLocal(root, path); } catch (IOException) { rejected = true; }
    Check(rejected, "Reject " + path);
}
using var device = new VitaConnection("ftp://192.168.0.223:1337");
using var same = new VitaConnection("ftp://user:secret@192.168.0.223:1337");
Check(device.DeviceKey == same.DeviceKey, "Device identity ignores credentials");
Check(new Uri(same.ExplorerFolder("PCSE00001")).GetComponents(UriComponents.Path, UriFormat.Unescaped) == "ux0:/app/PCSE00001" && !same.ExplorerFolder("PCSE00001").Contains("secret"), "Explorer FTP location targets app folder without exposing credentials");
bool invalid = false; try { using var bad = new VitaConnection("https://example.com"); } catch (ArgumentException) { invalid = true; }
Check(invalid, "Reject non-FTP URL");
Console.WriteLine($"{passed} checks passed.");

var events = new List<string>();
var src = new FakeDevice("source", events);
var dst = new FakeDevice("destination", events);
src.Folders.Add("app/TEST"); src.Folders.Add("save/TEST");
src.Folders.Add("addcont/TEST"); src.Folders.Add("patch/TEST");
src.Folders.Add("rePatch/TEST");
var engine = new TransferEngine();
var games = new[] { new Game("TEST", "Test", null, true) };
var progress = new Progress<TransferUpdate>();
await engine.Transfer(src, dst, games, false, ConflictPolicy.Skip, progress, default);
Check(dst.Folders.SetEquals(new[] { "save/TEST" }), "Unchecked copies saves only");
events.Clear();
await engine.Transfer(src, dst, games, true, ConflictPolicy.Skip, progress, default);
Check(dst.Folders.SetEquals(new[] { "save/TEST", "app/TEST", "addcont/TEST", "patch/TEST", "rePatch/TEST" }), "Checked copies game, DLC, update and rePatch; existing save skipped");
Check(events.FindLastIndex(e => e.StartsWith("source:download:")) < events.FindIndex(e => e.StartsWith("destination:upload:")), "Stage all game, DLC and update folders before uploading");
events.Clear();
await engine.Transfer(src, dst, games, true, ConflictPolicy.ReplaceWithBackup, progress, default);
Check(new[] { "app/TEST", "addcont/TEST", "patch/TEST", "rePatch/TEST", "save/TEST" }.All(path => events.IndexOf("destination:download:" + path) < events.IndexOf("destination:delete:" + path)), "Replacement backs up each game, DLC, update, rePatch and save folder");
var noExtras = new FakeDevice("no-extras", events);
noExtras.Folders.Add("app/TEST");
var emptyDestination = new FakeDevice("empty", events);
await engine.Transfer(noExtras, emptyDestination, games, true, ConflictPolicy.Skip, progress, default);
Check(emptyDestination.Folders.SetEquals(new[] { "app/TEST" }), "Missing DLC, updates and saves are optional");
events.Clear();
await engine.Transfer(src, dst, games, false, ConflictPolicy.ReplaceWithBackup, progress, default);
Check(events.IndexOf("destination:download:save/TEST") < events.IndexOf("destination:delete:save/TEST"), "Backup precedes deletion");
events.Clear(); src.FailDownload = true;
bool failure = false;
try { await engine.Transfer(src, dst, games, true, ConflictPolicy.ReplaceWithBackup, progress, default); } catch (IOException) { failure = true; }
Check(failure && !events.Any(e => e.Contains(":delete:") || e.Contains(":upload:")), "Failed staging never modifies destination");
src.FailDownload = false; dst.FailDownload = true; events.Clear();
try { await engine.Transfer(src, dst, games, false, ConflictPolicy.ReplaceWithBackup, progress, default); } catch (IOException) { }
Check(!events.Any(e => e.Contains(":delete:") || e.Contains(":upload:")), "Failed backup never modifies destination");
var deletionEvents = new List<string>();
var invalidDeletionSource = new FakeDevice("delete-preflight", deletionEvents) { FailManifestRoot = "app/TEST" };
foreach (var folder in new[] { "app/TEST", "addcont/TEST", "patch/TEST", "rePatch/TEST", "save/TEST" }) invalidDeletionSource.Folders.Add(folder);
bool deletePreflightFailed = false;
try { await GameDeletion.Delete(invalidDeletionSource, "TEST", false, new Progress<string>(), default); } catch (IOException) { deletePreflightFailed = true; }
Check(deletePreflightFailed && invalidDeletionSource.Folders.Count == 5 && deletionEvents.Count == 0, "Deletion validates every related folder before modifying any content");
var localTestRoot = Path.Combine(Path.GetTempPath(), "Vitransfer-local-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(localTestRoot);
try {
    var sourcePath = Path.Combine(localTestRoot, "source");
    var destinationPath = Path.Combine(localTestRoot, "destination");
    var noAppPath = Path.Combine(localTestRoot, "empty");
    Directory.CreateDirectory(noAppPath); Directory.CreateDirectory(destinationPath);
    string title = "PCSE00001";
    var contents = new Dictionary<string, byte[]> {
        [$"app/{title}/data"] = [1, 2, 3],
        [$"app/{title}/sce_sys/icon0.png"] = validPng,
        [$"addcont/{title}/DLC/data"] = [4, 5, 6, 7],
        [$"patch/{title}/data"] = [8, 9],
        [$"rePatch/{title}/nested/data"] = [15, 16, 17, 18, 19],
        [$"user/00/savedata/{title}/data"] = [10, 11, 12, 13, 14]
    };
    foreach (var pair in contents) {
        var path = Path.Combine(sourcePath, pair.Key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllBytesAsync(path, pair.Value);
    }
    var localCache = new DeviceMetadataCache(Path.Combine(localTestRoot, "metadata"));
    using var localSource = new LocalConnection(sourcePath, metadataCache: localCache);
    using var localDestination = new LocalConnection(destinationPath, metadataCache: localCache);
    using var empty = new LocalConnection(noAppPath, metadataCache: localCache);
    using var routed = DeviceConnection.Create(sourcePath);
    Check(routed is LocalConnection, "Absolute folder address routes to local handling");
    using var routedFtp = DeviceConnection.Create("ftp://127.0.0.1:1337");
    Check(routedFtp is VitaConnection, "ftp:// address retains FTP handling");
    await localSource.Connect(default); await localDestination.Connect(default); await empty.Connect(default);
    Check((await empty.Scan(new Progress<string>(), default)).Count == 0, "Existing local folder without app displays no games");
    var localGames = await localSource.Scan(new Progress<string>(), default);
    Check(localGames.Single().Detail == title + " · Save / DLC / Update / Patch" && localGames.Single().IconPath == Path.Combine(IconArtwork.SharedCache(title), "icon0.png"), "Local scan discovers content and uses shared title artwork");
    Check(localGames.Single().SizeBytes == null, "Local scan does not calculate sizes automatically");
    await localSource.CalculateSizes(localGames, new Progress<string>(), default);
    Check(localSource.CachedMetadata.Apply(localGames.Single()).SizeBytes == contents.Where(pair => !pair.Key.StartsWith("user/")).Sum(pair => pair.Value.Length), "Local size includes app, DLC and updates, excluding saves");
    using var sameLocal = new LocalConnection(sourcePath + Path.DirectorySeparatorChar);
    Check(sameLocal.DeviceKey == localSource.DeviceKey && localDestination.DeviceKey != localSource.DeviceKey, "Local cache identity normalizes paths and separates roots");
    var stagingRoot = Path.Combine(AppPaths.Root, "Staging");
    var stagingBefore = Directory.Exists(stagingRoot) ? Directory.GetDirectories(stagingRoot).ToHashSet() : [];
    await engine.Transfer(localSource, localDestination, localGames, false, ConflictPolicy.Skip, progress, default);
    Check(File.ReadAllBytes(Path.Combine(destinationPath, "user", "00", "savedata", title, "data")).SequenceEqual(contents[$"user/00/savedata/{title}/data"]) && !Directory.Exists(localDestination.AppRoot), "Direct local save-only copy does not copy base game");
    await engine.Transfer(localSource, localDestination, localGames, true, ConflictPolicy.Skip, progress, default);
    Check(contents.All(pair => File.ReadAllBytes(Path.Combine(destinationPath, pair.Key.Replace('/', Path.DirectorySeparatorChar))).SequenceEqual(pair.Value)), "Direct local copy preserves game, DLC, update and save bytes");
    await File.WriteAllBytesAsync(Path.Combine(localDestination.AppRoot, title, "data"), [99]);
    string replaced = await engine.Transfer(localSource, localDestination, localGames, true, ConflictPolicy.ReplaceWithBackup, progress, default);
    Check(replaced.Contains("Backups:") && File.ReadAllBytes(Path.Combine(localDestination.AppRoot, title, "data")).SequenceEqual(new byte[] { 1, 2, 3 }), "Local replacement backs up and restores source content");
    using (var localFtpServer = new LimitedFtpServer()) {
        using var ftpDestination = new VitaConnection($"ftp://127.0.0.1:{localFtpServer.Port}");
        await ftpDestination.Connect(default);
        await engine.Transfer(localSource, ftpDestination, localGames, true, ConflictPolicy.Skip, progress, default);
        Check(localFtpServer.Files[$"/ux0:/app/{title}/data"].SequenceEqual(new byte[] { 1, 2, 3 }) && localFtpServer.Files[$"/ux0:/addcont/{title}/DLC/data"].Length == 4, "Local files upload directly through real FTP");
        await GameDeletion.Delete(ftpDestination, title, true, new Progress<string>(), default);
        Check(localFtpServer.Files.Keys.All(path => !path.StartsWith("/ux0:/app/" + title) && !path.StartsWith("/ux0:/addcont/" + title) && !path.StartsWith("/ux0:/patch/" + title) && !path.StartsWith("/ux0:/rePatch/" + title)) && localFtpServer.Files.ContainsKey($"/ux0:/user/00/savedata/{title}/data"), "FTP Keep saves removes game, DLC, updates and rePatch but retains saves");
        await GameDeletion.Delete(ftpDestination, title, false, new Progress<string>(), default);
        Check(!localFtpServer.Files.ContainsKey($"/ux0:/user/00/savedata/{title}/data"), "FTP Delete also removes remaining save folder");
    }
    var fakeRemote = new FakeDevice("remote-download", events);
    fakeRemote.Folders.Add("app/TEST");
    await engine.Transfer(fakeRemote, empty, games, true, ConflictPolicy.Skip, progress, default);
    Check(fakeRemote.DownloadedLocalPaths.Single() == empty.AppRoot + "/TEST" && File.ReadAllBytes(Path.Combine(empty.AppRoot, "TEST", "data")).SequenceEqual(new byte[] { 1, 2, 3 }), "Remote files download directly into selected local root");
    Check(stagingBefore.SetEquals(Directory.Exists(stagingRoot) ? Directory.GetDirectories(stagingRoot) : []), "Local transfers create no staging folders or file copies");
    bool overlapRejected = false;
    using var overlapping = new LocalConnection(Path.Combine(sourcePath, "nested-root"));
    try { await engine.Transfer(localSource, overlapping, localGames, true, ConflictPolicy.Skip, progress, default); } catch (IOException) { overlapRejected = true; }
    Check(overlapRejected, "Reject overlapping local roots before modification");
    bool outsideRejected = false;
    try { await localSource.Delete(destinationPath, default); } catch (IOException) { outsideRejected = true; }
    Check(outsideRejected && Directory.Exists(destinationPath), "Local deletion cannot escape selected root");
    Check(localDestination.ExplorerFolder(title) == Path.Combine(localDestination.AppRoot, title), "Local Explorer location targets the selected base-game folder");
    foreach (var folder in new[] { localDestination.AppRoot, localDestination.AddcontRoot, localDestination.PatchRoot, localDestination.RePatchRoot, localDestination.SaveRoot }) {
        Directory.CreateDirectory(Path.Combine(folder, "OTHER"));
        await File.WriteAllBytesAsync(Path.Combine(folder, "OTHER", "data"), [42]);
    }
    await localDestination.CalculateSizes(localGames, new Progress<string>(), default);
    await GameDeletion.Delete(localDestination, title, true, new Progress<string>(), default);
    Check(new[] { localDestination.AppRoot, localDestination.AddcontRoot, localDestination.PatchRoot, localDestination.RePatchRoot }.All(folder => !Directory.Exists(Path.Combine(folder, title))) && File.ReadAllBytes(Path.Combine(localDestination.SaveRoot, title, "data")).SequenceEqual(contents[$"user/00/savedata/{title}/data"]), "Local Keep saves preserves save bytes while removing related content");
    Check(new[] { localDestination.AppRoot, localDestination.AddcontRoot, localDestination.PatchRoot, localDestination.RePatchRoot, localDestination.SaveRoot }.All(folder => File.ReadAllBytes(Path.Combine(folder, "OTHER", "data")).SequenceEqual(new byte[] { 42 })), "Deletion leaves other titles untouched across every folder");
    Check(!localDestination.CachedMetadata.Games.ContainsKey(title) && localDestination.CachedMetadata.Storage == null, "Deletion invalidates cached game size and device capacity");
    await GameDeletion.Delete(localDestination, title, false, new Progress<string>(), default);
    Check(!Directory.Exists(Path.Combine(localDestination.SaveRoot, title)), "Local Delete removes saves even when base game is already absent");
    bool invalidTitleRejected = false;
    try { await GameDeletion.Delete(localDestination, "../OTHER", false, new Progress<string>(), default); } catch (IOException) { invalidTitleRejected = true; }
    Check(invalidTitleRejected && Directory.Exists(Path.Combine(localDestination.AppRoot, "OTHER")), "Deletion rejects a title ID that escapes its folder");
    using var cancelDelete = new CancellationTokenSource(); cancelDelete.Cancel();
    bool deleteCancelled = false;
    try { await GameDeletion.Delete(localDestination, "OTHER", false, new Progress<string>(), cancelDelete.Token); } catch (OperationCanceledException) { deleteCancelled = true; }
    Check(deleteCancelled && Directory.Exists(Path.Combine(localDestination.AppRoot, "OTHER")), "Cancellation before deletion leaves all folders intact");
} finally {
    if (!Path.GetFullPath(localTestRoot).StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe test cleanup path");
    Directory.Delete(localTestRoot, recursive: true);
}
Console.WriteLine($"Total: {passed} checks passed. Test jobs/backups are retained under LocalAppData for recovery inspection.");

sealed class FakeDevice(string key, List<string> events) : IVitaConnection {
    public string AppRoot => "app";
    public string SaveRoot => "save";
    public string AddcontRoot => "addcont";
    public string PatchRoot => "patch";
    public string RePatchRoot => "rePatch";
    public string DeviceKey => key;
    public HashSet<string> Folders { get; } = [];
    public bool FailDownload { get; set; }
    public string? FailManifestRoot { get; set; }
    public List<string> DownloadedLocalPaths { get; } = [];
    public Task<bool> Exists(string path, CancellationToken token) => Task.FromResult(Folders.Contains(path));
    public Task<List<RemoteEntry>> Manifest(string root, CancellationToken token) => root == FailManifestRoot
        ? Task.FromException<List<RemoteEntry>>(new IOException("Simulated unreadable directory"))
        : Task.FromResult(new List<RemoteEntry> { new("data", false, 3) });
    public async Task Download(string remote, string local, IReadOnlyList<RemoteEntry> entries, IProgress<TransferUpdate> progress, CancellationToken token) {
        events.Add(key + ":download:" + remote);
        DownloadedLocalPaths.Add(local);
        if (FailDownload) throw new IOException("Simulated disconnect");
        Directory.CreateDirectory(local);
        await File.WriteAllBytesAsync(Path.Combine(local, "data"), new byte[] { 1, 2, 3 }, token);
    }
    public Task Upload(string local, string remote, IReadOnlyList<RemoteEntry> entries, IProgress<TransferUpdate> progress, CancellationToken token) {
        events.Add(key + ":upload:" + remote); Folders.Add(remote); return Task.CompletedTask;
    }
    public Task Delete(string path, CancellationToken token) { events.Add(key + ":delete:" + path); Folders.Remove(path); return Task.CompletedTask; }
}
