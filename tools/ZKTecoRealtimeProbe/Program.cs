namespace ZKTecoRealtimeProbe;

internal static class Program
{
    private const string Commands = "Comandos: [s] estado  [t] leer última ventana  [n] ReadNewGLogData  [a] ReadAllGLogData (solo cuenta y tiempo)  [h] ayuda  [q] salir";

    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Esta prueba solo corre en Windows (zkemkeeper es COM).");
            return 1;
        }

        if (Environment.Is64BitProcess)
        {
            Console.Error.WriteLine("El proceso es de 64 bits: zkemkeeper es de 32 bits. Publique con -r win-x86.");
            return 1;
        }

        var options = ProbeOptions.Parse(args, out var error);
        if (options == null)
        {
            if (error != null) Console.Error.WriteLine(error);
            Console.WriteLine(ProbeOptions.Usage);
            return error == null ? 0 : 1;
        }

        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        using var log = new ProbeLog(options.LogDirectory);
        log.Raw($"Prueba de tiempo real ZKTeco. Bitácora: {log.TextPath}");
        log.Raw($"CSV de checadas: {log.CsvPath}");
        log.Raw($"Latido cada {options.HeartbeatSeconds} s; caída tras {options.HeartbeatFailuresToDisconnect} fallos; máscara {options.EventMask}; recuperación de huecos {(options.CatchUpAfterReconnect ? "sí" : "no")}.");
        log.Raw(Commands);

        var probes = options.Devices.Select(d => new DeviceProbe(d, options, log)).ToList();
        foreach (var probe in probes) probe.Start();

        using var quit = new ManualResetEventSlim();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            quit.Set();
        };

        while (!quit.IsSet)
        {
            if (!Console.KeyAvailable)
            {
                quit.Wait(200);
                continue;
            }

            switch (char.ToLowerInvariant(Console.ReadKey(intercept: true).KeyChar))
            {
                case 's': probes.ForEach(p => p.Status()); break;
                case 't': probes.ForEach(p => p.ReadLastWindow()); break;
                case 'n': probes.ForEach(p => p.ReadNew()); break;
                case 'a': probes.ForEach(p => p.ReadAllCountOnly()); break;
                case 'h': log.Raw(Commands); break;
                case 'q': quit.Set(); break;
            }
        }

        log.Raw("Cerrando…");
        probes.ForEach(p => p.Status());
        foreach (var probe in probes) probe.Stop();
        return 0;
    }
}
