using System.Text.Json;
using LegacyDisplay.Protocol;
using LegacyDisplay.Studio.Core;
using LegacyDisplay.Windows;
using Xunit;

public sealed class EditorTests
{
    private static LayoutEditor Editor() => new(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "dashboard.json")));
    [Fact]
    public void InvalidOpenOrPropertyEditPreservesLayoutAndHistory()
    {
        var editor = Editor(); var original = editor.Json;
        Assert.Throws<JsonException>(() => editor.Load("{}"));
        Assert.Equal(original, editor.Json); Assert.False(editor.CanUndo);
        var widget = editor.Document.Widgets[0];
        Assert.Throws<JsonException>(() => editor.Replace(widget.Id, widget with { X = -1 }));
        Assert.Equal(original, editor.Json); Assert.False(editor.IsDirty);
    }
    [Fact]
    public void DragIsOneUndoStepAndCancelRestoresTheStart()
    {
        var editor = Editor(); var original = editor.Json;
        editor.BeginGesture(); editor.Move("title", 50, 100); editor.Move("title", 60, 110); editor.CompleteGesture();
        Assert.True(editor.IsDirty); editor.Undo(); Assert.Equal(original, editor.Json); Assert.False(editor.CanUndo); Assert.False(editor.IsDirty);
        editor.Redo(); var changed = editor.Json;
        editor.BeginGesture(); editor.Move("title", 70, 160); editor.CancelGesture(); Assert.Equal(changed, editor.Json);
    }
    [Fact]
    public void AddDuplicateDeleteAndOrderingHaveStableUniqueIds()
    {
        var editor = Editor(); var before = editor.Document.Widgets.Count;
        var id = editor.Add("button"); var copied = editor.Duplicate(id);
        Assert.NotEqual(id, copied); Assert.Equal(before + 2, editor.Document.Widgets.Count);
        editor.Reorder(copied, -1); Assert.Equal(copied, editor.Document.Widgets[^2].Id);
        editor.Delete(id); Assert.DoesNotContain(editor.Document.Widgets, w => w.Id == id);
        editor.Undo(); Assert.Contains(editor.Document.Widgets, w => w.Id == id);
    }
    [Fact]
    public void SmallerScreenAndExtremeGeometryStayInsideTheLayout()
    {
        var editor = Editor(); editor.Move("title", -999, 99999, 8); editor.Resize("title", 9999, 9999, 8);
        editor.SetScreen(400, 640, "#223344");
        editor.Document.Validate();
        Assert.All(editor.Document.Widgets, w => { Assert.InRange(w.X + w.Width, 1, 400); Assert.InRange(w.Y + w.Height, 1, 640); });
    }
    [Fact]
    public void SavingAndUndoTrackTheSavedRevision()
    {
        var editor = Editor(); var directory = Path.Combine(Path.GetTempPath(), "legacydisplay-editor-" + Guid.NewGuid());
        Directory.CreateDirectory(directory); var path = Path.Combine(directory, "layout.json");
        try
        {
            editor.Add("text"); editor.Save(path); Assert.False(editor.IsDirty); LayoutDocument.Parse(File.ReadAllText(path));
            editor.Add("value"); Assert.True(editor.IsDirty); editor.Undo(); Assert.False(editor.IsDirty);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public void SourceSelectionIsOneUndoStepAndFormattingMatchesProtocol()
    {
        var editor = Editor(); var original = editor.Json;
        var id = editor.Add("value", new SourceInfo("pc.memory.usedMiB", "RAM", "MiB"));
        var widget = editor.Document.Widgets.First(w => w.Id == id);
        Assert.Equal("pc.memory.usedMiB", widget.Source); Assert.Equal("{value} MiB", widget.Format);
        editor.Undo(); Assert.Equal(original, editor.Json);
        Assert.Equal("12.3 %", SourceCatalog.Format(12.34, "{value} %"));
        Assert.Equal("— °C", SourceCatalog.Format(null, "{value} °C")); Assert.Equal("ON", SourceCatalog.Format(true, null));
    }
    [Fact]
    public void AdbListingSeparatesUnauthorizedAndReadyDevices()
    {
        var devices = AdbConnector.ParseDevices("* daemon started successfully *\nList of devices attached\n31000 device product:lineage model:SM_T561\nother unauthorized\noff offline\n");
        Assert.Equal(3, devices.Count); Assert.Equal("SM T561", devices[0].Model);
        Assert.Equal("device", devices[0].State); Assert.Equal("unauthorized", devices[1].State);
    }
}
