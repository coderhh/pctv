#!/usr/bin/env python3
"""Headless PCTV client: connect, HELLO/WELCOME, optional pairing, save streams.

  python tools/test_client.py [--host 127.0.0.1] [--port 47800] [--pair PIN]
                              [--token T] [--seconds N] [--video out.hevc] [--audio out.wav]
                              [--input script.json]
"""
import argparse, json, socket, struct, sys, time, wave

HELLO, WELCOME, PAIR_BEGIN, PAIR_RESULT = 0x01, 0x02, 0x03, 0x04
VIDEO, AUDIO, INPUT, TEXT_COMMIT = 0x10, 0x11, 0x20, 0x21
TEXT_FOCUS, APPS, PING, PONG, STATS = 0x30, 0x31, 0x40, 0x41, 0x42


def send(sock, t, payload=b""):
    sock.sendall(struct.pack(">BBI", t, 0, len(payload)) + payload)


def send_json(sock, t, obj):
    send(sock, t, json.dumps(obj).encode())


def recv_exact(sock, n):
    buf = bytearray()
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk:
            raise EOFError
        buf += chunk
    return bytes(buf)


def recv(sock):
    t, _flags, ln = struct.unpack(">BBI", recv_exact(sock, 6))
    return t, recv_exact(sock, ln)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=47800)
    ap.add_argument("--tv-id", default="test-client")
    ap.add_argument("--token")
    ap.add_argument("--pair", metavar="PIN")
    ap.add_argument("--seconds", type=float, default=0, help="keep streaming this long after WELCOME")
    ap.add_argument("--video")
    ap.add_argument("--audio")
    ap.add_argument("--input", help="JSON file: list of INPUT objects to send after WELCOME")
    a = ap.parse_args()

    s = socket.create_connection((a.host, a.port), timeout=10)
    s.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)

    def hello():
        send_json(s, HELLO, {"v": 1, "tvId": a.tv_id, "token": a.token,
                             "display": {"w": 3840, "h": 2160, "fps": 60},
                             "codecs": ["hevc"], "audio": ["pcm_s16le_48k_2ch"]})
        t, p = recv(s)
        assert t == WELCOME, f"expected WELCOME, got {t:#x}"
        return json.loads(p)

    welcome = hello()
    if welcome.get("error") == "unpaired" and a.pair:
        send_json(s, PAIR_BEGIN, {"tvId": a.tv_id, "pin": a.pair})
        t, p = recv(s)
        res = json.loads(p)
        assert t == PAIR_RESULT and res.get("ok"), f"pairing failed: {res}"
        a.token = res["token"]
        print("paired; token =", a.token)
        welcome = hello()
    if "error" in welcome:
        print("WELCOME error:", welcome["error"])
        return 1
    print("WELCOME:", json.dumps(welcome))

    t0 = time.monotonic()
    send(s, PING, struct.pack(">Q", int(t0 * 1e6)))
    if a.input:
        for ev in json.load(open(a.input)):
            send_json(s, INPUT, ev)

    vf = open(a.video, "wb") if a.video else None
    wf = None
    if a.audio:
        wf = wave.open(a.audio, "wb")
        wf.setnchannels(welcome["audio"]["channels"]); wf.setsampwidth(2); wf.setframerate(welcome["audio"]["rate"])

    s.settimeout(1.0)
    deadline = time.monotonic() + a.seconds
    counts = {}
    while time.monotonic() < deadline or (a.seconds == 0 and counts.get("pong", 0) == 0):
        try:
            t, p = recv(s)
        except socket.timeout:
            continue
        except EOFError:
            print("host closed connection"); break
        if t == PONG:
            counts["pong"] = counts.get("pong", 0) + 1
            print(f"PONG rtt={(time.monotonic() * 1e6 - struct.unpack('>Q', p)[0]) / 1000:.2f} ms")
        elif t == VIDEO:
            counts["video"] = counts.get("video", 0) + 1
            if vf: vf.write(p[8:])
        elif t == AUDIO:
            counts["audio"] = counts.get("audio", 0) + 1
            if wf: wf.writeframes(p[8:])
        elif t == APPS:
            print("APPS:", p.decode())
        elif t == TEXT_FOCUS:
            print("TEXT_FOCUS:", p.decode())
    print("received:", counts)
    if vf: vf.close()
    if wf: wf.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
