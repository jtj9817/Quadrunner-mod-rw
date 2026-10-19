#!/usr/bin/env -S python3 -I
"""Drive the Quadrunner test instance: an isolated RimWorld on a private Xvfb display.

  autotest.py run [filter]    one iteration: build, restart the instance in game (or start it), run the scenarios,
                              diff the log against the baseline, record the run. Exit 0 pass, 1 fail, 2 hang/crash,
                              3 not run (the environment is broken)
  autotest.py start           launch the instance and wait until its quicktest map is ready
  autotest.py restart         in-game restart (GenCommandLine.Restart, as after toggling Texture Compression)
  autotest.py stop            quit the instance (in game, then SIGTERM/SIGKILL) and its Xvfb
  autotest.py status          the instance, its build, the user's game, Steam
  autotest.py baseline        record error signatures from a run WITHOUT Quadrunner
  autotest.py errors [log]    new error blocks in a log (default: the live log), baseline noise hidden
  autotest.py follow          stream [Quadrunner] lines and new error blocks from the live log
  autotest.py hang            watchdog self-test: freeze the instance's main thread, expect HANG

The instance never uses the user's profile: -savedatafolder, -logFile, XDG_CONFIG_HOME and XDG_CACHE_HOME all point
under .devlog/. The mod side is Source/Quadrunner/AutoTest.cs: it reads one command at a time from
.devlog/autotest/command ("<id> <verb> [arg]"), appends JSON lines to .devlog/autotest/events.jsonl and touches
.devlog/autotest/heartbeat every second. Standard library only.
"""
import argparse
import json
import os
import re
import signal
import subprocess
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
DEVLOG = Path(os.environ.get("QR_DEVLOG", REPO / ".devlog"))
# Same variables and defaults as tools/env.sh.
RW_DIR = Path(os.environ.get("RW_DIR", Path.home() / ".steam/debian-installation/steamapps/common/RimWorld"))
RW_DATA = Path(os.environ.get("RW_DATA", DEVLOG / "rwdata"))
RW_LOG = RW_DATA / "Player.log"
HARMONY_DIR = Path(os.environ.get("HARMONY_DIR", RW_DIR / "../../workshop/content/294100/2009463077"))
RW_EXPECTED_VERSION = "1.6."

EXE = RW_DIR / "RimWorldLinux"
MOD_LINK = RW_DIR / "Mods" / "Quadrunner"
CTL = DEVLOG / "autotest"
EVENTS = CTL / "events.jsonl"
COMMAND = CTL / "command"
HEARTBEAT = CTL / "heartbeat"
STATE = CTL / "state.json"
RUNS = DEVLOG / "runs"
BASELINE = DEVLOG / "baseline-errors.txt"
LAST_BUILD = DEVLOG / "last-build"

PACKAGE_ID = "josh9817.quadrunner"
CORE_MODS = ["brrainz.harmony", "ludeon.rimworld"]
AUTOTEST_ARG = "-quadrunner-autotest"

EXIT_PASS, EXIT_FAIL, EXIT_HANG, EXIT_NOT_RUN = 0, 1, 2, 3


class Outcome(Exception):
    """The instance stopped answering: kind is HANG, CRASH or TIMEOUT."""

    def __init__(self, kind, detail):
        super().__init__(f"{kind}: {detail}")
        self.kind, self.detail = kind, detail


def say(msg):
    print(f"autotest: {msg}", file=sys.stderr, flush=True)


def die(msg, code=EXIT_NOT_RUN):
    print(f"error: {msg}", file=sys.stderr)
    sys.exit(code)


def utc_stamp():
    return time.strftime("%Y%m%d-%H%M%S", time.gmtime())


# --- processes -------------------------------------------------------------------------------------------------

def rimworld_procs():
    """(pid, argv) of every running RimWorldLinux."""
    for p in Path("/proc").iterdir():
        if not p.name.isdigit():
            continue
        try:
            argv = [a.decode(errors="replace") for a in (p / "cmdline").read_bytes().split(b"\0") if a]
        except OSError:
            continue
        if argv and os.path.basename(argv[0]) == "RimWorldLinux":
            yield int(p.name), argv


def test_pids():
    return [pid for pid, argv in rimworld_procs() if f"-savedatafolder={RW_DATA}" in argv]


