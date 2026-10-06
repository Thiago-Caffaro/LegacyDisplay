using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LegacyDisplay.Protocol;

namespace LegacyDisplay.Windows;

public sealed record DeviceStatus(int ProtocolVersion, string Name, bool Paired, bool AgentConnected, int Width, int Height);
public sealed record DeviceConfig(bool KeepScreenOn, bool StartOnBoot);

public sealed class DeviceClient : IDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
    public DeviceClient(Uri device, string? token = null)
    {
        ValidateDevice(device);
        http.BaseAddress = device;
        if (token is not null) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public static void ValidateDevice(Uri device)
    {
        if (!device.IsAbsoluteUri || device.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(device.UserInfo) ||
            device.AbsolutePath != "/" || !string.IsNullOrEmpty(device.Query) || !string.IsNullOrEmpty(device.Fragment))
            throw new ArgumentException("Device must be a base URL such as http://192.168.1.70:8765.");
    }

    public async Task<string> PairAsync(string code, CancellationToken cancellationToken)
    {
        if (code.Length != 6 || !code.All(char.IsAsciiDigit)) throw new ArgumentException("Pairing code must contain six digits.");
        using var body = new StringContent(JsonSerializer.Serialize(new { code }), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("/api/v1/pair", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var token = json.RootElement.GetProperty("token").GetString();
        if (token is null || token.Length != 64 || !token.All(Uri.IsHexDigit)) throw new JsonException("Invalid device token.");
        return token;
    }

    public async Task<DeviceStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync("/api/v1/status", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = json.RootElement;
        var version = root.GetProperty("protocolVersion").GetInt32();
        var name = root.GetProperty("name").GetString() ?? throw new JsonException("Missing device name.");
        var screen = root.GetProperty("screen");
        var width = screen.GetProperty("width").GetInt32(); var height = screen.GetProperty("height").GetInt32();
        if (version != 1 || width is < 1 or > 4096 || height is < 1 or > 4096 || name.Length > 256) throw new JsonException("Unsupported device status.");
        return new DeviceStatus(version, name, root.GetProperty("paired").GetBoolean(), root.GetProperty("agentConnected").GetBoolean(), width, height);
    }

    public async Task<DeviceConfig> GetConfigAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync("/api/v1/config", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return new DeviceConfig(json.RootElement.GetProperty("keepScreenOn").GetBoolean(), json.RootElement.GetProperty("startOnBoot").GetBoolean());
    }

    public async Task ConfigureAsync(DeviceConfig config, CancellationToken cancellationToken)
    {
        using var body = new StringContent(JsonSerializer.Serialize(config, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json");
        using var response = await http.PutAsync("/api/v1/config", body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<LayoutDocument> GetLayoutAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync("/api/v1/layout", cancellationToken);
        response.EnsureSuccessStatusCode();
        return LayoutDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public async Task DeployAsync(string json, CancellationToken cancellationToken)
    {
        LayoutDocument.Parse(json);
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("/api/v1/layout", body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => http.Dispose();
}
