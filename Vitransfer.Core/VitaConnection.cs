using System.Security.Cryptography;
using System.Text;
using FluentFTP;

namespace Vitransfer.Core;

public sealed class VitaConnection : IDeviceConnection {
    private readonly AsyncFtpClient client;
    private string lastCommand = "unknown";
    private readonly Func<string, bool> isReadableIcon;
    private readonly DeviceMetadataCache metadataCache;
    private readonly string deviceAddress;
    private readonly Uri explorerAddress;
    public DeviceMetadata CachedMetadata => metadataCache.Load(DeviceKey);
    public string AppRoot { get; private set; } = "/ux0:/app";
    public string SaveRoot { get; private set; } = "/ux0:/user/00/savedata";
    public string AddcontRoot => AppRoot[..^3] + "addcont";
    public string PatchRoot => AppRoot[..^3] + "patch";
    public string RePatchRoot => AppRoot[..^3] + "rePatch";
    public string DeviceKey { get; }
    public bool IsConnected => client.IsConnected;

    public VitaConnection(string address, Func<string, bool>? isReadableIcon = null, DeviceMetadataCache? metadataCache = null) {
        this.metadataCache = metadataCache ?? new DeviceMetadataCache();
        this.isReadableIcon = isReadableIcon ?? IconArtwork.HasPngSignature;
        if (!Uri.TryCreate(address.Contains("://") ? address : "ftp://" + address, UriKind.Absolute, out var uri) || uri.Scheme != "ftp" || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("Enter an FTP address, for example ftp://192.168.0.223:1337.");
        if (uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Enter the FTP server address without a folder path.");
        var credentials = uri.UserInfo.Split(':', 2);
        client = new AsyncFtpClient(uri.Host, credentials[0].Length == 0 ? "anonymous" : Uri.UnescapeDataString(credentials[0]), credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "anonymous@", uri.Port);
        client.Config.ConnectTimeout = 10000;
        client.Config.ReadTimeout = 15000;
        client.Config.DataConnectionConnectTimeout = 10000;
        client.Config.DataConnectionReadTimeout = 30000;
        client.Config.DataConnectionType = FtpDataConnectionType.PASV;
        client.Config.Noop = false;
        client.LegacyLogger = (_, message) => {
            // Keep only the command verb; never retain credentials or arguments.
            const string prefix = "Command: ";
            if (message.StartsWith(prefix, StringComparison.Ordinal))
                lastCommand = message[prefix.Length..].Split(' ', 2)[0];
        };
        DeviceKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{uri.Host.ToLowerInvariant()}:{uri.Port}")))[..16];
        deviceAddress = $"{uri.Host.ToLowerInvariant()}:{uri.Port}";
        explorerAddress = new UriBuilder(uri) { UserName = "", Password = "" }.Uri;
    }
    public string ExplorerFolder(string titleId) {
        _ = AppPaths.SafeLocal(Path.Combine(AppPaths.Root, "Validation"), titleId);
        return new UriBuilder(explorerAddress) { Path = AppRoot.TrimStart('/') + "/" + Uri.EscapeDataString(titleId) }.Uri.AbsoluteUri;
    }
    public void ForgetGameMetadata(string titleId) => metadataCache.ForgetGame(DeviceKey, titleId);

    public async Task Connect(CancellationToken token) {
        await client.Connect(token);
        foreach (var root in new[] { "/ux0:/app", "ux0:/app", "/ux0:app", "ux0:app" }) {
            try { await ListDirectory(root, token); }
            catch (FluentFTP.Exceptions.FtpCommandException) { continue; }
            AppRoot = root;
            SaveRoot = root[..^3] + "user/00/savedata";
            return;
        }
        throw new IOException("Connected, but ux0:app was not found. Check VitaShell and the mounted storage.");
    }

    public async Task<List<Game>> Scan(IProgress<string> status, CancellationToken token) {
        var metadata = CachedMetadata;
        async Task<HashSet<string>> TitlesUnder(string root) {
            try {
                return await Exists(root, token)
                    ? (await ListDirectory(root, token)).Where(x => x.Type == FtpObjectType.Directory).Select(x => x.Name).ToHashSet(StringComparer.Ordinal)
                    : [];
            } catch (FluentFTP.Exceptions.FtpCommandException) { return []; }
        }
        var saves = await TitlesUnder(SaveRoot);
        var dlcs = await TitlesUnder(AddcontRoot);
        dlcs.UnionWith(await TitlesUnder(AppRoot[..^3] + "reAddcont"));
        var updates = await TitlesUnder(PatchRoot);
        var patches = await TitlesUnder(RePatchRoot);
        var result = new List<Game>();
        foreach (var directory in (await ListDirectory(AppRoot, token)).Where(x => x.Type == FtpObjectType.Directory && x.Name is not "." and not "..")) {
            token.ThrowIfCancellationRequested();
            var id = directory.Name;
            var cache = AppPaths.SafeLocal(Path.Combine(AppPaths.Root, "Cache", DeviceKey), id);
            Directory.CreateDirectory(cache);
            status.Report($"Reading {id}…");
            string name = metadata.Games.TryGetValue(id, out var saved) ? saved.Name : id;
            string? icon = null;
            try {
                var sfo = Path.Combine(cache, "param.sfo");
                if (await client.DownloadFile(sfo, $"{AppRoot}/{id}/sce_sys/param.sfo", FtpLocalExists.Overwrite, token: token) == FtpStatus.Success)
                    name = SfoReader.ReadTitle(await File.ReadAllBytesAsync(sfo, token)) ?? name;
            } catch (OperationCanceledException) { throw; } catch { /* Metadata is optional. */ }
            var artworkRoot = AppRoot[..^3].Replace("ux0:", "ur0:") + "appmeta";
            icon = await IconArtwork.ResolveShared(new[] { $"{artworkRoot}/{id}/icon0.png", $"{AppRoot}/{id}/sce_sys/icon0.png" }, IconArtwork.SharedCache(id),
                async (remote, local, ct) => await client.DownloadFile(local, remote, FtpLocalExists.Overwrite, token: ct) == FtpStatus.Success,
                isReadableIcon, token);
            result.Add(metadata.Apply(new Game(id, string.IsNullOrWhiteSpace(name) ? id : name, icon, saves.Contains(id),
                HasDlc: dlcs.Contains(id), HasUpdate: updates.Contains(id), HasPatch: patches.Contains(id))));
        }
        return result.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<List<DlcEntry>> ReadDlcs(string titleId, CancellationToken token) {
        var cache = AppPaths.SafeLocal(Path.Combine(AppPaths.Root, "Cache", DeviceKey, "Dlc"), titleId);
        Directory.CreateDirectory(cache);
        var result = new List<DlcEntry>();
        foreach (var kind in new[] { "addcont", "reAddcont" }) {
            string folder = AppRoot[..^3] + kind + "/" + titleId;
            if (!await Exists(folder, token)) continue;
            foreach (var item in (await ListDirectory(folder, token)).Where(item => item.Type == FtpObjectType.Directory && item.Name is not "." and not "..")) {
                token.ThrowIfCancellationRequested();
                if (item.Name.Equals("sce_pfs", StringComparison.OrdinalIgnoreCase) || item.Name.Equals("sce_sys", StringComparison.OrdinalIgnoreCase)) continue;
                var localFolder = AppPaths.SafeLocal(Path.Combine(cache, kind), item.Name);
                Directory.CreateDirectory(localFolder);
                string? name = null, contentId = null;
                try {
                    string sfo = Path.Combine(localFolder, "param.sfo");
                    if (await client.DownloadFile(sfo, folder + "/" + item.Name + "/sce_sys/param.sfo", FtpLocalExists.Overwrite, token: token) == FtpStatus.Success) {
                        var bytes = await File.ReadAllBytesAsync(sfo, token);
                        name = SfoReader.ReadTitle(bytes); contentId = SfoReader.ReadString(bytes, "CONTENT_ID");
                    }
                } catch (OperationCanceledException) { throw; } catch { /* DLC metadata is optional. */ }
                result.Add(new(item.Name, name ?? item.Name, kind + "/" + titleId + "/" + item.Name, contentId, name != null));
            }
        }
        return result.OrderBy(dlc => dlc.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    public Task<bool> Exists(string path, CancellationToken token) => client.DirectoryExists(path, token);
    public async Task CalculateSizes(IReadOnlyList<Game> games, IProgress<string> status, CancellationToken token) {
        var metadata = CachedMetadata;
        metadata.Address = deviceAddress;
        status.Report("Reading device storage…");
        metadata.Storage = await ReadStorage(token);
        metadata.StorageUpdatedUtc = DateTimeOffset.UtcNow;
        metadataCache.Save(metadata);
        for (int i = 0; i < games.Count; i++) {
            token.ThrowIfCancellationRequested();
            var game = games[i];
            status.Report($"Calculating size ({i + 1}/{games.Count}): {game.Name}…");
            long? size = null;
            try {
                size = (await Manifest($"{AppRoot}/{game.Id}", token)).Where(e => !e.IsDirectory).Sum(e => e.Size);
                foreach (var root in new[] { AddcontRoot, PatchRoot, RePatchRoot }) {
                    string path = $"{root}/{game.Id}";
                    if (await Exists(path, token))
                        size = checked(size + (await Manifest(path, token)).Where(e => !e.IsDirectory).Sum(e => e.Size));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { size = null; /* Do not display an incomplete app/DLC/update total. */ }
            metadata.Games[game.Id] = new(game.Name, size, DateTimeOffset.UtcNow);
            // Save each completed game so cancelling a long scan retains its progress.
            metadataCache.Save(metadata);
        }
    }
    public async Task<StorageSpace> ReadStorage(CancellationToken token) {
        var partition = AppRoot[..^3].TrimEnd('/');
        StorageSpace? partial = null;
        foreach (var command in new[] { "STAT " + partition, "SITE DF " + partition, "AVBL " + partition }) {
            token.ThrowIfCancellationRequested();
            try {
                var reply = await client.Execute(command, token);
                if (!reply.Success) continue;
                var space = StorageSpace.Parse(reply.InfoMessages + "\n" + reply.Message);
                if (command.StartsWith("AVBL ") && space == null && long.TryParse(reply.Message.Trim(), out var available) && available >= 0)
                    space = new StorageSpace(FreeBytes: available);
                if (space?.TotalBytes != null && space.UsedBytes != null) return space;
                partial ??= space;
            } catch (OperationCanceledException) { throw; }
            catch { /* Optional capacity commands are unsupported on many Vita servers. */ }
        }
        return partial ?? new StorageSpace();
    }
    private Task<FtpListItem[]> ListDirectory(string path, CancellationToken token) => VitaDirectoryListing.Read(path,
        (directory, ct) => client.SetWorkingDirectory(directory, ct),
        ct => client.GetListing(null!, FtpListOption.ForceList | FtpListOption.NoPath, ct), token);
    public async Task<List<RemoteEntry>> Manifest(string root, CancellationToken token) {
        var entries = new List<RemoteEntry>();
        async Task Walk(string relative) {
            token.ThrowIfCancellationRequested();
            if (relative.Count(c => c == '/') > 64) throw new IOException("Remote directory nesting is too deep.");
            foreach (var item in await ListDirectory(relative.Length == 0 ? root : root + "/" + relative, token)) {
                if (item.Name is "." or "..") continue;
                var path = relative.Length == 0 ? item.Name : relative + "/" + item.Name;
                _ = AppPaths.SafeLocal(Path.Combine(AppPaths.Root, "Validation"), path);
                if (item.Type == FtpObjectType.Directory) { entries.Add(new(path, true, 0)); await Walk(path); }
                else if (item.Type == FtpObjectType.File) {
                    var size = item.Size >= 0 ? item.Size : await client.GetFileSize(root + "/" + path, token: token);
                    if (size < 0) throw new IOException($"Cannot determine size of {path}.");
                    entries.Add(new(path, false, size));
                } else throw new IOException($"Unsupported remote entry: {path}");
            }
        }
        await Walk("");
        if (entries.Select(e => e.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
            throw new IOException("Remote names collide on Windows; this folder cannot be safely staged.");
        return entries;
    }

    public async Task Download(string remote, string local, IReadOnlyList<RemoteEntry> entries, IProgress<TransferUpdate> progress, CancellationToken token) {
        Directory.CreateDirectory(local);
        foreach (var entry in entries.Where(e => e.IsDirectory)) Directory.CreateDirectory(AppPaths.SafeLocal(local, entry.RelativePath));
        long total = entries.Sum(e => e.Size), complete = 0;
        foreach (var entry in entries.Where(e => !e.IsDirectory)) {
            var target = AppPaths.SafeLocal(local, entry.RelativePath);
            var baseline = complete;
            var report = new Progress<FtpProgress>(p => progress.Report(new($"Downloading {entry.RelativePath}", total == 0 ? 0 : 100d * (baseline + entry.Size * p.Progress / 100) / total)));
            if (await client.DownloadFile(target, remote + "/" + entry.RelativePath, FtpLocalExists.Overwrite, progress: report, token: token) != FtpStatus.Success || new FileInfo(target).Length != entry.Size)
                throw new IOException($"Download verification failed: {entry.RelativePath}");
            complete += entry.Size;
        }
        progress.Report(new("Download verified by file sizes", 100));
    }

    public async Task Upload(string local, string remote, IReadOnlyList<RemoteEntry> entries, IProgress<TransferUpdate> progress, CancellationToken token) {
        try {
        await client.CreateDirectory(remote, token);
        foreach (var entry in entries.Where(e => e.IsDirectory)) await client.CreateDirectory(remote + "/" + entry.RelativePath, token);
        long total = entries.Sum(e => e.Size), complete = 0;
        foreach (var entry in entries.Where(e => !e.IsDirectory)) {
            var baseline = complete;
            var report = new Progress<FtpProgress>(p => progress.Report(new($"Uploading {entry.RelativePath}", total == 0 ? 0 : 100d * (baseline + entry.Size * p.Progress / 100) / total)));
            // The engine already creates a fresh destination folder (after backing
            // up and deleting an existing one). Avoid FileExists, which falls back
            // to NLST on servers without SIZE/MDTM, and redundant MKD probes.
            if (await client.UploadFile(AppPaths.SafeLocal(local, entry.RelativePath), remote + "/" + entry.RelativePath, FtpRemoteExists.NoCheck, false, progress: report, token: token) != FtpStatus.Success)
                throw new IOException($"Upload failed: {entry.RelativePath}");
            complete += entry.Size;
        }
        var actual = await Manifest(remote, token);
        var expected = entries.OrderBy(e => e.RelativePath, StringComparer.Ordinal).ToArray();
        if (!expected.SequenceEqual(actual.OrderBy(e => e.RelativePath, StringComparer.Ordinal))) throw new IOException("Destination manifest does not match source.");
        progress.Report(new("Upload verified by file counts and sizes", 100));
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            throw new IOException($"Upload to {remote} failed (last FTP command: {lastCommand}): {ex.GetBaseException().Message}", ex);
        }
    }
    public async Task Delete(string path, CancellationToken token) {
        // Avoid the library's recursive delete: its internal LIST path commands
        // have the same compatibility issue as game discovery on Vita servers.
        var entries = await Manifest(path, token);
        await client.SetWorkingDirectory(path[..path.LastIndexOf('/')], token);
        foreach (var entry in entries.Where(e => !e.IsDirectory))
            await client.DeleteFile(path + "/" + entry.RelativePath, token);
        foreach (var entry in entries.Where(e => e.IsDirectory).OrderByDescending(e => e.RelativePath.Count(c => c == '/'))) {
            var reply = await client.Execute("RMD " + path + "/" + entry.RelativePath, token);
            if (!reply.Success) throw new IOException($"Cannot remove directory {entry.RelativePath}: {reply.Message}");
        }
        var final = await client.Execute("RMD " + path, token);
        if (!final.Success) throw new IOException($"Cannot remove directory {path}: {final.Message}");
    }
    public void Dispose() => client.Dispose();
}