def user_game_pids():
    return [pid for pid, argv in rimworld_procs() if f"-savedatafolder={RW_DATA}" not in argv]


def comm_running(name):
    for p in Path("/proc").iterdir():
        try:
            if p.name.isdigit() and (p / "comm").read_text().strip() == name:
                return True
        except OSError:
            continue
    return False


def proc_comm(pid):
    try:
        return Path(f"/proc/{pid}/comm").read_text().strip()
    except OSError:
        return None


def pid_alive(pid):
    try:
        os.kill(pid, 0)
        return True
    except (OSError, TypeError):
        return False


def kill_instance():
    pids = test_pids()
    for pid in pids:
        os.kill(pid, signal.SIGTERM)
    deadline = time.time() + 15
    while test_pids() and time.time() < deadline:
        time.sleep(0.5)
    for pid in test_pids():
        os.kill(pid, signal.SIGKILL)
    if pids:
        say(f"killed test instance {pids}")


# --- state, events, commands -----------------------------------------------------------------------------------

def load_state():
    try:
        return json.loads(STATE.read_text())
    except (OSError, ValueError):
        return {}


def save_state(state):
    STATE.write_text(json.dumps(state, indent=1))


def events_size():
    return EVENTS.stat().st_size if EVENTS.exists() else 0


def read_events(offset):
    """Complete JSON lines appended after offset, as (end offset, event)."""
    if not EVENTS.exists():
        return []
    with open(EVENTS, "rb") as f:
        f.seek(offset)
        data = f.read()
    out, pos = [], offset
    for raw in data.split(b"\n")[:-1]:  # the last piece is empty or still being written
        pos += len(raw) + 1
        try:
            out.append((pos, json.loads(raw)))
        except ValueError:
            say(f"unparsable event: {raw[:200]!r}")
    return out


def send(verb, arg=""):
    """Hands one command to the instance; it deletes the file once it has read it."""
    deadline = time.time() + 30
    while COMMAND.exists():
        if time.time() > deadline:
            raise Outcome("HANG", f"the previous command was never picked up: {COMMAND.read_text().strip()}")
        time.sleep(0.2)
    cid = str(int(time.time() * 1000))
    tmp = COMMAND.with_suffix(".tmp")
    tmp.write_text(f"{cid} {verb} {arg}".rstrip() + "\n")
    tmp.rename(COMMAND)
    return cid


def current():
    """The ready event of the live instance, or None when none is running and ready."""
    pids = test_pids()
    if not pids:
        return None
    ready = [e for _, e in read_events(0) if e.get("ev") == "ready" and e.get("pid") in pids]
    return ready[-1] if ready else None


def heartbeat():
    """(launch id, age in seconds) of the last heartbeat, or (None, None)."""
    try:
        return HEARTBEAT.read_text().split()[0], time.time() - HEARTBEAT.stat().st_mtime
    except (OSError, IndexError):
        return None, None


def wait_event(pred, offset, timeout, launch=None, hang_after=120, label="event"):
    """Waits for the first event after offset that matches pred.

    Watchdog: TIMEOUT after timeout seconds, CRASH when no test instance runs for 30 s, HANG when the heartbeat of
    `launch` is older than hang_after seconds. With launch=None, only a launch that started beating after this wait
    began counts (the previous launch's heartbeat file goes stale during every restart).
    """
    start = time.time()
    gone_since = None
    old_launch, _ = heartbeat()
    last_note = start
    while True:
        for pos, ev in read_events(offset):
            offset = pos
            if pred(ev):
                return ev
        now = time.time()
        if now - start > timeout:
            raise Outcome("TIMEOUT", f"no {label} after {timeout} s")
        if test_pids():
            gone_since = None
        else:
            gone_since = gone_since or now
            if now - gone_since > 30:
                raise Outcome("CRASH", f"no test instance running while waiting for {label}")
        beat_launch, age = heartbeat()
        watched = beat_launch == launch if launch else beat_launch not in (None, old_launch)
        if watched and age > hang_after:
            raise Outcome("HANG", f"heartbeat {age:.0f} s old while waiting for {label}")
        if now - last_note >= 30:
            say(f"still waiting for {label} ({now - start:.0f} s)")
            last_note = now
        time.sleep(0.5)


