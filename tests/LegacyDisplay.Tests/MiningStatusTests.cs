using System.Text.Json;
using LegacyDisplay.Windows;
using LegacyDisplay.Windows.Actions;
using Xunit;

namespace LegacyDisplay.Tests;

public sealed class MiningStatusTests
{
    [Fact]
    public void BlackoutRequiresFreshLiveConfirmedLightsAndDisplaysAndClearsInvalidData()
    {
        var directory = Path.Combine(Path.GetTempPath(), "legacy-mining-status-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = new ActionCatalog { Actions = [new() { Id = "mining.auto", Label = "Auto", Type = "application", Application = new() {
                Executable = Path.Combine(directory, "MiningController.Windows.exe"), Arguments = ["--data-directory", directory, "--mode", "auto"] } }] };
            void Write(bool off, string display, bool dry = false, int age = 0) => File.WriteAllText(Path.Combine(directory, "status.json"), JsonSerializer.Serialize(new {
                timestamp = DateTimeOffset.UtcNow.AddSeconds(-age), process_id = 123, mode = "auto", state = "night_deep", lighting = new { off }, display_state = display, dry_run = dry }));
            Dictionary<string, object?> Read(bool alive = true) => new MiningRoutineStatus(() => catalog, (_, _) => alive).Sample();
            Write(true, "off"); Assert.Equal(true, Read()["mining.blackout"]); Assert.Equal(false, Read(false)["mining.blackout"]);
            Write(false, "off"); Assert.Equal(false, Read()["mining.blackout"]);
            Write(true, "on"); Assert.Equal(false, Read()["mining.blackout"]);
            Write(true, "off", dry: true); Assert.Equal(false, Read()["mining.blackout"]);
            Write(true, "off", age: 16); Assert.Equal(false, Read()["mining.status.available"]);
            Write(true, "off", age: -30); Assert.Equal(false, Read()["mining.blackout"]);
            File.WriteAllText(Path.Combine(directory, "status.json"), "corrupt"); Assert.Equal(false, Read()["mining.blackout"]);
            catalog = new(); Assert.Equal(false, Read()["mining.blackout"]);
        }
        finally { Directory.Delete(directory, true); }
    }
}
