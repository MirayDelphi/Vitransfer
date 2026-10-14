using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Vitransfer.Core;

public sealed class AppSettings {
    public string[] Addresses { get; set; } = ["ftp://192.168.0.223:1337", "ftp://192.168.0.222:1337"];
    public string[] RecentAddresses { get; set; } = [];
    public bool HideApps { get; set; }
    public string[] HideAppsExceptions { get; set; } = ["MMFE*"];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    public static AppSettings Parse(string json) {
        using var document = JsonDocument.Parse(json);
        // Migrate the original two-address array without losing saved addresses.
        if (document.RootElement.ValueKind == JsonValueKind.Array)
            return new AppSettings { Addresses = JsonSerializer.Deserialize<string[]>(json) ?? [] };
        var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        settings.Addresses ??= [];
        settings.HideAppsExceptions ??= [];
        settings.RecentAddresses = (settings.RecentAddresses ?? []).Select(AddressForStorage)
            .Where(address => address.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray();
        return settings;
    }
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    public static string AddressForStorage(string? value) {
        value = value?.Trim() ?? "";
        if (value.Length == 0) return "";
        if (value.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase)) {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)) return "";
            return new UriBuilder(uri) { UserName = "", Password = "" }.Uri.ToString();
        }
        try { return Path.IsPathFullyQualified(value) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(value)) : ""; }
        catch (ArgumentException) { return ""; }
    }
    public void RememberSuccessfulAddress(string address) {
        string stored = AddressForStorage(address);
        if (stored.Length == 0) return;
        RecentAddresses = new[] { stored }.Concat(RecentAddresses)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray();
    }

    public bool IsGameVisible(string id) => !HideApps || GameFilter.Matches(id, HideAppsExceptions);
}

public static class GameFilter {
    private static readonly string[] Prefixes = ["PCSA", "PCSB", "PCSC", "PCSD", "PCSE", "PCSF", "PCSG", "PCSH"];
    public static bool Matches(string id, IEnumerable<string> exceptions) =>
        Prefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
        exceptions.Any(pattern => !string.IsNullOrWhiteSpace(pattern) && Regex.IsMatch(id,
            "\\A" + Regex.Escape(pattern.Trim()).Replace("\\*", ".*") + "\\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100)));
}
