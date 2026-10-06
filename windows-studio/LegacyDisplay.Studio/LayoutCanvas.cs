using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LegacyDisplay.Protocol;
using LegacyDisplay.Studio.Core;

namespace LegacyDisplay.Studio;

public sealed class LayoutCanvas : ScrollViewer
{
    private readonly Canvas scene = new();
    private readonly Dictionary<string, (Grid Root, Border Border, TextBlock Text, Thumb Move, Thumb Resize)> visuals = [];
    private LayoutEditor? editor;
    private IReadOnlyDictionary<string, object?> values = new Dictionary<string, object?>();
    private string? selected;
    private bool dragging;
    private Widget? dragStart;
    private double dragX, dragY;
    public bool SnapToGrid { get; set; } = true;
    public event Action<string?>? SelectionChanged;
    public event Action<string>? EditingError;
    public string? SelectedId => selected;
    public LayoutCanvas()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Background = Brush("#091017"); Padding = new Thickness(32);
        Content = scene;
        scene.MouseLeftButtonDown += (_, e) => { if (ReferenceEquals(e.OriginalSource, scene)) Select(null); };
    }
    public void Attach(LayoutEditor document)
    {
        if (editor != null) editor.Changed -= Render;
        editor = document; editor.Changed += Render; Render();
    }
    public void Detach() { if (editor != null) editor.Changed -= Render; }
    public void SetZoom(double percentage) => scene.LayoutTransform = new ScaleTransform(percentage / 100, percentage / 100);
    public void Select(string? id)
    {
        selected = id;
        UpdateSelection(); SelectionChanged?.Invoke(id);
    }
    public void UpdateValues(IReadOnlyDictionary<string, object?> sources)
    {
        values = sources;
        if (editor == null) return;
        foreach (var widget in editor.Document.Widgets)
            if (visuals.TryGetValue(widget.Id, out var visual)) visual.Text.Text = Display(widget);
    }
    private void Render()
    {
        if (editor == null) return;
        var document = editor.Document;
        if (dragging)
        {
            foreach (var widget in document.Widgets)
                if (visuals.TryGetValue(widget.Id, out var visual)) SetGeometry(visual.Root, widget);
            return;
        }
        scene.Children.Clear(); visuals.Clear();
        scene.Width = document.Screen.Width; scene.Height = document.Screen.Height; scene.Background = Brush(document.Screen.Background);
        foreach (var widget in document.Widgets)
        {
            var root = new Grid { ClipToBounds = true, Tag = widget.Id };
            var text = new TextBlock {
                Text = Display(widget), FontSize = widget.Style.FontSize, FontWeight = widget.Style.FontWeight == "bold" ? FontWeights.Bold : FontWeights.Normal,
                Foreground = Brush(widget.Style.Color), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = widget.Style.Alignment switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, _ => TextAlignment.Left }
            };
            var border = new Border { Background = Brush(widget.Style.Background), CornerRadius = new CornerRadius(widget.Style.BorderRadius),
                Padding = new Thickness(widget.Style.Padding), Opacity = widget.Style.Opacity, Child = text };
            root.Children.Add(border);
            var move = new Thumb { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Template = TransparentThumbTemplate() };
            var outline = new Border { BorderBrush = Brush("#6DE5B7"), BorderThickness = new Thickness(3), IsHitTestVisible = false, Tag = "selection" };
            root.Children.Add(move); root.Children.Add(outline);
            var resize = new Thumb { Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Background = Brush("#6DE5B7"), Cursor = Cursors.SizeNWSE };
            root.Children.Add(resize);
            move.PreviewMouseLeftButtonDown += (_, _) => Select(widget.Id);
            resize.PreviewMouseLeftButtonDown += (_, _) => Select(widget.Id);
            move.DragStarted += (_, _) => Begin(widget.Id);
            resize.DragStarted += (_, _) => Begin(widget.Id);
            move.DragDelta += (_, e) => Drag(e, false);
            resize.DragDelta += (_, e) => Drag(e, true);
            move.DragCompleted += (_, e) => Complete(e.Canceled);
            resize.DragCompleted += (_, e) => Complete(e.Canceled);
            SetGeometry(root, widget); scene.Children.Add(root);
            visuals[widget.Id] = (root, border, text, move, resize);
        }
        var footer = new Border { Width = scene.Width, Height = 48, Background = Brush("#101820"), IsHitTestVisible = false,
            Child = new TextBlock { Text = "PREVIEW LOCAL  ·  arraste os widgets  ·  Deploy envia ao tablet", FontSize = 18, Foreground = Brush("#A7BAC8"), Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center } };
        Canvas.SetTop(footer, Math.Max(0, scene.Height - 48)); scene.Children.Add(footer);
        if (selected != null && !visuals.ContainsKey(selected)) selected = null;
        UpdateSelection();
    }
    private void UpdateSelection()
    {
        foreach (var (id, visual) in visuals)
        {
            ((Border)visual.Root.Children[2]).Visibility = id == selected ? Visibility.Visible : Visibility.Collapsed;
            visual.Resize.Visibility = id == selected ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    private void Begin(string id)
    {
        if (editor == null) return;
        Select(id); dragStart = editor.Document.Widgets.First(w => w.Id == id);
        dragX = 0; dragY = 0; dragging = true; editor.BeginGesture();
    }
    private void Drag(DragDeltaEventArgs delta, bool resize)
    {
        if (editor == null || dragStart == null) return;
        dragX += delta.HorizontalChange; dragY += delta.VerticalChange;
        try
        {
            if (resize) editor.Resize(dragStart.Id, (int)Math.Round(dragStart.Width + dragX), (int)Math.Round(dragStart.Height + dragY), SnapToGrid ? 8 : 1);
            else editor.Move(dragStart.Id, (int)Math.Round(dragStart.X + dragX), (int)Math.Round(dragStart.Y + dragY), SnapToGrid ? 8 : 1);
        }
        catch (Exception error) { EditingError?.Invoke(error.Message); Complete(true); }
    }
    private void Complete(bool cancel)
    {
        if (!dragging || editor == null) return;
        dragging = false; dragStart = null;
        if (cancel) editor.CancelGesture(); else editor.CompleteGesture();
    }
    private string Display(Widget widget) => widget.Type == "value" ? SourceCatalog.Format(values.GetValueOrDefault(widget.Source!), widget.Format) : widget.Text ?? "";
    private static void SetGeometry(FrameworkElement root, Widget widget)
    {
        root.Width = widget.Width; root.Height = widget.Height; Canvas.SetLeft(root, widget.X); Canvas.SetTop(root, widget.Y);
    }
    private static ControlTemplate TransparentThumbTemplate()
    {
        var template = new ControlTemplate(typeof(Thumb));
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.BackgroundProperty, Brushes.Transparent); template.VisualTree = factory; return template;
    }
    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); return brush;
    }
}