# --- the test profile and launching ----------------------------------------------------------------------------

PREFS = """<?xml version="1.0" encoding="utf-8"?>
<PrefsData>
  <volumeMaster>0</volumeMaster>
  <screenWidth>1280</screenWidth>
  <screenHeight>720</screenHeight>
  <fullscreen>False</fullscreen>
  <customCursorEnabled>False</customCursorEnabled>
  <textureCompression>False</textureCompression>
  <runInBackground>True</runInBackground>
  <autosaveIntervalDays>1000</autosaveIntervalDays>
  <pauseOnLoad>False</pauseOnLoad>
  <automaticPauseMode>Never</automaticPauseMode>
  <pauseOnError>False</pauseOnError>
  <devMode>True</devMode>
  <adaptiveTrainingEnabled>False</adaptiveTrainingEnabled>
  <resetModsConfigOnCrash>False</resetModsConfigOnCrash>
  <langFolderName>English</langFolderName>
  <logVerbose>False</logVerbose>
  <openLogOnWarnings>False</openLogOnWarnings>
</PrefsData>
"""


def game_version():
    try:
        return (RW_DIR / "Version.txt").read_text().strip()
    except OSError:
        return "missing"


def write_profile(with_mod):
    """Rewrites the test profile's config every launch, so the game can't drift from it."""
    config = RW_DATA / "Config"
    config.mkdir(parents=True, exist_ok=True)
    mods = CORE_MODS + ([PACKAGE_ID] if with_mod else [])
    items = "\n".join(f"    <li>{m}</li>" for m in mods)
    (config / "ModsConfig.xml").write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n<ModsConfigData>\n'
        f"  <version>{game_version()}</version>\n  <activeMods>\n{items}\n  </activeMods>\n"
        "  <knownExpansions />\n</ModsConfigData>\n")
    (config / "Prefs.xml").write_text(PREFS)


def ensure_mod_link():
    if MOD_LINK.is_symlink():
        if MOD_LINK.resolve() != REPO:
            die(f"{MOD_LINK} links to {MOD_LINK.resolve()}, not this repo; move it away by hand")
    elif MOD_LINK.exists():
        die(f"{MOD_LINK} exists and is not the dev symlink; move it away by hand")
    else:
        MOD_LINK.symlink_to(REPO)
        say(f"installed dev symlink {MOD_LINK} -> {REPO}")


def preflight():
    if not EXE.is_file():
        die(f"no game at {EXE}")
    if not game_version().startswith(RW_EXPECTED_VERSION):
        die(f"game version is '{game_version()}', expected {RW_EXPECTED_VERSION}* (Steam update? ask the user)")
    if not comm_running("steam"):
        die("Steam is not running: workshop mods (Harmony) only load while it runs")
    if not (HARMONY_DIR / "About").is_dir():
        die(f"Harmony not found at {HARMONY_DIR}")
    users = user_game_pids()
    if users:
        # Proven safe on 2026-10-19: own profile, logs, XDG dirs and display; the user's profile stays untouched.
        say(f"the user's RimWorld is open (pid {users[0]}); the test instance runs beside it")
    for d in (CTL, RUNS, RW_DATA):
        d.mkdir(parents=True, exist_ok=True)


def ensure_xvfb():
    state = load_state()
    display = state.get("display")
    if display and pid_alive(state.get("xvfb_pid")) and Path(f"/tmp/.X11-unix/X{display[1:]}").exists():
        return display
    n = next(n for n in range(90, 140)
             if not Path(f"/tmp/.X11-unix/X{n}").exists() and not Path(f"/tmp/.X{n}-lock").exists())
    proc = subprocess.Popen(["Xvfb", f":{n}", "-screen", "0", "1280x720x24", "-nolisten", "tcp", "-noreset"],
                            stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                            stderr=open(CTL / "xvfb.log", "w"), start_new_session=True)
    deadline = time.time() + 10
    while not Path(f"/tmp/.X11-unix/X{n}").exists():
        if proc.poll() is not None or time.time() > deadline:
            die(f"Xvfb :{n} didn't start; see {CTL / 'xvfb.log'}")
        time.sleep(0.1)
    state.update(display=f":{n}", xvfb_pid=proc.pid)
    save_state(state)
    say(f"started Xvfb :{n} (pid {proc.pid})")
    return f":{n}"


