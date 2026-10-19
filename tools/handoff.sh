#!/usr/bin/env bash
# Open a new INTERACTIVE Claude Code instance that continues from a hand-off doc (the context-limit rule,
# tools/context-guard.py). In tmux: a new window in the current tmux session (it becomes the active window).
# Without tmux: an Alacritty window on the user's display ($QR_HANDOFF_DISPLAY, default :1).
set -euo pipefail
source "$(dirname "$0")/env.sh"

usage() { echo "usage: handoff.sh [--session <id>] [--dry-run] docs/handoff/<doc>.md    (extra claude args: \$QR_HANDOFF_CLAUDE_ARGS)"; }
session="" dry=""
while (( $# )); do
  case "$1" in
    --session) session="$2"; shift 2 ;;
    --dry-run) dry=1; shift ;;
    -h | --help) usage; exit 0 ;;
    *) break ;;
  esac
done
(( $# == 1 )) || { usage; exit 1; }
doc="${1#"$REPO"/}"
[[ -f "$REPO/$doc" ]] || die "no hand-off doc at $doc"
[[ -z "$(git_ status --porcelain -- "$doc")" ]] || die "$doc is not committed; commit it first"

claude_bin="$(command -v claude || echo "$HOME/.local/bin/claude")"
[[ -x "$claude_bin" ]] || die "claude not found"
prompt="Continue the Quadrunner mod work from the hand-off doc $doc: read it first, then follow AGENTS.md's Session start. The previous Claude Code session stopped at its context limit; the repo and that doc are all you have from it."
# shellcheck disable=SC2206  # word-splitting the extra args is the point
cmd=("$claude_bin" ${QR_HANDOFF_CLAUDE_ARGS:-} "$prompt")
name="qr-$(date +%m%d-%H%M)"

if [[ -n "${TMUX:-}" ]] && command -v tmux > /dev/null; then
  tsession="$(tmux display -p '#S')"
  where="tmux window $tsession:$name"
  launch=(tmux new-window -t "$tsession:" -n "$name" -c "$REPO" "${cmd[@]}")
elif command -v alacritty > /dev/null; then
  where="Alacritty window on display ${QR_HANDOFF_DISPLAY:-:1}"
  launch=(env DISPLAY="${QR_HANDOFF_DISPLAY:-:1}" setsid alacritty --title "$name" --working-directory "$REPO" -e "${cmd[@]}")
else
  die "neither tmux (\$TMUX unset) nor alacritty available to open an interactive instance"
fi

if [[ -n "$dry" ]]; then
  printf 'would run:'; printf ' %q' "${launch[@]}"; echo
  exit 0
fi
if [[ "${launch[0]}" == env ]]; then
  "${launch[@]}" > /dev/null 2>&1 < /dev/null &
else
  "${launch[@]}"
fi

if [[ -n "$session" ]]; then
  mkdir -p "$DEVLOG/handoff"
  python3 -I -c 'import json,sys,time; p,doc,where=sys.argv[1:]
try: s=json.load(open(p))
except (OSError, ValueError): s={}
s.update(launched=time.time(), doc=doc, where=where); json.dump(s, open(p,"w"), indent=1)' \
    "$DEVLOG/handoff/$session.json" "$doc" "$where"
fi
echo "handoff: new interactive Claude Code instance in $where, working from $doc"
