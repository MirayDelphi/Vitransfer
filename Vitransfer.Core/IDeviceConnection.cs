namespace Vitransfer.Core;

public interface IDeviceConnection : IVitaConnection, IDisposable {
    bool IsConnected { get; }
    DeviceMetadata CachedMetadata { get; }
    string ExplorerFolder(string titleId);
    void ForgetGameMetadata(string titleId);
    Task Connect(CancellationToken token);
    Task<List<Game>> Scan(IProgress<string> status, CancellationToken token);
    Task<List<DlcEntry>> ReadDlcs(string titleId, CancellationToken token);
    Task CalculateSizes(IReadOnlyList<Game> games, IProgress<string> status, CancellationToken token);
}

public static class DeviceConnection {
    public static IDeviceConnection Create(string address, Func<string, bool>? isReadableIcon = null) =>
        address.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase)
            ? new VitaConnection(address, isReadableIcon)
            : new LocalConnection(address, isReadableIcon);
}