def archive_log(name):
    """Moves the live log into runs/<name>/. Safe while a game still writes it: it keeps the same inode."""
    if not RW_LOG.exists():
        return None
    dest = RUNS / name
    dest.mkdir(parents=True, exist_ok=True)
    target = dest / "Player.log"
    RW_LOG.rename(target)
    return target


def launch(with_mod=True):
    """Starts a fresh instance in its own session, so it outlives this script."""
    write_profile(with_mod)
    if with_mod:
        ensure_mod_link()
    archive_log(f"orphan-{utc_stamp()}")
    env = dict(os.environ)
    env.pop("WAYLAND_DISPLAY", None)
    env.update(DISPLAY=ensure_xvfb(), LC_ALL="C",
               XDG_CONFIG_HOME=str(DEVLOG / "xdg/config"), XDG_CACHE_HOME=str(DEVLOG / "xdg/cache"))
    argv = [str(EXE), f"-savedatafolder={RW_DATA}", "-logFile", str(RW_LOG), "-quicktest", "-disable-compute-shaders"]
    if with_mod:
        argv.append(f"{AUTOTEST_ARG}={CTL}")
    proc = subprocess.Popen(argv, cwd=RW_DIR, env=env, stdin=subprocess.DEVNULL,
                            stdout=open(CTL / "stdout.log", "w"), stderr=subprocess.STDOUT, start_new_session=True)
    say(f"launched test instance pid {proc.pid} on {env['DISPLAY']}{'' if with_mod else ' (without Quadrunner)'}")
    return proc.pid


def start(timeout):
    offset = events_size()
    t0 = time.time()
    launch()
    ready = wait_event(lambda e: e.get("ev") == "ready", offset, timeout, label="the quicktest map")
    say(f"ready in {time.time() - t0:.0f} s: launch {ready['launch']}, build {ready['build']}")
    return ready


def restart(cur, timeout):
    """In-game restart: the game relaunches itself with the same arguments. Falls back to a cold start."""
    offset = events_size()
    t0 = time.time()
    archive_log(cur["launch"])
    try:
        send("restart")
        wait_event(lambda e: e.get("ev") == "restarting", offset, 30, launch=cur["launch"], hang_after=30,
                   label="the restart acknowledgement")
        ready = wait_event(lambda e: e.get("ev") == "ready" and e.get("launch") != cur["launch"], offset, timeout,
                           label="the restarted quicktest map")
    except Outcome as o:
        say(f"in-game restart failed ({o}); killing and starting cold")
        kill_instance()
        return start(timeout)
    say(f"restarted in game in {time.time() - t0:.0f} s: launch {ready['launch']}, build {ready['build']}")
    return ready


def build_id(ready):
    return ready["build"].split("+")[-1]


# --- Player.log analysis (port of Queen Bee's qblog.awk) -------------------------------------------------------

ERROR_RE = re.compile(r"[Ee]xception|[Ee]rror|ERROR|[Ff]ailed|[Cc]ould not|[Cc]annot |Missing|not found")
CONTINUES_RE = re.compile(r"^\[Ref [0-9A-F]+\]|^  at |^--- End of |^Rethrow as |^[A-Za-z0-9_.]+(Exception|Error)(: |$)")
REF_RE = re.compile(r"^\[Ref ([0-9A-F]+)\]")
OURS = "[Quadrunner]"


def signature(line):
    """Stable identity of an error across runs: ref hashes, IDs, coordinates and counts removed."""
    return re.sub(r"[0-9]+", "N", re.sub(r"\[Ref [0-9A-F]+\]", "[Ref]", line))[:60]


class Block:
    def __init__(self, end, line):
        self.end, self.lines = end, [line]
        ours = OURS in line
        # Our own lines are messages unless QR.Error wrote them.
        self.error = bool(ERROR_RE.search(line)) and "GL_" not in line and (not ours or f"{OURS} ERROR" in line)
        self.ours = ours

    @property
    def sig(self):
        return signature(self.lines[0])


