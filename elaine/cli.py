"""CLI entry point: interactive REPL plus a toolbox of inspection subcommands.

Subcommands:
  (default)  interactive REPL against --script   (slash-commands: type /help)
  graph      export the graph (--format dot|mermaid|json)
  check      run the acceptance checker (--json for machine output)
  train      train the optional ML intent classifier (offline)
  validate   load a script and report OK / the first error
  lint       report non-fatal quality warnings (unused intents/symbols/pools, dead edges)
  stats      counts: states, transitions, intents, pools, ... (--json)
  pools      list response pools and their sizes (--json)
  sample     preview N grammar expansions of a #symbol#
  explain    show which transition an input fires from a given state
  replay     run a replay-fixture transcript and report pass/fail
  profile    show a stored user's Self from a SQLite DB
  users      list stored users in a SQLite DB
  reset      wipe a stored user
  export-self  dump a stored user's Self as JSON
  version    print the version
"""
from __future__ import annotations

import argparse
import json
import os
import sys

from . import __version__
from .engine import Engine
from .script import ScriptError, load


# ----------------------------------------------------------------------------
# Color / output helpers
# ----------------------------------------------------------------------------

_CODES = {"elaine": "\033[36m", "dim": "\033[2m", "warn": "\033[33m",
          "err": "\033[31m", "ok": "\033[32m", "bold": "\033[1m", "reset": "\033[0m"}


def _use_color(stream, explicit: bool | None) -> bool:
    if explicit is not None:
        return explicit
    if os.environ.get("NO_COLOR"):
        return False
    return bool(getattr(stream, "isatty", lambda: False)())


def _paint(text: str, code: str, enabled: bool) -> str:
    if not enabled:
        return text
    return f"{_CODES[code]}{text}{_CODES['reset']}"


# ----------------------------------------------------------------------------
# Loading with friendly errors
# ----------------------------------------------------------------------------

def _load_or_exit(path: str):
    try:
        return load(path)
    except FileNotFoundError as e:
        name = getattr(e, "filename", None) or path
        print(f"error: file not found: {name}", file=sys.stderr)
        raise SystemExit(2)
    except ScriptError as e:
        print(f"script error: {e}", file=sys.stderr)
        raise SystemExit(2)
    except Exception as e:  # noqa: BLE001 — YAML/parse errors, surfaced cleanly
        print(f"error: could not load {path}: {type(e).__name__}: {e}", file=sys.stderr)
        raise SystemExit(2)


# ----------------------------------------------------------------------------
# Interactive REPL
# ----------------------------------------------------------------------------

_REPL_HELP = """commands:
  /help              show this help
  /quit /exit        leave
  /reset             start a fresh conversation (new Self)
  /state             current dialogue state
  /mode              current affect mode
  /regs              register vector (anger/warmth/...)
  /slots             memory slots
  /whoami            your name + relationship role
  /history           recent inputs
  /trace             toggle per-turn state/mode trace
  /reload            reload the script (keeps this conversation's Self)
  /metrics           intents fired / modes seen this session
  /achievements      badges unlocked so far
  /seed <n>          set the logical clock (changes which line is picked)
  /save <file>       save this conversation's Self to JSON
  /load <file>       load a saved Self from JSON"""


def _fresh_engine(loaded, name: str | None, seed: int | None, personality: str | None = None):
    from .script import build_personality
    from .self_model import AffectEngine
    p = build_personality(loaded, personality) if personality else None
    engine = AffectEngine(loaded, personality=p)
    if seed:
        engine.engine.memory.turn = int(seed)
    if name:
        engine.memory.set("name", name)
        if "CHAT" in loaded.states:
            engine.engine.current = "CHAT"
            engine.self_state.dialogue_state = "CHAT"
    return engine


def time_bucket(hour: int | None = None) -> str:
    """Map an hour (default: now, wall-clock — adapter-side only) to a daypart word."""
    if hour is None:
        import datetime as _dt
        hour = _dt.datetime.now().hour
    if 5 <= hour < 12:
        return "morning"
    if 12 <= hour < 17:
        return "afternoon"
    if 17 <= hour < 22:
        return "evening"
    return "night"


def _load_config(path: str = ".elaine.toml") -> dict:
    """Read [repl] defaults from .elaine.toml if present (CLI args still win)."""
    import os.path
    if not os.path.exists(path):
        return {}
    try:
        import tomllib
        with open(path, "rb") as f:
            data = tomllib.load(f)
    except Exception:  # noqa: BLE001 — a broken config must not crash the CLI
        return {}
    return data.get("repl", {}) if isinstance(data, dict) else {}


