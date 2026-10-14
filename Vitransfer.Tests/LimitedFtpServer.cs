using System.Net;
using System.Net.Sockets;
using System.Text;

// Models Vita-style FTP: LIST ignores its path, and NLST/SIZE/MDTM/EPSV
// return 502. Actual TCP data connections exercise FluentFTP itself.
sealed class LimitedFtpServer : IDisposable {
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private TcpClient? control;
    private TcpListener? data;
    private readonly HashSet<string> folders = ["/", "/ux0:/app"];
    public Dictionary<string, byte[]> Files { get; } = [];
    public List<string> Commands { get; } = [];
    public string? StorageReply { get; set; }
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public Task Completion { get; }
    public LimitedFtpServer() { listener.Start(); Completion = Run(); }
    private async Task Run() {
        try {
            control = await listener.AcceptTcpClientAsync(stop.Token);
            using var stream = control.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 Limited FTP ready");
            string cwd = "/";
            while (await reader.ReadLineAsync(stop.Token) is { } line) {
                string verb = line.Split(' ', 2)[0], argument = line.Length > verb.Length ? line[(verb.Length + 1)..] : "";
                Commands.Add(verb);
                string Absolute(string path) => (path.StartsWith('/') ? path : cwd.TrimEnd('/') + "/" + path).TrimEnd('/');
                switch (verb) {
                    case "USER": await writer.WriteLineAsync("331 Password required"); break;
                    case "PASS": await writer.WriteLineAsync("230 Logged in"); break;
                    case "FEAT": await writer.WriteLineAsync("211 No extensions"); break;
                    case "SYST": await writer.WriteLineAsync("215 UNIX Type: L8"); break;
                    case "TYPE": await writer.WriteLineAsync("200 Type set"); break;
                    case "STAT": await writer.WriteLineAsync(StorageReply == null ? "502 Sorry, command not implemented" : "213 " + StorageReply); break;
                    case "PWD": await writer.WriteLineAsync($"257 \"{cwd}\""); break;
                    case "CWD":
                        string target = argument == "/" ? "/" : Absolute(argument);
                        if (folders.Contains(target)) { cwd = target; await writer.WriteLineAsync("250 Directory changed"); }
                        else await writer.WriteLineAsync("550 Not found");
                        break;
                    case "MKD": folders.Add(Absolute(argument)); await writer.WriteLineAsync("257 Directory created"); break;
                    case "DELE": await writer.WriteLineAsync(Files.Remove(Absolute(argument)) ? "250 File deleted" : "550 Not found"); break;
                    case "RMD":
                        string directory = Absolute(argument);
                        bool nonempty = Files.Keys.Any(path => path.StartsWith(directory + "/")) || folders.Any(path => path.StartsWith(directory + "/"));
                        await writer.WriteLineAsync(!nonempty && folders.Remove(directory) ? "250 Directory removed" : "550 Not empty or missing"); break;
                    case "PASV":
                        data?.Stop(); data = new TcpListener(IPAddress.Loopback, 0); data.Start();
                        int port = ((IPEndPoint)data.LocalEndpoint).Port;
                        await writer.WriteLineAsync($"227 Entering Passive Mode (127,0,0,1,{port / 256},{port % 256})"); break;
                    case "LIST":
                        await writer.WriteLineAsync("150 Listing");
                        using (var connection = await data!.AcceptTcpClientAsync(stop.Token)) {
                            using var output = new StreamWriter(connection.GetStream(), Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
                            string prefix = cwd.TrimEnd('/') + "/";
                            foreach (var folder in folders.Where(p => p.StartsWith(prefix) && !p[prefix.Length..].Contains('/') && p != cwd))
                                await output.WriteLineAsync($"drwxr-xr-x 1 owner group 0 Jan 01 2026 {folder[prefix.Length..]}");
                            foreach (var file in Files.Where(p => p.Key.StartsWith(prefix) && !p.Key[prefix.Length..].Contains('/')))
                                await output.WriteLineAsync($"-rw-r--r-- 1 owner group {file.Value.Length} Jan 01 2026 {file.Key[prefix.Length..]}");
                        }
                        data.Stop(); await writer.WriteLineAsync("226 Listing complete"); break;
                    case "RETR":
                        if (!Files.TryGetValue(Absolute(argument), out var download)) { await writer.WriteLineAsync("550 Not found"); break; }
                        await writer.WriteLineAsync("150 Ready to send");
                        using (var connection = await data!.AcceptTcpClientAsync(stop.Token))
                            await connection.GetStream().WriteAsync(download, stop.Token);
                        data.Stop(); await writer.WriteLineAsync("226 Transfer complete"); break;
                    case "STOR":
                        await writer.WriteLineAsync("150 Ready to receive");
                        using (var connection = await data!.AcceptTcpClientAsync(stop.Token)) {
                            using var buffer = new MemoryStream();
                            await connection.GetStream().CopyToAsync(buffer, stop.Token);
                            Files[Absolute(argument)] = buffer.ToArray();
                        }
                        data.Stop(); await writer.WriteLineAsync("226 Transfer complete"); break;
                    case "QUIT": await writer.WriteLineAsync("221 Bye"); return;
                    default: await writer.WriteLineAsync("502 Sorry, command not implemented"); break;
                }
            }
        } catch (Exception) when (stop.IsCancellationRequested) { }
    }
    public void Dispose() { stop.Cancel(); control?.Dispose(); data?.Stop(); listener.Stop(); stop.Dispose(); }
}
