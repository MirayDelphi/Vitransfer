using System.Text.Json;

namespace Vitransfer.Core;

public sealed class TransferEngine {
    public async Task<string> Transfer(IVitaConnection source, IVitaConnection destination, IReadOnlyList<Game> games, bool includeBase, ConflictPolicy policy, IProgress<TransferUpdate> progress, CancellationToken token) {
        if (source.DeviceKey == destination.DeviceKey) throw new InvalidOperationException("Source and destination are the same address or folder.");
        if (source is LocalConnection localSource && destination is LocalConnection localDestination) {
            static bool Contains(string parent, string child) => child.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
                child.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (Contains(localSource.Root, localDestination.Root) || Contains(localDestination.Root, localSource.Root))
                throw new IOException("Source and destination local folders overlap. Select separate folders.");
        }
        bool direct = source is LocalConnection || destination is LocalConnection;
        string job = AppPaths.NewJob(direct ? "Transfers" : "Staging");
        int copied = 0, skipped = 0;
        string? backup = null;
        var plans = new List<(string Source, string Destination, string Local, List<RemoteEntry> Entries, bool Exists)>();
        try {
            // Prepare every manifest before modifying the destination. Local
            // transfers use the actual source files instead of staging a copy.
            foreach (var game in games) {
                foreach (var kind in includeBase ? new[] { "app", "addcont", "patch", "rePatch", "save" } : new[] { "save" }) {
                    token.ThrowIfCancellationRequested();
                    static string Root(IVitaConnection device, string kind) => kind switch {
                        "app" => device.AppRoot, "addcont" => device.AddcontRoot,
                        "patch" => device.PatchRoot, "rePatch" => device.RePatchRoot, _ => device.SaveRoot
                    };
                    string src = Root(source, kind) + "/" + game.Id;
                    string dst = Root(destination, kind) + "/" + game.Id;
                    if (!await source.Exists(src, token)) { skipped++; continue; }
                    bool exists = await destination.Exists(dst, token);
                    if (exists && policy == ConflictPolicy.Skip) { skipped++; continue; }
                    progress.Report(new($"Preparing {game.Name} ({kind})…", 0));
                    var entries = await source.Manifest(src, token);
                    plans.Add((src, dst, Path.Combine(job, kind, game.Id), entries, exists));
                }
            }
            var required = plans.Sum(p => p.Entries.Sum(e => e.Size));
            if (!direct && new DriveInfo(Path.GetPathRoot(job)!).AvailableFreeSpace < required + 64L * 1024 * 1024) throw new IOException("Insufficient PC staging space.");
            await File.WriteAllTextAsync(Path.Combine(job, "job.json"), JsonSerializer.Serialize(new { source = source.DeviceKey, destination = destination.DeviceKey, folders = plans.Select(p => new { p.Source, p.Destination, p.Entries }) }, new JsonSerializerOptions { WriteIndented = true }), token);
            if (!direct) foreach (var plan in plans) await source.Download(plan.Source, plan.Local, plan.Entries, progress, token);
            foreach (var plan in plans) {
                if (plan.Exists) {
                    backup ??= AppPaths.NewJob("Backups");
                    var localBackup = Path.Combine(backup, "folder-" + copied);
                    var old = await destination.Manifest(plan.Destination, token);
                    if (new DriveInfo(Path.GetPathRoot(backup)!).AvailableFreeSpace < old.Sum(e => e.Size) + 64L * 1024 * 1024) throw new IOException("Insufficient PC backup space.");
                    await destination.Download(plan.Destination, localBackup, old, progress, token);
                    await File.WriteAllTextAsync(localBackup + ".json", JsonSerializer.Serialize(new { device = destination.DeviceKey, path = plan.Destination, entries = old }), token);
                    // Persist recovery instructions before the first destructive operation.
                    await File.AppendAllTextAsync(Path.Combine(job, "recovery.txt"), $"Destination: {plan.Destination}\nBackup: {localBackup}\n", token);
                    await destination.Delete(plan.Destination, token);
                } else if (await destination.Exists(plan.Destination, token)) {
                    throw new IOException("Destination changed during transfer. Refresh and try again.");
                }
                await File.AppendAllTextAsync(Path.Combine(job, "recovery.txt"), $"Copying: {plan.Destination}\nSource: {(direct ? plan.Source : plan.Local)}\n", token);
                if (source is LocalConnection) {
                    await destination.Upload(plan.Source, plan.Destination, plan.Entries, progress, token);
                } else if (destination is LocalConnection local) {
                    local.ValidateDestination(plan.Destination, plan.Entries);
                    await source.Download(plan.Source, plan.Destination, plan.Entries, progress, token);
                    if (!(await destination.Manifest(plan.Destination, token)).OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
                        .SequenceEqual(plan.Entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)))
                        throw new IOException("Destination manifest does not match source.");
                } else await destination.Upload(plan.Local, plan.Destination, plan.Entries, progress, token);
                copied++;
            }
            Directory.Delete(job, true);
            return $"Completed: {copied} folder(s) copied; {skipped} skipped (missing or already present)." + (backup == null ? "" : $" Backups: {backup}");
        } catch (Exception ex) {
            throw new IOException($"{(ex is OperationCanceledException ? "Transfer cancelled" : ex.Message)}. Recovery files retained at {job}" + (backup == null ? "" : $"; backups at {backup}"), ex);
        }
    }
}
