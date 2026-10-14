namespace Vitransfer.Core;

public static class IconArtwork {
    public static string SharedCache(string titleId) => AppPaths.SafeLocal(Path.Combine(AppPaths.Root, "Cache", "Icons"), titleId.ToUpperInvariant());

    public static async Task<string?> ResolveShared(IEnumerable<string> candidates, string cache,
        Func<string, string, CancellationToken, Task<bool>> download,
        Func<string, bool> isReadable, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        var target = Path.Combine(cache, "icon0.png");
        try {
            if (File.Exists(target) && HasPngSignature(target) && isReadable(target)) return target;
        } catch { /* A damaged cached icon can be replaced by a readable source. */ }
        return await Resolve(candidates, cache, download, isReadable, token);
    }
    public static bool HasPngSignature(string path) {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[8];
        return stream.Read(header) == 8 && header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
    }

    public static async Task<string?> Resolve(IEnumerable<string> candidates, string cache,
        Func<string, string, CancellationToken, Task<bool>> download,
        Func<string, bool> isReadable, CancellationToken token) {
        Directory.CreateDirectory(cache);
        var target = Path.Combine(cache, "icon0.png");
        foreach (var remote in candidates) {
            token.ThrowIfCancellationRequested();
            var temporary = Path.Combine(cache, "icon-" + Guid.NewGuid().ToString("N") + ".png");
            try {
                if (!await download(remote, temporary, token) || !HasPngSignature(temporary) || !isReadable(temporary)) continue;
                File.Move(temporary, target, overwrite: true);
                return target;
            } catch (OperationCanceledException) { throw; }
            catch { /* Missing, encrypted, or damaged artwork tries the next source. */ }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        // Do not return a stale icon from a previous scan when both sources fail.
        return null;
    }
}
