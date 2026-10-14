namespace Vitransfer.Core;

public static class GameDeletion {
    public static async Task Delete(IVitaConnection connection, string titleId, bool keepSaves, IProgress<string> status, CancellationToken token) {
        // Title IDs must be single safe folder names, never a relative path.
        if (titleId.Contains('/') || titleId.Contains('\\')) throw new IOException("Invalid title ID.");
        _ = AppPaths.SafeLocal(Path.Combine(AppPaths.Root, "Validation"), titleId);
        var roots = new[] { connection.AddcontRoot, connection.PatchRoot, connection.RePatchRoot, connection.AppRoot }
            .Concat(keepSaves ? [] : new[] { connection.SaveRoot });
        var paths = new List<string>();
        foreach (var root in roots) {
            token.ThrowIfCancellationRequested();
            string path = root + "/" + titleId;
            if (await connection.Exists(path, token)) {
                // Validate every folder before deleting anything.
                await connection.Manifest(path, token);
                paths.Add(path);
            }
        }
        if (connection is IDeviceConnection device) device.ForgetGameMetadata(titleId);
        foreach (var path in paths) {
            token.ThrowIfCancellationRequested();
            status.Report("Deleting " + path + "…");
            await connection.Delete(path, token);
        }
    }
}
