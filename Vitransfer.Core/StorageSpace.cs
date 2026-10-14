using System.Globalization;
using System.Text.RegularExpressions;

namespace Vitransfer.Core;

public static class ByteSize {
    public static string Format(long bytes) {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];
        double value = bytes; int index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return value.ToString(index == 0 ? "0" : "0.##", CultureInfo.CurrentCulture) + " " + units[index];
    }
}

public sealed record StorageSpace(long? TotalBytes = null, long? UsedBytes = null, long? FreeBytes = null) {
    public string Display => TotalBytes is long total && UsedBytes is long used
        ? $"ux0: {ByteSize.Format(used)} used / {ByteSize.Format(total)} total"
        : FreeBytes is long free ? $"ux0: {ByteSize.Format(free)} free · used/total unavailable"
        : "ux0: N/A";

    // FTP capacity replies have no universal schema. Only accept explicitly
    // labelled amounts with known units; never interpret arbitrary STAT numbers.
    public static StorageSpace? Parse(string text) {
        long? Read(string label) {
            var match = Regex.Match(text, $@"\b{label}(?:\s+(?:space|size))?\s*(?<prefix>bytes)?\s*[:=]\s*(?<value>\d+(?:\.\d+)?)\s*(?<unit>bytes|KiB|MiB|GiB|TiB|KB|MB|GB|TB|B)?\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            if (!match.Success || (!match.Groups["prefix"].Success && !match.Groups["unit"].Success)) return null;
            if (!decimal.TryParse(match.Groups["value"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) return null;
            decimal multiplier = match.Groups["unit"].Value.ToUpperInvariant() switch {
                "KIB" => 1024m, "MIB" => 1048576m, "GIB" => 1073741824m, "TIB" => 1099511627776m,
                "KB" => 1000m, "MB" => 1000000m, "GB" => 1000000000m, "TB" => 1000000000000m, _ => 1m
            };
            if (value > long.MaxValue / multiplier) return null;
            return (long)(value * multiplier);
        }
        var total = Read("total"); var used = Read("used"); var free = Read("free") ?? Read("available");
        if (total == null && used != null && free != null && used <= long.MaxValue - free) total = used + free;
        if (total != null && free != null && used == null) used = total - free;
        if (total != null && used != null && free == null) free = total - used;
        if (used < 0 || free < 0 || used > total || free > total || (total != null && used != null && free != null && total - used != free)) return null;
        return total == null && free == null ? null : new(total, used, free);
    }
}
