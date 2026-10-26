using PcTv.Host;
using PcTv.Host.Net;

// pctv run [--test-pattern] | pair <PIN> | doctor
var cmd = args.Length > 0 ? args[0] : "run";
var cfg = Config.Load();

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
    case "--test-pattern":
        var testPattern = args.Contains("--test-pattern");
        using (var cts = new CancellationTokenSource())
        {
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            await new Server(cfg, new Pairing(), testPattern).RunAsync(cts.Token);
        }
        return 0;

    default:
        Console.Error.WriteLine("usage: pctv run [--test-pattern] | pair <PIN> | doctor");
        return 2;
}
