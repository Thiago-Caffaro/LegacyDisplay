using LibreHardwareMonitor.Hardware;
using System.Security.Cryptography;
using System.Text;

namespace LegacyDisplay.Windows;

public sealed record HardwareReading(string HardwareId, string HardwareName, string SensorId, string SensorName, SensorType Type, float? Value);
public sealed record HardwareSensorInfo(string Id, string Label, string Unit);
public sealed record HardwareSnapshot(IReadOnlyDictionary<string, object?> Values, IReadOnlyList<HardwareSensorInfo> Sensors);

/// <summary>Only GPU vendor APIs are enabled. CPU/motherboard kernel access remains disabled.</summary>
public sealed class HardwareMetrics : IDisposable
{
    private Computer? computer;
    private bool attempted;
    private long sampledAt = long.MinValue;
    private HardwareSnapshot snapshot = Project([]);
    public string? UnavailableReason { get; private set; }

    public HardwareSnapshot Sample()
    {
        var now = Environment.TickCount64;
        if (sampledAt != long.MinValue && now - sampledAt < 1000) return snapshot;
        sampledAt = now;
        try
        {
            if (!attempted)
            {
                attempted = true;
                computer = new Computer { IsGpuEnabled = true };
                computer.Open();
            }
            var readings = new List<HardwareReading>();
            foreach (var hardware in computer!.Hardware)
            {
                if (hardware.HardwareType is not (HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)) continue;
                // A failed update must clear last values instead of keeping an old temperature alive.
                try
                {
                    hardware.Update();
                    readings.AddRange(hardware.Sensors.Where(s => Unit(s.SensorType) != null).Select(s =>
                        new HardwareReading(hardware.Identifier.ToString(), hardware.Name, s.Identifier.ToString(), s.Name, s.SensorType, s.Value)));
                }
                catch (Exception) { }
            }
            snapshot = Merge(snapshot, Project(readings));
            UnavailableReason = readings.Count == 0 ? "Nenhum sensor de GPU disponível pelo driver instalado." : null;
        }
        catch (Exception error)
        {
            snapshot = Merge(snapshot, Project([]));
            UnavailableReason = "Sensores de GPU indisponíveis (" + error.GetType().Name + ").";
        }
        return snapshot;
    }

    public static HardwareSnapshot Project(IEnumerable<HardwareReading> input)
    {
        var readings = input.OrderBy(r => r.HardwareId, StringComparer.Ordinal).ThenBy(r => r.SensorId, StringComparer.Ordinal).ToArray();
        var values = new Dictionary<string, object?> {
            ["pc.gpu.name"] = null, ["pc.gpu.temperature"] = null, ["pc.gpu.usage"] = null,
            ["pc.gpu.powerWatts"] = null, ["pc.gpu.memory.usedMiB"] = null, ["pc.gpu.memory.totalMiB"] = null
        };
        var sensors = new List<HardwareSensorInfo>();
        var duplicates = readings.GroupBy(r => r.SensorId).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        foreach (var reading in readings)
        {
            var unit = Unit(reading.Type);
            if (unit == null) continue;
            var id = "hw." + reading.SensorId.Trim('/').Replace('/', '.');
            // Some drivers expose two sensors with the same LHM index (for example GPU Bus and GPU Memory).
            if (duplicates.Contains(reading.SensorId)) id += "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reading.SensorName)))[..8].ToLowerInvariant();
            if (id.Length > 64 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_'))) continue;
            if (sensors.Count >= 200 || values.ContainsKey(id)) continue;
            values[id] = Finite(reading.Value);
            sensors.Add(new(id, reading.HardwareName + " · " + reading.SensorName, unit));
        }
        var first = readings.FirstOrDefault();
        if (first != null)
        {
            values["pc.gpu.name"] = first.HardwareName;
            var primary = readings.Where(r => r.HardwareId == first.HardwareId).ToArray();
            object? Find(SensorType type, string name) => Finite(primary.FirstOrDefault(r => r.Type == type && r.SensorName == name)?.Value);
            values["pc.gpu.temperature"] = Find(SensorType.Temperature, "GPU Core");
            values["pc.gpu.usage"] = Find(SensorType.Load, "GPU Core");
            values["pc.gpu.powerWatts"] = Find(SensorType.Power, "GPU Package");
            values["pc.gpu.memory.usedMiB"] = Find(SensorType.SmallData, "GPU Memory Used");
            values["pc.gpu.memory.totalMiB"] = Find(SensorType.SmallData, "GPU Memory Total");
        }
        return new(values, sensors);
    }
    public static HardwareSnapshot Merge(HardwareSnapshot previous, HardwareSnapshot current)
    {
        var sensors = previous.Sensors.Concat(current.Sensors).DistinctBy(s => s.Id).Take(200).ToArray();
        var values = current.Values.Where(v => !v.Key.StartsWith("hw.", StringComparison.Ordinal)).ToDictionary(v => v.Key, v => v.Value);
        foreach (var sensor in sensors) values[sensor.Id] = current.Values.GetValueOrDefault(sensor.Id);
        return new(values, sensors);
    }
    private static double? Finite(float? value) => value is { } number && float.IsFinite(number) ? Math.Round(number, 1) : null;
    private static string? Unit(SensorType type) => type switch {
        SensorType.Temperature => "°C", SensorType.Load => "%", SensorType.Power => "W", SensorType.SmallData => "MiB",
        SensorType.Clock => "MHz", SensorType.Fan => "RPM", SensorType.Voltage => "V", _ => null
    };
    public void Dispose() { computer?.Close(); computer = null; }
}
