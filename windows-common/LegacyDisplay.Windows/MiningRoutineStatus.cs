using System.Diagnostics;
using System.Text.Json;
using LegacyDisplay.Windows.Actions;

namespace LegacyDisplay.Windows;

public sealed class MiningRoutineStatus(Func<ActionCatalog> catalog, Func<int, DateTimeOffset, bool>? alive = null)
{
    private long sampled;
    private Dictionary<string, object?> cached = Unavailable();
    private static Dictionary<string, object?> Unavailable() => new() { ["mining.blackout"] = false, ["mining.status.available"] = false,
        ["mining.mode"] = null, ["mining.state"] = null, ["mining.lighting.off"] = null, ["mining.display.off"] = null };
    public Dictionary<string, object?> Sample()
    {
        if (sampled != 0 && Environment.TickCount64 - sampled < 1000) return cached;
        sampled = Environment.TickCount64; cached = Unavailable();
        try
        {
            var paths = catalog().Actions.Where(a => a.Id.StartsWith("mining.", StringComparison.Ordinal) && a.Application != null &&
                Path.GetFileName(a.Application.Executable).Equals("MiningController.Windows.exe", StringComparison.OrdinalIgnoreCase) &&
                a.Application.Arguments.Count >= 2 && a.Application.Arguments[0] == "--data-directory")
                .Select(a => a.Application!.Arguments[1]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (paths.Length != 1 || !Path.IsPathFullyQualified(paths[0])) return cached;
            var path = Path.Combine(paths[0], "status.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 131072) return cached;
            using var json = JsonDocument.Parse(File.ReadAllText(path)); var s = json.RootElement;
            var timestamp = s.GetProperty("timestamp").GetDateTimeOffset();
            var age = DateTimeOffset.UtcNow - timestamp;
            if (age.TotalSeconds < -5 || age.TotalSeconds > 15 || !(alive ?? IsAlive)(s.GetProperty("process_id").GetInt32(), timestamp)) return cached;
            var lights = s.GetProperty("lighting").GetProperty("off").GetBoolean();
            var display = s.GetProperty("display_state").GetString() == "off";
            cached["mining.status.available"] = true; cached["mining.mode"] = s.GetProperty("mode").GetString();
            cached["mining.state"] = s.GetProperty("state").GetString();
            cached["mining.lighting.off"] = lights; cached["mining.display.off"] = display;
            cached["mining.blackout"] = !s.GetProperty("dry_run").GetBoolean() && lights && display;
        }
        catch (Exception) { cached = Unavailable(); }
        return cached;
    }
    private static bool IsAlive(int pid, DateTimeOffset timestamp)
    {
        using var process = Process.GetProcessById(pid);
        return !process.HasExited && process.ProcessName == "MiningController.Windows" && process.StartTime.ToUniversalTime() <= timestamp.UtcDateTime;
    }
}
