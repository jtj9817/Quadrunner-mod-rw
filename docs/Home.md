# Quadrunner wiki

> RimWorld 1.6 mod (Core + Harmony only) adding stable-dispatched utility, rescue, and four-tier combat robot dogs with clean save removal.

Quadrunner stores all persistent state on `Building_Stable` and `CompRobodog` (zero `MapComponent`s or `GameComponent`s), coordinating per-map discovery, queues, packs, and surveying through a transient `DogNetwork` hosted on one elected node ([Architecture](architecture.md)).

## Spaces & canonical docs

| Area | What is in it |
| --- | --- |
| [Architecture](architecture.md) | Runtime overview, Think Tree contract, Harmony patches, rendering, clean removal, test harness |
| [Queue & order dispatch](architecture/queue-and-order-dispatch.md) | Whys, hows, algorithms, and invariants for queues, matching, handoff, smoke clusters, survey & charge caches |
| [Interactive diagrams](diagrams/index.html) | Subsystem pages, behaviour flows, and traceability matrix (`diagrams/model.json`) |
| [Tickets](tickets/QR-0004-queue-and-order-tracking-improvements.md) | Local JIRA-style tickets (`QR-0001`–`QR-0004`) |
| [Plans](plans/0001-quadrunner.md) | Design decisions and acceptance targets (`0001`–`0012`) |
| [Test runs](test-runs.md) | Per-run automated verification log (dev and `dist/Quadrunner` packages) |
| [Changelog](CHANGELOG.md) | Player-facing release notes |

## I want to…

| I want to… | Go to |
| --- | --- |
| understand how queues, matching, backoff, and handoff work | [Queue tracking, order assignment, and runtime algorithms](architecture/queue-and-order-dispatch.md) |
| see the full runtime ownership, Think Tree, and Harmony patches | [Architecture](architecture.md) |
| install, build, or cleanly remove the mod | [README](../README.md) |
| run automated scenarios or package `dist/Quadrunner` | [AGENTS.md](../AGENTS.md) |
| inspect recent changes and shipped builds | [Changelog](CHANGELOG.md) · [Test runs](test-runs.md) |
| check open bugs or completed investigations | [QR-0003](tickets/QR-0003-paired-gun-duplicate-primary.md) · [QR-0004](tickets/QR-0004-queue-and-order-tracking-improvements.md) |