def _repl_banner(engine, name, color, out):
    hint = _paint("type /help for commands, /quit to leave", "dim", color)
    print(hint, file=out, flush=True)
    if name and "CHAT" in engine.loaded.states:
        text = engine._render(engine.loaded.states["CHAT"].on_enter, {}, "CHAT", 0)
    else:
        text = engine.greeting()
    if text:
        print(_paint(text, "elaine", color), file=out, flush=True)


def run_repl(script_path: str, trace: bool = False, plain: bool = False,
             in_stream=None, out_stream=None, name: str | None = None,
             seed: int | None = None, color: bool | None = None,
             personality: str | None = None, log: str | None = None) -> None:
    """Interactive REPL. Full AffectEngine by default; plain=True for the bare engine.

    Supports in-session slash-commands (see /help). Deterministic and clock-seeded.
    A --personality preset, a time-of-day slot, and optional JSONL --log are adapter-side.
    """
    in_stream = in_stream or sys.stdin
    out_stream = out_stream or sys.stdout
    use_color = _use_color(out_stream, color)

    loaded = _load_or_exit(script_path)
    if plain:
        engine = Engine(loaded.states, loaded.start)
        greeting = engine.greeting()
        if greeting:
            print(greeting, file=out_stream, flush=True)
    else:
        engine = _fresh_engine(loaded, name, seed, personality)
        engine.memory.set("time_of_day", time_bucket())  # adapter supplies wall-clock
        _repl_banner(engine, name, use_color, out_stream)

    while True:
        prompt = "> "
        if trace and not plain:
            prompt = f"[{engine.current}]> "
        try:
            print(prompt, end="", file=out_stream, flush=True)
            line = in_stream.readline()
        except KeyboardInterrupt:
            print("\nbye.", file=out_stream, flush=True)
            break
        if not line:  # EOF
            print(file=out_stream, flush=True)
            break
        text = line.rstrip("\n")
        stripped = text.strip()

        if stripped in ("/quit", "/exit"):
            break
        if stripped.startswith("/") and not plain:
            trace = _handle_command(engine, stripped, trace, use_color, out_stream,
                                    script_path, name, seed, personality)
            continue

        prev = engine.current
        try:
            result = engine.step(text)
        except KeyboardInterrupt:
            print("\nbye.", file=out_stream, flush=True)
            break
        mode = getattr(getattr(engine, "self_state", None), "last_mode", "")
        if trace:
            tag = f" mode={mode}" if mode else ""
            print(_paint(f"[trace] {prev} -> {engine.current}{tag}", "dim", use_color),
                  file=out_stream, flush=True)
        if result.reply:
            print(_paint(result.reply, "elaine", use_color), file=out_stream, flush=True)
        if log:
            _log_turn(log, {"turn": engine.memory.turn, "input": text, "from": prev,
                            "to": engine.current, "mode": mode, "reply": result.reply})
        if result.halted:
            break


def _log_turn(path: str, obj: dict) -> None:
    try:
        with open(path, "a", encoding="utf-8") as f:
            f.write(json.dumps(obj) + "\n")
    except OSError:
        pass


