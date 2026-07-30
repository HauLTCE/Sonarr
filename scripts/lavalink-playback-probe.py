#!/usr/bin/env python3
"""Prove the Lavalink node can actually *play* a track, not just resolve its metadata.

Why this exists: /loadtracks and the /version healthcheck both passed green through the
outage of 2026-07-29, because the failure was one layer deeper -- the youtube plugin
resolved the track fine and then failed to pick an audio format when playback started
(AllClientsFailedException). Nothing that checks liveness can see that. This opens its own
session so it never disturbs the running bot's players, starts a real track, and reports
which of TrackStart / TrackException / TrackStuck the node emits.

Stdlib only (the CT has no `websockets` and no node), and read-only apart from the
throwaway player it creates under a guild id the bot does not manage, which it deletes.

It plays the way the bot plays: by *identifier*, not by the encoded blob. That distinction
is the whole point -- TrackMapping.FromDomain keeps only the identifier, so the node
re-resolves the track when playback starts, and a probe that sends `encoded` skips that
resolve entirely and passes while the bot gets a 400. Pass `encoded` as the third argument
to compare the two paths.

Usage:  python3 lavalink-playback-probe.py <password> [url-or-search] [identifier|encoded]
Exit 0 = a track actually started playing. Exit 1 = it did not.
"""
import json
import os
import socket
import sys
import urllib.parse
import urllib.request

HOST, PORT = "127.0.0.1", 2333
# A guild the bot is not in, so the events below can never collide with a live player and
# TrackEventRelay never sees them (they arrive on this script's session, not the bot's).
PROBE_GUILD = "1"
DEADLINE_S = 40.0


def rest(password, method, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(f"http://{HOST}:{PORT}{path}", data=data, method=method)
    req.add_header("Authorization", password)
    if data:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            raw = r.read()
            return r.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode(errors="replace")


def ws_connect(password):
    """Minimal RFC6455 client handshake -- enough to make Lavalink mint a session."""
    s = socket.create_connection((HOST, PORT), timeout=15)
    key = "dGhlIHNhbXBsZSBub25jZQ=="  # fixed nonce is fine; we do not verify the accept
    s.sendall(
        f"GET /v4/websocket HTTP/1.1\r\nHost: {HOST}:{PORT}\r\n"
        "Upgrade: websocket\r\nConnection: Upgrade\r\n"
        f"Sec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n"
        f"Authorization: {password}\r\nUser-Id: 1\r\nClient-Name: sonarr-probe/1.0\r\n\r\n".encode()
    )
    buf = b""
    while b"\r\n\r\n" not in buf:
        chunk = s.recv(4096)
        if not chunk:
            raise SystemExit("node closed the connection during the websocket handshake")
        buf += chunk
    head, _, rest_bytes = buf.partition(b"\r\n\r\n")
    if b"101" not in head.split(b"\r\n")[0]:
        raise SystemExit(f"websocket upgrade refused: {head.decode(errors='replace')[:200]}")
    return s, rest_bytes


def frames(sock, pending):
    """Yield text-frame payloads. Server-to-client frames are never masked."""
    buf = pending

    def need(n):
        nonlocal buf
        while len(buf) < n:
            chunk = sock.recv(65536)
            if not chunk:
                raise StopIteration
            buf += chunk
        out, buf = buf[:n], buf[n:]
        return out

    while True:
        try:
            b0, b1 = need(2)
            opcode, length = b0 & 0x0F, b1 & 0x7F
            if length == 126:
                length = int.from_bytes(need(2), "big")
            elif length == 127:
                length = int.from_bytes(need(8), "big")
            payload = need(length) if length else b""
        except (StopIteration, socket.timeout, OSError):
            return
        if opcode == 0x8:  # close
            return
        if opcode == 0x1:
            yield payload.decode(errors="replace")


def main():
    if len(sys.argv) < 2:
        raise SystemExit("usage: lavalink-playback-probe.py <password> [url-or-search] [identifier|encoded]")
    password = sys.argv[1]
    wanted = sys.argv[2] if len(sys.argv) > 2 else "ytsearch:temper city self aware"
    mode = sys.argv[3] if len(sys.argv) > 3 else "identifier"
    if mode not in ("identifier", "encoded", "uri"):
        raise SystemExit(f"mode must be 'identifier', 'uri' or 'encoded', not {mode!r}")

    status, info = rest(password, "GET", "/v4/info")
    if status != 200:
        raise SystemExit(f"/v4/info failed: {status} {info}")
    print(f"node {info['version']['semver']}  plugins={[p['name'] + ' ' + p['version'] for p in info['plugins']]}")

    ident = urllib.parse.quote(wanted, safe="")
    status, loaded = rest(password, "GET", f"/v4/loadtracks?identifier={ident}")
    if status != 200:
        raise SystemExit(f"loadtracks failed: {status} {loaded}")
    load_type = loaded.get("loadType")
    data = loaded.get("data")
    track = data[0] if load_type == "search" else (data.get("tracks", [None])[0] if load_type == "playlist" else data)
    if load_type in ("empty", "error") or not track:
        raise SystemExit(f"nothing to play (loadType={load_type}): {data}")
    print(f"loaded [{load_type}] {track['info']['title']} ({track['info']['identifier']})")

    sock, pending = ws_connect(password)
    sock.settimeout(DEADLINE_S)
    session = None
    try:
        for msg in frames(sock, pending):
            event = json.loads(msg)
            if event.get("op") == "ready":
                session = event["sessionId"]
                break
        if not session:
            raise SystemExit("node never sent a ready op")
        print(f"probe session {session}")

        # No voice payload on purpose: the track executor that failed in the outage runs
        # independently of the Discord voice connection, so this reaches the same code
        # without the bot joining any channel.
        #
        # `identifier` is the default because that is what the bot sends. A 400 here
        # ("Something went wrong while looking up the track") is a real /play failure that
        # the `encoded` path cannot see, because encoded carries the resolved formats with it.
        payload = ({"identifier": track["info"]["identifier"]} if mode == "identifier"
                   else {"identifier": track["info"]["uri"]} if mode == "uri"
                   else {"encoded": track["encoded"]})
        print(f"playing by {mode}: {json.dumps(payload)[:120]}")
        status, resp = rest(
            password, "PATCH",
            f"/v4/sessions/{session}/players/{PROBE_GUILD}?noReplace=false",
            {"track": payload, "volume": 0},
        )
        if status not in (200, 204):
            print(f"player update REJECTED: {status} {json.dumps(resp)[:400] if not isinstance(resp, str) else resp[:400]}")
            return 1

        verdict = 1
        for msg in frames(sock, pending=b""):
            event = json.loads(msg)
            if event.get("op") != "event":
                continue
            kind = event.get("type")
            if kind == "TrackStartEvent":
                print("TrackStartEvent -- the node picked an audio format and began playing")
                verdict = 0
                break
            if kind in ("TrackExceptionEvent", "TrackStuckEvent"):
                detail = event.get("exception") or {"thresholdMs": event.get("thresholdMs")}
                print(f"{kind} -- playback FAILED: {json.dumps(detail)[:600]}")
                break
            if kind == "TrackEndEvent":
                print(f"TrackEndEvent reason={event.get('reason')} (no start seen)")
                break
        else:
            print(f"no track event within {DEADLINE_S:.0f}s")
        return verdict
    finally:
        if session:
            rest(password, "DELETE", f"/v4/sessions/{session}/players/{PROBE_GUILD}")
        sock.close()


if __name__ == "__main__":
    sys.exit(main())
