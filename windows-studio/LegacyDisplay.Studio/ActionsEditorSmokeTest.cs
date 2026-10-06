using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LegacyDisplay.Windows.Actions;

namespace LegacyDisplay.Studio;

public static class ActionsEditorSmokeTest
{
    public static void Run(string output, string? existingStore = null, string? httpUrl = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "legacydisplay-actions-ui-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = existingStore ?? Path.Combine(directory, "actions.json");
        ActionsWindow? window = null;
        try
        {
            window = new ActionsWindow(path, true);
            T Control<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
            void Click(string name) => Control<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            void Add(string type, string id, string label)
            {
                Control<ComboBox>("NewTypeBox").SelectedValue = type; Click("AddActionButton");
                Control<TextBox>("ActionIdBox").Text = id; Control<TextBox>("ActionLabelBox").Text = label;
            }
            Add("http", "api.fixture", "Acionar API local");
            Control<TextBox>("HttpUrlBox").Text = httpUrl ?? "http://127.0.0.1:8080/action";
            Control<TextBox>("HttpBodyBox").Text = "{\"enabled\":true}";
            Control<TextBox>("HttpHeadersBox").Text = "{\"X-LegacyDisplay-Test\":\"local\"}";
            Control<PasswordBox>("BearerTokenBox").Password = "LOCAL_TEST_TOKEN";
            Click("ApplyActionButton");
            if (window.Catalog.Actions.FirstOrDefault(a => a.Id == "api.fixture")?.Http?.Method != "POST") throw new InvalidOperationException("HTTP form failed to apply method/identifier.");
            Add("application", "app.fixture", "Abrir aplicativo");
            Control<TextBox>("ExecutableBox").Text = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            Control<TextBox>("ArgumentsBox").Text = "C:\\A folder\\example.txt\n--test"; Click("ApplyActionButton");
            if (existingStore != null)
            {
                // A harmless real process exercises the application launcher without opening a desktop app.
                var originalExecutable = Control<TextBox>("ExecutableBox").Text;
                var originalArguments = Control<TextBox>("ArgumentsBox").Text;
                Control<TextBox>("ExecutableBox").Text = LegacyDisplay.Windows.AgentLauncher.FindExecutable() ?? throw new FileNotFoundException("Published Agent probe missing.");
                Control<TextBox>("ArgumentsBox").Text = "--help"; Click("TestActionButton");
                if (!Control<TextBlock>("ActionStatusText").Text.Contains("aplicativo iniciado")) throw new InvalidOperationException("GUI real application test failed.");
                Control<TextBox>("ExecutableBox").Text = originalExecutable; Control<TextBox>("ArgumentsBox").Text = originalArguments; Click("ApplyActionButton");
            }
            Add("function", "function.fixture", "Confirmar recebimento"); Click("ApplyActionButton");
            Click("TestActionButton");
            if (Control<TextBlock>("ActionStatusText").Text != "PC recebeu o toque!") throw new InvalidOperationException("GUI function test did not execute.");
            Add("sequence", "sequence.fixture", "Confirmação e API");
            Control<ComboBox>("StepActionBox").SelectedItem = "api.fixture"; Control<TextBox>("StepDelayBox").Text = "25";
            typeof(ActionsWindow).GetMethod("AddStepClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, [window, new RoutedEventArgs()]);
            Click("ApplyActionButton");
            var sequence = window.Catalog.Actions.First(a => a.Id == "sequence.fixture");
            if (sequence.Steps.Count != 2 || sequence.Steps[1] != new ActionStep("api.fixture", 25)) throw new InvalidOperationException("Sequence controls lost ordering/delay.");
            Control<ListBox>("ActionsList").SelectedItem = window.Catalog.Actions.First(a => a.Id == "api.fixture");
            var before = window.Catalog.Actions.First(a => a.Id == "api.fixture").Http!.Url;
            Control<TextBox>("HttpUrlBox").Text = "file:///invalid"; Click("ApplyActionButton");
            if (window.Catalog.Actions.First(a => a.Id == "api.fixture").Http!.Url != before) throw new InvalidOperationException("Invalid edit replaced a valid action.");
            Control<TextBox>("HttpUrlBox").Text = before; Control<TextBox>("ActionIdBox").Text = "api.renamed"; Click("ApplyActionButton");
            if (window.Catalog.Actions.First(a => a.Id == "sequence.fixture").Steps[1].ActionId != "api.renamed") throw new InvalidOperationException("Rename did not update dependencies.");
            Control<TextBox>("ActionIdBox").Text = "api.fixture"; Click("ApplyActionButton");
            // Import into the actual editor, preserve existing actions and save/reopen the result.
            var miningProject = Path.Combine(directory, "MiningRoutine");
            Directory.CreateDirectory(Path.Combine(miningProject, "publish"));
            File.WriteAllText(Path.Combine(miningProject, "publish", "MiningController.Windows.exe"), "fixture; never executed");
            if (!window.ImportMiningRoutine(miningProject) || !window.ImportMiningRoutine(miningProject)) throw new InvalidOperationException("GUI MiningRoutine import failed.");
            var quiet = window.Catalog.Actions.Single(a => a.Id == "mining.quiet");
            if (window.Catalog.Actions.Count != 9 || quiet.Type != "application" || quiet.Function != null || quiet.Application!.Arguments[^1] != "--quiet-mining")
                throw new InvalidOperationException("MiningRoutine import produced an unavailable internal function or lost existing actions.");
            Control<ListBox>("ActionsList").SelectedItem = window.Catalog.Actions.First(a => a.Id == "api.fixture");
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(1120, 850)); root.Arrange(new Rect(0, 0, 1120, 850)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1120, 850, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            var outputPath = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using (var file = File.Create(outputPath)) png.Save(file);
            Click("SaveActionsButton");
            if (ActionStore.Load(path).Actions.Count != 9 || File.ReadAllText(path).Contains("LOCAL_TEST_TOKEN")) throw new InvalidOperationException("Protected GUI save failed.");
            window = new ActionsWindow(path, true);
            var arguments = window.Catalog.Actions.First(a => a.Id == "app.fixture").Application!.Arguments;
            if (arguments.Count != 2 || arguments[0] != "C:\\A folder\\example.txt") throw new InvalidOperationException("Application arguments did not round-trip.");
            File.WriteAllText(outputPath + ".result.txt", "PASS: WPF HTTP/application/function/sequence forms, invalid edit preservation, dependency rename, DPAPI save/reopen and rendering.");
        }
        finally { window?.Close(); Directory.Delete(directory, true); }
    }
}
