using FluentFTP;

namespace Vitransfer.Core;

public static class VitaDirectoryListing {
    // Vita FTP servers can ignore the path argument on LIST. Navigate first,
    // then issue a plain LIST with no path or flags sent to the server.
    public static async Task<FtpListItem[]> Read(string path,
        Func<string, CancellationToken, Task> changeDirectory,
        Func<CancellationToken, Task<FtpListItem[]>> listCurrentDirectory,
        CancellationToken token) {
        await changeDirectory(path, token);
        var entries = await listCurrentDirectory(token);
        if (entries.Any(e => e.Name.EndsWith(':')))
            throw new IOException($"The FTP server returned partition names instead of contents of {path}. Check access to ux0: on the device.");
        return entries;
    }
}
