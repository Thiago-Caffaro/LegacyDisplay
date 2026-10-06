using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LegacyDisplay.Protocol;
using LegacyDisplay.Studio.Core;
using LegacyDisplay.Windows;
using LegacyDisplay.Windows.Actions;
using Microsoft.Win32;

namespace LegacyDisplay.Studio;

public partial class MainWindow : Window
{
    public LayoutEditor Editor { get; }
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly WindowsMetrics metrics = new();
    private readonly AgentLauncher launcher;
    private readonly string credentialPath;
    private readonly string actionPath;
    private readonly bool smoke;
    private string? filename;
    private bool refreshing;
    private bool networkBusy;
    private bool polling;
    private int connectionRevision;
    private bool deviceReady;
    private DeviceStatus? deviceStatus;
    private Uri? connectedDevice;
    private int ticks;
    private bool previewBusy;
    private string sourceSignature = "";

    internal bool IsNetworkBusy => networkBusy;

    public MainWindow(bool smokeTest = false, string? credentials = null, string? launcherStatePath = null, string? actions = null)
    {
        smoke = smokeTest;
        credentialPath = credentials ?? CredentialStore.DefaultPath;
        actionPath = actions ?? (smokeTest && credentials != null ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(credentials))!, "actions.json") : ActionStore.DefaultPath);
        launcher = new AgentLauncher(launcherStatePath);
        using var asset = Assembly.GetExecutingAssembly().GetManifestResourceStream("dashboard.json")!;
        using var reader = new System.IO.StreamReader(asset);
        Editor = new LayoutEditor(reader.ReadToEnd());
        InitializeComponent();
        PreviewCanvas.Attach(Editor); PreviewCanvas.SetZoom(45);
        PreviewCanvas.SelectionChanged += _ => { RefreshInspector(); SyncListSelection(); };
        PreviewCanvas.EditingError += text => SetStatus(text, true);
        Editor.Changed += RefreshDocument;
        SourceBox.ItemsSource = SourceCatalog.Sources; SourcesList.ItemsSource = SourceCatalog.Sources;
        RefreshActions();
        DeviceUrlBox.Text = "http://192.168.1.70:8765";
        if (!smoke)
        {
            try { DeviceUrlBox.Text = CredentialStore.Load(credentialPath).Device; }
            catch (PairingRequiredException) { SetStatus("Primeiro pareamento: informe o endereço e o código do tablet."); }
            catch (Exception) { SetStatus("Não foi possível ler a credencial. Pareie novamente pelo tablet.", true); }
        }
        timer.Tick += TimerTick;
        RefreshDocument(); UpdateConnectionControls();
    }
    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (smoke) return;
        timer.Start(); await RefreshPreviewAsync();
        await ConnectAsync(false);
    }
    private async void TimerTick(object? sender, EventArgs e)
    {
        await RefreshPreviewAsync();
        if (++ticks % 5 == 0 && connectedDevice != null && !networkBusy) await PollAsync();
    }
    internal async Task RefreshPreviewAsync()
    {
        if (previewBusy || lifetime.IsCancellationRequested) return;
        previewBusy = true;
        try
        {
            var values = await Task.Run(() => metrics.Sample(false));
            if (lifetime.IsCancellationRequested) return;
            PreviewCanvas.UpdateValues(values);
            var sources = SourceCatalog.Sources.Concat(metrics.HardwareSensors.Select(s => new SourceInfo(s.Id, s.Label, s.Unit))).ToArray();
            var signature = string.Join('|', sources.Select(s => s.Id));
            if (signature != sourceSignature)
            {
                sourceSignature = signature;
                var sourceText = SourceBox.Text; var selected = (SourcesList.SelectedItem as SourceInfo)?.Id;
                SourceBox.ItemsSource = sources; SourceBox.Text = sourceText;
                SourcesList.ItemsSource = sources; SourcesList.SelectedItem = sources.FirstOrDefault(s => s.Id == selected);
            }
            GpuStatusText.Text = metrics.HardwareUnavailableReason ?? $"GPU · {metrics.HardwareSensors.Count} sensores detectados";
        }
        catch (Exception) { SetStatus("Não foi possível atualizar o preview dos sensores locais.", true); }
        finally { previewBusy = false; }
    }
    private void RefreshDocument()
    {
        refreshing = true;
        var document = Editor.Document;
        WidgetList.ItemsSource = document.Widgets;
        WidgetList.SelectedValue = PreviewCanvas.SelectedId;
        WidgetCountText.Text = $"WIDGETS · {document.Widgets.Count}";
        ScreenLabel.Text = $"{document.Screen.Width} × {document.Screen.Height}";
        ScreenWidthBox.Text = document.Screen.Width.ToString(CultureInfo.InvariantCulture);
        ScreenHeightBox.Text = document.Screen.Height.ToString(CultureInfo.InvariantCulture);
        ScreenBackgroundBox.Text = document.Screen.Background;
        UndoButton.IsEnabled = Editor.CanUndo; RedoButton.IsEnabled = Editor.CanRedo;
        DocumentStateText.Text = Editor.IsDirty ? "Alterações não salvas" : "Layout salvo";
        Title = $"{(Editor.IsDirty ? "* " : "")}{(filename == null ? "Sem título" : System.IO.Path.GetFileName(filename))} · LegacyDisplay Studio";
        refreshing = false;
        RefreshInspector();
    }
    private void SyncListSelection()
    {
        refreshing = true; WidgetList.SelectedValue = PreviewCanvas.SelectedId; refreshing = false;
    }
    private void RefreshInspector()
    {
        var widget = Editor.Document.Widgets.FirstOrDefault(w => w.Id == PreviewCanvas.SelectedId);
        Inspector.IsEnabled = widget != null;
        if (widget == null) { SelectionTitle.Text = "PROPRIEDADES · selecione um widget"; return; }
        SelectionTitle.Text = $"PROPRIEDADES · {widget.Type}";
        IdBox.Text = widget.Id; XBox.Text = IntText(widget.X); YBox.Text = IntText(widget.Y);
        WidthBox.Text = IntText(widget.Width); HeightBox.Text = IntText(widget.Height);
        WidgetTextBox.Text = widget.Text ?? ""; SourceBox.Text = widget.Source ?? ""; FormatBox.Text = widget.Format ?? "{value}"; ActionBox.Text = widget.Action ?? "";
        TextField.Visibility = widget.Type == "value" ? Visibility.Collapsed : Visibility.Visible;
        SourceField.Visibility = widget.Type == "value" ? Visibility.Visible : Visibility.Collapsed;
        ActionField.Visibility = widget.Type == "button" ? Visibility.Visible : Visibility.Collapsed;
        BackgroundBox.Text = widget.Style.Background; ColorBox.Text = widget.Style.Color;
        FontSizeBox.Text = IntText(widget.Style.FontSize); BoldCheck.IsChecked = widget.Style.FontWeight == "bold";
        AlignmentBox.SelectedValue = widget.Style.Alignment; RadiusBox.Text = IntText(widget.Style.BorderRadius);
        PaddingBox.Text = IntText(widget.Style.Padding); OpacityBox.Text = widget.Style.Opacity.ToString(CultureInfo.CurrentCulture);
    }
    private void WidgetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshing) PreviewCanvas.Select(WidgetList.SelectedValue as string);
    }
    private void Add(string type, SourceInfo? source = null)
    {
        TryEdit(() => {
            var id = Editor.Add(type, source);
            PreviewCanvas.Select(id);
        });
    }
    private void AddTextClick(object sender, RoutedEventArgs e) => Add("text");
    private void AddValueClick(object sender, RoutedEventArgs e) => Add("value", SourcesList.SelectedItem as SourceInfo);
    private void AddButtonClick(object sender, RoutedEventArgs e) => Add("button");
    private void SourceDoubleClick(object sender, MouseButtonEventArgs e) { if (SourcesList.SelectedItem is SourceInfo source) Add("value", source); }
    private void DeleteClick(object sender, RoutedEventArgs e) { if (PreviewCanvas.SelectedId is { } id) TryEdit(() => Editor.Delete(id)); }
    private void DuplicateClick(object sender, RoutedEventArgs e) { if (PreviewCanvas.SelectedId is { } id) TryEdit(() => PreviewCanvas.Select(Editor.Duplicate(id))); }
    private void BackwardClick(object sender, RoutedEventArgs e) { if (PreviewCanvas.SelectedId is { } id) TryEdit(() => Editor.Reorder(id, -1)); }
    private void ForwardClick(object sender, RoutedEventArgs e) { if (PreviewCanvas.SelectedId is { } id) TryEdit(() => Editor.Reorder(id, 1)); }
    private void UndoClick(object sender, RoutedEventArgs e) => Editor.Undo();
    private void RedoClick(object sender, RoutedEventArgs e) => Editor.Redo();
    private void ZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PreviewCanvas == null) return;
        PreviewCanvas.SetZoom(e.NewValue); ZoomLabel.Text = $"{e.NewValue:0} %";
    }
    private void SnapChanged(object sender, RoutedEventArgs e) { if (PreviewCanvas != null) PreviewCanvas.SnapToGrid = SnapCheck.IsChecked == true; }
    private void PropertiesApplyClick(object sender, RoutedEventArgs e)
    {
        if (PreviewCanvas.SelectedId is not { } id) return;
        TryEdit(() => {
            var widget = Editor.Document.Widgets.First(w => w.Id == id);
            var updated = widget with {
                Id = IdBox.Text.Trim(), X = Integer(XBox), Y = Integer(YBox), Width = Integer(WidthBox), Height = Integer(HeightBox),
                Text = widget.Type == "value" ? widget.Text : WidgetTextBox.Text,
                Source = widget.Type == "value" ? SourceBox.Text.Trim() : widget.Source, Format = widget.Type == "value" ? FormatBox.Text : widget.Format,
                Action = widget.Type == "button" ? ActionBox.Text.Trim() : null,
                Style = widget.Style with { Background = BackgroundBox.Text.Trim(), Color = ColorBox.Text.Trim(), FontSize = Integer(FontSizeBox),
                    FontWeight = BoldCheck.IsChecked == true ? "bold" : "normal", Alignment = (string?)AlignmentBox.SelectedValue ?? "left",
                    BorderRadius = Integer(RadiusBox), Padding = Integer(PaddingBox), Opacity = double.Parse(OpacityBox.Text.Replace(',', '.'), CultureInfo.InvariantCulture) }
            };
            Editor.Replace(id, updated); PreviewCanvas.Select(updated.Id);
        });
    }
    private void ScreenApplyClick(object sender, RoutedEventArgs e) => TryEdit(() => Editor.SetScreen(Integer(ScreenWidthBox), Integer(ScreenHeightBox), ScreenBackgroundBox.Text.Trim()));
    private void TryEdit(Action edit)
    {
        try { edit(); SetStatus("Layout atualizado. Use Deploy para enviar ao tablet."); }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException or System.Text.Json.JsonException or InvalidOperationException)
        { SetStatus("Alteração recusada: " + error.Message, true); }
    }
    private void NewClick(object sender, RoutedEventArgs e)
    {
        if (!MayDiscard()) return;
        filename = null; Editor.Load("{\"version\":1,\"screen\":{\"width\":800,\"height\":1280},\"widgets\":[]}"); PreviewCanvas.Select(null);
    }
    private void OpenClick(object sender, RoutedEventArgs e)
    {
        if (!MayDiscard()) return;
        var dialog = new OpenFileDialog { Filter = "Layout JSON (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try { Editor.Load(System.IO.File.ReadAllText(dialog.FileName)); filename = dialog.FileName; RefreshDocument(); PreviewCanvas.Select(null); SetStatus("Layout aberto e validado."); }
        catch (Exception error) { SetStatus("Não foi possível abrir: " + error.Message, true); }
    }
    private void SaveClick(object sender, RoutedEventArgs e) => Save(false);
    private void SaveAsClick(object sender, RoutedEventArgs e) => Save(true);
    private bool Save(bool choose)
    {
        var path = filename;
        if (choose || path == null)
        {
            var dialog = new SaveFileDialog { Filter = "Layout JSON (*.json)|*.json", DefaultExt = ".json", FileName = path == null ? "dashboard.json" : System.IO.Path.GetFileName(path) };
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }
        try { Editor.Save(path); filename = path; RefreshDocument(); SetStatus("Layout salvo em " + path); return true; }
        catch (Exception error) { SetStatus("Falha ao salvar: " + error.Message, true); return false; }
    }
    private bool MayDiscard()
    {
        if (!Editor.IsDirty) return true;
        var answer = MessageBox.Show(this, "Salvar as alterações do layout?", "LegacyDisplay Studio", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer == MessageBoxResult.No || answer == MessageBoxResult.Yes && Save(false);
    }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (!smoke && !MayDiscard()) { e.Cancel = true; return; }
        timer.Stop(); lifetime.Cancel(); PreviewCanvas.Detach(); launcher.Dispose(); metrics.Dispose();
    }
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.S) { Save(false); e.Handled = true; }
            else if (e.Key == Key.O) { OpenClick(this, e); e.Handled = true; }
            else if (e.Key == Key.N) { NewClick(this, e); e.Handled = true; }
            else if (e.OriginalSource is not TextBox && e.OriginalSource is not ComboBox)
            {
                if (e.Key == Key.Z) { Editor.Undo(); e.Handled = true; }
                if (e.Key == Key.Y) { Editor.Redo(); e.Handled = true; }
            }
        }
        if (e.Key == Key.Delete && e.OriginalSource is not TextBox && e.OriginalSource is not ComboBox) { DeleteClick(this, e); e.Handled = true; }
    }
    private async void ConnectClick(object sender, RoutedEventArgs e) => await ConnectAsync(true);
    private void DeviceUrlChanged(object sender, TextChangedEventArgs e)
    {
        if (DeployButton == null) return;
        connectionRevision++;
        deviceReady = false; deviceStatus = null; connectedDevice = null;
        DeviceStateText.Text = "Endereço alterado · clique em Conectar";
        UpdateConnectionControls();
    }
    private async Task ConnectAsync(bool verbose)
    {
        await Network(async () => {
            if (verbose) SetStatus("Conectando ao tablet…");
            var device = SelectedDevice();
            connectedDevice = device;
            using var publicClient = new DeviceClient(device);
            deviceStatus = await publicClient.GetStatusAsync(lifetime.Token); connectedDevice = device; deviceReady = false;
            DeviceStateText.Text = $"{deviceStatus.Name} · {deviceStatus.Width}×{deviceStatus.Height} · {(deviceStatus.AgentConnected ? "Agent conectado" : "Agent offline")}";
            if (!deviceStatus.Paired)
            {
                DeviceStateText.Text = $"{deviceStatus.Name} · Wi-Fi disponível · não pareado";
                SetStatus("Tablet encontrado. Informe o código exibido no tablet e clique em Parear; depois, Iniciar Agent.");
                return;
            }
            try
            {
                var credential = CredentialStore.Load(credentialPath);
                using var client = new DeviceClient(device, credential.Token);
                var config = await client.GetConfigAsync(lifetime.Token);
                KeepScreenCheck.IsChecked = config.KeepScreenOn; StartBootCheck.IsChecked = config.StartOnBoot; deviceReady = true;
                if (verbose) SetStatus("Conectado. O Studio pode enviar layouts enquanto o Agent funciona.");
            }
            catch (PairingRequiredException) { DeviceStateText.Text = "Tablet encontrado · credencial ausente neste usuário Windows"; SetStatus("Informe o código do tablet e clique em Parear."); }
        }, verbose);
    }
    private async Task PollAsync()
    {
        if (connectedDevice == null || polling || networkBusy) return;
        var endpoint = connectedDevice;
        var revision = connectionRevision;
        polling = true;
        try
        {
            using var client = new DeviceClient(endpoint);
            var status = await client.GetStatusAsync(lifetime.Token);
            if (networkBusy || revision != connectionRevision || endpoint != connectedDevice || lifetime.IsCancellationRequested) return;
            deviceStatus = status;
            if (!status.Paired)
            {
                deviceReady = false;
                DeviceStateText.Text = $"{status.Name} · Wi-Fi disponível · não pareado";
            }
            else
            {
                DeviceStateText.Text = $"{status.Name} · {status.Width}×{status.Height} · {(status.AgentConnected ? "Agent conectado" : "Agent offline")}";
                if (!deviceReady)
                {
                    try
                    {
                        using var authenticated = new DeviceClient(endpoint, CredentialStore.Load(credentialPath).Token);
                        var config = await authenticated.GetConfigAsync(lifetime.Token);
                        if (networkBusy || revision != connectionRevision || endpoint != connectedDevice || lifetime.IsCancellationRequested) return;
                        KeepScreenCheck.IsChecked = config.KeepScreenOn; StartBootCheck.IsChecked = config.StartOnBoot;
                        deviceReady = true;
                    }
                    catch (Exception) { DeviceStateText.Text = "Tablet encontrado · confira o pareamento neste PC"; }
                }
            }
            UpdateConnectionControls();
        }
        catch (Exception) when (!lifetime.IsCancellationRequested)
        {
            if (!networkBusy && revision == connectionRevision && endpoint == connectedDevice)
            {
                deviceReady = false; deviceStatus = null;
                DeviceStateText.Text = "Tablet desconectado · confira endereço/rede";
                UpdateConnectionControls();
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { polling = false; }
    }
    private async void PairClick(object sender, RoutedEventArgs e)
    {
        var paired = false;
        await Network(async () => {
            var device = SelectedDevice();
            SetStatus("Pareando com o tablet…");
            using var client = new DeviceClient(device);
            var token = await client.PairAsync(PairCodeBox.Text.Trim(), lifetime.Token);
            CredentialStore.Save(credentialPath, device, token); PairCodeBox.Clear();
            paired = true;
            SetStatus("Pareamento salvo. Conecte para carregar as opções do tablet.");
        });
        if (!lifetime.IsCancellationRequested && paired) await ConnectAsync(false);
    }
    private async void UsbClick(object sender, RoutedEventArgs e)
    {
        var detected = false;
        await Network(async () => {
            var usb = await AdbConnector.ConnectAsync(lifetime.Token); DeviceUrlBox.Text = usb.Endpoint.AbsoluteUri;
            SetStatus($"USB detectado: {usb.Device.Model}. A conexão usa a mesma API do Wi-Fi."); detected = true;
        });
        if (detected) await ConnectAsync(true);
    }
    private async void DeployClick(object sender, RoutedEventArgs e)
    {
        var json = Editor.Json;
        await Network(async () => {
            var catalog = ActionStore.Load(actionPath);
            var unknown = Editor.Document.Widgets.FirstOrDefault(w => w.Type == "button" && (w.Action == null || !catalog.Contains(w.Action)));
            if (unknown != null) throw new InvalidOperationException($"O botão '{unknown.Id}' usa uma ação não cadastrada ou desativada. Abra Ações para configurar.");
            using var client = AuthenticatedClient(); await client.DeployAsync(json, lifetime.Token);
            SetStatus(Editor.Json == json ? "Deploy concluído. O layout foi persistido no tablet." : "Deploy concluído. Há alterações posteriores no editor que ainda não foram enviadas.");
        });
    }
    private async void ReadTabletClick(object sender, RoutedEventArgs e)
    {
        if (!MayDiscard()) return;
        var before = Editor.Json;
        await Network(async () => {
            using var client = AuthenticatedClient(); var document = await client.GetLayoutAsync(lifetime.Token);
            if (Editor.Json != before) { SetStatus("O editor mudou durante a leitura. O layout local foi preservado; tente ler novamente."); return; }
            filename = null; Editor.Load(System.Text.Json.JsonSerializer.Serialize(document, LayoutDocument.JsonOptions)); PreviewCanvas.Select(null);
            SetStatus("Layout atual do tablet carregado. Salve uma cópia para manter o arquivo no Windows.");
        });
    }
    private async void DeviceConfigClick(object sender, RoutedEventArgs e)
    {
        var config = new DeviceConfig(KeepScreenCheck.IsChecked == true, StartBootCheck.IsChecked == true);
        await Network(async () => { using var client = AuthenticatedClient(); await client.ConfigureAsync(config, lifetime.Token); SetStatus("Opções salvas no tablet."); });
    }
    private void StartAgentClick(object sender, RoutedEventArgs e)
    {
        try
        {
            launcher.Start(AgentLauncher.FindExecutable() ?? throw new System.IO.FileNotFoundException("Agent não encontrado. Mantenha as pastas agent e studio juntas."), SelectedDevice(), credentialPath, actionPath);
            SetStatus("Agent iniciado em segundo plano. Ele continua rodando ao fechar o Studio."); UpdateConnectionControls();
        }
        catch (Exception error) { SetStatus(FriendlyError(error), true); }
    }
    private void StopAgentClick(object sender, RoutedEventArgs e)
    {
        try { launcher.Stop(); SetStatus("Agent iniciado pelo Studio foi encerrado."); UpdateConnectionControls(); }
        catch (Exception error) { SetStatus(FriendlyError(error), true); }
    }
    private async Task Network(Func<Task> action, bool verbose = true)
    {
        if (networkBusy || lifetime.IsCancellationRequested) return;
        connectionRevision++;
        networkBusy = true; UpdateConnectionControls();
        try { await action(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (error is System.Net.Http.HttpRequestException { StatusCode: HttpStatusCode.Unauthorized })
            { deviceReady = false; DeviceStateText.Text = "Credencial recusada · repita o pareamento"; }
            else if (error is System.Net.Http.HttpRequestException { StatusCode: null } or TaskCanceledException)
            { deviceReady = false; deviceStatus = null; DeviceStateText.Text = "Tablet desconectado · confira endereço/rede"; }
            if (verbose) SetStatus(FriendlyError(error), true);
        }
        finally { networkBusy = false; if (!lifetime.IsCancellationRequested) UpdateConnectionControls(); }
    }
    private void UpdateConnectionControls()
    {
        ConnectButton.IsEnabled = PairButton.IsEnabled = UsbButton.IsEnabled = !networkBusy;
        DeviceUrlBox.IsEnabled = PairCodeBox.IsEnabled = !networkBusy;
        ReadTabletButton.IsEnabled = DeployButton.IsEnabled = !networkBusy && deviceReady;
        KeepScreenCheck.IsEnabled = StartBootCheck.IsEnabled = DeviceConfigButton.IsEnabled = !networkBusy && deviceReady;
        StartAgentButton.IsEnabled = !networkBusy && deviceReady && deviceStatus?.AgentConnected != true && !launcher.IsRunning;
        StopAgentButton.IsEnabled = !networkBusy && launcher.IsRunning;
        StopAgentButton.ToolTip = launcher.IsRunning ? "Encerra o Agent iniciado pelo Studio" : "Se iniciou pelo terminal, use Ctrl+C naquele terminal";
    }
    private DeviceClient AuthenticatedClient() => new(SelectedDevice(), CredentialStore.Load(credentialPath).Token);
    private Uri SelectedDevice()
    {
        if (!Uri.TryCreate(DeviceUrlBox.Text.Trim(), UriKind.Absolute, out var device)) throw new ArgumentException("Informe uma URL válida para o tablet.");
        DeviceClient.ValidateDevice(device); return device;
    }
    private void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text; StatusText.Foreground = new System.Windows.Media.SolidColorBrush(error ? System.Windows.Media.Color.FromRgb(255, 150, 140) : System.Windows.Media.Color.FromRgb(213, 225, 232));
        StatusText.ToolTip = text;
    }
    private static string FriendlyError(Exception error) => error switch {
        ArgumentException when error.Message.StartsWith("Pairing code", StringComparison.Ordinal) => "Informe o código de seis dígitos mostrado no tablet e clique em Parear.",
        System.Net.Http.HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } => "Credencial recusada. Gere Novo pareamento no tablet e use o novo código.",
        System.Net.Http.HttpRequestException { StatusCode: HttpStatusCode.Conflict } => "O tablet já está pareado ou a ação não está disponível. Confira o estado no aparelho.",
        System.Net.Http.HttpRequestException { StatusCode: HttpStatusCode.BadRequest } => "Código inválido ou expirado. Confira o código atual no tablet e clique em Parear.",
        System.Net.Http.HttpRequestException => "Não foi possível conectar ao tablet. Confira o endereço, rede e se o app está aberto.",
        TaskCanceledException => "O tablet não respondeu a tempo. Confira a conexão.",
        System.Security.Cryptography.CryptographicException => "Credencial pertence a outro usuário Windows ou está inválida. Pareie novamente.",
        _ => error.Message
    };
    private static string IntText(int value) => value.ToString(CultureInfo.InvariantCulture);
    private sealed record ActionChoice(string Id, string Label);
    internal void RefreshActions()
    {
        var current = ActionBox.Text;
        try
        {
            var catalog = ActionStore.Load(actionPath);
            ActionBox.ItemsSource = new[] { new ActionChoice("demo.ping", "Confirmação de toque") }.Concat(catalog.Actions.Where(a => a.Enabled).Select(a => new ActionChoice(a.Id, a.Label))).ToArray();
            ActionBox.Text = current;
        }
        catch (Exception) { SetStatus("Não foi possível ler o cadastro de ações. Confira o arquivo protegido no usuário Windows.", true); }
    }
    private void ActionsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ActionsWindow(actionPath) { Owner = this };
            dialog.ShowDialog(); RefreshActions();
            if (dialog.ActionsSaved)
            {
                Editor.BeginGesture();
                try
                {
                    foreach (var widget in Editor.Document.Widgets.Where(w => w.Type == "button" && w.Action != null))
                        if (dialog.RenamedIds.TryGetValue(widget.Action!, out var renamed) && dialog.Catalog.Actions.Any(a => a.Id == renamed)) Editor.Replace(widget.Id, widget with { Action = renamed });
                    Editor.CompleteGesture();
                }
                catch { Editor.CancelGesture(); throw; }
                RefreshInspector(); SetStatus("Ações salvas. Escolha a ação do botão e faça Deploy. O Agent v0.3 usa o cadastro atualizado no próximo toque.");
            }
        }
        catch (Exception error) { SetStatus(error is System.Security.Cryptography.CryptographicException ? "Cadastro de ações pertence a outro usuário Windows ou está corrompido." : error.Message, true); }
    }
    private static int Integer(TextBox box) => int.Parse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture);
}