def _handle_command(engine, cmd: str, trace: bool, color: bool, out, script_path,
                    name, seed, personality=None) -> bool:
    """Dispatch a REPL slash-command. Returns the (possibly toggled) trace flag."""
    parts = cmd.split(maxsplit=1)
    head = parts[0].lower()
    arg = parts[1].strip() if len(parts) > 1 else ""
    r = engine.self_state.registers

    def say(s, code="dim"):
        print(_paint(s, code, color), file=out, flush=True)

    if head == "/help":
        say(_REPL_HELP)
    elif head == "/state":
        say(f"state: {engine.current}")
    elif head == "/mode":
        say(f"mode: {engine.self_state.last_mode} (live: {engine.current_mode()})")
    elif head == "/regs":
        say(f"anger={r.anger:.1f} warmth={r.warmth:.1f} amusement={r.amusement:.1f} "
            f"boredom={r.boredom:.1f} confidence={r.confidence:.1f} energy={r.energy:.1f} "
            f"relationship={r.relationship:.1f} trust={r.trust:.1f} "
            f"role={r.role} grudge={r.grudge} times_insulted={r.times_insulted}")
    elif head == "/slots":
        slots = engine.memory.slots()
        say(f"slots: {slots}" if slots else "slots: (none)")
    elif head == "/whoami":
        say(f"name: {engine.memory.get('name', '(unknown)')}  role: {r.role}")
    elif head == "/history":
        hist = engine.memory.history()
        say("history: " + (" | ".join(hist) if hist else "(empty)"))
    elif head == "/trace":
        trace = not trace
        say(f"trace {'on' if trace else 'off'}")
    elif head == "/metrics":
        for ln in engine.metrics.as_lines():
            say(ln)
    elif head == "/achievements":
        from .achievements import unlocked
        badges = unlocked(engine)
        if not badges:
            say("no achievements yet. try harder.")
        for b in badges:
            say(f"[{b.title}] — {b.desc}")
    elif head == "/seed":
        if arg.isdigit():
            engine.engine.memory.turn = int(arg)
            say(f"logical clock set to {arg}")
        else:
            say(f"logical clock: {engine.memory.turn}")
    elif head == "/reset":
        new = _fresh_engine(engine.loaded, name, seed, personality)
        new.memory.set("time_of_day", time_bucket())
        engine.__dict__.update(new.__dict__)
        say("conversation reset.")
        g = engine.greeting()
        if g:
            print(_paint(g, "elaine", color), file=out, flush=True)
    elif head == "/reload":
        from .snapshot import dump_engine, load_engine_state
        try:
            new_loaded = load(script_path)
        except Exception as e:  # noqa: BLE001 — keep the live session on a bad edit
            say(f"reload failed (keeping current): {e}", "err")
            return trace
        snap = dump_engine(engine)
        new_eng = _fresh_engine(new_loaded, None, None, personality)
        try:
            load_engine_state(new_eng, snap)
        except Exception:  # noqa: BLE001 — incompatible Self: start fresh
            pass
        if new_eng.current not in new_loaded.states:
            new_eng.engine.current = new_loaded.start
            new_eng.self_state.dialogue_state = new_loaded.start
        engine.__dict__.update(new_eng.__dict__)
        say(f"reloaded {script_path} (state={engine.current})", "ok")
    elif head == "/save":
        from .snapshot import dump_engine
        if not arg:
            say("usage: /save <file>", "warn")
        else:
            try:
                with open(arg, "w", encoding="utf-8") as f:
                    json.dump(dump_engine(engine), f, indent=2)
                say(f"saved -> {arg}", "ok")
            except OSError as e:
                say(f"save failed: {e}", "err")
    elif head == "/load":
        from .snapshot import load_engine_state
        if not arg:
            say("usage: /load <file>", "warn")
        else:
            try:
                with open(arg, encoding="utf-8") as f:
                    load_engine_state(engine, json.load(f))
                say(f"loaded <- {arg} (state={engine.current})", "ok")
            except (OSError, ValueError, KeyError) as e:
                say(f"load failed: {e}", "err")
    else:
        say(f"unknown command {head!r}. type /help.", "warn")
    return trace


def echo_repl(in_stream=None, out_stream=None) -> None:
    """Phase 0 echo loop, kept for the skeleton test."""
    in_stream = in_stream or sys.stdin
    out_stream = out_stream or sys.stdout
    print("E.L.A.I.N.E (echo mode). Type /quit to exit.", file=out_stream, flush=True)
    while True:
        print("> ", end="", file=out_stream, flush=True)
        line = in_stream.readline()
        if not line:
            break
        text = line.rstrip("\n")
        if text.strip() == "/quit":
            break
        print(text, file=out_stream, flush=True)
    print("bye.", file=out_stream, flush=True)


# ----------------------------------------------------------------------------
# Inspection subcommands
# ----------------------------------------------------------------------------

def _cmd_validate(args) -> int:
    _load_or_exit(args.script)  # exits 2 on any problem
    print(f"ok: {args.script} loaded and validated.")
    return 0


def _cmd_lint(args) -> int:
    from .analysis import lint
    loaded = _load_or_exit(args.script)
    warnings = lint(loaded)
    if not warnings:
        print("clean: no lint warnings.")
        return 0
    for w in warnings:
        print(f"warning: {w}")
    print(f"\n{len(warnings)} warning(s).")
    return 1 if args.strict else 0


def _cmd_stats(args) -> int:
    from .analysis import script_stats
    stats = script_stats(_load_or_exit(args.script))
    if args.json:
        print(json.dumps(stats, indent=2))
        return 0
    print(f"start:            {stats['start']}")
    print(f"states:           {stats['states']} ({stats['terminal_states']} terminal)")
    print(f"affect states:    {', '.join(stats['affect_states']) or '(none)'}")
    print(f"transitions:      {stats['transitions']} (+{stats['fallbacks']} fallbacks)")
    print(f"intents:          {stats['intents']}")
    print(f"modes:            {', '.join(stats['modes'])}")
    print(f"local symbols:    {stats['local_grammar_symbols']}")
    print(f"pools:            {stats['pools']} ({stats['pool_lines']} lines)")
    print("transitions/state:")
    for name, n in stats["transitions_per_state"].items():
        print(f"    {name:<12} {n}")
    return 0


