using LegacyDisplay.Windows;
using LegacyDisplay.Windows.Actions;
using Xunit;

namespace LegacyDisplay.Tests;

public sealed class IntegrationMetricsTests
{
    [Fact]
    public void CoreWithoutConfiguredIntegrationEmitsOnlyGenericDisplayState()
    {
        var source = new ConfiguredIntegrationMetrics(() => new ActionCatalog());
        var values = source.Sample();
        Assert.Single(values);
        Assert.Equal(false, values["display.blackout"]);
    }
    [Fact]
    public void UnavailableCatalogDoesNotInterruptCoreMetrics()
    {
        var source = new ConfiguredIntegrationMetrics(() => throw new IOException("Unavailable catalog"));
        Assert.Equal(false, source.Sample()["display.blackout"]);
    }
    [Fact]
    public void UnavailableIntegrationClearsGenericBlackout()
    {
        var source = new ConfiguredIntegrationMetrics(() => new ActionCatalog { Actions = [new() {
            Id = "mining.auto", Type = "application", Application = new() {
                Executable = Path.Combine(Path.GetTempPath(), "missing-controller.exe") } }] });
        Assert.Equal(false, source.Sample()["display.blackout"]);
    }
}
