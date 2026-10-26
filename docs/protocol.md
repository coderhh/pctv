# PCTV wire protocol v1

Single TCP connection (default port 47800), `TCP_NODELAY` on both sides. One TV session at a time.

## Framing

Every message:

```
u8  type
u8  flags      (reserved, 0)
u32 length     (big-endian, payload bytes)
... payload
```

Max payload: 16 MiB (receivers must drop the connection on larger).
JSON payloads are UTF-8. Integers inside binary payloads are big-endian.

## Messages

| Type | Name | Dir | Payload |
|---|---|---|---|
| 0x01 | HELLO | TV→PC | JSON `{v:1, tvId, token?, display:{w,h,fps}, codecs:["hevc"], audio:["pcm_s16le_48k_2ch"]}` |
| 0x02 | WELCOME | PC→TV | JSON `{v:1, hostId, name, video:{codec,w,h,fps,bitrate}, audio:{codec,rate,channels}, viewport:{w,h}}` or `{v:1, error:"unpaired"}` (host then closes) |
| 0x03 | PAIR_BEGIN | TV→PC | JSON `{tvId, pin}` (TV displays the same PIN) |
| 0x04 | PAIR_RESULT | PC→TV | JSON `{ok, token?}` |
| 0x10 | VIDEO | PC→TV | `u64 ptsMicros` + one Annex-B HEVC access unit |
| 0x11 | AUDIO | PC→TV | `u64 ptsMicros` + PCM s16le interleaved |
| 0x20 | INPUT | TV→PC | JSON, see below |
| 0x21 | TEXT_COMMIT | TV→PC | JSON `{text}` |
| 0x30 | TEXT_FOCUS | PC→TV | JSON `{focused, inputType, value, placeholder}` |
| 0x31 | APPS | PC→TV | JSON array `[{id,title,url,icon?}]` |
| 0x40 | PING | both | `u64 sentMicros` |
| 0x41 | PONG | both | echo of the PING payload |
| 0x42 | STATS | TV→PC | JSON `{fps, dropped, bufferedFrames, rttMs}` |

### INPUT payloads

- `{t:"move", x, y}` — normalized 0..1 within the viewport
- `{t:"click", button:"left"|"right", count}`
- `{t:"scroll", dx, dy}` — CSS px
- `{t:"key", key}` — named key (ArrowUp, Enter, Escape, Space, Tab, Backspace, MediaPlayPause, …)
- `{t:"back"}`
- `{t:"navigate", url}`

## Session flow

1. TV connects, sends HELLO.
2. Host checks `token` against its paired list for `tvId`.
   - Valid (or pairing not required): WELCOME, then starts streaming VIDEO/AUDIO and sends APPS.
   - Otherwise: WELCOME `{error:"unpaired"}`; the TV may then send PAIR_BEGIN on the same connection.
3. Pairing: owner runs `pctv pair <PIN>` on the PC; host compares with `PAIR_BEGIN.pin`; on match it generates a 32-byte token (hex), stores `{tvId, token}`, replies PAIR_RESULT `{ok:true, token}`. The TV then reconnects with the token.
4. Either side sends PING periodically; peer answers PONG with the same payload.

## Versioning

`v` is in HELLO/WELCOME. A host receiving an unsupported `v` replies `{error:"version"}` and closes.
