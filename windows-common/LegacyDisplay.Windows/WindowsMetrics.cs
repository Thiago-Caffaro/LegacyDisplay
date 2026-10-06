using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace LegacyDisplay.Windows;

public sealed class WindowsMetrics : IDisposable
{
    private readonly HardwareMetrics hardware = new();
    private readonly object sync = new();
    private bool disposed;
    public IReadOnlyList<HardwareSensorInfo> HardwareSensors { get; private set; } = [];
    public string? HardwareUnavailableReason => hardware.UnavailableReason;
    private ulong previousIdle, previousTotal;
    private long previousReceived, previousSent;
    private long previousTicks;

    public Dictionary<string, object?> Sample(bool demo)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return SampleCore(demo);
        }
    }
    private Dictionary<string, object?> SampleCore(bool demo)
    {
        var values = new Dictionary<string, object?> { ["pc.uptime.seconds"] = Environment.TickCount64 / 1000 };
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var total = kernel + user;
            if (previousTotal != 0 && total > previousTotal)
                values["pc.cpu.usage"] = Math.Round(Math.Clamp(100.0 * (1 - (double)(idle - previousIdle) / (total - previousTotal)), 0, 100), 1);
            previousIdle = idle;
            previousTotal = total;
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref memory))
        {
            values["pc.memory.usage"] = memory.MemoryLoad;
            values["pc.memory.usedMiB"] = (memory.TotalPhysical - memory.AvailablePhysical) / 1048576;
        }
        long received = 0, sent = 0;
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            try { var stats = network.GetIPStatistics(); received += stats.BytesReceived; sent += stats.BytesSent; }
            catch (NetworkInformationException) { }
        }
        var ticks = Environment.TickCount64;
        if (previousTicks != 0 && ticks > previousTicks)
        {
            var elapsed = (ticks - previousTicks) / 1000.0;
            values["pc.network.rxBytesPerSecond"] = Math.Round(Math.Max(0, received - previousReceived) / elapsed);
            values["pc.network.txBytesPerSecond"] = Math.Round(Math.Max(0, sent - previousSent) / elapsed);
        }
        previousTicks = ticks; previousReceived = received; previousSent = sent;
        var gpu = hardware.Sample();
        HardwareSensors = gpu.Sensors;
        foreach (var pair in gpu.Values) values[pair.Key] = pair.Value;
        if (demo) values["demo.gpu.temperature"] = Math.Round(55 + Math.Sin(ticks / 5000.0) * 8, 1);
        return values;
    }

    public void Dispose() { lock (sync) { if (disposed) return; disposed = true; hardware.Dispose(); } }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
}
