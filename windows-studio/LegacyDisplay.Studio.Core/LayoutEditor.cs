using System.Text.Json;
using LegacyDisplay.Protocol;

namespace LegacyDisplay.Studio.Core;

public sealed class LayoutEditor
{
    private string current;
    private string saved;
    private string? gestureStart;
    private readonly List<string> undo = [];
    private readonly List<string> redo = [];
    public event Action? Changed;
    public LayoutDocument Document => LayoutDocument.Parse(current);
    public string Json => current;
    public bool IsDirty => current != saved;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public LayoutEditor(string json)
    {
        current = Serialize(LayoutDocument.Parse(json));
        saved = current;
    }

    public void Load(string json)
    {
        var validated = Serialize(LayoutDocument.Parse(json));
        current = validated; saved = current; gestureStart = null;
        undo.Clear(); redo.Clear(); Changed?.Invoke();
    }
    public void MarkSaved() { saved = current; Changed?.Invoke(); }
    public void Save(string path)
    {
        var absolute = Path.GetFullPath(path);
        var temporary = absolute + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, current);
            File.Move(temporary, absolute, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        MarkSaved();
    }
    public string Add(string type, SourceInfo? source = null)
    {
        var document = Document;
        var id = UniqueId(type, document);
        var width = Math.Min(360, document.Screen.Width);
        var height = Math.Min(100, document.Screen.Height);
        var widget = new Widget {
            Id = id, Type = type, X = Math.Min(40, document.Screen.Width - width), Y = Math.Min(40, document.Screen.Height - height),
            Width = width, Height = height, Text = type == "value" ? null : type == "button" ? "TESTAR AÇÃO" : "Novo texto",
            Source = type == "value" ? source?.Id ?? "pc.cpu.usage" : null,
            Format = type == "value" ? source == null ? "{value} %" : "{value}" + (source.Unit.Length > 0 ? " " + source.Unit : "") : null,
            Action = type == "button" ? "demo.ping" : null,
            Style = new WidgetStyle { Background = type == "button" ? "#6DE5B7" : "#1B2B38", Color = type == "button" ? "#101820" : "#FFFFFF", Alignment = "center", BorderRadius = 16 }
        };
        Apply(document with { Widgets = [.. document.Widgets, widget] });
        return id;
    }
    public void Replace(string id, Widget widget)
    {
        var document = Document;
        var index = document.Widgets.FindIndex(w => w.Id == id);
        if (index < 0) throw new ArgumentException("Widget não encontrado.");
        document.Widgets[index] = widget;
        Apply(document);
    }
    public void Delete(string id)
    {
        var document = Document;
        if (document.Widgets.RemoveAll(w => w.Id == id) != 0) Apply(document);
    }
    public string Duplicate(string id)
    {
        var document = Document;
        var widget = document.Widgets.First(w => w.Id == id);
        var duplicated = widget with { Id = UniqueId(widget.Type, document), X = Math.Min(widget.X + 24, document.Screen.Width - widget.Width), Y = Math.Min(widget.Y + 24, document.Screen.Height - widget.Height) };
        Apply(document with { Widgets = [.. document.Widgets, duplicated] });
        return duplicated.Id;
    }
    public void Move(string id, int x, int y, int snap = 1)
    {
        var document = Document;
        var widget = document.Widgets.First(w => w.Id == id);
        var moved = widget with {
            X = Math.Clamp(Snap(x, snap), 0, document.Screen.Width - widget.Width),
            Y = Math.Clamp(Snap(y, snap), 0, document.Screen.Height - widget.Height)
        };
        Replace(id, moved);
    }
    public void Resize(string id, int width, int height, int snap = 1)
    {
        var document = Document;
        var widget = document.Widgets.First(w => w.Id == id);
        Replace(id, widget with {
            Width = Math.Clamp(Snap(width, snap), 1, document.Screen.Width - widget.X),
            Height = Math.Clamp(Snap(height, snap), 1, document.Screen.Height - widget.Y)
        });
    }
    public void Reorder(string id, int direction)
    {
        var document = Document;
        var index = document.Widgets.FindIndex(w => w.Id == id);
        if (index < 0) return;
        var target = Math.Clamp(index + direction, 0, document.Widgets.Count - 1);
        (document.Widgets[index], document.Widgets[target]) = (document.Widgets[target], document.Widgets[index]);
        Apply(document);
    }
    public void SetScreen(int width, int height, string background)
    {
        var document = Document;
        if (width is < 1 or > 4096 || height is < 1 or > 4096) throw new ArgumentException("Dimensões devem ficar entre 1 e 4096.");
        var widgets = document.Widgets.Select(w => {
            var newWidth = Math.Min(w.Width, width); var newHeight = Math.Min(w.Height, height);
            return w with { Width = newWidth, Height = newHeight, X = Math.Min(w.X, width - newWidth), Y = Math.Min(w.Y, height - newHeight) };
        }).ToList();
        Apply(document with { Screen = new Screen { Width = width, Height = height, Background = background }, Widgets = widgets });
    }
    public void BeginGesture()
    {
        if (gestureStart != null) throw new InvalidOperationException("Gesto já iniciado.");
        gestureStart = current;
    }
    public void CompleteGesture()
    {
        if (gestureStart is { } previous && previous != current) { PushUndo(previous); redo.Clear(); }
        gestureStart = null; Changed?.Invoke();
    }
    public void CancelGesture()
    {
        if (gestureStart is { } previous) current = previous;
        gestureStart = null; Changed?.Invoke();
    }
    public void Undo()
    {
        if (gestureStart != null) CancelGesture();
        if (!CanUndo) return;
        redo.Add(current); current = undo[^1]; undo.RemoveAt(undo.Count - 1); Changed?.Invoke();
    }
    public void Redo()
    {
        if (!CanRedo || gestureStart != null) return;
        PushUndo(current); current = redo[^1]; redo.RemoveAt(redo.Count - 1); Changed?.Invoke();
    }
    private void Apply(LayoutDocument document)
    {
        var json = Serialize(document);
        if (json == current) return;
        if (gestureStart == null) { PushUndo(current); redo.Clear(); }
        current = json; Changed?.Invoke();
    }
    private void PushUndo(string json) { undo.Add(json); if (undo.Count > 100) undo.RemoveAt(0); }
    private static string Serialize(LayoutDocument document)
    {
        document.Validate();
        var json = JsonSerializer.Serialize(document, LayoutDocument.JsonOptions);
        LayoutDocument.Parse(json);
        return json;
    }
    private static int Snap(int value, int grid) => (int)Math.Round((double)value / Math.Max(1, grid)) * Math.Max(1, grid);
    private static string UniqueId(string type, LayoutDocument document)
    {
        for (var i = 1; ; i++) { var candidate = $"{type}-{i}"; if (document.Widgets.All(w => w.Id != candidate)) return candidate; }
    }
}
