using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LegacyDisplay.Windows.Actions;

public static class ActionStore
{
    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(CredentialStore.DefaultPath)!, "actions.json");
    private sealed record Envelope(int Version, string ProtectedConfiguration);
    public static ActionCatalog Load(string path)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 131072) throw new JsonException("Arquivo de ações excede o limite.");
        var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path)) ?? throw new JsonException("Arquivo de ações inválido.");
        if (envelope.Version != 1 || string.IsNullOrEmpty(envelope.ProtectedConfiguration)) throw new JsonException("Versão do arquivo de ações inválida.");
        var plaintext = ProtectedData.Unprotect(Convert.FromBase64String(envelope.ProtectedConfiguration), null, DataProtectionScope.CurrentUser);
        try { return ActionCatalog.Parse(Encoding.UTF8.GetString(plaintext)); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    public static void Save(string path, ActionCatalog catalog)
    {
        catalog.Validate();
        var plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(catalog, ActionCatalog.JsonOptions));
        byte[] protectedBytes;
        try { protectedBytes = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        var absolute = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        var temporary = absolute + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Envelope(1, Convert.ToBase64String(protectedBytes))));
            File.Move(temporary, absolute, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
