using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LegacyDisplay.Windows;

public sealed record AdbDevice(string Serial, string State, string Model);
public sealed record UsbConnection(Uri Endpoint, AdbDevice Device);

public static class AdbConnector
{
    public static IReadOnlyList<AdbDevice> ParseDevices(string output)
    {
        var devices = new List<AdbDevice>();
        foreach (var line in output.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] is "List" or "*") continue;
            if (parts[1] is not ("device" or "offline" or "unauthorized")) continue;
            if (!Regex.IsMatch(parts[0], "^[A-Za-z0-9._:-]{1,128}\\z")) continue;
            var model = parts.FirstOrDefault(p => p.StartsWith("model:", StringComparison.Ordinal))?[6..].Replace('_', ' ') ?? "Android";
            devices.Add(new AdbDevice(parts[0], parts[1], model));
        }
        return devices;
    }
    public static async Task<UsbConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        var executable = FindExecutable() ?? throw new FileNotFoundException("ADB não encontrado. Instale Android platform-tools e configure ANDROID_HOME ou PATH.");
        var devices = ParseDevices(await RunAsync(executable, ["devices", "-l"], cancellationToken));
        var ready = devices.Where(d => d.State == "device").ToArray();
        if (ready.Length == 0) throw new InvalidOperationException(devices.Any(d => d.State == "unauthorized") ? "Autorize a depuração USB no tablet e tente novamente." : "Nenhum tablet disponível. Conecte o USB e habilite depuração.");
        if (ready.Length > 1) throw new InvalidOperationException("Conecte apenas um dispositivo Android nesta versão.");
        var device = ready[0];
        var forwards = await RunAsync(executable, ["forward", "--list"], cancellationToken);
        foreach (var line in forwards.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3 && parts[0] == device.Serial && parts[1].StartsWith("tcp:", StringComparison.Ordinal) && parts[2] == "tcp:8765" && int.TryParse(parts[1][4..], out var existing) && existing is > 0 and <= 65535)
                return new UsbConnection(new Uri($"http://127.0.0.1:{existing}"), device);
        }
        // ADB chooses a free local port; existing forwards and local services remain untouched.
        var portText = await RunAsync(executable, ["-s", device.Serial, "forward", "tcp:0", "tcp:8765"], cancellationToken);
        if (!int.TryParse(portText.Trim(), out var port) || port is <= 0 or > 65535) throw new IOException("ADB não retornou uma porta válida.");
        return new UsbConnection(new Uri($"http://127.0.0.1:{port}"), device);
    }
    private static async Task<string> RunAsync(string executable, string[] arguments, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Não foi possível iniciar ADB.");
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            var stdout = await output; var stderr = await error;
            if (process.ExitCode != 0) throw new IOException("ADB: " + stderr.Trim());
            return stdout;
        }
        catch { if (!process.HasExited) process.Kill(); throw; }
    }
    private static string? FindExecutable()
    {
        var candidates = new List<string>();
        foreach (var sdk in new[] { Environment.GetEnvironmentVariable("ANDROID_HOME"), Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk") })
            if (!string.IsNullOrWhiteSpace(sdk)) candidates.Add(Path.Combine(sdk, "platform-tools", "adb.exe"));
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (!string.IsNullOrWhiteSpace(directory)) candidates.Add(Path.Combine(directory.Trim('"'), "adb.exe"));
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "LegacyDisplay.sln"))) candidates.Add(Path.Combine(directory.FullName, ".tools", "android-sdk", "platform-tools", "adb.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }
}
