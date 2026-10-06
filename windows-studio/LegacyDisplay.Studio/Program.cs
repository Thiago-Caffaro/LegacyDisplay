using System.Globalization;
using System.Windows;

namespace LegacyDisplay.Studio;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");
        var application = new Application { ShutdownMode = args.Length == 0 ? ShutdownMode.OnMainWindowClose : ShutdownMode.OnExplicitShutdown };
        try
        {
            if (args.Length == 2 && args[0] == "--smoke-test") { StudioSmokeTest.Run(args[1]); return 0; }
            if (args.Length == 2 && args[0] == "--actions-smoke-test") { ActionsEditorSmokeTest.Run(args[1]); return 0; }
            if (args.Length == 5 && args[0] == "--connection-smoke-test") { StudioConnectionSmokeTest.Run(new Uri(args[1]), args[2], args[3], args[4]); return 0; }
            if (args.Length == 6 && args[0] == "--connection-actions-smoke-test") { StudioConnectionSmokeTest.Run(new Uri(args[1]), args[2], args[3], args[5], args[4]); return 0; }
            if (args.Length != 0) throw new ArgumentException("Uso: LegacyDisplay.Studio.exe");
            application.Run(new MainWindow());
            return 0;
        }
        catch (Exception error)
        {
            if (args.Length > 0) { System.IO.File.WriteAllText(args[^1] + ".error.txt", error.ToString()); return 1; }
            MessageBox.Show(error.Message, "LegacyDisplay Studio", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
}
