using System.Text.Json;

namespace LegacyDisplay.Windows.Actions;

public static class MiningRoutineActions
{
    public static IReadOnlyList<ActionDefinition> Create(string projectDirectory)
    {
        var root = Path.GetFullPath(projectDirectory);
        var executable = Path.Combine(root, "publish", "MiningController.Windows.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("MiningRoutine publicado não encontrado.", executable);
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiningController");
        var configuration = Path.Combine(root, "controller.json");
        if (File.Exists(configuration))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(configuration));
            if (json.RootElement.TryGetProperty("data_directory", out var value) && value.ValueKind != JsonValueKind.Null)
            {
                var configured = value.GetString();
                if (!string.IsNullOrEmpty(configured))
                {
                    if (!Path.IsPathFullyQualified(configured)) throw new ArgumentException("data_directory do MiningRoutine deve ser absoluto.");
                    data = Path.GetFullPath(configured);
                }
            }
        }
        ActionDefinition Action(string id, string label, params string[] command) => new() {
            Id = id, Label = label, Type = "application",
            Application = new() { Executable = executable, WorkingDirectory = root, Arguments = ["--data-directory", data, .. command] }
        };
        return [Action("mining.auto", "MiningRoutine · AUTO", "--mode", "auto"),
            Action("mining.paused", "MiningRoutine · PAUSADO", "--mode", "paused"),
            Action("mining.force_mine", "MiningRoutine · FORÇAR MINERAÇÃO", "--mode", "force_mine"),
            Action("mining.exit_night", "MiningRoutine · SAIR DO NOTURNO", "--exit-night"),
            Action("mining.quiet", "MiningRoutine · LUZES E TELAS APAGADAS", "--quiet-mining")];
    }

    public static ActionCatalog Merge(ActionCatalog existing, IReadOnlyList<ActionDefinition> presets)
    {
        existing.Validate();
        var result = existing with { Actions = [.. existing.Actions] };
        foreach (var preset in presets)
        {
            var previous = result.Actions.FirstOrDefault(a => a.Id == preset.Id);
            if (previous != null)
            {
                if (JsonSerializer.Serialize(previous, ActionCatalog.JsonOptions) != JsonSerializer.Serialize(preset, ActionCatalog.JsonOptions))
                    throw new ArgumentException("Ação existente com configuração diferente: " + preset.Id + ". Renomeie-a no Studio antes de importar.");
            }
            else result.Actions.Add(preset);
        }
        result.Validate(); return result;
    }
}
