# System diagrams

Interactive HTML diagrams of the Quadrunner mod: a context overview, one page
per subsystem, three behaviour flows, and a requirements-to-verification
traceability page. Everything works offline from this folder — open
[`index.html`](index.html) in a browser.

## What is here

| Page | Shows |
| --- | --- |
| `index.html` | Actors, the system boundary and the twelve subsystems |
| `decomposition.html` | System → subsystem → component, with requirement counts |
| `traceability.html` | The V: every requirement, its allocation and the scenario/tool that proves it, plus coverage stats and a gap report |
| `sub--<id>.html` | One per subsystem: its components and immediate neighbours (faded) |
| `flow--<id>.html` | One per behaviour, with the mechanism under every step and a failure-mode table: one haul end to end, raid/war mode to recovery, the stable-vs-conduit charge decision |
| `model.json` | **The source of truth.** Every box, arrow, requirement and verification below is derived from it |

The model is brownfield: components come from the code (`Source/Quadrunner/`,
`tools/`), requirements are the acceptance boxes of
[docs/plans/](../plans/0001-quadrunner.md), and every verification names the
scenario file or tool that proved it, with run evidence in
[docs/test-runs.md](../test-runs.md). Prose architecture lives in
[docs/architecture.md](../architecture.md); the two should stay consistent.

## Reader controls

Scroll to zoom, drag to pan, click any element to inspect it (what it is, what
it depends on, which requirements it satisfies, which tests prove them, where
its evidence lives). `/` search, `t` trace mode (follows every hop instead of
just neighbours), `a` annotate, `0` fit, `1` actual size, `Esc` clear.
Annotations you add are stored in your browser and export as JSON in exactly
`model.json`'s `annotations` shape, so a review's comments can be pasted back
into the model and become permanent.

## Regenerating after a change

Edit `model.json`, then validate and render (Python 3.9+, standard library
only; the scripts are the `system-diagrams` skill's, run from its checkout):

```sh
python3 <skill-dir>/scripts/validate_model.py docs/diagrams/model.json
python3 <skill-dir>/scripts/render.py docs/diagrams/model.json -o docs/diagrams
```

Warnings from the validator are engineering findings (a component nothing
asked for, a requirement nobody verifies) — surface them, do not suppress
them. Evidence line numbers in the model were captured when it was written
(2026-11-07) and will drift as source moves; class names and files stay
accurate much longer.
