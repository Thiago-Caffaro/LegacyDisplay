using System.Net;
using System.Text;
using System.Text.Json;
using LegacyDisplay.Protocol;
using LegacyDisplay.Windows.Actions;
using Xunit;

namespace LegacyDisplay.Tests;

public sealed class ActionTests
{
    private static ActionDefinition Http(string id = "api.test") => new() { Id = id, Label = "Chamada local", Type = "http", Http = new() { Url = "http://127.0.0.1:8123/action", Body = "{\"on\":true}", BearerToken = "TEST_TOKEN", Headers = new() { ["X-Test"] = "local" } } };
    private static LayoutDocument Layout(string action) => new() { Version = 1, Screen = new() { Width = 800, Height = 1280 }, Widgets = [new() { Id = "button", Type = "button", Text = "Testar", X = 0, Y = 0, Width = 100, Height = 60, Action = action }] };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request, cancellationToken); }
    [Fact]
    public void CatalogRejectsUnknownFieldsIdsCyclesAndMissingDependencies()
    {
        Assert.Throws<JsonException>(() => ActionCatalog.Parse("{\"version\":1,\"actions\":[],\"shell\":\"ignored?\"}"));
        Assert.Throws<JsonException>(() => new ActionCatalog { Actions = [Http("demo.ping")] }.Validate());
        Assert.Throws<JsonException>(() => new ActionCatalog { Actions = [Http(), Http()] }.Validate());
        ActionDefinition Seq(string id, string child) => new() { Id = id, Label = id, Type = "sequence", Steps = [new(child)] };
        Assert.Throws<JsonException>(() => new ActionCatalog { Actions = [Seq("a", "b"), Seq("b", "a")] }.Validate());
        Assert.Throws<JsonException>(() => new ActionCatalog { Actions = [Seq("a", "missing")] }.Validate());
        Assert.Throws<JsonException>(() => new ActionCatalog { Actions = [new() { Id = "bad", Label = "Bad", Type = "sequence", Steps = [new("demo.ping", 4000), new("demo.ping", 4000)] }] }.Validate());
    }
    [Fact]
    public void HttpValidationRejectsAmbiguousAndMalformedRequests()
    {
        var action = Http(); var h = action.Http!;
        foreach (var invalid in new[] { h with { Url = "file:///C:/example" }, h with { Method = "GET" }, h with { TimeoutMs = 8000 },
            h with { Headers = new() { ["Host"] = "elsewhere" } }, h with { Headers = new() { ["X-Test"] = "bad\r\nInjected: true" } }, h with { Headers = new() { ["Authorization"] = "Duplicate" } } })
            Assert.Throws<JsonException>(() => new ActionCatalog { Actions = [action with { Http = invalid }] }.Validate());
    }
    [Fact]
    public void ProtectedStoreIsAtomicAndRejectsInvalidChanges()
    {
        var directory = Path.Combine(Path.GetTempPath(), "legacy-actions-" + Guid.NewGuid()); var path = Path.Combine(directory, "actions.json");
        try
        {
            Assert.Empty(ActionStore.Load(path).Actions);
            ActionStore.Save(path, new() { Actions = [Http()] }); var saved = File.ReadAllText(path);
            Assert.DoesNotContain("TEST_TOKEN", saved); Assert.DoesNotContain("127.0.0.1", saved);
            Assert.Equal("TEST_TOKEN", ActionStore.Load(path).Actions[0].Http!.BearerToken);
            Assert.Throws<JsonException>(() => ActionStore.Save(path, new() { Actions = [Http("demo.ping")] }));
            Assert.Equal(saved, File.ReadAllText(path)); Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task HttpCallUsesLocalParametersAndReturnsNoResponseBodyOrCredentials()
    {
        var calls = 0;
        using var executor = new ActionExecutor(() => new() { Actions = [Http()] }, new Handler(async (request, token) => {
            calls++; Assert.Equal("POST", request.Method.Method); Assert.Equal("Bearer TEST_TOKEN", request.Headers.Authorization!.ToString());
            Assert.Equal("{\"on\":true}", await request.Content!.ReadAsStringAsync(token)); Assert.Equal("application/json", request.Content.Headers.ContentType!.MediaType);
            return new(HttpStatusCode.OK) { Content = new StringContent("SECRET_SERVER_RESPONSE") };
        }));
        var result = await executor.InvokeAsync(Guid.NewGuid().ToString(), "button", "api.test", Layout("api.test"), default);
        Assert.True(result.Success); Assert.Equal("Chamada local · HTTP 200", result.Message); Assert.Equal(1, calls);
        Assert.False((await executor.InvokeAsync(Guid.NewGuid().ToString(), "wrong-widget", "api.test", Layout("api.test"), default)).Success);
        Assert.False((await executor.InvokeAsync("invalid-request", "button", "api.test", Layout("api.test"), default)).Success);
        Assert.Equal(1, calls);
    }
    [Fact]
    public async Task CatalogReloadAndDisabledActionsNeverReuseOldPermission()
    {
        var catalog = new ActionCatalog { Actions = [Http()] }; var calls = 0;
        using var executor = new ActionExecutor(() => catalog, new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }));
        var first = await executor.InvokeAsync(Guid.NewGuid().ToString(), "button", "api.test", Layout("api.test"), default); Assert.True(first.Success);
        catalog = new() { Actions = [Http() with { Enabled = false }] };
        Assert.False((await executor.InvokeAsync(Guid.NewGuid().ToString(), "button", "api.test", Layout("api.test"), default)).Success); Assert.Equal(1, calls);
    }
    [Fact]
    public async Task CorruptedProtectedFileNeverFallsBackToPreviousExecutableParameters()
    {
        var directory = Path.Combine(Path.GetTempPath(), "legacy-action-corrupt-" + Guid.NewGuid()); var path = Path.Combine(directory, "actions.json");
        try
        {
            ActionStore.Save(path, new() { Actions = [Http()] }); var calls = 0;
            using var executor = new ActionExecutor(() => ActionStore.Load(path), new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }));
            Assert.True((await executor.InvokeAsync(Guid.NewGuid().ToString(), "button", "api.test", Layout("api.test"), default)).Success);
            File.WriteAllText(path, "corrupted");
            Assert.False((await executor.InvokeAsync(Guid.NewGuid().ToString(), "button", "api.test", Layout("api.test"), default)).Success); Assert.Equal(1, calls);
            Assert.True((await executor.InvokeAsync(Guid.NewGuid().ToString(), "button", "demo.ping", Layout("demo.ping"), default)).Success);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task SequenceStopsAtFailureAndDoesNotLaunchLaterApplications()
    {
        var applicationCalls = 0; var httpCalls = 0;
        var app = new ActionDefinition { Id = "app.test", Label = "App", Type = "application", Application = new() { Executable = Path.Combine(Environment.SystemDirectory, "notepad.exe") } };
        var sequence = new ActionDefinition { Id = "seq.test", Label = "Sequência", Type = "sequence", Steps = [new("api.test"), new("app.test")] };
        using var executor = new ActionExecutor(() => new() { Actions = [Http(), app, sequence] }, new Handler((_, _) => { httpCalls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)); }), (_, _) => { applicationCalls++; return Task.CompletedTask; });
        var outcome = await executor.InvokeAsync(Guid.NewGuid().ToString(), "button", "seq.test", Layout("seq.test"), default);
        Assert.False(outcome.Success); Assert.Contains("403", outcome.Message); Assert.Equal(1, httpCalls); Assert.Equal(0, applicationCalls);
    }
    [Fact]
    public async Task ConcurrentTouchesAreRejectedAndTimeoutReleasesTheActionSlot()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = Http() with { Http = Http().Http! with { TimeoutMs = 100 } };
        using var executor = new ActionExecutor(() => new() { Actions = [action] }, new Handler(async (_, token) => { entered.TrySetResult(); await Task.Delay(10000, token); return new(HttpStatusCode.OK); }));
        var first = executor.InvokeAsync(Guid.NewGuid().ToString(), "button", "api.test", Layout("api.test"), default);
        await entered.Task;
        var busy = await executor.InvokeAsync(Guid.NewGuid().ToString(), "button", "api.test", Layout("api.test"), default);
        Assert.False(busy.Success); Assert.Contains("andamento", busy.Message);
        Assert.Contains("tempo limite", (await first).Message);
        Assert.True((await executor.InvokeAsync(Guid.NewGuid().ToString(), "button", "demo.ping", Layout("demo.ping"), default)).Success);
    }
    [Fact]
    public async Task SequenceUsesRegisteredFunctionsAndApplicationArgumentsAsSeparateValues()
    {
        var seen = new List<string>();
        var app = new ApplicationAction { Executable = Path.Combine(Environment.SystemDirectory, "notepad.exe"), Arguments = ["C:\\A folder\\file.txt", "literal & value"] };
        var info = ActionExecutor.CreateLaunchInfo(app); Assert.False(info.UseShellExecute); Assert.Equal(app.Arguments, info.ArgumentList);
        var catalog = new ActionCatalog { Actions = [
            new() { Id = "function.test", Label = "Função", Type = "function", Function = "custom.test" },
            new() { Id = "app.test", Label = "App", Type = "application", Application = app },
            new() { Id = "seq.test", Label = "Sequência", Type = "sequence", Steps = [new("function.test"), new("app.test", 1)] }
        ] };
        using var executor = new ActionExecutor(() => catalog, launchApplication: (_, _) => { seen.Add("app"); return Task.CompletedTask; }, customFunctions: new Dictionary<string, Func<CancellationToken, Task<ActionOutcome>>> {
            ["custom.test"] = _ => { seen.Add("function"); return Task.FromResult(new ActionOutcome(true, "Concluído")); }
        });
        Assert.True((await executor.InvokeAsync(Guid.NewGuid().ToString(), "button", "seq.test", Layout("seq.test"), default)).Success); Assert.Equal(new[] { "function", "app" }, seen);
    }
}
