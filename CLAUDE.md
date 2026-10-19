# CLAUDE.md

@AGENTS.md

## Claude Code specifics

- **Running the test instance.** Run `tools/autotest.py run [filter]` as a background Bash command and wait for its exit notification instead of polling; for a live view during a long run, stream `tools/autotest.py follow` through the Monitor tool.
- **The user's game.** Before a step that needs the user's game closed, say so in one plain line and wait for their reply; when they may be away, also send a PushNotification. Never act on their game process yourself.
- **Commands the user must run.** When the user has to run something themselves (e.g. a global git config change), suggest it with the `! <command>` prefix so the output lands in the session.
- **Questions.** Ask the user (AskUserQuestion) only for decisions the plan doesn't already record. Record each answer in the plan's Decisions table in the same commit.
- **Decompiled game source.** Keep it in `.tools/decomp/`, never in the scratchpad, so later sessions can reuse it.
