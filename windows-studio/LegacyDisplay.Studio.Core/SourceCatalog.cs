namespace LegacyDisplay.Studio.Core;

public sealed record SourceInfo(string Id, string Label, string Unit);

public static class SourceCatalog
{
    public static IReadOnlyList<SourceInfo> Sources { get; } = [
        new("pc.cpu.usage", "CPU · uso", "%"), new("pc.memory.usage", "Memória · uso", "%"),
        new("pc.memory.usedMiB", "Memória · utilizada", "MiB"), new("pc.uptime.seconds", "Windows · uptime", "s"),
        new("pc.network.rxBytesPerSecond", "Rede · recebimento", "bytes/s"), new("pc.network.txBytesPerSecond", "Rede · envio", "bytes/s"),
        new("pc.gpu.name", "GPU principal · nome", ""), new("pc.gpu.temperature", "GPU principal · temperatura", "°C"),
        new("pc.gpu.usage", "GPU principal · uso", "%"), new("pc.gpu.powerWatts", "GPU principal · potência", "W"),
        new("pc.gpu.memory.usedMiB", "GPU principal · VRAM utilizada", "MiB"), new("pc.gpu.memory.totalMiB", "GPU principal · VRAM total", "MiB"),
        new("action.lastResult", "Última resposta de ação", ""), new("demo.gpu.temperature", "GPU simulada · requer Agent --demo", "°C")
    ];
    public static string Format(object? value, string? format)
    {
        var text = value switch {
            null => "—", bool boolean => boolean ? "ON" : "OFF",
            IFormattable number => number.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "—"
        };
        return (format ?? "{value}").Replace("{value}", text, StringComparison.Ordinal);
    }
}
