namespace LegacyDisplay.Windows;

/// <summary>Optional metrics from an external integration. The core works without a provider.</summary>
public interface IAdditionalMetrics
{
    Dictionary<string, object?> Sample();
}
