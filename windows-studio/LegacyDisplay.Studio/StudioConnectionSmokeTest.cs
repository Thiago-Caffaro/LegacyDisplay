using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LegacyDisplay.Windows;
using LegacyDisplay.Windows.Actions;

namespace LegacyDisplay.Studio;

public static class StudioConnectionSmokeTest
{
    public static void Run(Uri device, string code, string credentialsPath, string resultPath, string? actionUrl = null)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var launchState = resultPath + ".agent.json";
        var window = new MainWindow(true, credentialsPath, launchState);
        T Control<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
        void Click(string name)
        {
            var button = Control<Button>(name);
            if (!button.IsEnabled) throw new InvalidOperationException(name + " is unexpectedly disabled.");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => !window.IsNetworkBusy);
        }
        try
        {
            if (actionUrl != null)
            {
                ActionsEditorSmokeTest.Run(resultPath + ".actions.png", Path.Combine(Path.GetDirectoryName(credentialsPath)!, "actions.json"), actionUrl);
                window.RefreshActions();
                if (!Control<ComboBox>("ActionBox").Items.Cast<object>().Any(a => a.GetType().GetProperty("Id")?.GetValue(a) as string == "api.fixture")) throw new InvalidOperationException("Button chooser did not load registered actions.");
                ((LayoutCanvas)window.FindName("PreviewCanvas")).Select("ping");
                Control<ComboBox>("ActionBox").Text = "sequence.fixture";
                Control<Button>("ApplyPropertiesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (window.Editor.Document.Widgets.First(w => w.Id == "ping").Action != "sequence.fixture") throw new InvalidOperationException("Button properties did not apply the custom action.");
            }
            Await(window.RefreshPreviewAsync());
            var sources = Control<ListBox>("SourcesList").Items.Cast<LegacyDisplay.Studio.Core.SourceInfo>().ToArray();
            if (!sources.Any(s => s.Id == "pc.gpu.temperature") || sources.Select(s => s.Id).Distinct().Count() != sources.Length)
                throw new InvalidOperationException("GUI sensor catalog has missing aliases or duplicate source ids.");
            Control<TextBox>("DeviceUrlBox").Text = device.AbsoluteUri;
            Click("ConnectButton");
            if (!Control<TextBlock>("DeviceStateText").Text.Contains("não pareado", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unpaired Wi-Fi tablet looks connected without explaining why controls stay disabled.");
            var poll = (Task)typeof(MainWindow).GetMethod("PollAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null)!;
            if (!Control<Button>("ConnectButton").IsEnabled || !Control<Button>("PairButton").IsEnabled)
                throw new InvalidOperationException("Background status polling disables Connect/Pair and makes the controls blink.");
            Await(poll);
            Control<TextBox>("PairCodeBox").Text = "";
            Click("PairButton");
            if (!Control<TextBlock>("StatusText").Text.Contains("seis dígitos", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Pairing failure was overwritten by an automatic reconnect.");
            Control<TextBox>("PairCodeBox").Text = code;
            Click("PairButton");
            if (!File.Exists(credentialsPath) || !Control<Button>("DeployButton").IsEnabled || Control<TextBox>("PairCodeBox").Text != "")
                throw new InvalidOperationException("GUI pairing did not complete: " + Control<TextBlock>("StatusText").Text);
            var credential = CredentialStore.Load(credentialsPath);
            using var client = new DeviceClient(device, credential.Token);
            Control<TextBox>("DeviceUrlBox").Text = "http://127.0.0.1:1";
            if (Control<Button>("DeployButton").IsEnabled || Control<Button>("StartAgentButton").IsEnabled) throw new InvalidOperationException("Unverified address kept deployment enabled.");
            Control<TextBox>("DeviceUrlBox").Text = device.AbsoluteUri; Click("ConnectButton");
            window.Editor.Move("cpu", 64, 360);
            Click("DeployButton");
            var deployed = Await(client.GetLayoutAsync(CancellationToken.None));
            if (deployed.Widgets.First(w => w.Id == "cpu").X != 64) throw new InvalidOperationException("GUI deploy did not persist edited geometry.");
            Control<CheckBox>("KeepScreenCheck").IsChecked = false; Control<CheckBox>("StartBootCheck").IsChecked = false;
            Click("DeviceConfigButton");
            var config = Await(client.GetConfigAsync(CancellationToken.None));
            if (config.KeepScreenOn || config.StartOnBoot) throw new InvalidOperationException("GUI config PUT failed.");
            window.Editor.MarkSaved();
            var read = Control<Button>("ReadTabletButton"); read.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Editor.Add("text");
            PumpUntil(() => !window.IsNetworkBusy);
            if (window.Editor.Document.Widgets.Count != deployed.Widgets.Count + 1) throw new InvalidOperationException("Concurrent read discarded a new edit.");
            window.Editor.MarkSaved(); Click("ReadTabletButton");
            if (window.Editor.Document.Widgets.Count != deployed.Widgets.Count) throw new InvalidOperationException("GUI read did not load tablet layout.");
            Click("StartAgentButton"); Await(WaitForAgentAsync(device, true));
            window.Close();
            Await(WaitForAgentAsync(device, true));
            // A new Studio instance must safely reattach and stop its own Agent.
            window = new MainWindow(true, credentialsPath, launchState);
            Control<TextBox>("DeviceUrlBox").Text = device.AbsoluteUri; Click("ConnectButton"); Click("StopAgentButton");
            Await(WaitForAgentAsync(device, false));
            File.WriteAllText(resultPath, "PASS: GUI unpaired connection guidance, background polling preserves Connect/Pair, pairing errors persist, real sensor catalog, address change invalidates session, pairing, deploy, config PUT, safe concurrent read, layout read, background Agent survives Studio close, reattach and stop.");
        }
        finally
        {
            using var cleanup = new AgentLauncher(launchState);
            if (cleanup.IsRunning) cleanup.Stop();
            window.Close();
        }
    }
    private static async Task<bool> WaitForAgentAsync(Uri device, bool connected)
    {
        using var client = new DeviceClient(device);
        for (var i = 0; i < 50; i++)
        {
            if ((await client.GetStatusAsync(CancellationToken.None)).AgentConnected == connected) return true;
            await Task.Delay(100);
        }
        throw new TimeoutException("Agent connection state did not converge.");
    }
    private static T Await<T>(Task<T> task) { PumpUntil(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    private static void Await(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void PumpUntil(Func<bool> complete)
    {
        if (complete()) return;
        var frame = new DispatcherFrame(); var deadline = DateTime.UtcNow.AddSeconds(25); var timeout = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (complete() || DateTime.UtcNow >= deadline) { timeout = !complete(); frame.Continue = false; } };
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        if (timeout) throw new TimeoutException("WPF operation did not complete.");
    }
}
