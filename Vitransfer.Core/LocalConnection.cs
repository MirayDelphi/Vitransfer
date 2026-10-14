using System.Security.Cryptography;
using System.Text;

namespace Vitransfer.Core;

public sealed class LocalConnection : IDeviceConnection {
    private readonly Func<string, bool> readableIcon;
    private readonly DeviceMetadataCache metadataCache;
    public string Root { get; }
    public string AppRoot => Path.Combine(Root, "app");
    public string SaveRoot => Path.Combine(Root, "user", "00", "savedata");
    public string AddcontRoot => Path.Combine(Root, "addcont");
    public string PatchRoot => Path.Combine(Root, "patch");
    public string RePatchRoot => Path.Combine(Root, "rePatch");
    public string DeviceKey { get; }
    public bool IsConnected { get; private set; }
    public DeviceMetadata CachedMetadata => metadataCache.Load(DeviceKey);
    public string ExplorerFolder(string titleId) => Checked(AppPaths.SafeLocal(AppRoot, titleId));
    public void ForgetGameMetadata(string titleId) => metadataCache.ForgetGame(DeviceKey, titleId);

    public LocalConnection(string path, Func<string, bool>? isReadableIcon = null, DeviceMetadataCache? metadataCache = null) {
        if (!Path.IsPathFullyQualified(path) || path.Contains("://")) throw new ArgumentException("Enter ftp://host:port or an absolute local folder path.");
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        DeviceKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("local:" + Root.ToUpperInvariant())))[..16];
        readableIcon = isReadableIcon ?? IconArtwork.HasPngSignature;
        this.metadataCache = metadataCache ?? new DeviceMetadataCache();
    }
    private string Checked(string path) {
        string full = Path.GetFullPath(path);
        if (!full.Equals(Root, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Local path escapes the selected folder.");
        // Do not follow junctions or symbolic links, including ancestors of Root.
        for (string? current = full; current != null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Local symbolic links and junctions are not supported: " + current);
        return full;
    }
    public Task Connect(CancellationToken token) {
        token.ThrowIfCancellationRequested();
        if (!Directory.Exists(Checked(Root))) throw new DirectoryNotFoundException("Local folder not found: " + Root);
        IsConnected = true;
        return Task.CompletedTask;
    }
    public Task<bool> Exists(string path, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(Directory.Exists(Checked(path)));
    }
    public Task<List<Game>> Scan(IProgress<string> status, CancellationToken token) => Task.Run(async () => {
        if (!Directory.Exists(Checked(AppRoot))) return new List<Game>();
        HashSet<string> Titles(string root) => Directory.Exists(Checked(root))
            ? Directory.EnumerateDirectories(root).Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0).Select(path => Path.GetFileName(path)!).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        var saves = Titles(SaveRoot); var dlcs = Titles(AddcontRoot); var updates = Titles(PatchRoot);
        dlcs.UnionWith(Titles(Path.Combine(Root, "reAddcont")));
        var patches = Titles(RePatchRoot);
        var metadata = CachedMetadata;
        var result = new List<Game>();
        foreach (var id in Titles(AppRoot)) {
            token.ThrowIfCancellationRequested();
            status.Report("Reading " + id + "…");
            string folder = Checked(Path.Combine(AppRoot, id));
            string name = metadata.Games.TryGetValue(id, out var saved) ? saved.Name : id;
            try {
                var sfo = Checked(Path.Combine(folder, "sce_sys", "param.sfo"));
                if (File.Exists(sfo)) name = SfoReader.ReadTitle(await File.ReadAllBytesAsync(sfo, token)) ?? name;
            } catch (OperationCanceledException) { throw; } catch { }
            string? icon = null;
            try {
                string candidate = Path.Combine(folder, "sce_sys", "icon0.png");
                icon = await IconArtwork.ResolveShared(new[] { candidate }, IconArtwork.SharedCache(id),
                    async (source, target, ct) => {
                        await using var input = File.OpenRead(Checked(source));
                        await using var output = File.Create(target);
                        await input.CopyToAsync(output, ct); return true;
                    }, readableIcon, token);
            } catch (OperationCanceledException) { throw; } catch { }
            result.Add(metadata.Apply(new Game(id, string.IsNullOrWhiteSpace(name) ? id : name, icon, saves.Contains(id), HasDlc: dlcs.Contains(id), HasUpdate: updates.Contains(id), HasPatch: patches.Contains(id))));
        }
        return result.OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }, token);

    public Task<List<DlcEntry>> ReadDlcs(string titleId, CancellationToken token) => Task.Run(async () => {
        var result = new List<DlcEntry>();
        foreach (var kind in new[] { "addcont", "reAddcont" }) {
            var folder = Checked(AppPaths.SafeLocal(Path.Combine(Root, kind), titleId));
            if (!Directory.Exists(folder)) continue;
            foreach (var path in Directory.EnumerateDirectories(folder)) {
                token.ThrowIfCancellationRequested();
                if (Path.GetFileName(path).Equals("sce_pfs", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Equals("sce_sys", StringComparison.OrdinalIgnoreCase)) continue;
                Checked(path);
                string id = Path.GetFileName(path), location = kind + "/" + titleId + "/" + id;
                string? name = null, contentId = null;
                try {
                    var sfo = Checked(Path.Combine(path, "sce_sys", "param.sfo"));
                    if (File.Exists(sfo)) {
                        var bytes = await File.ReadAllBytesAsync(sfo, token);
                        name = SfoReader.ReadTitle(bytes); contentId = SfoReader.ReadString(bytes, "CONTENT_ID");
                    }
                } catch (OperationCanceledException) { throw; } catch (IOException) { } catch (UnauthorizedAccessException) { }
                result.Add(new(id, name ?? id, location, contentId, name != null));
            }
        }
        return result.OrderBy(dlc => dlc.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }, token);

    public Task<List<RemoteEntry>> Manifest(string root, CancellationToken token) => Task.Run(() => {
        root = Checked(root);
        var entries = new List<RemoteEntry>();
        void Walk(string folder) {
            token.ThrowIfCancellationRequested();
            foreach (var path in Directory.EnumerateFileSystemEntries(Checked(folder))) {
                string full = Checked(path);
                string relative = Path.GetRelativePath(root, full).Replace('\\', '/');
                if (relative.Count(character => character == '/') > 64) throw new IOException("Local directory nesting is too deep.");
                _ = AppPaths.SafeLocal(root, relative);
                bool directory = Directory.Exists(full);
                entries.Add(new(relative, directory, directory ? 0 : new FileInfo(full).Length));
                if (directory) Walk(full);
            }
        }
        Walk(root);
        if (entries.Select(entry => entry.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
            throw new IOException("Local names collide on Windows.");
        return entries;
    }, token);

    public async Task CalculateSizes(IReadOnlyList<Game> games, IProgress<string> status, CancellationToken token) {
        var metadata = CachedMetadata;
        metadata.Address = Root;
        try {
            var drive = new DriveInfo(Path.GetPathRoot(Root)!);
            long capacity = drive.TotalSize, free = drive.AvailableFreeSpace;
            metadata.Storage = new(capacity, capacity - free, free);
        } catch { metadata.Storage = new(); }
        metadata.StorageUpdatedUtc = DateTimeOffset.UtcNow;
        metadataCache.Save(metadata);
        for (int i = 0; i < games.Count; i++) {
            token.ThrowIfCancellationRequested();
            var game = games[i];
            status.Report($"Calculating size ({i + 1}/{games.Count}): {game.Name}…");
            long? size = null;
            try {
                long total = 0;
                foreach (var root in new[] { AppRoot, AddcontRoot, PatchRoot, RePatchRoot }) {
                    string folder = Path.Combine(root, game.Id);
                    if (root == AppRoot || await Exists(folder, token)) total = checked(total + (await Manifest(folder, token)).Sum(entry => entry.Size));
                }
                size = total;
            } catch (OperationCanceledException) { throw; } catch { }
            metadata.Games[game.Id] = new(game.Name, size, DateTimeOffset.UtcNow);
            metadataCache.Save(metadata);
        }
    }

    private static async Task CopyFiles(string source, string destination, IReadOnlyList<RemoteEntry> entries, IProgress<TransferUpdate> progress, CancellationToken token) {
        Directory.CreateDirectory(destination);
        foreach (var entry in entries.Where(entry => entry.IsDirectory)) {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(AppPaths.SafeLocal(destination, entry.RelativePath));
        }
        long total = entries.Sum(entry => entry.Size), copied = 0;
        foreach (var entry in entries.Where(entry => !entry.IsDirectory)) {
            token.ThrowIfCancellationRequested();
            string from = AppPaths.SafeLocal(source, entry.RelativePath), to = AppPaths.SafeLocal(destination, entry.RelativePath);
            if ((File.GetAttributes(from) & FileAttributes.ReparsePoint) != 0) throw new IOException("Source became a symbolic link.");
            await using (var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            await using (var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await input.CopyToAsync(output, token);
            if (new FileInfo(to).Length != entry.Size) throw new IOException("Copy verification failed: " + entry.RelativePath);
            copied += entry.Size;
            progress.Report(new("Copying " + entry.RelativePath, total == 0 ? 100 : copied * 100d / total));
        }
    }
    public async Task Download(string remote, string local, IReadOnlyList<RemoteEntry> entries, IProgress<TransferUpdate> progress, CancellationToken token) {
        foreach (var entry in entries) Checked(AppPaths.SafeLocal(remote, entry.RelativePath));
        await CopyFiles(Checked(remote), local, entries, progress, token);
    }
    public async Task Upload(string local, string remote, IReadOnlyList<RemoteEntry> entries, IProgress<TransferUpdate> progress, CancellationToken token) {
        foreach (var entry in entries) Checked(AppPaths.SafeLocal(remote, entry.RelativePath));
        await CopyFiles(local, Checked(remote), entries, progress, token);
        if (!(await Manifest(remote, token)).OrderBy(entry => entry.RelativePath, StringComparer.Ordinal).SequenceEqual(entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)))
            throw new IOException("Destination manifest does not match source.");
    }
    internal void ValidateDestination(string path, IReadOnlyList<RemoteEntry> entries) {
        Checked(path);
        foreach (var entry in entries) Checked(AppPaths.SafeLocal(path, entry.RelativePath));
    }
    public async Task Delete(string path, CancellationToken token) {
        path = Checked(path);
        if (path.Equals(Root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Cannot delete the selected local root.");
        var entries = await Manifest(path, token);
        foreach (var entry in entries.Where(entry => !entry.IsDirectory)) { token.ThrowIfCancellationRequested(); File.Delete(Checked(AppPaths.SafeLocal(path, entry.RelativePath))); }
        foreach (var entry in entries.Where(entry => entry.IsDirectory).OrderByDescending(entry => entry.RelativePath.Count(c => c == '/'))) {
            token.ThrowIfCancellationRequested(); Directory.Delete(Checked(AppPaths.SafeLocal(path, entry.RelativePath)));
        }
        token.ThrowIfCancellationRequested(); Directory.Delete(Checked(path));
    }
    public void Dispose() => IsConnected = false;
}
