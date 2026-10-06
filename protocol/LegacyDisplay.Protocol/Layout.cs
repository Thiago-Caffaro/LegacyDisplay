using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LegacyDisplay.Protocol;

public sealed record LayoutDocument
{
    public required int Version { get; init; }
    public required Screen Screen { get; init; }
    public required List<Widget> Widgets { get; init; }

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static LayoutDocument Parse(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 65_536)
            throw new JsonException("Layout exceeds 64 KiB.");
        var layout = JsonSerializer.Deserialize<LayoutDocument>(json, JsonOptions)
            ?? throw new JsonException("Layout must be an object.");
        layout.Validate();
        return layout;
    }

    public void Validate()
    {
        Require(Version == 1, "Only layout version 1 is supported.");
        Require(Screen is not null, "screen is required.");
        Require(Screen!.Width is >= 1 and <= 4096 && Screen.Height is >= 1 and <= 4096, "Invalid screen dimensions.");
        Require(Screen.Background is not null && Color.IsMatch(Screen.Background), "Invalid screen background.");
        Require(Widgets is not null && Widgets.Count <= 128, "At most 128 widgets are allowed.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var widget in Widgets!)
        {
            Require(widget is not null, "Widget must be an object.");
            Require(widget!.Id is not null && Identifier.IsMatch(widget.Id) && ids.Add(widget.Id), "Invalid or duplicate widget id.");
            Require(widget.Type is "text" or "value" or "button", "Unsupported widget type.");
            Require(widget.X >= 0 && widget.Y >= 0 && widget.Width > 0 && widget.Height > 0 &&
                (long)widget.X + widget.Width <= Screen.Width && (long)widget.Y + widget.Height <= Screen.Height, "Widget must fit inside screen.");
            Require(widget.Text is null || widget.Text.Length <= 256, "Text is too long.");
            Require(widget.Format is null || widget.Format.Length <= 128, "Format is too long.");
            Require(widget.Source is null || Identifier.IsMatch(widget.Source), "Invalid source.");
            if (widget.Type == "value")
                Require(widget.Source is not null && Identifier.IsMatch(widget.Source), "Value requires a valid source.");
            else
                Require(widget.Text is not null, "Text and Button require text.");
            if (widget.Type == "button")
                Require(widget.Action is not null && Identifier.IsMatch(widget.Action), "Button requires an action id.");
            else
                Require(widget.Action is null, "Only buttons can have actions.");
            var style = widget.Style;
            Require(style is not null, "Style cannot be null.");
            Require(style!.Background is not null && style.Color is not null && Color.IsMatch(style.Background) && Color.IsMatch(style.Color), "Invalid style color.");
            Require(style.FontSize is >= 1 and <= 200 && style.Padding is >= 0 and <= 100 &&
                style.BorderRadius is >= 0 and <= 200 && double.IsFinite(style.Opacity) && style.Opacity is >= 0 and <= 1, "Invalid style dimensions or opacity.");
            Require(style.FontWeight is "normal" or "bold", "Invalid fontWeight.");
            Require(style.Alignment is "left" or "center" or "right", "Invalid alignment.");
        }
    }

    private static readonly Regex Identifier = new("^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}\\z", RegexOptions.CultureInvariant);
    private static readonly Regex Color = new("^#[0-9a-fA-F]{6}\\z", RegexOptions.CultureInvariant);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new JsonException(message);
    }
}

public sealed record Screen
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public string Background { get; init; } = "#101820";
}

public sealed record Widget
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public string? Text { get; init; }
    public string? Source { get; init; }
    public string? Format { get; init; }
    public string? Action { get; init; }
    public WidgetStyle Style { get; init; } = new();
}

public sealed record WidgetStyle
{
    public string Background { get; init; } = "#101820";
    public string Color { get; init; } = "#FFFFFF";
    public int FontSize { get; init; } = 36;
    public string FontWeight { get; init; } = "normal";
    public string Alignment { get; init; } = "left";
    public int BorderRadius { get; init; } = 0;
    public int Padding { get; init; } = 12;
    public double Opacity { get; init; } = 1;
}
