using LegacyDisplay.Agent;
using LegacyDisplay.Windows;
using LegacyDisplay.Windows.Actions;

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    Console.WriteLine("""
        LegacyDisplay Agent v0.3 (Windows / .NET 8)
        pair   --device http://TABLET_IP:8765 --code 123456 [--credentials PATH]
        deploy --layout PATH [--device URL] [--credentials PATH]
        run    [--device URL] [--interval 1000] [--demo] [--credentials PATH] [--actions PATH]
        sensors   Prints local real sensor values and GPU source identifiers as JSON.
        configure-mining --project PATH [--actions PATH]   Registers MiningRoutine buttons without executing commands.

        --device overrides the stored URL, for example after an IP change or ADB forwarding.
        --interval: 100..10000 ms. --demo adds explicitly simulated demo.gpu.temperature.
        --actions: protected action catalog saved by Studio. Changes apply on the next touch.
        Ctrl+C stops the Agent. Tokens are protected using Windows DPAPI for the current user.
        """);
    return 0;
}

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
try
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 1; i < args.Length; i++)
    {
        var key = args[i];
        if (key == "--demo") { if (!options.TryAdd(key, "true")) throw new ArgumentException("Duplicate option."); continue; }
        if (key is not ("--device" or "--code" or "--credentials" or "--layout" or "--interval" or "--actions" or "--project") || i + 1 >= args.Length)
            throw new ArgumentException("Unknown option or missing value. Use --help.");
        if (!options.TryAdd(key, args[++i])) throw new ArgumentException("Duplicate option.");
    }
    string Required(string key) => options.TryGetValue(key, out var value) ? value : throw new ArgumentException($"{key} is required.");
    var path = options.GetValueOrDefault("--credentials", CredentialStore.DefaultPath);
    if (args[0] == "configure-mining")
    {
        var actionPath = options.GetValueOrDefault("--actions", ActionStore.DefaultPath);
        var catalog = MiningRoutineActions.Merge(ActionStore.Load(actionPath), MiningRoutineActions.Create(Required("--project")));
        if (File.Exists(actionPath)) File.Copy(actionPath, actionPath + ".backup-" + Guid.NewGuid().ToString("N"));
        ActionStore.Save(actionPath, catalog);
        Console.WriteLine("MiningRoutine actions registered: mining.auto, mining.paused, mining.force_mine, mining.exit_night, mining.quiet. No command executed. Reopen Studio to select them.");
        return 0;
    }
    if (args[0] == "pair")
    {
        var device = new Uri(Required("--device"));
        using var client = new DeviceClient(device);
        var token = await client.PairAsync(Required("--code"), shutdown.Token);
        CredentialStore.Save(path, device, token);
        Console.WriteLine($"Paired. Credential saved to {Path.GetFullPath(path)}.");
        return 0;
    }
    if (args[0] == "sensors")
    {
        using var metrics = new WindowsMetrics();
        metrics.Sample(false);
        await Task.Delay(1100, shutdown.Token);
        var values = metrics.Sample(false);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { values, sensors = metrics.HardwareSensors, gpuStatus = metrics.HardwareUnavailableReason }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    var credential = CredentialStore.Load(path);
    if (options.TryGetValue("--device", out var overrideUrl))
    {
        DeviceClient.ValidateDevice(new Uri(overrideUrl));
        credential = credential with { Device = overrideUrl };
    }
    switch (args[0])
    {
        case "deploy":
            using (var client = new DeviceClient(new Uri(credential.Device), credential.Token))
                await client.DeployAsync(await File.ReadAllTextAsync(Required("--layout"), shutdown.Token), shutdown.Token);
            Console.WriteLine("Layout validated, deployed and persisted on tablet.");
            break;
        case "run":
            var interval = int.Parse(options.GetValueOrDefault("--interval", "1000"), System.Globalization.CultureInfo.InvariantCulture);
            if (interval is < 100 or > 10000) throw new ArgumentException("Interval must be 100..10000 ms.");
            await new AgentSession().RunAsync(credential, options.ContainsKey("--demo"), interval, shutdown.Token, options.GetValueOrDefault("--actions"));
            break;
        default:
            throw new ArgumentException("Unknown command. Use --help.");
    }
    return 0;
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { return 0; }
catch (PairingRequiredException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
catch (Exception ex)
{
    // Never log server bodies or credentials.
    Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
    return 1;
}
