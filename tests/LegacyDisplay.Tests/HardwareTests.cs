using LegacyDisplay.Windows;
using LibreHardwareMonitor.Hardware;
using Xunit;

namespace LegacyDisplay.Tests;

public sealed class HardwareTests
{
    [Fact]
    public void ProjectionKeepsGpuIdentitiesAndNeverCombinesDifferentCards()
    {
        var snapshot = HardwareMetrics.Project([
            new("/gpu-nvidia/1", "GPU B", "/gpu-nvidia/1/temperature/0", "GPU Core", SensorType.Temperature, 80),
            new("/gpu-nvidia/0", "GPU A", "/gpu-nvidia/0/load/0", "GPU Core", SensorType.Load, 25),
            new("/gpu-nvidia/1", "GPU B", "/gpu-nvidia/1/power/0", "GPU Package", SensorType.Power, 200)
        ]);
        Assert.Equal("GPU A", snapshot.Values["pc.gpu.name"]);
        Assert.Equal(25d, snapshot.Values["pc.gpu.usage"]);
        Assert.Null(snapshot.Values["pc.gpu.temperature"]); Assert.Null(snapshot.Values["pc.gpu.powerWatts"]);
        Assert.Equal(80d, snapshot.Values["hw.gpu-nvidia.1.temperature.0"]);
        Assert.Contains(snapshot.Sensors, s => s.Id == "hw.gpu-nvidia.1.temperature.0" && s.Unit == "°C");
    }
    [Fact]
    public void UnavailableOrNonFiniteReadingsAreClearedAndJsonSafe()
    {
        var snapshot = HardwareMetrics.Project([
            new("/gpu-nvidia/0", "GPU", "/gpu-nvidia/0/temperature/0", "GPU Core", SensorType.Temperature, float.NaN),
            new("/gpu-nvidia/0", "GPU", "/gpu-nvidia/0/load/0", "GPU Core", SensorType.Load, float.PositiveInfinity)
        ]);
        Assert.Null(snapshot.Values["hw.gpu-nvidia.0.temperature.0"]);
        Assert.Null(snapshot.Values["pc.gpu.temperature"]);
        System.Text.Json.JsonSerializer.Serialize(snapshot.Values);
        Assert.All(HardwareMetrics.Project([]).Values, v => Assert.Null(v.Value));
        var disconnected = HardwareMetrics.Merge(snapshot, HardwareMetrics.Project([]));
        Assert.Contains(disconnected.Values, v => v.Key == "hw.gpu-nvidia.0.temperature.0" && v.Value == null);
        Assert.Equal(snapshot.Sensors.Count, disconnected.Sensors.Count);
    }
    [Fact]
    public void DuplicateLibraryIdsHaveDistinctStableSourceNames()
    {
        HardwareReading[] readings = [
            new("/gpu-nvidia/0", "GPU", "/gpu-nvidia/0/load/3", "GPU Bus", SensorType.Load, 10),
            new("/gpu-nvidia/0", "GPU", "/gpu-nvidia/0/load/3", "GPU Memory", SensorType.Load, 20)
        ];
        var snapshot = HardwareMetrics.Project(readings); var reversed = HardwareMetrics.Project(readings.Reverse());
        Assert.Equal(2, snapshot.Sensors.Select(s => s.Id).Distinct().Count());
        foreach (var sensor in snapshot.Sensors) Assert.Equal(snapshot.Values[sensor.Id], reversed.Values[sensor.Id]);
        Assert.Contains(snapshot.Sensors, s => s.Label.EndsWith("GPU Memory") && Equals(snapshot.Values[s.Id], 20d));
    }
}