def _cmd_pools(args) -> int:
    loaded = _load_or_exit(args.script)
    pools = loaded.global_grammar or {}
    sizes = {k: (len(v) if isinstance(v, list) else 1) for k, v in pools.items()}
    if args.json:
        print(json.dumps(sizes, indent=2))
        return 0
    for k in sorted(sizes):
        print(f"  {k:<28} {sizes[k]}")
    print(f"\n{len(sizes)} pools, {sum(sizes.values())} lines total.")
    return 0


def _cmd_sample(args) -> int:
    from .analysis import sample_symbol
    loaded = _load_or_exit(args.script)
    for line in sample_symbol(loaded, args.symbol, n=args.count, state_name=args.state):
        print(f"  {line}")
    return 0


def _cmd_explain(args) -> int:
    from .analysis import explain_route
    loaded = _load_or_exit(args.script)
    text = " ".join(args.text)
    try:
        matches = explain_route(loaded, args.state, text, slots=_parse_slots(args.slot))
    except KeyError as e:
        print(f"error: {e}", file=sys.stderr)
        return 2
    print(f"input {text!r} from state {args.state}:")
    for m in matches:
        idx = "fallback" if m.index == -1 else f"[{m.index}]"
        flag = " <== FIRES" if m.fired else ""
        if not m.matched and not m.fired:
            continue
        print(f"  {idx:<9} {m.matcher:<28} when={'ok' if m.when_ok else 'no':<3} -> {m.target}{flag}")
    fired = next((m for m in matches if m.fired), None)
    if fired is None:
        print("  (nothing matched; no fallback)")
    return 0


def _parse_slots(pairs) -> dict:
    out = {}
    for p in (pairs or []):
        if "=" in p:
            k, v = p.split("=", 1)
            out[k.strip()] = v.strip()
    return out


def _cmd_replay(args) -> int:
    from .replay import run_replay
    try:
        with open(args.fixture, encoding="utf-8") as f:
            text = f.read()
    except OSError as e:
        print(f"error: {e}", file=sys.stderr)
        return 2
    try:
        replies = run_replay(args.script, text, affect=not args.plain)
    except AssertionError as e:
        print(f"MISMATCH:\n{e}", file=sys.stderr)
        return 1
    print(f"ok: replay reproduced exactly ({len(replies)} turns).")
    return 0


def _cmd_graph(args) -> int:
    loaded = _load_or_exit(args.script)
    fmt = args.format
    if fmt == "dot":
        from .graphviz_export import to_dot
        text = to_dot(loaded)
    elif fmt == "mermaid":
        from .export import to_mermaid
        text = to_mermaid(loaded)
    elif fmt == "json":
        from .export import to_json
        text = to_json(loaded)
    else:
        print(f"error: unknown format {fmt!r}", file=sys.stderr)
        return 2
    if args.output:
        with open(args.output, "w", encoding="utf-8") as f:
            f.write(text)
        print(f"wrote {args.output}")
    else:
        print(text)
    return 0


# ----------------------------------------------------------------------------
# Persistence subcommands (SQLite)
# ----------------------------------------------------------------------------

def _open_store(args):
    from .store import Store
    return Store(args.db)


def _cmd_users(args) -> int:
    store = _open_store(args)
    rows = store.users()
    store.close()
    if args.json:
        print(json.dumps(rows, indent=2))
        return 0
    if not rows:
        print("(no stored users)")
        return 0
    print(f"{'user_key':<24} {'state':<12} {'clock':<7} updated")
    for r in rows:
        print(f"{r['user_key']:<24} {r['dialogue_state']:<12} {r['logical_clock']:<7} {r['updated_clock']}")
    return 0


def _self_to_dict(loaded_self) -> dict:
    ss = loaded_self.self_state
    return {
        "dialogue_state": ss.dialogue_state,
        "registers": ss.registers.to_dict(),
        "log": ss.log.to_list(),
        "slots": loaded_self.slots,
        "logical_clock": loaded_self.logical_clock,
    }


def _cmd_profile(args) -> int:
    store = _open_store(args)
    loaded_self = store.load(args.key, guild_id=args.guild)
    store.close()
    print(json.dumps(_self_to_dict(loaded_self), indent=2))
    return 0


def _cmd_export_self(args) -> int:
    store = _open_store(args)
    data = _self_to_dict(store.load(args.key, guild_id=args.guild))
    store.close()
    if args.output:
        with open(args.output, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2)
        print(f"wrote {args.output}")
    else:
        print(json.dumps(data, indent=2))
    return 0


