using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LegacyDisplay.Studio;

public static class StudioSmokeTest
{
    public static void Run(string output)
    {
        var window = new MainWindow(true);
        T Control<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
        var canvas = Control<LayoutCanvas>("PreviewCanvas");
        canvas.UpdateValues(new Dictionary<string, object?> { ["pc.cpu.usage"] = 27.5, ["pc.memory.usage"] = 51, ["action.lastResult"] = "Preview do editor" });
        canvas.Select("cpu");
        Control<TextBox>("WidthBox").Text = "640";
        Control<TextBox>("XBox").Text = "64";
        Control<TextBox>("FontSizeBox").Text = "72";
        Control<Button>("ApplyPropertiesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (window.Editor.Document.Widgets.First(w => w.Id == "cpu").X != 64) throw new InvalidOperationException("Property editor did not apply geometry.");
        var before = window.Editor.Json;
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(1440, 940)); root.Arrange(new Rect(0, 0, 1440, 940)); root.UpdateLayout();
        var scene = (Canvas)canvas.Content;
        var widgetRoot = scene.Children.OfType<Grid>().First(g => Equals(g.Tag, "cpu"));
        var thumb = (Thumb)widgetRoot.Children[1];
        thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        thumb.RaiseEvent(new DragDeltaEventArgs(40, 24) { RoutedEvent = Thumb.DragDeltaEvent });
        thumb.RaiseEvent(new DragCompletedEventArgs(40, 24, false) { RoutedEvent = Thumb.DragCompletedEvent });
        var moved = window.Editor.Document.Widgets.First(w => w.Id == "cpu");
        if (moved.X != 104 || moved.Y != 376) throw new InvalidOperationException("Canvas drag did not apply logical coordinates.");
        window.Editor.Undo(); if (window.Editor.Json != before) throw new InvalidOperationException("Canvas gesture did not undo atomically.");
        window.Editor.Redo();
        var id = window.Editor.Add("text"); canvas.Select(id); window.Editor.Delete(id); canvas.Select("cpu");
        var outputPath = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        window.Editor.Save(outputPath + ".layout.json");
        root.Measure(new Size(1440, 940)); root.Arrange(new Rect(0, 0, 1440, 940)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1440, 940, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(outputPath)) encoder.Save(file);
        window.Close();
        File.WriteAllText(outputPath + ".result.txt", "PASS: WPF load, property edit, Canvas drag, gesture undo/redo, widget add/delete, JSON save and rendering.");
    }
}
