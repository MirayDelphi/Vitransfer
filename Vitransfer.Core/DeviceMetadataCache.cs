using System.Text.Json;

namespace Vitransfer.Core;

public sealed record GameMetadata(string Name, long? SizeBytes, DateTimeOffset UpdatedUtc);
public sealed class DeviceMetadata {
    public int Version { get; set; } = 3;
    public string DeviceKey { get; set; } = "";
    public string Address { get; set; } = "";
    public StorageSpace? Storage { get; set; }
    public DateTimeOffset? StorageUpdatedUtc { get; set; }
    public Dictionary<string, GameMetadata> Games { get; set; } = new(StringComparer.Ordinal);
    public Game Apply(Game game) => Games.TryGetValue(game.Id, out var metadata) ? game with { SizeBytes = metadata.SizeBytes } : game with { SizeBytes = null };
}

public sealed class DeviceMetadataCache(string? directory = null) {
    private readonly string root = directory ?? Path.Combine(AppPaths.Root, "Metadata");
    private string FilePath(string key) => AppPaths.SafeLocal(root, key + ".json");
    public void ForgetGame(string key, string titleId) {
        var metadata = Load(key);
        metadata.Games.Remove(titleId);
        metadata.Storage = null;
        metadata.StorageUpdatedUtc = null;
        Save(metadata);
    }
    public DeviceMetadata Load(string key) {
        try {
            var value = JsonSerializer.Deserialize<DeviceMetadata>(File.ReadAllText(FilePath(key)));
            if (value?.Version is 1 or 2 or 3 && value.DeviceKey == key && value.Games != null) {
                // Earlier sizes omitted rePatch (and version 1 omitted DLC and
                // updates). Preserve names/capacity; explicitly recalculate sizes.
                if (value.Version < 3) {
                    value.Games = value.Games.Where(pair => pair.Value != null)
                        .ToDictionary(pair => pair.Key, pair => pair.Value with { SizeBytes = null }, StringComparer.Ordinal);
                    value.Version = 3;
                }
                return value;
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new DeviceMetadata { DeviceKey = key };
    }
    public void Save(DeviceMetadata metadata) {
        Directory.CreateDirectory(root);
        string target = FilePath(metadata.DeviceKey), temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(temporary, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, target, overwrite: true);
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