def _cmd_reset(args) -> int:
    store = _open_store(args)
    store.reset(args.key)
    store.close()
    print(f"reset user {args.key!r}.")
    return 0


# ----------------------------------------------------------------------------
# Analysis / dev tools
# ----------------------------------------------------------------------------

def _cmd_intents(args) -> int:
    from .analysis import intent_summary
    loaded = _load_or_exit(args.script)
    for name, summary in intent_summary(loaded):
        s = summary if len(summary) <= 88 else summary[:85] + "..."
        print(f"  {name:<24} {s}")
    print(f"\n{len(loaded.intents)} intents.")
    return 0


def _cmd_grammar_cov(args) -> int:
    from .analysis import grammar_coverage
    cov = grammar_coverage(_load_or_exit(args.script))
    if args.json:
        print(json.dumps(cov, indent=2))
        return 0
    print(f"pools:             {cov['pools']}")
    print(f"referenced:        {cov['referenced_pools']}")
    print(f"unreferenced:      {cov['unreferenced_pools']}")
    print(f"reachable lines:   {cov['reachable_lines']} / {cov['total_lines']}")
    print(f"orphan lines:      {cov['orphan_lines']}")
    return 0


def _cmd_personalities(args) -> int:
    loaded = _load_or_exit(args.script)
    print("default   (base personality: block)")
    for n in sorted(loaded.personalities or {}):
        print(f"  {n}")
    if not loaded.personalities:
        print("  (no presets defined)")
    return 0


def _cmd_diff(args) -> int:
    from .analysis import diff_scripts
    d = diff_scripts(_load_or_exit(args.a), _load_or_exit(args.b))
    if args.json:
        print(json.dumps(d, indent=2))
        return 0
    changed = False
    for cat, ch in d.items():
        if ch["added"] or ch["removed"]:
            changed = True
            print(f"{cat}:")
            for x in ch["added"]:
                print(f"  + {x}")
            for x in ch["removed"]:
                print(f"  - {x}")
    if not changed:
        print("no structural differences.")
    return 0


def _read_input_lines(path: str) -> list[str]:
    """Read plain inputs from a file; tolerate replay-fixture markers ('>' user, '<' bot)."""
    out = []
    with open(path, encoding="utf-8") as f:
        for ln in f:
            ln = ln.rstrip("\n")
            if ln.startswith("<"):
                continue
            if ln.startswith(">"):
                ln = ln[1:].strip()
            if ln.strip():
                out.append(ln)
    return out


def _cmd_chat(args) -> int:
    from .self_model import AffectEngine
    eng = AffectEngine(_load_or_exit(args.script))
    g = eng.greeting()
    if g:
        print(f"< {g}")
    for line in _read_input_lines(args.inputs):
        res = eng.step(line)
        print(f"> {line}")
        print(f"< {res.reply}")
        if res.halted:
            break
    return 0


def _cmd_trace(args) -> int:
    from .matchers import Intent
    from .self_model import AffectEngine
    eng = AffectEngine(_load_or_exit(args.script))
    inputs = _read_input_lines(args.file) if args.file else list(args.text)
    print(f"greeting: {eng.greeting()!r}")
    for msg in inputs:
        prev = eng.current
        res = eng.step(msg)
        t = getattr(eng.engine, "_last_transition", None)
        if t is None or t.matcher is None:
            fired = "when/fallback"
        elif isinstance(t.matcher, Intent):
            fired = f"intent:{t.matcher.name}"
        else:
            fired = type(t.matcher).__name__
        r = eng.self_state.registers
        print(f"\n> {msg}")
        print(f"  sentiment={r.your_sentiment}  {prev}->{eng.current}  "
              f"mode={eng.self_state.last_mode}  fired={fired}")
        print(f"  anger={r.anger:.1f} warmth={r.warmth:.1f} amus={r.amusement:.1f} "
              f"bore={r.boredom:.1f} conf={r.confidence:.1f} rel={r.relationship:.1f} role={r.role}")
        print(f"  reply: {res.reply!r}")
    return 0


def _cmd_coverage(args) -> int:
    from .self_model import AffectEngine
    loaded = _load_or_exit(args.script)
    eng = AffectEngine(loaded)
    eng.greeting()
    total = 0
    for path in args.inputs:
        try:
            for line in _read_input_lines(path):
                eng.step(line)
                total += 1
        except OSError as e:
            print(f"error: {e}", file=sys.stderr)
            return 2
    hit = set(eng.metrics.intents)
    allint = set(loaded.intents)
    missed = sorted(allint - hit)
    print(f"fed {total} inputs; {len(hit)}/{len(allint)} intents exercised.")
    print("hit:    " + (", ".join(sorted(hit)) or "(none)"))
    print(f"missed: " + (", ".join(missed) or "(none)"))
    return 0


