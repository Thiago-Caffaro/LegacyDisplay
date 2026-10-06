using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LegacyDisplay.Protocol;
using LegacyDisplay.Windows.Actions;
using Microsoft.Win32;

namespace LegacyDisplay.Studio;

public partial class ActionsWindow : Window
{
    public ActionCatalog Catalog { get; private set; }
    public Dictionary<string, string> RenamedIds { get; } = new(StringComparer.Ordinal);
    public bool ActionsSaved => saved;
    private readonly string storePath;
    private readonly bool smoke;
    private string savedJson;
    private ActionDefinition? selected;
    private List<ActionStep> steps = [];
    private bool loading, saved, discarding;
    private CancellationTokenSource? testLifetime;
    public ActionsWindow(string path, bool smokeTest = false)
    {
        storePath = path; smoke = smokeTest; Catalog = ActionStore.Load(path);
        savedJson = JsonSerializer.Serialize(Catalog, ActionCatalog.JsonOptions);
        InitializeComponent(); RefreshList(Catalog.Actions.FirstOrDefault()?.Id);
    }
    private void RefreshList(string? id)
    {
        loading = true;
        ActionsList.ItemsSource = Catalog.Actions;
        ActionsList.SelectedItem = Catalog.Actions.FirstOrDefault(a => a.Id == id);
        loading = false;
        LoadForm(ActionsList.SelectedItem as ActionDefinition);
    }
    private void LoadForm(ActionDefinition? action)
    {
        selected = action; FormPanel.IsEnabled = action != null; DeleteActionButton.IsEnabled = action != null;
        ApplyActionButton.IsEnabled = TestActionButton.IsEnabled = action != null;
        if (action == null) return;
        ActionIdBox.Text = action.Id; ActionLabelBox.Text = action.Label; EnabledActionCheck.IsChecked = action.Enabled;
        ActionTypeText.Text = action.Type switch { "http" => "CHAMADA HTTP", "application" => "ABRIR APLICATIVO", "sequence" => "SEQUÊNCIA", _ => "FUNÇÃO INTERNA" };
        HttpPanel.Visibility = action.Type == "http" ? Visibility.Visible : Visibility.Collapsed;
        ApplicationPanel.Visibility = action.Type == "application" ? Visibility.Visible : Visibility.Collapsed;
        SequencePanel.Visibility = action.Type == "sequence" ? Visibility.Visible : Visibility.Collapsed;
        FunctionPanel.Visibility = action.Type == "function" ? Visibility.Visible : Visibility.Collapsed;
        if (action.Http is { } h)
        {
            HttpUrlBox.Text = h.Url; HttpMethodBox.Text = h.Method; HttpContentTypeBox.Text = h.ContentType;
            HttpTimeoutBox.Text = h.TimeoutMs.ToString(CultureInfo.InvariantCulture); HttpBodyBox.Text = h.Body;
            HttpHeadersBox.Text = JsonSerializer.Serialize(h.Headers, ActionCatalog.JsonOptions); BearerTokenBox.Password = h.BearerToken;
        }
        if (action.Application is { } app)
        {
            ExecutableBox.Text = app.Executable; ArgumentsBox.Text = string.Join(Environment.NewLine, app.Arguments); WorkingDirectoryBox.Text = app.WorkingDirectory;
        }
        FunctionBox.Text = action.Function ?? "demo.ping";
        steps = [.. action.Steps]; RefreshSteps();
        StepActionBox.ItemsSource = new[] { "demo.ping" }.Concat(Catalog.Actions.Where(a => a.Id != action.Id).Select(a => a.Id));
        StepActionBox.SelectedIndex = 0; StepDelayBox.Text = "0";
    }
    private ActionDefinition CaptureForm()
    {
        var action = selected ?? throw new InvalidOperationException("Selecione uma ação.");
        return action with {
            Id = ActionIdBox.Text.Trim(), Label = ActionLabelBox.Text.Trim(), Enabled = EnabledActionCheck.IsChecked == true,
            Http = action.Type != "http" ? null : new HttpAction {
                Url = HttpUrlBox.Text.Trim(), Method = HttpMethodBox.Text, ContentType = HttpContentTypeBox.Text.Trim(), Body = HttpBodyBox.Text,
                TimeoutMs = int.Parse(HttpTimeoutBox.Text, CultureInfo.InvariantCulture), BearerToken = BearerTokenBox.Password,
                Headers = JsonSerializer.Deserialize<Dictionary<string, string>>(HttpHeadersBox.Text, ActionCatalog.JsonOptions) ?? throw new JsonException("Cabeçalhos devem ser um objeto JSON.") },
            Application = action.Type != "application" ? null : new ApplicationAction { Executable = ExecutableBox.Text.Trim(), WorkingDirectory = WorkingDirectoryBox.Text.Trim(),
                Arguments = ArgumentsBox.Text.Length == 0 ? [] : ArgumentsBox.Text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList() },
            Steps = action.Type == "sequence" ? [.. steps] : [], Function = action.Type == "function" ? FunctionBox.Text.Trim() : null
        };
    }
    private bool ApplyCurrent()
    {
        if (selected == null) return true;
        try
        {
            var updated = CaptureForm(); var oldId = selected.Id;
            var candidate = Catalog with { Actions = Catalog.Actions.Select(a => a.Id == oldId ? updated : a).ToList() };
            if (oldId != updated.Id)
                candidate = candidate with { Actions = candidate.Actions.Select(a => a with { Steps = a.Steps.Select(s => s.ActionId == oldId ? s with { ActionId = updated.Id } : s).ToList() }).ToList() };
            candidate.Validate(); Catalog = candidate;
            if (oldId != updated.Id)
            {
                foreach (var key in RenamedIds.Keys.ToArray()) if (RenamedIds[key] == oldId) RenamedIds[key] = updated.Id;
                RenamedIds[oldId] = updated.Id;
            }
            RefreshList(updated.Id); SetStatus("Ação aplicada. Salvar ações disponibiliza o cadastro ao Agent."); return true;
        }
        catch (Exception error) { SetStatus(Friendly(error), true); return false; }
    }
    private void ActionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (loading) return;
        var next = ActionsList.SelectedItem as ActionDefinition;
        if (selected != null && !ApplyCurrent()) { loading = true; ActionsList.SelectedItem = selected; loading = false; return; }
        RefreshList(next?.Id);
    }
    private void AddClick(object sender, RoutedEventArgs e)
    {
        if (!ApplyCurrent()) return;
        if (Catalog.Actions.Count >= 128) { SetStatus("Limite de 128 ações atingido.", true); return; }
        var type = (string?)NewTypeBox.SelectedValue ?? "http";
        var index = 1; while (Catalog.Actions.Any(a => a.Id == "action-" + index)) index++;
        var action = new ActionDefinition { Id = "action-" + index, Label = "Nova ação", Type = type,
            Http = type == "http" ? new HttpAction { Url = "http://127.0.0.1:8080/" } : null,
            Application = type == "application" ? new ApplicationAction { Executable = System.IO.Path.Combine(Environment.SystemDirectory, "notepad.exe") } : null,
            Steps = type == "sequence" ? [new("demo.ping")] : [], Function = type == "function" ? "demo.ping" : null };
        Catalog = Catalog with { Actions = [.. Catalog.Actions, action] }; RefreshList(action.Id);
    }
    private void ApplyClick(object sender, RoutedEventArgs e) => ApplyCurrent();
    private void ImportMiningClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Selecione a pasta do projeto MiningRoutine" };
        if (dialog.ShowDialog(this) == true) ImportMiningRoutine(dialog.FolderName);
    }
    internal bool ImportMiningRoutine(string directory)
    {
        if (!ApplyCurrent()) return false;
        try
        {
            Catalog = MiningRoutineActions.Merge(Catalog, MiningRoutineActions.Create(directory));
            RefreshList("mining.quiet");
            SetStatus("MiningRoutine importado. Salve as ações e escolha mining.quiet nas propriedades do botão.");
            return true;
        }
        catch (Exception error) { SetStatus(Friendly(error), true); return false; }
    }
    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (selected == null) return;
        try
        {
            var candidate = Catalog with { Actions = Catalog.Actions.Where(a => a.Id != selected.Id).ToList() };
            candidate.Validate(); Catalog = candidate; RefreshList(Catalog.Actions.FirstOrDefault()?.Id);
            SetStatus("Ação removida do cadastro local. Salve para atualizar o Agent.");
        }
        catch (Exception error) { SetStatus(Friendly(error), true); }
    }
    private void BrowseApplicationClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Aplicativo Windows (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) ExecutableBox.Text = dialog.FileName;
    }
    private void RefreshSteps(int index = -1) { StepsList.ItemsSource = steps.ToArray(); StepsList.SelectedIndex = index; }
    private void StepSelected(object sender, SelectionChangedEventArgs e)
    {
        if (StepsList.SelectedItem is not ActionStep step) return;
        StepActionBox.SelectedItem = step.ActionId; StepDelayBox.Text = step.DelayMs.ToString(CultureInfo.InvariantCulture);
    }
    private ActionStep CaptureStep()
    {
        var id = StepActionBox.SelectedItem as string ?? throw new ArgumentException("Escolha a ação da etapa.");
        var delay = int.Parse(StepDelayBox.Text, CultureInfo.InvariantCulture);
        if (delay is < 0 or > 4000) throw new ArgumentException("Pausa deve ficar entre 0 e 4000 ms.");
        return new(id, delay);
    }
    private void AddStepClick(object sender, RoutedEventArgs e) { try { steps.Add(CaptureStep()); RefreshSteps(steps.Count - 1); } catch (Exception error) { SetStatus(Friendly(error), true); } }
    private void UpdateStepClick(object sender, RoutedEventArgs e) { try { var index = StepsList.SelectedIndex; if (index >= 0) { steps[index] = CaptureStep(); RefreshSteps(index); } } catch (Exception error) { SetStatus(Friendly(error), true); } }
    private void DeleteStepClick(object sender, RoutedEventArgs e) { var index = StepsList.SelectedIndex; if (index >= 0) { steps.RemoveAt(index); RefreshSteps(Math.Min(index, steps.Count - 1)); } }
    private void StepUpClick(object sender, RoutedEventArgs e) => MoveStep(-1);
    private void StepDownClick(object sender, RoutedEventArgs e) => MoveStep(1);
    private void MoveStep(int direction)
    {
        var index = StepsList.SelectedIndex; var next = index + direction;
        if (index < 0 || next < 0 || next >= steps.Count) return;
        (steps[index], steps[next]) = (steps[next], steps[index]); RefreshSteps(next);
    }
    private bool SaveAll()
    {
        if (!ApplyCurrent()) return false;
        try { ActionStore.Save(storePath, Catalog); savedJson = JsonSerializer.Serialize(Catalog, ActionCatalog.JsonOptions); saved = true; return true; }
        catch (Exception error) { SetStatus(Friendly(error), true); return false; }
    }
    private void SaveClick(object sender, RoutedEventArgs e) { if (!SaveAll()) return; if (smoke) Close(); else DialogResult = true; }
    private void CancelClick(object sender, RoutedEventArgs e) { discarding = true; Close(); }
    private bool HasEdits()
    {
        if (JsonSerializer.Serialize(Catalog, ActionCatalog.JsonOptions) != savedJson) return true;
        if (selected == null) return false;
        try { return JsonSerializer.Serialize(selected, ActionCatalog.JsonOptions) != JsonSerializer.Serialize(CaptureForm(), ActionCatalog.JsonOptions); }
        catch (Exception) { return true; }
    }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (!smoke && !saved && !discarding && HasEdits())
        {
            var answer = MessageBox.Show(this, "Salvar as alterações nas ações?", "LegacyDisplay", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel || answer == MessageBoxResult.Yes && !SaveAll()) { e.Cancel = true; return; }
        }
        testLifetime?.Cancel();
    }
    private async void TestClick(object sender, RoutedEventArgs e)
    {
        if (testLifetime != null || !ApplyCurrent() || selected == null) return;
        using var lifetime = new CancellationTokenSource(); testLifetime = lifetime;
        FormPanel.IsEnabled = ActionsList.IsEnabled = AddActionButton.IsEnabled = DeleteActionButton.IsEnabled = SaveActionsButton.IsEnabled = false;
        ApplyActionButton.IsEnabled = TestActionButton.IsEnabled = false;
        ImportMiningButton.IsEnabled = false;
        try
        {
            SetStatus("Executando teste no PC…");
            using var executor = new ActionExecutor(() => Catalog);
            var layout = new LayoutDocument { Version = 1, Screen = new Screen { Width = 800, Height = 1280 }, Widgets = [new Widget { Id = "test", Type = "button", Text = "Testar", X = 0, Y = 0, Width = 100, Height = 60, Action = selected.Id }] };
            layout.Validate();
            var result = await executor.InvokeAsync(Guid.NewGuid().ToString(), "test", selected.Id, layout, lifetime.Token);
            SetStatus(result.Message, !result.Success);
        }
        catch (OperationCanceledException) { SetStatus("Teste cancelado."); }
        finally
        {
            testLifetime = null; FormPanel.IsEnabled = ActionsList.IsEnabled = AddActionButton.IsEnabled = DeleteActionButton.IsEnabled = SaveActionsButton.IsEnabled = true;
            ApplyActionButton.IsEnabled = TestActionButton.IsEnabled = selected != null;
            ImportMiningButton.IsEnabled = true;
        }
    }
    private void SetStatus(string message, bool error = false)
    {
        ActionStatusText.Text = message;
        ActionStatusText.Foreground = new System.Windows.Media.SolidColorBrush(error ? System.Windows.Media.Color.FromRgb(255, 150, 140) : System.Windows.Media.Color.FromRgb(213, 225, 232));
    }
    private static string Friendly(Exception error) => error is System.Security.Cryptography.CryptographicException ? "Cadastro pertence a outro usuário Windows ou está corrompido." :
        error is FormatException or OverflowException ? "Confira os valores numéricos dos campos." : error is JsonException ? "Cadastro inválido: " + error.Message : error.Message;
}
