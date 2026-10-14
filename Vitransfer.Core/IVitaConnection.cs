namespace Vitransfer.Core;

public interface IVitaConnection {
    string AppRoot { get; }
    string SaveRoot { get; }
    string AddcontRoot { get; }
    string PatchRoot { get; }
    string RePatchRoot { get; }
    string DeviceKey { get; }
    Task<bool> Exists(string path, CancellationToken token);
    Task<List<RemoteEntry>> Manifest(string root, CancellationToken token);
    Task Download(string remote, string local, IReadOnlyList<RemoteEntry> entries, IProgress<TransferUpdate> progress, CancellationToken token);
    Task Upload(string local, string remote, IReadOnlyList<RemoteEntry> entries, IProgress<TransferUpdate> progress, CancellationToken token);
    Task Delete(string path, CancellationToken token);
}
