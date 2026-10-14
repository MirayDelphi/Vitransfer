namespace Vitransfer.Core;

public sealed record Game(string Id, string Name, string? IconPath, bool HasSave, long? SizeBytes = null, bool HasDlc = false, bool HasUpdate = false, bool HasPatch = false) {
    public string SizeDisplay => SizeBytes is long size ? ByteSize.Format(size) : "N/A";
    public string Detail {
        get {
            var available = new List<string>();
            if (HasSave) available.Add("Save");
            if (HasDlc) available.Add("DLC");
            if (HasUpdate) available.Add("Update");
            if (HasPatch) available.Add("Patch");
            return available.Count == 0 ? Id : Id + " · " + string.Join(" / ", available);
        }
    }
}
public sealed record RemoteEntry(string RelativePath, bool IsDirectory, long Size);
public sealed record TransferUpdate(string Message, double Percent);
public enum ConflictPolicy { Skip, ReplaceWithBackup }

public static class AppPaths {
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vitransfer");
    public static string NewJob(string category) {
        var path = Path.Combine(Root, category, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    public static string SafeLocal(string root, string relative) {
        var parts = relative.Split('/');
        if (parts.Any(p => string.IsNullOrEmpty(p) || p is "." or ".." || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || p.EndsWith('.') || p.EndsWith(' ') || Reserved(p)))
            throw new IOException($"Unsafe file name: {relative}");
        var full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("File path escapes staging directory.");
        return full;
    }
    private static bool Reserved(string name) {
        var stem = name.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9');
    }
}
