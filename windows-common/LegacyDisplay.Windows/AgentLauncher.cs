using System.Diagnostics;
using System.Text.Json;

namespace LegacyDisplay.Windows;

public sealed record AgentLaunchRecord(int ProcessId, DateTime StartedUtc, string Executable);

public sealed class AgentLauncher : IDisposable
{
    private Process? process;
    private readonly string recordPath;
    public bool IsRunning => process is { HasExited: false };
    public AgentLauncher(string? statePath = null)
    {
        recordPath = statePath ?? Path.Combine(Path.GetDirectoryName(CredentialStore.DefaultPath)!, "studio-agent.json");
        try
        {
            var record = JsonSerializer.Deserialize<AgentLaunchRecord>(File.ReadAllText(recordPath));
            if (record == null) return;
            var candidate = Process.GetProcessById(record.ProcessId);
            if (Matches(candidate, record)) process = candidate; else candidate.Dispose();
        }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException) { }
    }
    public void Start(string executable, Uri device, string credentialsPath, string? actionsPath = null)
    {
        if (IsRunning) throw new InvalidOperationException("O Agent iniciado pelo Studio já está rodando.");
        DeviceClient.ValidateDevice(device); CredentialStore.Load(credentialsPath);
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable) || !Path.GetFileName(executable).Equals("LegacyDisplay.Agent.exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Mantenha as pastas studio e agent do pacote juntas.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var argument in new[] { "run", "--device", device.AbsoluteUri, "--credentials", Path.GetFullPath(credentialsPath) }) start.ArgumentList.Add(argument);
        if (actionsPath != null) { start.ArgumentList.Add("--actions"); start.ArgumentList.Add(Path.GetFullPath(actionsPath)); }
        process?.Dispose(); process = Process.Start(start) ?? throw new IOException("Não foi possível iniciar o Agent.");
        var record = new AgentLaunchRecord(process.Id, process.StartTime.ToUniversalTime(), executable);
        Directory.CreateDirectory(Path.GetDirectoryName(recordPath)!);
        var temporary = recordPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(record)); File.Move(temporary, recordPath, true);
    }
    public void Stop()
    {
        if (process is { HasExited: false })
        {
            var record = JsonSerializer.Deserialize<AgentLaunchRecord>(File.ReadAllText(recordPath));
            if (record == null || !Matches(process, record)) throw new InvalidOperationException("Identidade do processo mudou. O Agent não foi encerrado.");
            process.Kill(); process.WaitForExit(5000);
        }
        process?.Dispose(); process = null;
        if (File.Exists(recordPath)) File.Delete(recordPath);
    }
    public static string? FindExecutable()
    {
        var sibling = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "agent", "LegacyDisplay.Agent.exe"));
        if (File.Exists(sibling)) return sibling;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "LegacyDisplay.sln"))) continue;
            var development = Path.Combine(directory.FullName, "artifacts", "agent", "LegacyDisplay.Agent.exe");
            return File.Exists(development) ? development : null;
        }
        return null;
    }
    private static bool Matches(Process candidate, AgentLaunchRecord record) => !string.IsNullOrWhiteSpace(record.Executable) && !candidate.HasExited && candidate.StartTime.ToUniversalTime() == record.StartedUtc &&
        candidate.MainModule is { FileName: { } modulePath } && string.Equals(Path.GetFullPath(modulePath), Path.GetFullPath(record.Executable), StringComparison.OrdinalIgnoreCase) &&
        Path.GetFileName(record.Executable).Equals("LegacyDisplay.Agent.exe", StringComparison.OrdinalIgnoreCase);
    public void Dispose() { process?.Dispose(); process = null; } // Closing Studio intentionally leaves Agent running.
}
