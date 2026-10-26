using System.Runtime.InteropServices;
using System.Windows.Forms;
using PcTv.Host;
using PcTv.Host.Net;

// pctv [ui] [--test-pattern] | run [--test-pattern] | pair <PIN> | doctor
var cmd = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "ui";
var cfg = Config.Load();
var testPattern = args.Contains("--test-pattern");

switch (cmd)
{
    case "pair":
        if (args.Length < 2) { Console.Error.WriteLine("usage: pctv pair <PIN>"); return 2; }
        Pairing.SetPendingPin(args[1]);
        Console.WriteLine("PIN recorded; confirm it on the TV within a minute.");
        return 0;

    case "doctor":
        return Doctor.Run(cfg);

    case "run":
        using (var cts = new CancellationTokenSource())
        {
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            await new Server(cfg, new Pairing(), testPattern).RunAsync(cts.Token);
        }
        return 0;

    case "ui":
    {
        FreeConsole(); // no console window for the tray app
        var cts = new CancellationTokenSource();
        var server = Task.Run(async () =>
        {
            try { await new Server(cfg, new Pairing(), testPattern).RunAsync(cts.Token); }
            catch (Exception e) when (e is not OperationCanceledException) { HostStatus.Error ??= e.Message; HostStatus.Log($"server stopped: {e.Message}"); }
        });
        var ui = new Thread(() =>
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new StatusWindow(() => cts.Cancel()));
        });
        ui.SetApartmentState(ApartmentState.STA); // WinForms needs STA
        ui.Start();
        ui.Join();
        cts.Cancel();
        return 0;
    }

    default:
        Console.Error.WriteLine("usage: pctv [ui] [--test-pattern] | run [--test-pattern] | pair <PIN> | doctor");
        return 2;
}

[DllImport("kernel32.dll")] static extern bool FreeConsole();
