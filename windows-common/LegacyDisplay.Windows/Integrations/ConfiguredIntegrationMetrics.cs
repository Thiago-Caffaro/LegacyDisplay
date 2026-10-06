using LegacyDisplay.Windows.Actions;

namespace LegacyDisplay.Windows;

/// <summary>Loads only integrations explicitly configured in the user's action catalog.</summary>
public sealed class ConfiguredIntegrationMetrics(Func<ActionCatalog> catalog) : IAdditionalMetrics
{
    private readonly MiningRoutineStatus mining = new(catalog);
    public Dictionary<string, object?> Sample()
    {
        try
        {
            // No mining files are accessed unless the user has configured a mining action.
            if (!catalog().Actions.Any(a => a.Id.StartsWith("mining.", StringComparison.Ordinal) && a.Application != null))
                return new() { ["display.blackout"] = false };
            var values = mining.Sample();
            values["display.blackout"] = values["mining.blackout"];
            return values;
        }
        catch (Exception)
        {
            // An unavailable optional catalog must not interrupt core PC metrics.
            return new() { ["display.blackout"] = false };
        }
    }
}
