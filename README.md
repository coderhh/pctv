# PCTV

Use a Windows PC's browser on Apple TV. Host (C#/.NET 8) in `host/`, tvOS app (Swift) in `tvos/`, wire protocol in `docs/protocol.md`.

Status: M0 and the host side of M1 are done; the tvOS app has not been compiled yet.

## Build the tvOS app (Mac)

```bash
brew install xcodegen
cd tvos && xcodegen && open PcTv.xcodeproj
```

1. On first launch the app asks for the PC's IP (saved across rebuilds; Play/Pause on the remote changes it). Or set env var `PCTV_HOST` in the Xcode scheme (Run > Arguments).
2. Set your signing team, then run on the Apple TV.

## Run the host (Windows)

Set `"RequirePairing": false` in `%APPDATA%\pctv\pctv.json` (until M7), then:

```
cd host
dotnet run --project PcTv.Host -- ui --test-pattern
```

`ui` opens a small status window with a tray icon (server, TV connection, fps/bitrate, log); closing the window hides it, quit from the tray menu. `run` is the console-only equivalent.

Allow TCP 47800 through Windows Firewall. Needs ffmpeg with `hevc_nvenc` on PATH.

## M2 prerequisites (Windows)

- A virtual 3840x2160@60 monitor (e.g. VirtualDrivers/Virtual-Display-Driver). The host only auto-picks monitors whose hardware ID looks virtual; otherwise set `VirtualMonitorName` (e.g. `\\.\DISPLAY3`) in `pctv.json`. Run `pctv monitors` to list outputs.
- A separate Chrome (Chrome for Testing) at `C:\pctv\chrome\chrome.exe`, or set `ChromePath`.
- Then `pctv ui` (without `--test-pattern`) launches TV Chrome on that monitor and streams it. `pctv doctor` checks all of this.
