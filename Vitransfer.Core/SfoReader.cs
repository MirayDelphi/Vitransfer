using System.Buffers.Binary;
using System.Text;

namespace Vitransfer.Core;

public static class SfoReader {
    public static string? ReadTitle(byte[] data) => ReadString(data, "TITLE") ?? ReadString(data, "STITLE");
    public static string? ReadString(byte[] data, string field) {
        if (data.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x46535000) return null;
        uint keys = U32(data, 8), values = U32(data, 12), count = U32(data, 16);
        if (count > (data.Length - 20) / 16) return null;
        for (int i = 0; i < count; i++) {
            int entry = 20 + i * 16;
            long key = (long)keys + BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entry, 2));
            long value = (long)values + U32(data, entry + 12);
            uint length = U32(data, entry + 4);
            if (key >= data.Length || value > data.Length || length > data.Length - value) continue;
            int end = Array.IndexOf(data, (byte)0, (int)key);
            if (end < 0) continue;
            if (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entry + 2, 2)) == 0x0204 && Encoding.UTF8.GetString(data, (int)key, end - (int)key) == field) {
                var text = Encoding.UTF8.GetString(data, (int)value, (int)length).TrimEnd('\0').Trim();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }
        return null;
    }
    private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
}
