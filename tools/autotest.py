#!/usr/bin/env -S python3 -I
"""Drive the Quadrunner test instance: an isolated RimWorld on a private Xvfb display.

  autotest.py run [filter]    one iteration: build, restart the instance in game (or start it), run the scenarios,
                              diff the log against the baseline, record the run. Exit 0 pass, 1 fail, 2 hang/crash,
                              3 not run (the environment is broken)
  autotest.py pkg-run [filter]
                              like run, but against the packaged copy in dist/Quadrunner (QR_PKG) without building:
                              the mod link temporarily points at the package, and the loaded assembly must report the
                              package's BUILD stamp; the dev link is restored afterwards
  autotest.py removal-check   load the prepared removal save WITHOUT Quadrunner, with an independent test-only observer.
                              Require 600 live ticks, a new save/reload, 600 more ticks, retained vanilla contents and
                              zero new errors. Never installs the observer into the release package.
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
import shutil
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
# Quicktest map size (vanilla: 250; empty keeps it). 200 ran about twice as fast as 250 (2026-10-20). Takes effect at
# the next cold start (`stop`, then `run`): an in-game restart reuses the old command line.
MAP_SIZE = os.environ.get("QR_MAP_SIZE", "200")
# The instance renders in software (llvmpipe): window size, and llvmpipe's thread count (LP_NUM_THREADS; empty: one
# per core). At 1280x720 with every core it used ~1800% CPU; 640x360 with 4 threads ran as fast at ~400% (2026-10-20).
SCREEN_W, SCREEN_H = (int(v) for v in os.environ.get("QR_SCREEN", "640x360").split("x"))
RENDER_THREADS = os.environ.get("QR_RENDER_THREADS", "4")
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
MAPSIZE_ARG = "-quadrunner-mapsize"
# The packaged release copy (tools/package.sh): `autotest.py pkg-run` points the mod link at it instead of the dev
# repo, without building. Same variable and default as tools/env.sh.
PKG = Path(os.environ.get("QR_PKG", REPO / "dist/Quadrunner"))
# Where the mod link points for a normal launch: the dev repo. pkg-run temporarily repoints it (and restores it).
MOD_DIR = REPO
# The save the "Removal save" scenario writes and `removal-check` loads without the mod.
REMOVAL_SAVE = "QR_removal"
LOAD_MARKER = "Loading game from file autostart"
PROBE_ID = "quadrunner.removalprobe"
PROBE_MOD = DEVLOG / "removal-probe-mod"
PROBE_LINK = RW_DIR / "Mods" / "QuadrunnerRemovalProbe"
PROBE_CTL = DEVLOG / "removal-probe"

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
  <screenWidth>{w}</screenWidth>
  <screenHeight>{h}</screenHeight>
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


def write_profile(with_mod, removal_probe=False):
    """Rewrites the test profile's config every launch, so the game can't drift from it."""
    config = RW_DATA / "Config"
    config.mkdir(parents=True, exist_ok=True)
    mods = CORE_MODS + ([PACKAGE_ID] if with_mod else []) + ([PROBE_ID] if removal_probe else [])
    items = "\n".join(f"    <li>{m}</li>" for m in mods)
    (config / "ModsConfig.xml").write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n<ModsConfigData>\n'
        f"  <version>{game_version()}</version>\n  <activeMods>\n{items}\n  </activeMods>\n"
        "  <knownExpansions />\n</ModsConfigData>\n")
    (config / "Prefs.xml").write_text(PREFS.replace("{w}", str(SCREEN_W)).replace("{h}", str(SCREEN_H)))


def ensure_mod_link():
    """The mod link must point at MOD_DIR (the dev repo, or dist/Quadrunner during pkg-run)."""
    target = MOD_DIR
    if MOD_LINK.is_symlink():
        if MOD_LINK.resolve() != target:
            die(f"{MOD_LINK} links to {MOD_LINK.resolve()}, not {target}; move it away by hand")
    elif MOD_LINK.exists():
        die(f"{MOD_LINK} exists and is not the mod link; move it away by hand")
    else:
        MOD_LINK.symlink_to(target)
        say(f"installed mod link {MOD_LINK} -> {target}")


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
    proc = subprocess.Popen(["Xvfb", f":{n}", "-screen", "0", f"{SCREEN_W}x{SCREEN_H}x24", "-nolisten", "tcp", "-noreset"],
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


def launch(with_mod=True, removal_probe=False):
    """Starts a fresh instance in its own session, so it outlives this script."""
    write_profile(with_mod, removal_probe)
    if with_mod:
        ensure_mod_link()
        # A leftover autostart save would load instead of the quicktest map (Root_Play checks it first). It is only
        # ever written by the removal check, as a copy; remove it so a normal launch always quicktests.
        (RW_DATA / "Saves" / "autostart.rws").unlink(missing_ok=True)
    archive_log(f"orphan-{utc_stamp()}")
    env = dict(os.environ)
    env.pop("WAYLAND_DISPLAY", None)
    env.update(DISPLAY=ensure_xvfb(), LC_ALL="C",
               XDG_CONFIG_HOME=str(DEVLOG / "xdg/config"), XDG_CACHE_HOME=str(DEVLOG / "xdg/cache"))
    if RENDER_THREADS:
        env["LP_NUM_THREADS"] = RENDER_THREADS
    argv = [str(EXE), f"-savedatafolder={RW_DATA}", "-logFile", str(RW_LOG), "-quicktest", "-disable-compute-shaders"]
    if with_mod:
        argv.append(f"{AUTOTEST_ARG}={CTL}")
        if MAP_SIZE:
            argv.append(f"{MAPSIZE_ARG}={MAP_SIZE}")
    if removal_probe:
        argv.append(f"-quadrunner-removal-probe={PROBE_CTL}")
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

def run_harness(expected_build, args, build_first):
    """Start or restart the instance until it runs `expected_build` (the latest dev build, or a package's BUILD
    stamp), run the scenarios and report. Never builds when `build_first` is false (packaged runs)."""
    if build_first:
        say("building")
        if subprocess.run([str(REPO / "tools/build.sh")]).returncode != 0:
            print("RESULT: BUILD FAILED")
            return EXIT_FAIL
    last = expected_build or (LAST_BUILD.read_text().strip() if LAST_BUILD.exists() else None)
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
        if Path(cur.get("modRoot", "")).resolve() != MOD_DIR.resolve():
            print(f"RESULT: WRONG MOD SOURCE: loaded {cur.get('modRoot')}, expected {MOD_DIR}")
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


def cmd_run(args):
    preflight()
    return run_harness(None, args, build_first=not args.no_build)


def cmd_pkg_run(args):
    """Cold-launch the actual package, never build it or reuse a dev process with the same build stamp."""
    preflight()
    pkg = PKG.resolve()
    about = pkg / "About" / "About.xml"
    import xml.etree.ElementTree as ET
    try:
        if ET.parse(about).getroot().findtext("packageId") != PACKAGE_ID:
            die(f"{about} does not declare packageId {PACKAGE_ID}")
    except (OSError, ET.ParseError) as e:
        die(f"no valid package at {pkg}: {e} (tools/package.sh writes it from a clean tree)")
    stamp_file = pkg / "BUILD"
    if not stamp_file.is_file() or not (stamp := stamp_file.read_text().strip()):
        die(f"no BUILD stamp at {stamp_file}")
    dll = pkg / "1.6" / "Assemblies" / "Quadrunner.dll"
    if not dll.is_file() or dll.stat().st_size == 0:
        die(f"no Quadrunner.dll at {dll}")
    if MOD_LINK.is_symlink():
        if MOD_LINK.resolve() not in (REPO, pkg):
            die(f"refusing to replace an unrelated mod link: {MOD_LINK} -> {MOD_LINK.resolve()}")
    elif MOD_LINK.exists():
        die(f"{MOD_LINK} exists and is not a symlink")
    global MOD_DIR
    try:
        cmd_stop(args)
        MOD_DIR = pkg
        MOD_LINK.unlink(missing_ok=True)
        MOD_LINK.symlink_to(pkg)
        say(f"packaged run against {pkg}, expected build {stamp}; cold launch and modRoot checked")
        return run_harness(stamp, args, build_first=False)
    finally:
        cmd_stop(args)
        MOD_DIR = REPO
        MOD_LINK.unlink(missing_ok=True)
        MOD_LINK.symlink_to(REPO)
        say(f"restored mod link {MOD_LINK} -> {REPO}")


def cmd_removal_check(args):
    """Exercise a prepared save in a fresh process that cannot load Quadrunner.dll."""
    preflight()
    if test_pids():
        die("the test instance is running; autotest.py stop first")
    if Path(args.save).name != args.save:
        die("--save must be a save name, not a path")
    src = RW_DATA / "Saves" / f"{args.save}.rws"
    expected = CTL / "removal-expected.txt"
    if not src.is_file() or not expected.is_file():
        die("run 'autotest.py run \"Prepare removal\"' first: removal save or expected vanilla contents missing")
    if PROBE_LINK.exists() or PROBE_LINK.is_symlink():
        if not PROBE_LINK.is_symlink() or PROBE_LINK.resolve() != PROBE_MOD:
            die(f"refusing to replace unrelated removal-observer install {PROBE_LINK}")
    assemblies = PROBE_MOD / "1.6" / "Assemblies"
    if subprocess.run(["dotnet", "build", str(REPO / "tools/removal-probe/RemovalProbe.csproj"), "-c", "Release",
                       "-nologo", "-v", "quiet", "-o", str(assemblies)]).returncode:
        return EXIT_FAIL
    about = PROBE_MOD / "About"
    about.mkdir(parents=True, exist_ok=True)
    (about / "About.xml").write_text(
        f"<ModMetaData><name>Quadrunner removal observer (test only)</name><author>Quadrunner tools</author>"
        f"<packageId>{PROBE_ID}</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>")
    PROBE_CTL.mkdir(parents=True, exist_ok=True)
    shutil.copy2(expected, PROBE_CTL / "expected.txt")
    for name in ("result", "heartbeat"):
        (PROBE_CTL / name).unlink(missing_ok=True)
    auto = RW_DATA / "Saves" / "autostart.rws"
    auto.unlink(missing_ok=True)
    shutil.copy2(src, auto)
    result, code = "", EXIT_FAIL
    log = None
    try:
        if not PROBE_LINK.is_symlink():
            PROBE_LINK.symlink_to(PROBE_MOD)
        launch(with_mod=False, removal_probe=True)
        t0 = time.time()
        while not (PROBE_CTL / "result").is_file():
            time.sleep(0.5)
            if not test_pids():
                raise Outcome("CRASH", "the no-Quadrunner process exited")
            if time.time() - t0 > args.ready_timeout:
                raise Outcome("TIMEOUT", "the independent observer did not finish")
            beat = PROBE_CTL / "heartbeat"
            if beat.is_file() and time.time() - beat.stat().st_mtime > args.hang_after:
                raise Outcome("HANG", "the independent observer stopped beating")
        result = (PROBE_CTL / "result").read_text().strip()
        code = EXIT_PASS if result.startswith("PASS:") else EXIT_FAIL
    except Outcome as o:
        result, code = str(o), EXIT_HANG
    finally:
        kill_instance()
        log = archive_log(f"removal-{utc_stamp()}")
        auto.unlink(missing_ok=True)
        PROBE_LINK.unlink(missing_ok=True)
        write_profile(with_mod=True)
    text = log.read_text(errors="replace") if log else ""
    errors = new_errors(log) if log else []
    for b in errors:
        print("\\n".join(b.lines[:20]))
    if LOAD_MARKER not in text or OURS in text:
        result, code = "invalid proof: save was not loaded or Quadrunner was still active", EXIT_FAIL
    if errors and code == EXIT_PASS:
        code = EXIT_FAIL
    print(f"RESULT: {'LOADED CLEAN' if code == EXIT_PASS else 'REMOVAL FAILED'}: {result}; {len(errors)} new errors")
    print(f"log: {log}")
    print(f"observer proof: {PROBE_CTL / 'result'}; screenshot: {PROBE_CTL / 'removed-colony.png'}")
    return code


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
             mod_root=cur.get("modRoot"),
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
    p = sub.add_parser("pkg-run")
    p.add_argument("filter", nargs="?", help="comma-separated, case-insensitive parts of scenario names")
    p.set_defaults(fn=cmd_pkg_run)
    p = sub.add_parser("removal-check")
    p.add_argument("--save", default=REMOVAL_SAVE, help="prepared save name (not a path)")
    p.set_defaults(fn=cmd_removal_check)
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