def _cmd_fuzz(args) -> int:
    from .analysis import fuzz_inputs
    from .self_model import AffectEngine
    loaded = _load_or_exit(args.script)
    eng = AffectEngine(loaded)
    eng.greeting()
    crashes = []
    for i, msg in enumerate(fuzz_inputs(args.count, seed=args.seed)):
        try:
            eng.step(msg)
        except Exception as e:  # noqa: BLE001 — the whole point is to catch these
            crashes.append((i, msg, f"{type(e).__name__}: {e}"))
            eng = AffectEngine(loaded)
            eng.greeting()
    if crashes:
        for i, msg, err in crashes[:20]:
            print(f"CRASH @ {i} on {msg!r}: {err}", file=sys.stderr)
        print(f"\n{len(crashes)} crash(es) in {args.count} inputs.", file=sys.stderr)
        return 1
    print(f"ok: {args.count} fuzz inputs, no crashes.")
    return 0


def _cmd_bench(args) -> int:
    import time
    from .analysis import fuzz_inputs
    from .self_model import AffectEngine
    loaded = _load_or_exit(args.script)
    inputs = fuzz_inputs(args.count, seed=1)
    eng = AffectEngine(loaded)
    eng.greeting()
    t0 = time.perf_counter()
    for msg in inputs:
        try:
            eng.step(msg)
        except Exception:  # noqa: BLE001
            eng = AffectEngine(loaded)
            eng.greeting()
    dt = time.perf_counter() - t0
    rate = args.count / dt if dt else 0.0
    per = (dt / args.count * 1e6) if args.count else 0.0
    print(f"{args.count} turns in {dt*1000:.1f} ms  ({rate:.0f} turns/sec, {per:.0f} us/turn)")
    return 0


def _cmd_doctor(args) -> int:
    import sys as _sys
    ok = True
    print(f"python:        {_sys.version.split()[0]}")
    try:
        import yaml
        print(f"pyyaml:        {yaml.__version__}")
    except Exception as e:  # noqa: BLE001
        print(f"pyyaml:        MISSING ({e})")
        ok = False
    try:
        import sklearn
        print(f"scikit-learn:  {sklearn.__version__} (optional)")
    except Exception:  # noqa: BLE001
        print("scikit-learn:  not installed (ok — ML is optional)")
    script = args.script or "scripts/elaine.yaml"
    try:
        from .analysis import script_stats
        s = script_stats(load(script))
        print(f"script:        OK — {script} ({s['states']} states, {s['intents']} intents, {s['pools']} pools)")
    except Exception as e:  # noqa: BLE001
        print(f"script:        FAILED — {script}: {e}")
        ok = False
    print(f"model.pkl:     {'present' if os.path.exists('model.pkl') else 'absent'} (optional)")
    print("status:        " + ("healthy" if ok else "problems found"))
    return 0 if ok else 1


