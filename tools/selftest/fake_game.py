"""A fake RimWorld for tools/selftest.sh: speaks AutoTest.cs's file protocol so autotest.py can be tested without the
game. Launched through ./RimWorldLinux with the real game's arguments.

  FAKE_STARTUP_ERROR=1   log one red error at startup (autotest.py must count it)
  FAKE_FAIL=<name>       that scenario FAILs
"""
import json
import os
import subprocess
import sys
import threading
import time
from pathlib import Path

args = sys.argv[1:]
log_path = Path(args[args.index("-logFile") + 1])
ctl_arg = [a.split("=", 1)[1] for a in args if a.startswith("-quadrunner-autotest=")]
if not ctl_arg:
    # Baseline run (no Quadrunner): log some vanilla noise, then idle until killed.
    log_path.write_text("Command line arguments: " + " ".join(args) + "\n"
                        "Exception in fake startup: NullReferenceException\n[Ref ABCD1234]\n  at Fake.Startup ()\n")
    while True:
        time.sleep(1)
ctl = Path(ctl_arg[0])
devlog = Path(os.environ["QR_DEVLOG"])
build = (devlog / "last-build").read_text().strip()  # "loads" whatever was built last, like the real DLL
pid = os.getpid()
launch = f"{time.strftime('%Y%m%d-%H%M%S', time.gmtime())}-{pid}"
log = open(log_path, "w", buffering=1)
beating = True


def event(ev, **fields):
    with open(ctl / "events.jsonl", "a") as f:
        f.write(json.dumps(dict(ev=ev, launch=launch, pid=pid, **fields)) + "\n")


def beat():
    while beating:
        (ctl / "heartbeat").write_text(launch + "\n")
        time.sleep(1)


log.write(f"Command line arguments: {' '.join(args)}\n")
log.write(f"[Quadrunner] loaded build 0.1.0+{build}\n")
if os.environ.get("FAKE_STARTUP_ERROR"):
    log.write("Exception in fake startup: NullReferenceException\n[Ref ABCD1234]\n  at Fake.Startup ()\n")
threading.Thread(target=beat, daemon=True).start()
event("launched", build=f"0.1.0+{build}")
time.sleep(2)  # "generating the quicktest map"
event("ready", build=f"0.1.0+{build}", game="1.6.4871 rev598", map="(250, 1, 250)")

while True:
    time.sleep(0.5)
    cmd = ctl / "command"
    if not cmd.exists():
        continue
    text = cmd.read_text().strip()
    cmd.unlink()
    cid, verb, arg = (text.split(" ", 2) + ["", ""])[:3]
    log.write(f"[Quadrunner] autotest: command {text}\n")
    if verb == "run":
        verdicts = []
        for name in ("Smoke", "Save and reload"):
            if arg and not any(p.strip().lower() in name.lower() for p in arg.split(",")):
                continue
            verdict = "FAIL" if os.environ.get("FAKE_FAIL") == name else "PASS"
            log.write(f"[Quadrunner] SCENARIO {name}: {verdict} in 300 ticks\n")
            event("scenario", name=name, verdict=verdict, detail=f"{verdict} in 300 ticks")
            verdicts.append(f"{name} {verdict}")
        event("result", id=cid, passed=sum(v.endswith("PASS") for v in verdicts), total=len(verdicts), verdicts=verdicts)
    elif verb == "restart":
        # Like GenCommandLine.Restart: start a new process with the same command line, then quit.
        event("restarting", id=cid)
        subprocess.Popen([os.environ["FAKE_EXE"]] + args, start_new_session=True,
                         stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        sys.exit(0)
    elif verb == "quit":
        event("quitting", id=cid)
        sys.exit(0)
    elif verb == "hang":
        event("hanging", id=cid)
        beating = False
        while True:
            time.sleep(1)
