using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LegacyDisplay.Windows.Actions;

public sealed record HttpAction
{
    public string Url { get; init; } = "";
    public string Method { get; init; } = "POST";
    public string Body { get; init; } = "";
    public string ContentType { get; init; } = "application/json";
    public string BearerToken { get; init; } = "";
    public Dictionary<string, string> Headers { get; init; } = [];
    public int TimeoutMs { get; init; } = 3000;
}
public sealed record ApplicationAction
{
    public string Executable { get; init; } = "";
    public List<string> Arguments { get; init; } = [];
    public string WorkingDirectory { get; init; } = "";
}
public sealed record ActionStep(string ActionId, int DelayMs = 0)
{
    [JsonIgnore] public string Description => $"{ActionId}" + (DelayMs == 0 ? "" : $" · pausa antes: {DelayMs} ms");
}
public sealed record ActionDefinition
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Type { get; init; } = "http";
    public bool Enabled { get; init; } = true;
    public HttpAction? Http { get; init; }
    public ApplicationAction? Application { get; init; }
    public List<ActionStep> Steps { get; init; } = [];
    public string? Function { get; init; }
    [JsonIgnore] public string Description => $"{Label} · {Id}" + (Enabled ? "" : " · desativada");
}
public sealed record ActionCatalog
{
    public int Version { get; init; } = 1;
    public List<ActionDefinition> Actions { get; init; } = [];
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32, WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly Regex Identifier = new("^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}\\z", RegexOptions.CultureInvariant);
    private static readonly Regex Header = new("^[!#$%&'*+.^_`|~0-9A-Za-z-]+\\z", RegexOptions.CultureInvariant);
    public bool Contains(string id) => id == "demo.ping" || Actions.Any(a => a.Id == id && a.Enabled);
    public static ActionCatalog Parse(string json)
    {
        Require(Encoding.UTF8.GetByteCount(json) <= 65536, "Configuração de ações excede 64 KiB.");
        var catalog = JsonSerializer.Deserialize<ActionCatalog>(json, JsonOptions) ?? throw new JsonException("Configuração ausente.");
        catalog.Validate(); return catalog;
    }
    public void Validate()
    {
        Require(Version == 1 && Actions is { Count: <= 128 }, "Versão ou quantidade de ações inválida.");
        var ids = new HashSet<string>(StringComparer.Ordinal) { "demo.ping" };
        foreach (var a in Actions)
        {
            Require(a != null && a.Id != null && Identifier.IsMatch(a.Id) && ids.Add(a.Id), "Id inválido, duplicado ou reservado (demo.ping).");
            Require(!string.IsNullOrWhiteSpace(a.Label) && a.Label.Length <= 80, "Nome da ação deve ter 1 a 80 caracteres.");
            Require(a.Type is "http" or "application" or "sequence" or "function", "Tipo de ação inválido.");
            Require(a.Steps != null && (a.Type == "http") == (a.Http != null) && (a.Type == "application") == (a.Application != null) &&
                (a.Type == "function") == (a.Function != null) && (a.Type == "sequence" || a.Steps.Count == 0), "Parâmetros não correspondem ao tipo da ação.");
            if (a.Http is { } h)
            {
                Require(h.Url is { Length: <= 2048 } && Uri.TryCreate(h.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment), "Informe uma URL HTTP/HTTPS válida, sem usuário/senha na URL.");
                Require(h.Method is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD", "Método HTTP inválido.");
                Require(h.Body != null && Encoding.UTF8.GetByteCount(h.Body) <= 16384 && (h.Method is not ("GET" or "HEAD") || h.Body.Length == 0), "Corpo excede 16 KiB ou não é permitido para GET/HEAD.");
                Require(h.ContentType != null && h.ContentType.Length <= 128 && !h.ContentType.Any(char.IsControl) && MediaTypeHeaderValue.TryParse(h.ContentType, out _), "Content-Type inválido.");
                Require(h.BearerToken != null && h.BearerToken.Length <= 4096 && !h.BearerToken.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)), "Token Bearer inválido.");
                Require(h.TimeoutMs is >= 100 and <= 7500 && h.Headers is { Count: <= 16 }, "Timeout deve ficar entre 100 e 7500 ms; no máximo 16 cabeçalhos.");
                var headerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var header in h.Headers)
                {
                    Require(header.Key.Length <= 64 && Header.IsMatch(header.Key) && headerNames.Add(header.Key) && header.Value != null && header.Value.Length <= 4096 && !header.Value.Any(char.IsControl), "Cabeçalho HTTP inválido ou duplicado.");
                    Require(!new[] { "Host", "Content-Length", "Connection", "Transfer-Encoding", "Content-Type" }.Contains(header.Key, StringComparer.OrdinalIgnoreCase), "Configure Content-Type no campo próprio; cabeçalhos de transporte não são editáveis.");
                    Require(h.BearerToken.Length == 0 || !header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase), "Use Token Bearer ou Authorization, sem duplicar.");
                }
            }
            if (a.Application is { } app)
            {
                Require(!string.IsNullOrWhiteSpace(app.Executable) && app.Executable.Length <= 2048 && Path.IsPathFullyQualified(app.Executable) && Path.GetExtension(app.Executable).Equals(".exe", StringComparison.OrdinalIgnoreCase), "Selecione o caminho absoluto de um aplicativo .exe.");
                Require(app.WorkingDirectory != null && (app.WorkingDirectory.Length == 0 || Path.IsPathFullyQualified(app.WorkingDirectory)), "Pasta de trabalho deve ser um caminho absoluto.");
                Require(app.Arguments is { Count: <= 32 } && app.Arguments.All(x => x != null && x.Length <= 2048 && !x.Contains('\0')), "No máximo 32 argumentos de até 2048 caracteres.");
            }
            if (a.Function != null) Require(Identifier.IsMatch(a.Function), "Id da função interna inválido.");
            if (a.Type == "sequence") Require(a.Steps.Count is >= 1 and <= 16 && a.Steps.All(s => s != null && s.ActionId != null && Identifier.IsMatch(s.ActionId) && s.DelayMs is >= 0 and <= 4000), "Sequência requer 1 a 16 etapas, com pausas de 0 a 4000 ms.");
        }
        foreach (var a in Actions.Where(x => x.Type == "sequence"))
        {
            var count = 0; var delay = 0;
            Visit(a.Id, new HashSet<string>(StringComparer.Ordinal), 0, ref count, ref delay);
            Require(count <= 32 && delay <= 7000, "Sequência expandida excede 32 etapas ou 7 segundos de pausas.");
        }
        Require(Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(this, JsonOptions)) <= 65536, "Configuração de ações excede 64 KiB.");
    }
    private void Visit(string id, HashSet<string> path, int depth, ref int count, ref int delay)
    {
        Require(depth <= 8 && path.Add(id), "Sequência cíclica ou excede 8 níveis.");
        if (id == "demo.ping") count++;
        else
        {
            var action = Actions.FirstOrDefault(a => a.Id == id);
            Require(action != null, "Sequência referencia uma ação inexistente: " + id);
            if (action.Type != "sequence") count++;
            else foreach (var step in action.Steps) { delay += step.DelayMs; Visit(step.ActionId, path, depth + 1, ref count, ref delay); }
        }
        Require(count <= 32 && delay <= 7000, "Sequência expandida excede os limites.");
        path.Remove(id);
    }
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message)
    { if (!condition) throw new JsonException(message); }
}