def _cmd_record(args) -> int:
    from .self_model import AffectEngine
    eng = AffectEngine(_load_or_exit(args.script))
    lines = []
    g = eng.greeting()
    if g:
        lines.append(f"< {g}")
        print(g)
    print("(recording — type /quit to stop and write the fixture)")
    while True:
        try:
            raw = input("> ")
        except (EOFError, KeyboardInterrupt):
            break
        if raw.strip() in ("/quit", "/exit"):
            break
        res = eng.step(raw)
        lines.append(f"> {raw}")
        for piece in res.reply.split("\n"):
            lines.append(f"< {piece}")
        print(res.reply)
        if res.halted:
            break
    with open(args.output, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print(f"\nwrote {args.output} ({len(lines)} lines)")
    return 0


# ----------------------------------------------------------------------------
# argparse wiring
# ----------------------------------------------------------------------------

def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="elaine",
        description="E.L.A.I.N.E — symbolic conversational FSM (no LLM).",
        epilog="examples:\n"
               "  elaine --script scripts/elaine.yaml --trace\n"
               "  elaine stats scripts/elaine.yaml\n"
               "  elaine sample scripts/elaine.yaml eight_ball -n 5\n"
               "  elaine explain scripts/elaine.yaml CHAT 'flip a coin'\n"
               "  elaine graph scripts/elaine.yaml --format mermaid",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument("--version", action="version", version=f"elaine {__version__}")
    parser.add_argument("--script", help="path to a dialogue YAML script (runs the REPL)")
    parser.add_argument("--trace", action="store_true", help="print state/mode each turn")
    parser.add_argument("--plain", action="store_true", help="bare engine (no grammar/affect)")
    parser.add_argument("--name", help="preset your name and start in CHAT")
    parser.add_argument("--seed", type=int, help="initial logical clock (varies picked lines)")
    parser.add_argument("--personality", help="personality preset (see the `personalities` command)")
    parser.add_argument("--log", help="append per-turn JSONL to this file")
    color = parser.add_mutually_exclusive_group()
    color.add_argument("--color", dest="color", action="store_true", default=None, help="force color")
    color.add_argument("--no-color", dest="color", action="store_false", help="disable color")

    sub = parser.add_subparsers(dest="command")

    g = sub.add_parser("graph", help="export the graph")
    g.add_argument("script")
    g.add_argument("-f", "--format", choices=["dot", "mermaid", "json"], default="dot")
    g.add_argument("-o", "--output", help="output path (default: stdout)")

    c = sub.add_parser("check", help="run the acceptance checker")
    c.add_argument("--phase", type=int, help="only run this phase's gate")
    c.add_argument("--acceptance", default="acceptance.yaml")
    c.add_argument("--json", action="store_true", help="machine-readable output")

    tr = sub.add_parser("train", help="train the optional ML intent classifier (offline)")
    tr.add_argument("labeled", help="YAML mapping label -> [example phrases]")
    tr.add_argument("-o", "--output", default="model.pkl")

    v = sub.add_parser("validate", help="load a script and report OK / first error")
    v.add_argument("script")

    li = sub.add_parser("lint", help="report non-fatal quality warnings")
    li.add_argument("script")
    li.add_argument("--strict", action="store_true", help="exit nonzero if any warning")

    st = sub.add_parser("stats", help="counts: states/transitions/intents/pools/...")
    st.add_argument("script")
    st.add_argument("--json", action="store_true")

    pl = sub.add_parser("pools", help="list response pools and sizes")
    pl.add_argument("script")
    pl.add_argument("--json", action="store_true")

    sa = sub.add_parser("sample", help="preview N expansions of a #symbol#")
    sa.add_argument("script")
    sa.add_argument("symbol")
    sa.add_argument("-n", "--count", type=int, default=10)
    sa.add_argument("-s", "--state", help="also resolve against this state's local grammar")

    ex = sub.add_parser("explain", help="show which transition an input fires from a state")
    ex.add_argument("script")
    ex.add_argument("state")
    ex.add_argument("text", nargs="+")
    ex.add_argument("--slot", action="append", help="seed a memory slot, e.g. --slot warned=true")

    rp = sub.add_parser("replay", help="run a replay fixture and report pass/fail")
    rp.add_argument("script")
    rp.add_argument("fixture")
    rp.add_argument("--plain", action="store_true", help="replay through the bare engine")

    pf = sub.add_parser("profile", help="show a stored user's Self")
    pf.add_argument("db")
    pf.add_argument("key")
    pf.add_argument("--guild", default="default")

    us = sub.add_parser("users", help="list stored users in a DB")
    us.add_argument("db")
    us.add_argument("--json", action="store_true")

    rs = sub.add_parser("reset", help="wipe a stored user")
    rs.add_argument("db")
    rs.add_argument("key")

    es = sub.add_parser("export-self", help="dump a stored user's Self as JSON")
    es.add_argument("db")
    es.add_argument("key")
    es.add_argument("--guild", default="default")
    es.add_argument("-o", "--output")

    it = sub.add_parser("intents", help="list intents with their trigger summary")
    it.add_argument("script")

    df = sub.add_parser("diff", help="structural diff of two scripts")
    df.add_argument("a")
    df.add_argument("b")
    df.add_argument("--json", action="store_true")

    ch = sub.add_parser("chat", help="batch: run inputs from a file, print replies")
    ch.add_argument("script")
    ch.add_argument("inputs", help="a file of one input per line (fixture markers tolerated)")

    tc = sub.add_parser("trace", help="verbose per-turn trace (route, mode, registers, reply)")
    tc.add_argument("script")
    tc.add_argument("text", nargs="*", help="inputs (or use --file)")
    tc.add_argument("--file", help="read inputs from a file instead")

    cv = sub.add_parser("coverage", help="which intents a set of inputs exercises")
    cv.add_argument("script")
    cv.add_argument("inputs", nargs="+", help="one or more input/fixture files")

    fz = sub.add_parser("fuzz", help="feed random inputs and assert no crash")
    fz.add_argument("script")
    fz.add_argument("-n", "--count", type=int, default=500)
    fz.add_argument("--seed", type=int, default=0)

    bn = sub.add_parser("bench", help="time N turns (perf sanity)")
    bn.add_argument("script")
    bn.add_argument("-n", "--count", type=int, default=1000)

    dr = sub.add_parser("doctor", help="environment + script health check")
    dr.add_argument("--script", help="script to validate (default scripts/elaine.yaml)")

    rc = sub.add_parser("record", help="interactive REPL that writes a replay fixture")
    rc.add_argument("script")
    rc.add_argument("-o", "--output", required=True)

    gc = sub.add_parser("grammar-cov", help="pool reachability / authored-line coverage")
    gc.add_argument("script")
    gc.add_argument("--json", action="store_true")

    pp = sub.add_parser("personalities", help="list personality presets in a script")
    pp.add_argument("script")

    sub.add_parser("version", help="print the version")
    return parser


def main(argv: list[str] | None = None) -> int:
    parser = _build_parser()
    args = parser.parse_args(argv)

    if args.command == "graph":
        return _cmd_graph(args)
    if args.command == "check":
        from .checker import run_check
        if getattr(args, "json", False):
            return _check_json(args)
        return run_check(args.acceptance, args.phase)
    if args.command == "train":
        import yaml as _yaml
        from .classifier import train
        with open(args.labeled, encoding="utf-8") as f:
            examples = _yaml.safe_load(f)
        train(examples, args.output)
        print(f"trained model -> {args.output}")
        return 0
    if args.command == "validate":
        return _cmd_validate(args)
    if args.command == "lint":
        return _cmd_lint(args)
    if args.command == "stats":
        return _cmd_stats(args)
    if args.command == "pools":
        return _cmd_pools(args)
    if args.command == "sample":
        return _cmd_sample(args)
    if args.command == "explain":
        return _cmd_explain(args)
    if args.command == "replay":
        return _cmd_replay(args)
    if args.command == "profile":
        return _cmd_profile(args)
    if args.command == "users":
        return _cmd_users(args)
    if args.command == "reset":
        return _cmd_reset(args)
    if args.command == "export-self":
        return _cmd_export_self(args)
    if args.command == "intents":
        return _cmd_intents(args)
    if args.command == "diff":
        return _cmd_diff(args)
    if args.command == "chat":
        return _cmd_chat(args)
    if args.command == "trace":
        return _cmd_trace(args)
    if args.command == "coverage":
        return _cmd_coverage(args)
    if args.command == "fuzz":
        return _cmd_fuzz(args)
    if args.command == "bench":
        return _cmd_bench(args)
    if args.command == "doctor":
        return _cmd_doctor(args)
    if args.command == "record":
        return _cmd_record(args)
    if args.command == "grammar-cov":
        return _cmd_grammar_cov(args)
    if args.command == "personalities":
        return _cmd_personalities(args)
    if args.command == "version":
        print(f"elaine {__version__}")
        return 0

    # REPL: CLI args win, then .elaine.toml [repl] defaults.
    cfg = _load_config()
    script = args.script or cfg.get("script")
    if script:
        color = args.color if args.color is not None else cfg.get("color")
        run_repl(script, trace=args.trace or bool(cfg.get("trace", False)), plain=args.plain,
                 name=args.name or cfg.get("name"), seed=args.seed, color=color,
                 personality=args.personality or cfg.get("personality"),
                 log=args.log or cfg.get("log"))
        return 0

    echo_repl()
    return 0


def _check_json(args) -> int:
    """Run the acceptance checker but emit JSON (machine-readable)."""
    import subprocess
    import yaml as _yaml
    from pathlib import Path
    from .checker import _split_env_prefix

    path = Path(args.acceptance)
    if not path.exists():
        print(json.dumps({"error": f"not found: {args.acceptance}"}))
        return 2
    items = _yaml.safe_load(path.read_text(encoding="utf-8")) or []
    if args.phase is not None:
        items = [i for i in items if i.get("phase") == args.phase]
    results = []
    must_failed = False
    for item in items:
        verify = item.get("verify")
        level = item.get("level", "MUST")
        if not verify:
            status = "SKIP"
        else:
            env_overrides, cmd = _split_env_prefix(verify)
            env = {**os.environ, **env_overrides} if env_overrides else None
            proc = subprocess.run(cmd, shell=True, capture_output=True, text=True, env=env)
            status = "PASS" if proc.returncode == 0 else "FAIL"
        if status == "FAIL" and level == "MUST":
            must_failed = True
        results.append({"id": item.get("id"), "phase": item.get("phase"),
                        "level": level, "status": status, "desc": item.get("desc")})
    print(json.dumps({"results": results,
                      "pass": sum(1 for r in results if r["status"] == "PASS"),
                      "fail": sum(1 for r in results if r["status"] == "FAIL"),
                      "total": len(results)}, indent=2))
    return 1 if must_failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
