using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LegacyDisplay.Windows;

public sealed record DeviceCredential(string Device, string Token);

public sealed class PairingRequiredException(string path) : Exception(
    $"Nenhum pareamento salvo em '{Path.GetFullPath(path)}'.\n" +
    "Abra o app no tablet e execute:\n" +
    "LegacyDisplay.Agent.exe pair --device http://IP_DO_TABLET:8765 --code CODIGO_DA_TELA");

public static class CredentialStore
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegacyDisplay", "agent-device.json");
    public static void Save(string path, Uri device, string token)
    {
        var protectedToken = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        var json = JsonSerializer.Serialize(new DeviceCredential(device.AbsoluteUri, Convert.ToBase64String(protectedToken)));
        var absolutePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        var temporaryPath = absolutePath + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, absolutePath, true);
    }

    public static DeviceCredential Load(string path)
    {
        string json;
        try { json = File.ReadAllText(path); }
        catch (IOException error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new PairingRequiredException(path);
        }
        var stored = JsonSerializer.Deserialize<DeviceCredential>(json) ?? throw new JsonException("Invalid credentials.");
        var token = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored.Token), null, DataProtectionScope.CurrentUser));
        var device = new Uri(stored.Device);
        DeviceClient.ValidateDevice(device);
        return new DeviceCredential(stored.Device, token);
    }
}
