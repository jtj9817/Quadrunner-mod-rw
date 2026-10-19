#!/usr/bin/env python3
"""Claude Code status line that records usage (rate-limit) data.

Reads the status-line JSON from stdin, appends its `rate_limits` (with a UTC
timestamp, the session id and the subscription type) to a JSON-lines file
whenever they change, and prints the same line as the user's global status
line: user@host:cwd.

Usage: python3 -I tools/statusline.py [-h] [--out FILE]
  --out FILE   where to append (default: $QR_USAGE_LOG, else
               <project>/.devlog/usage.jsonl)

Wire it in a settings file:
  "statusLine": {"type": "command",
                 "command": "python3 -I \"$CLAUDE_PROJECT_DIR/tools/statusline.py\""}
"""
import datetime
import getpass
import json
import os
import socket
import sys


def default_out():
    if os.environ.get("QR_USAGE_LOG"):
        return os.environ["QR_USAGE_LOG"]
    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    devlog = os.environ.get("QR_DEVLOG", os.path.join(repo, ".devlog"))
    return os.path.join(devlog, "usage.jsonl")


def record(data, out):
    """Append the rate limits to `out` unless they equal the last record."""
    limits = data.get("rate_limits")
    if limits is None and data.get("rate_limits_available") is None:
        return
    key = json.dumps([limits, data.get("rate_limits_available")], sort_keys=True)
    last = out + ".last"
    try:
        with open(last) as f:
            if f.read() == key:
                return
    except OSError:
        pass
    os.makedirs(os.path.dirname(out) or ".", exist_ok=True)
    row = {
        "at": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "session": data.get("session_id"),
        "subscription": data.get("subscription_type"),
        "available": data.get("rate_limits_available"),
        "rate_limits": limits,
    }
    with open(out, "a") as f:
        f.write(json.dumps(row, sort_keys=True) + "\n")
    with open(last, "w") as f:
        f.write(key)


def main():
    args = sys.argv[1:]
    if "-h" in args or "--help" in args:
        print(__doc__.strip())
        return
    out = args[args.index("--out") + 1] if "--out" in args else default_out()
    try:
        data = json.load(sys.stdin)
    except ValueError:
        data = {}
    try:
        record(data, out)
    except OSError:
        pass  # never break the status line over the log
    cwd = (data.get("workspace") or {}).get("current_dir") or data.get("cwd") or os.getcwd()
    host = socket.gethostname().split(".")[0]
    sys.stdout.write("\033[01;32m%s@%s\033[00m:\033[01;34m%s\033[00m" % (getpass.getuser(), host, cwd))


if __name__ == "__main__":
    main()
