# PCTV

Use a Windows PC's browser on Apple TV. Host (C#/.NET 8) in `host/`, tvOS app (Swift) in `tvos/`, wire protocol in `docs/protocol.md`.

Status: M0 and the host side of M1 are done; the tvOS app has not been compiled yet.

## Build the tvOS app (Mac)

```bash
brew install xcodegen
cd tvos && xcodegen && open PcTv.xcodeproj
```

1. Edit `hostAddress` in `tvos/PcTv/UI/StreamViewController.swift` to the PC's LAN IP.
2. Set your signing team, then run on the Apple TV.

## Run the host (Windows)

Set `"RequirePairing": false` in `%APPDATA%\pctv\pctv.json` (until M7), then:

```
cd host
dotnet run --project PcTv.Host -- run --test-pattern
```

Allow TCP 47800 through Windows Firewall. Needs ffmpeg with `hevc_nvenc` on PATH.