class LogParser:
    """Groups lines into [Quadrunner] lines and error blocks (head + stack trace); resolves 1.6's
    "[Ref X] Duplicate stacktrace" lines by copying the original trace back in."""

    def __init__(self):
        self.pos, self.block, self.ref, self.refs = 0, None, None, {}

    def feed(self, raw):
        """One line (bytes, without the newline); returns a finished block or None."""
        self.pos += len(raw) + 1
        line = raw.decode("utf-8", "replace").rstrip("\r")
        if self.block and CONTINUES_RE.search(line):
            m = REF_RE.match(line)
            if m:
                if "Duplicate stacktrace" in line:
                    self.block.lines += [line] + self.refs.get(m.group(1), [])
                    self.ref = None
                    return None
                self.ref = m.group(1)
                self.refs[self.ref] = []
            elif self.ref and line.startswith("  at "):
                self.refs[self.ref].append(line)
            self.block.lines.append(line)
            return None
        done, self.block, self.ref = self.block, None, None
        if OURS in line or (ERROR_RE.search(line) and "GL_" not in line):
            self.block = Block(self.pos, line)
        return done

    def flush(self):
        done, self.block = self.block, None
        return done


def log_blocks(path, start=0):
    parser = LogParser()
    data = Path(path).read_bytes()
    for raw in data.split(b"\n"):
        b = parser.feed(raw)
        if b and b.end > start:
            yield b
    b = parser.flush()
    if b and b.end > start:
        yield b


def known_signatures():
    return set(BASELINE.read_text().splitlines()) if BASELINE.exists() else set()


def new_errors(path, start=0, show_all=False):
    known = set() if show_all else known_signatures()
    return [b for b in log_blocks(path, start) if b.error and (b.ours or b.sig not in known)]


# --- commands --------------------------------------------------------------------------------------------------

def cmd_run(args):
    preflight()
    if not args.no_build:
        say("building")
        if subprocess.run([str(REPO / "tools/build.sh")]).returncode != 0:
            print("RESULT: BUILD FAILED")
            return EXIT_FAIL
    last = LAST_BUILD.read_text().strip() if LAST_BUILD.exists() else None
    cur = current()
    try:
        if cur is None:
            cur, fresh = start(args.ready_timeout), True
        elif build_id(cur) != last:
            say(f"instance runs {build_id(cur)}, latest build is {last}: restarting in game")
            cur, fresh = restart(cur, args.ready_timeout), True
        else:
            fresh = False
        if build_id(cur) != last:
            print(f"RESULT: STALE BUILD: loaded {build_id(cur)}, expected {last}")
            return EXIT_FAIL
        log_start = 0 if fresh else (RW_LOG.stat().st_size if RW_LOG.exists() else 0)
        offset = events_size()
        t0 = time.time()
        cid = send("run", args.filter or "")
        result = wait_event(lambda e: e.get("ev") == "result" and e.get("id") == cid, offset, args.timeout,
                            launch=cur["launch"], hang_after=args.hang_after, label="the scenario results")
    except Outcome as o:
        return report_outcome(o, cur)
    return report(cur, result, offset, log_start, time.time() - t0, args.filter)


def report_outcome(o, cur):
    kill_instance()
    name = cur["launch"] if cur else f"failed-{utc_stamp()}"
    log = archive_log(name)
    print(f"RESULT: {o.kind}: {o.detail}")
    if log:
        print(f"log: {log}")
        for b in new_errors(log)[-5:]:
            print("\n".join(b.lines[:12]))
    return EXIT_HANG


