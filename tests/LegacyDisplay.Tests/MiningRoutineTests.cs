using System.Text.Json;
using LegacyDisplay.Protocol;
using LegacyDisplay.Windows.Actions;
using Xunit;

namespace LegacyDisplay.Tests;

public sealed class MiningRoutineTests
{
    [Fact]
    public void PresetsUseConfiguredDataDirectoryAndPreserveOtherActions()
    {
        var root = Path.Combine(Path.GetTempPath(), "legacy-mining-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "publish"));
            File.WriteAllText(Path.Combine(root, "publish", "MiningController.Windows.exe"), "test fixture; never executed");
            var data = Path.Combine(root, "runtime with spaces");
            File.WriteAllText(Path.Combine(root, "controller.json"), JsonSerializer.Serialize(new { data_directory = data }));
            var actions = MiningRoutineActions.Create(root);
            Assert.Equal(5, actions.Count);
            var layout = LayoutDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "dashboard-mining.json")));
            Assert.True(layout.Screen.Width > layout.Screen.Height);
            Assert.Equal(actions.Select(a => a.Id).Order(), layout.Widgets.Where(w => w.Type == "button").Select(w => w.Action).Order());
            Assert.Equal(new[] { "--data-directory", data, "--mode", "paused" }, actions.Single(a => a.Id == "mining.paused").Application!.Arguments);
            Assert.Equal(new[] { "--data-directory", data, "--exit-night" }, actions.Single(a => a.Id == "mining.exit_night").Application!.Arguments);
            Assert.False(Directory.Exists(data));
            var custom = new ActionDefinition { Id = "custom.ping", Label = "Existing", Type = "function", Function = "demo.ping" };
            var merged = MiningRoutineActions.Merge(new() { Actions = [custom] }, actions);
            Assert.Equal(6, merged.Actions.Count); Assert.Equal(custom, merged.Actions[0]);
            Assert.Equal(6, MiningRoutineActions.Merge(merged, actions).Actions.Count);
            merged.Actions[1] = merged.Actions[1] with { Enabled = false };
            Assert.Throws<ArgumentException>(() => MiningRoutineActions.Merge(merged, actions));
            File.WriteAllText(Path.Combine(root, "controller.json"), "{\"data_directory\":\"relative\"}");
            Assert.Throws<ArgumentException>(() => MiningRoutineActions.Create(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
