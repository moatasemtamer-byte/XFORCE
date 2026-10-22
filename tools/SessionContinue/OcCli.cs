using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace SessionContinue;

/// <summary>Result of one opencode CLI invocation.</summary>
public sealed record CliResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>
/// Locates the opencode executable and runs it. Normally talks to the background
/// service the CLI manages itself; when that is broken (service stopped and the
/// CLI cannot respawn it) a private <c>opencode serve</c> instance is started on a
/// free port and every call is pointed at it with --server.
/// </summary>
public sealed class OcCli : IDisposable
{
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan BigTimeout = TimeSpan.FromMinutes(15);

    private readonly Launcher _launcher;
    private Process? _serve;
    private string? _serverUrl;

    public OcCli()
    {
        _launcher = Locate();
    }

    public string? ServerUrl => _serverUrl;

    /// <summary>How to invoke opencode: a native exe, a .cmd shim, a .ps1 shim, or bare PATH lookup through cmd.</summary>
    private sealed record Launcher(string Kind, string Path)
    {
        public const string Exe = "exe";
        public const string Cmd = "cmd";
        public const string Ps1 = "ps1";
        public const string Bare = "bare";
    }

    /// <summary>
    /// First launcher that exists wins: a real opencode.exe on PATH, the npm-layout
    /// exe that the WinGet shims point at, an opencode.cmd / opencode.ps1 shim, the
    /// cached service binary, and finally a bare PATH lookup through cmd.
    /// </summary>
    private static Launcher Locate()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var dirs = pathVar
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Trim())
            .Where(d => { try { return Directory.Exists(d); } catch { return false; } })
            .ToList();

        foreach (var dir in dirs)
        {
            var exe = Path.Combine(dir, "opencode.exe");
            if (File.Exists(exe)) return new Launcher(Launcher.Exe, exe);
        }

        foreach (var dir in dirs)
        {
            var npmExe = Path.Combine(dir, "node_modules", "@opencode", "cli", "bin", "opencode.exe");
            if (File.Exists(npmExe)) return new Launcher(Launcher.Exe, npmExe);
        }

        foreach (var dir in dirs)
        {
            var cmd = Path.Combine(dir, "opencode.cmd");
            if (File.Exists(cmd)) return new Launcher(Launcher.Cmd, cmd);
        }

        foreach (var dir in dirs)
        {
            var ps1 = Path.Combine(dir, "opencode.ps1");
            if (File.Exists(ps1)) return new Launcher(Launcher.Ps1, ps1);
        }

        var cache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "opencode", "bin", "opencode.exe");
        if (File.Exists(cache)) return new Launcher(Launcher.Exe, cache);

        return new Launcher(Launcher.Bare, "opencode");
    }

    /// <summary>Runs opencode with the given arguments in <paramref name="workingDir"/>.</summary>
    public async Task<CliResult> RunAsync(string workingDir, IReadOnlyList<string> args, TimeSpan? timeout = null)
    {
        var result = await RunProcessAsync(workingDir, args.ToList(), timeout ?? ListTimeout).ConfigureAwait(false);

        if (!result.Ok && _serverUrl is null && LooksLikeServerProblem(result))
        {
            if (await TryStartServeAsync(workingDir).ConfigureAwait(false))
            {
                var retried = WithServer(args.ToList());
                result = await RunProcessAsync(workingDir, retried, timeout ?? ListTimeout).ConfigureAwait(false);
            }
        }

        return result;
    }

    private List<string> WithServer(List<string> args)
    {
        args.Add("--server");
        args.Add(_serverUrl!);
        return args;
    }

    private static bool LooksLikeServerProblem(CliResult result)
    {
        var text = result.StdErr + result.StdOut;
        return text.Contains("Failed to start server", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Could not reach server", StringComparison.OrdinalIgnoreCase)
            || text.Contains("did not provide a compatible V2 health response", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Starts a private `opencode serve` on the first free port if none is running.</summary>
    private async Task<bool> TryStartServeAsync(string workingDir)
    {
        foreach (var port in new[] { 4096, 4097, 4098, 4317, 4318 })
        {
            if (IsPortBusy(port)) continue;

            try
            {
                var startInfo = BuildStartInfo(workingDir, new List<string> { "serve", "--port", port.ToString() });
                var process = Process.Start(startInfo);
                if (process is null) continue;

                for (var i = 0; i < 40; i++)
                {
                    await Task.Delay(250).ConfigureAwait(false);
                    if (process.HasExited) break;
                    if (IsPortBusy(port))
                    {
                        _serve = process;
                        _serverUrl = "http://127.0.0.1:" + port;
                        return true;
                    }
                }

                if (!process.HasExited) process.Kill();
            }
            catch
            {
                /* try next port */
            }
        }

        return false;
    }

    private static bool IsPortBusy(int port)
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync("127.0.0.1", port).Wait(TimeSpan.FromMilliseconds(200))
                && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private ProcessStartInfo BuildStartInfo(string workingDir, List<string> args)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        switch (_launcher.Kind)
        {
            case Launcher.Exe:
                startInfo.FileName = _launcher.Path;
                foreach (var arg in args) startInfo.ArgumentList.Add(arg);
                break;

            case Launcher.Cmd:
                startInfo.FileName = "cmd.exe";
                startInfo.Arguments = "/c \"\"" + _launcher.Path + "\" " + string.Join(" ", args.Select(Quote)) + "\"";
                break;

            case Launcher.Ps1:
                startInfo.FileName = "powershell.exe";
                startInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + _launcher.Path + "\" "
                                      + string.Join(" ", args.Select(Quote));
                break;

            default: // bare
                startInfo.FileName = "cmd.exe";
                startInfo.Arguments = "/c opencode " + string.Join(" ", args.Select(Quote));
                break;
        }

        return startInfo;
    }

    private async Task<CliResult> RunProcessAsync(string workingDir, List<string> args, TimeSpan timeout)
    {
        var startInfo = BuildStartInfo(workingDir, args);
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (stdout) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (stderr) stderr.AppendLine(e.Data); };

        if (!process.Start()) return new CliResult(-1, "", "failed to start opencode");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new CliResult(-1, "", "opencode timed out after " + timeout);
        }

        string so, se;
        lock (stdout) so = stdout.ToString();
        lock (stderr) se = stderr.ToString();
        return new CliResult(process.ExitCode, StripBom(so), StripBom(se));
    }

    private static string StripBom(string text) =>
        text.Length > 0 && text[0] == '﻿' ? text.Substring(1) : text;

    private static string Quote(string arg) =>
        arg.Contains(' ') || arg.Contains('"') ? "\"" + arg.Replace("\"", "\\\"") + "\"" : arg;

    public void Dispose()
    {
        if (_serve is { HasExited: false })
        {
            try { _serve.Kill(entireProcessTree: true); } catch { }
        }
        _serve?.Dispose();
    }
}
