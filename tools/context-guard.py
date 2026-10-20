#!/usr/bin/env -S python3 -I
"""Claude Code hook for this project only (.claude/settings.json: PostToolUse, Stop, UserPromptSubmit).

Once the session's context passes QR_HANDOFF_TOKENS (default 250000), the session must stop working, write a hand-off
doc under docs/handoff/, and run tools/handoff.sh, which arranges a new interactive Claude Code instance on that doc.
This hook enforces it: it tells Claude after a tool call, keeps it from ending the turn without handing off, and once
the hand-off is launched tells it to stop. At that last Stop it ends the old session: in tmux it runs the
`tmux respawn-pane -k` command handoff.sh recorded (the new instance starts in the same pane), otherwise it terminates
the old Claude Code process (the new one runs in its own window). State per session: .devlog/handoff/<session id>.json.

Disable with QR_HANDOFF_DISABLE=1 or by creating .devlog/handoff/disabled. Standard library only.
"""
import json
import os
import signal
import subprocess
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
STATE_DIR = REPO / ".devlog" / "handoff"
LIMIT = int(os.environ.get("QR_HANDOFF_TOKENS", "250000"))
TAIL_BYTES = 4 * 1024 * 1024  # the newest usage entry is near the end; transcripts grow to many MB


def context_tokens(transcript):
    """Context size of the latest main-thread request: its input tokens, cache writes and cache reads."""
    try:
        with open(transcript, "rb") as f:
            f.seek(0, os.SEEK_END)
            f.seek(max(0, f.tell() - TAIL_BYTES))
            lines = f.read().split(b"\n")
    except OSError:
        return 0
    for raw in reversed(lines):
        if b'"usage"' not in raw:
            continue
        try:
            entry = json.loads(raw)
        except ValueError:
            continue
        msg = entry.get("message")
        if entry.get("type") != "assistant" or entry.get("isSidechain") or not isinstance(msg, dict):
            continue
        u = msg.get("usage") or {}
        return u.get("input_tokens", 0) + u.get("cache_creation_input_tokens", 0) + u.get("cache_read_input_tokens", 0)
    return 0


def instructions(sid, tokens):
    stamp = time.strftime("%Y%m%d-%H%M")
    return (
        f"CONTEXT LIMIT (project rule): this session's context is {tokens:,} tokens, over the {LIMIT:,} limit. "
        "Stop the current work at the next safe point: start nothing new; let a running test finish or end it with "
        "`tools/autotest.py stop`. Then, in order: "
        "1) commit finished work; "
        f"2) write the hand-off doc `docs/handoff/{stamp}-<slug>.md` from `docs/handoff/TEMPLATE.md`, complete enough "
        "that a fresh instance with no memory of this conversation can continue (state, what was in progress, exact "
        "next steps, decisions and answers from the user, new gotchas, anything waiting on the user); "
        "3) commit it; "
        f"4) run `tools/handoff.sh --session {sid} docs/handoff/<that file>`, which arranges a new interactive Claude "
        "Code instance on it (in tmux it replaces this session in the same pane once your turn ends); "
        "5) end your turn with one line telling the user where the new instance runs. Do no other work."
    )


def emit(obj):
    print(json.dumps(obj))


def claude_pid():
    """The Claude Code process this hook runs under: the nearest ancestor named claude, or None."""
    pid = os.getppid()
    while pid > 1:
        try:
            comm = Path(f"/proc/{pid}/comm").read_text().strip()
            stat = Path(f"/proc/{pid}/stat").read_text()
        except OSError:
            return None
        if comm == "claude":
            return pid
        pid = int(stat.rsplit(")", 1)[1].split()[1])  # field 4, ppid; comm may hold spaces
    return None


def end_session(respawn):
    """
    Ends the old session a second after this hook returns, detached so it outlives it: `respawn` (tmux respawn-pane -k,
    which kills the pane's process and starts the new instance there) or else SIGTERM to the Claude Code process.
    """
    if not respawn:
        pid = claude_pid()
        if pid is None:
            return "no claude process found to close"
        respawn = ["kill", f"-{signal.SIGTERM.value}", str(pid)]
    subprocess.Popen(["sh", "-c", 'sleep 1; exec "$@"', "sh", *respawn], start_new_session=True,
                     stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return " ".join(respawn)


def main():
    try:
        hook = json.load(sys.stdin)
    except ValueError:
        return
    if os.environ.get("QR_HANDOFF_DISABLE") or (STATE_DIR / "disabled").exists():
        return
    event = hook.get("hook_event_name", "")
    sid = hook.get("session_id", "unknown")
    state_file = STATE_DIR / f"{sid}.json"
    try:
        state = json.loads(state_file.read_text())
    except (OSError, ValueError):
        state = {}

    def save():
        STATE_DIR.mkdir(parents=True, exist_ok=True)
        state_file.write_text(json.dumps(state, indent=1))

    if state.get("launched"):
        # Handed off: tell this session once to stop; once it has ended its turn, replace or close it.
        if event == "Stop" and not state.get("ended"):
            state["ended"] = time.time()
            state["end"] = end_session(state.get("respawn") or [])
            save()
            return
        if event == "PostToolUse" and not state.get("told_to_stop"):
            state["told_to_stop"] = time.time()
            save()
            emit({"decision": "block", "reason": f"The hand-off is launched ({state.get('where', 'new instance')}, "
                  f"doc {state.get('doc', '?')}). Stop now: no more work in this session. End your turn with one "
                  "line telling the user where the new instance runs."})
        return

    tokens = context_tokens(hook.get("transcript_path", ""))
    if tokens < LIMIT:
        return
    first = "triggered" not in state
    if first:
        state.update(triggered=time.time(), tokens=tokens)
        save()
    msg = instructions(sid, tokens)
    if event == "PostToolUse":
        # Full instructions once, then a short reminder after every tool call until the hand-off is launched.
        emit({"decision": "block", "reason": msg if first else
              f"Hand-off pending ({tokens:,} tokens): finish the doc, commit, then "
              f"`tools/handoff.sh --session {sid} docs/handoff/<file>`."})
    elif event == "Stop" and not hook.get("stop_hook_active"):
        emit({"decision": "block", "reason": msg})
    elif event == "UserPromptSubmit":
        emit({"hookSpecificOutput": {"hookEventName": "UserPromptSubmit", "additionalContext": msg}})


if __name__ == "__main__":
    main()
