using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using LegacyDisplay.Protocol;

namespace LegacyDisplay.Windows.Actions;

public sealed record ActionOutcome(bool Success, string Message);

public sealed class ActionExecutor : IDisposable
{
    private readonly Func<ActionCatalog> load;
    private readonly HttpClient http;
    private readonly Func<ApplicationAction, CancellationToken, Task> launch;
    private readonly IReadOnlyDictionary<string, Func<CancellationToken, Task<ActionOutcome>>> functions;
    private readonly SemaphoreSlim execution = new(1, 1);
    public ActionExecutor(Func<ActionCatalog> loadCatalog, HttpMessageHandler? handler = null,
        Func<ApplicationAction, CancellationToken, Task>? launchApplication = null,
        IReadOnlyDictionary<string, Func<CancellationToken, Task<ActionOutcome>>>? customFunctions = null)
    {
        load = loadCatalog;
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
        launch = launchApplication ?? LaunchAsync;
        var registry = customFunctions?.ToDictionary(x => x.Key, x => x.Value) ?? [];
        registry["demo.ping"] = _ => Task.FromResult(new ActionOutcome(true, "PC recebeu o toque!"));
        functions = registry;
    }
    public async Task<ActionOutcome> InvokeAsync(string? requestId, string? widgetId, string? actionId, LayoutDocument layout, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(requestId, out _) || actionId == null || !layout.Widgets.Any(w => w.Id == widgetId && w.Type == "button" && w.Action == actionId))
            return new(false, "Ação não permitida");
        if (!await execution.WaitAsync(0, cancellationToken)) return new(false, "Outra ação está em andamento. Aguarde e tente novamente.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(8000);
        try
        {
            var catalog = actionId == "demo.ping" ? new ActionCatalog() : load();
            catalog.Validate();
            if (!catalog.Contains(actionId)) return new(false, "Ação não permitida");
            var outcome = await ExecuteAsync(actionId, catalog, deadline.Token);
            return outcome with { Message = outcome.Message.Length <= 240 ? outcome.Message : outcome.Message[..240] };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, "Ação excedeu o tempo limite."); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return new(false, "Não foi possível executar a ação. Confira a configuração no Studio."); }
        finally { execution.Release(); }
    }
    private async Task<ActionOutcome> ExecuteAsync(string id, ActionCatalog catalog, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (id == "demo.ping") return await functions[id](cancellationToken);
        var action = catalog.Actions.FirstOrDefault(a => a.Id == id && a.Enabled);
        if (action == null) return new(false, "Ação não cadastrada ou desativada neste PC");
        switch (action.Type)
        {
            case "function":
                return functions.TryGetValue(action.Function!, out var function) ? await function(cancellationToken) : new(false, "Função interna indisponível nesta versão do Agent");
            case "application":
                await launch(action.Application!, cancellationToken);
                return new(true, action.Label + " · aplicativo iniciado");
            case "http":
                var h = action.Http!;
                using (var request = new HttpRequestMessage(new HttpMethod(h.Method), h.Url))
                using (var requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    requestDeadline.CancelAfter(h.TimeoutMs);
                    if (h.Method is not ("GET" or "HEAD"))
                    {
                        request.Content = new StringContent(h.Body, Encoding.UTF8);
                        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(h.ContentType);
                    }
                    if (h.BearerToken.Length != 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", h.BearerToken);
                    foreach (var header in h.Headers)
                        if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                        {
                            if (request.Content == null || !request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value)) return new(false, "Cabeçalho incompatível com a chamada HTTP");
                        }
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestDeadline.Token);
                    // Responses and credentials never travel back to the tablet; only status and the local label.
                    return new(response.IsSuccessStatusCode, action.Label + " · HTTP " + (int)response.StatusCode);
                }
            case "sequence":
                foreach (var step in action.Steps)
                {
                    await Task.Delay(step.DelayMs, cancellationToken);
                    var result = await ExecuteAsync(step.ActionId, catalog, cancellationToken);
                    if (!result.Success) return new(false, action.Label + " · " + result.Message);
                }
                return new(true, action.Label + " · sequência concluída");
            default: return new(false, "Tipo de ação indisponível");
        }
    }
    public static Task LaunchAsync(ApplicationAction application, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(application.Executable)) throw new FileNotFoundException("Aplicativo não encontrado.");
        if (application.WorkingDirectory.Length > 0 && !Directory.Exists(application.WorkingDirectory)) throw new DirectoryNotFoundException();
        var start = CreateLaunchInfo(application);
        using var process = Process.Start(start) ?? throw new IOException("Não foi possível iniciar o aplicativo.");
        return Task.CompletedTask;
    }
    public static ProcessStartInfo CreateLaunchInfo(ApplicationAction application)
    {
        var info = new ProcessStartInfo(application.Executable) { UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = application.WorkingDirectory.Length == 0 ? Path.GetDirectoryName(application.Executable)! : application.WorkingDirectory };
        foreach (var argument in application.Arguments) info.ArgumentList.Add(argument);
        return info;
    }
    public void Dispose() { http.Dispose(); execution.Dispose(); }
}
