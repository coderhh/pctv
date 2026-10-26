using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace PcTv.Host;

/// <summary>Small status window plus tray icon. Closing the window hides it; use the tray menu to quit.</summary>
public sealed class StatusWindow : Form
{
    readonly Label _server = new(), _tv = new(), _video = new(), _audio = new(), _monitor = new(), _chrome = new();
    readonly TextBox _log = new();
    readonly NotifyIcon _tray = new();
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    readonly Action _quit;
    long _lastFrames, _lastBytes;
    DateTime _lastTick = DateTime.UtcNow;
    bool _exiting;

    public StatusWindow(Action quit)
    {
        _quit = quit;
        Text = "PCTV Host";
        ClientSize = new Size(520, 420);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(10) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string name, Label value)
        {
            value.AutoSize = true;
            table.Controls.Add(new Label { Text = name, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 3, 0, 3) });
            table.Controls.Add(value);
        }
        Row("Server", _server);
        Row("Monitor", _monitor);
        Row("Chrome", _chrome);
        Row("Apple TV", _tv);
        Row("Video", _video);
        Row("Audio", _audio);

        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Dock = DockStyle.Fill;
        _log.Font = new Font("Consolas", 9);
        Controls.Add(_log);
        Controls.Add(table);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowWindow());
        menu.Items.Add("Quit", null, (_, _) => { _exiting = true; _tray.Visible = false; _quit(); Application.Exit(); });
        _tray.ContextMenuStrip = menu;
        _tray.Icon = SystemIcons.Application;
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowWindow();

        _timer.Tick += (_, _) => Refresh_();
        _timer.Start();
        Refresh_();
    }

    void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        _tray.Visible = false;
        base.OnFormClosing(e);
    }

    void Refresh_()
    {
        var now = DateTime.UtcNow;
        double dt = Math.Max(0.001, (now - _lastTick).TotalSeconds);
        long frames = Interlocked.Read(ref HostStatus.VideoFrames), bytes = Interlocked.Read(ref HostStatus.VideoBytes);
        double fps = (frames - _lastFrames) / dt, mbps = (bytes - _lastBytes) * 8 / dt / 1e6;
        (_lastFrames, _lastBytes, _lastTick) = (frames, bytes, now);
        if (fps < 0) { fps = 0; mbps = 0; } // counters reset on new session

        _server.Text = HostStatus.Listening
            ? $"Listening on port {HostStatus.Port}{(HostStatus.TestPattern ? "  (test pattern)" : "")}"
            : HostStatus.Error ?? "Not running";
        _server.ForeColor = HostStatus.Listening ? Color.SeaGreen : Color.Firebrick;
        _monitor.Text = HostStatus.TestPattern ? "n/a (test pattern)" : HostStatus.MonitorText;
        _monitor.ForeColor = HostStatus.Monitor is not null || HostStatus.TestPattern ? SystemColors.ControlText : Color.Firebrick;
        _chrome.Text = HostStatus.TestPattern ? "n/a (test pattern)" : HostStatus.ChromeText;
        _chrome.ForeColor = HostStatus.ChromeText.StartsWith("Running") ? Color.SeaGreen : SystemColors.ControlText;
        _tv.Text = HostStatus.Connected ? $"Connected: {HostStatus.TvId} ({HostStatus.Remote})" : "Waiting for connection";
        _tv.ForeColor = HostStatus.Connected ? Color.SeaGreen : SystemColors.GrayText;
        _video.Text = HostStatus.Connected && frames > 0 ? $"{fps:0} fps, {mbps:0.0} Mbps, {frames} frames" : "Idle";
        _audio.Text = HostStatus.Connected && HostStatus.AudioChunks > 0 ? $"{HostStatus.AudioChunks} chunks sent" : "Idle";

        var text = string.Join(Environment.NewLine, HostStatus.RecentLog());
        if (text != _log.Text)
        {
            _log.Text = text;
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }
        _tray.Text = HostStatus.Connected ? "PCTV: TV connected" : "PCTV: waiting";
    }
}