def report(cur, result, offset, log_start, seconds, filt):
    scen = [e for _, e in read_events(offset) if e.get("ev") == "scenario"]
    for e in scen:
        print(f"SCENARIO {e['name']}: {e['verdict']}  {e.get('detail', '')}".rstrip())
    errors = new_errors(RW_LOG, log_start)
    for b in errors:
        print("\n".join(b.lines[:20]) + ("\n  ..." if len(b.lines) > 20 else ""))
    passed, total = result.get("passed", 0), result.get("total", 0)
    ok = total > 0 and passed == total and not errors
    baseline = f"baseline {len(known_signatures())} signatures" if BASELINE.exists() else "NO BASELINE: every error counts"
    verdict = "PASS" if ok else "FAIL"
    summary = (f"{passed}/{total} PASS, {len(errors)} new error(s) ({baseline})"
               + ("" if total else ", no scenario matched the filter"))
    print(f"RESULT: {verdict}: {summary}, {seconds:.0f} s, build {build_id(cur)}, launch {cur['launch']}")
    run_dir = RUNS / cur["launch"]
    run_dir.mkdir(parents=True, exist_ok=True)
    stamp = time.strftime("%Y-%m-%d %H:%M", time.gmtime())
    names = ", ".join(e["name"] for e in scen) or (filt or "all")
    (run_dir / f"run-{utc_stamp()}.json").write_text(json.dumps(
        dict(verdict=verdict, passed=passed, total=total, errors=len(errors), seconds=round(seconds),
             build=build_id(cur), launch=cur["launch"], filter=filt, scenarios=scen,
             error_heads=[b.lines[0] for b in errors]), indent=1))
    print(f"row: | {stamp} | | {build_id(cur)} | {names} | {passed}/{total} PASS, {len(errors)} new errors | | |")
    return EXIT_PASS if ok else EXIT_FAIL


def cmd_start(args):
    preflight()
    cur = current()
    if cur:
        say(f"already running: launch {cur['launch']}, build {cur['build']}")
        return EXIT_PASS
    try:
        start(args.ready_timeout)
    except Outcome as o:
        return report_outcome(o, None)
    return EXIT_PASS


def cmd_restart(args):
    preflight()
    cur = current()
    if not cur:
        die("no ready test instance to restart (autotest.py start)")
    try:
        restart(cur, args.ready_timeout)
    except Outcome as o:
        return report_outcome(o, cur)
    return EXIT_PASS


def cmd_stop(args):
    cur = current()
    if cur:
        try:
            offset = events_size()
            send("quit")
            wait_event(lambda e: e.get("ev") == "quitting", offset, 20, launch=cur["launch"], hang_after=20,
                       label="the quit acknowledgement")
            deadline = time.time() + 30
            while test_pids() and time.time() < deadline:
                time.sleep(0.5)
        except Outcome as o:
            say(f"in-game quit failed ({o})")
    kill_instance()
    if cur:
        archive_log(cur["launch"])
    state = load_state()
    if pid_alive(state.get("xvfb_pid")) and proc_comm(state["xvfb_pid"]) == "Xvfb":   # never a reused pid
        os.kill(state["xvfb_pid"], signal.SIGTERM)
        say(f"stopped Xvfb {state['display']}")
    save_state({})
    COMMAND.unlink(missing_ok=True)
    return EXIT_PASS


def cmd_status(args):
    cur = current()
    last = LAST_BUILD.read_text().strip() if LAST_BUILD.exists() else "none"
    pids = test_pids()
    print(f"test instance  {'pids ' + str(pids) if pids else 'not running'}")
    if cur:
        stale = "" if build_id(cur) == last else f"  (STALE: latest build is {last})"
        print(f"  launch       {cur['launch']}, build {build_id(cur)}{stale}")
        beat, age = heartbeat()
        print(f"  heartbeat    {age:.0f} s ago" if age is not None else "  heartbeat    none")
    elif pids:
        print("  not ready yet (loading or hung)")
    print(f"latest build   {last}")
    users = user_game_pids()
    print(f"user's game    {'RUNNING, pid ' + str(users[0]) if users else 'not running'}")
    print(f"steam          {'running' if comm_running('steam') else 'NOT running'}")
    state = load_state()
    xvfb = state.get("display") if pid_alive(state.get("xvfb_pid")) else None
    print(f"xvfb           {xvfb or 'not running'}")
    print(f"baseline       {len(known_signatures())} signatures" if BASELINE.exists() else "baseline       none")
    return EXIT_PASS


