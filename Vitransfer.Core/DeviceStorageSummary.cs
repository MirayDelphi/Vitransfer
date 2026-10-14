namespace Vitransfer.Core;

public static class DeviceStorageSummary {
    public static string Display(StorageSpace? storage, IEnumerable<Game> allGames) {
        if (storage?.TotalBytes is long capacity)
            return storage.UsedBytes != null ? storage.Display : $"ux0: {ByteSize.Format(capacity)} total";
        var games = allGames.ToArray();
        int unknown = games.Count(game => game.SizeBytes == null);
        if (games.Length > 0 && unknown == games.Length) return "Total calculated size: N/A";
        long total = games.Sum(game => game.SizeBytes ?? 0);
        string result = "Total calculated size: " + ByteSize.Format(total);
        return unknown == 0 ? result : result + $" (partial; {unknown} size(s) unavailable)";
    }
}
