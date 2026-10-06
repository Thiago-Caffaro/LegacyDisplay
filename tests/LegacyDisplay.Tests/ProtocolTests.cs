using System.Text.Json;
using LegacyDisplay.Windows;
using LegacyDisplay.Protocol;
using Xunit;

public sealed class ProtocolTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingCredentialProvidesPairingInstructions(bool directoryExists)
    {
        var directory = Path.Combine(Path.GetTempPath(), "legacydisplay-missing-" + Guid.NewGuid());
        var path = Path.Combine(directory, "device.json");
        try
        {
            if (directoryExists) Directory.CreateDirectory(directory);
            var error = Assert.Throws<PairingRequiredException>(() => CredentialStore.Load(path));
            Assert.Contains("pair --device", error.Message);
            Assert.Contains("--code", error.Message);
            Assert.DoesNotContain(nameof(DirectoryNotFoundException), error.Message);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void ExampleAndSerializationRoundTripAgree()
    {
        var layout = LayoutDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "dashboard.json")));
        var json = JsonSerializer.Serialize(layout, LayoutDocument.JsonOptions);
        Assert.Equal(layout.Widgets.Count, LayoutDocument.Parse(json).Widgets.Count);
        Assert.Contains(layout.Widgets, w => w.Type == "button" && w.Action == "demo.ping");
    }

    [Fact]
    public void SharedConformanceCasesAgree()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "layout-conformance.json")));
        foreach (var test in fixture.RootElement.EnumerateArray())
        {
            var json = test.TryGetProperty("json", out var raw) ? raw.GetString()! : test.GetProperty("layout").GetRawText();
            if (test.GetProperty("valid").GetBoolean()) LayoutDocument.Parse(json);
            else Assert.ThrowsAny<Exception>(() => LayoutDocument.Parse(json));
        }
    }

    [Fact]
    public void FailedValidationDoesNotReachDevice()
    {
        Assert.Throws<JsonException>(() => LayoutDocument.Parse(new string(' ', 65_537)));
        Assert.Throws<JsonException>(() => LayoutDocument.Parse("null"));
    }

    [Fact]
    public void DeviceUrlCannotSmuggleCredentialsOrPaths()
    {
        DeviceClient.ValidateDevice(new Uri("http://127.0.0.1:8765"));
        foreach (var url in new[] { "ftp://localhost", "http://user:secret@localhost", "http://localhost/api", "http://localhost/?token=secret" })
            Assert.Throws<ArgumentException>(() => DeviceClient.ValidateDevice(new Uri(url)));
    }

    [Fact]
    public void WindowsCredentialRoundTripDoesNotStoreTokenInCleartext()
    {
        var directory = Path.Combine(Path.GetTempPath(), "legacydisplay-test-" + Guid.NewGuid());
        var path = Path.Combine(directory, "device.json");
        try
        {
            var token = new string('a', 64);
            CredentialStore.Save(path, new Uri("http://127.0.0.1:8765"), token);
            Assert.DoesNotContain(token, File.ReadAllText(path));
            Assert.Equal(token, CredentialStore.Load(path).Token);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void RealMetricsAreBoundedAndDemoIsExplicit()
    {
        var metrics = new WindowsMetrics();
        var first = metrics.Sample(false);
        Assert.InRange(Convert.ToDouble(first["pc.memory.usage"]), 0, 100);
        Assert.False(first.ContainsKey("demo.gpu.temperature"));
        Thread.Sleep(50);
        var second = metrics.Sample(true);
        Assert.InRange(Convert.ToDouble(second["pc.cpu.usage"]), 0, 100);
        Assert.True(second.ContainsKey("demo.gpu.temperature"));
    }
}
