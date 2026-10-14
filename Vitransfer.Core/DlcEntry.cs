namespace Vitransfer.Core;

public sealed record DlcEntry(string Id, string Name, string Location, string? ContentId, bool HasSfoName) {
    public string Detail => $"{Id} · {Location}" + (HasSfoName ? "" : " · SFO name unavailable") + (ContentId == null ? "" : $"\n{ContentId}");
}