def cmd_baseline(args):
    preflight()
    if test_pids():
        die("the test instance is running; autotest.py stop first")
    t0 = time.time()
    launch(with_mod=False)
    # Without Quadrunner nothing announces "map ready": wait until the log has been quiet for a while.
    last_size, quiet_since = -1, time.time()
    while True:
        time.sleep(2)
        size = RW_LOG.stat().st_size if RW_LOG.exists() else 0
        now = time.time()
        if size != last_size:
            last_size, quiet_since = size, now
        if not test_pids():
            die("the instance exited during the baseline run", EXIT_HANG)
        if now - t0 > args.min_seconds and now - quiet_since > args.quiet_seconds:
            break
        if now - t0 > args.ready_timeout:
            say("log never went quiet; recording what there is")
            break
    kill_instance()
    log = archive_log(f"baseline-{utc_stamp()}")
    if OURS in log.read_text(errors="replace"):
        die(f"{log} contains {OURS}; the baseline must come from a run without the mod")
    sigs = sorted({b.sig for b in log_blocks(log) if b.error})
    BASELINE.write_text("".join(s + "\n" for s in sigs))
    print(f"baseline: {len(sigs)} error signatures from {log} ({time.time() - t0:.0f} s) -> {BASELINE}")
    write_profile(with_mod=True)
    return EXIT_PASS


def cmd_errors(args):
    path = Path(args.log) if args.log else RW_LOG
    if not path.exists():
        die(f"no log at {path}")
    if not BASELINE.exists() and not args.all:
        say("no baseline yet, showing every error")
    blocks = new_errors(path, show_all=args.all)
    for b in blocks:
        print("\n".join(b.lines))
    print(f"errors: {len(blocks)} new error block(s) in {path}")
    return EXIT_PASS if not blocks else EXIT_FAIL


def cmd_follow(args):
    """Like tail -F: follows the live log across restarts (the file is moved away and recreated)."""
    known = set() if args.all else known_signatures()

    def show(b):
        if b and (b.ours or (b.error and b.sig not in known)):
            print("\n".join(b.lines), flush=True)

    inode, f, parser, pending = None, None, LogParser(), b""
    while True:
        try:
            st = RW_LOG.stat()
        except OSError:
            st = None
        if st and st.st_ino != inode:
            if f:
                print("--- new launch", flush=True)
            f, inode, parser, pending = open(RW_LOG, "rb"), st.st_ino, LogParser(), b""
            f.seek(0 if args.from_start else st.st_size)
        if f:
            pending += f.read()
            *lines, pending = pending.split(b"\n")
            for raw in lines:
                show(parser.feed(raw))
        time.sleep(0.5)


def cmd_hang(args):
    preflight()
    cur = current()
    if not cur:
        die("no ready test instance (autotest.py start)")
    offset = events_size()
    try:
        send("hang")
        wait_event(lambda e: False, offset, args.hang_after * 4, launch=cur["launch"], hang_after=args.hang_after,
                   label="nothing (the main thread is frozen on purpose)")
    except Outcome as o:
        code = report_outcome(o, cur)
        print("watchdog: OK" if o.kind == "HANG" else f"watchdog: expected HANG, got {o.kind}")
        return code if o.kind == "HANG" else EXIT_FAIL
    return EXIT_FAIL


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--ready-timeout", type=int, default=900, help="seconds to wait for the quicktest map")
    ap.add_argument("--timeout", type=int, default=1800, help="seconds to wait for the scenario results")
    ap.add_argument("--hang-after", type=int, default=120, help="heartbeat age (s) that counts as a hang")
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("run")
    p.add_argument("filter", nargs="?", help="comma-separated, case-insensitive parts of scenario names")
    p.add_argument("--no-build", action="store_true")
    p.set_defaults(fn=cmd_run)
    for name, fn in (("start", cmd_start), ("restart", cmd_restart), ("stop", cmd_stop), ("status", cmd_status),
                     ("hang", cmd_hang)):
        sub.add_parser(name).set_defaults(fn=fn)
    p = sub.add_parser("baseline")
    p.add_argument("--min-seconds", type=int, default=120, help="never stop before this many seconds")
    p.add_argument("--quiet-seconds", type=int, default=30, help="stop once the log was quiet this long")
    p.set_defaults(fn=cmd_baseline)
    p = sub.add_parser("errors")
    p.add_argument("log", nargs="?")
    p.add_argument("--all", action="store_true", help="include baseline noise")
    p.set_defaults(fn=cmd_errors)
    p = sub.add_parser("follow")
    p.add_argument("--all", action="store_true", help="include baseline noise")
    p.add_argument("--from-start", action="store_true")
    p.set_defaults(fn=cmd_follow)
    args = ap.parse_args()
    CTL.mkdir(parents=True, exist_ok=True)
    sys.exit(args.fn(args))


if __name__ == "__main__":
    main()
