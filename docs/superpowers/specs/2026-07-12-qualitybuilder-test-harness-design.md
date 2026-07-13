# QualityBuilder Automated Test Harness — Design

**Date:** 2026-07-12
**Status:** Approved design, pending implementation plan
**Target:** RimWorld 1.6, QualityBuilder (`hatti.qualitybuilder`)

## 1. Goal

Provide a repeatable automated test suite that verifies QualityBuilder functions
correctly, by driving a live RimWorld instance. The suite must be deterministic
(not dependent on the RNG quality roll), observe QB's real internal state
(`CompQualityBuilder` fields, designations, quality), and be runnable end-to-end
from a single Python command that starts/restarts the game, loads a known save,
runs every test, and reports pass/fail.

Non-goals: testing vanilla RimWorld construction itself; UI-pixel verification;
multiplayer; performance benchmarking.

## 2. Confirmed decisions

| Decision | Choice |
|---|---|
| Observability / control | New `QualityBuilderBridgeTools` companion DLL (RimBridge SDK pattern) |
| RNG quality | Force the rolled quality; tests exercise QB logic deterministically |
| Python transport | Direct GABP TCP to RimBridge (port/token from `Player.log`) |
| Test save | Dedicated hand-made `qb_test` save |
| Driving level | Logic-level primary + ~3 real-pawn integration tests |
| State reset | Per-test scratch-arena cleanup; real save+reload only for persistence tests |

## 3. Architecture

```
Python test runner  ──GABP/TCP──▶  RimBridgeServer  ──▶  QualityBuilderBridgeTools.dll
(orchestration,          (in RimWorld process)        (thin [Tool] wrappers that call
 assertions, lifecycle)                                 QB public API + read live state)
```

Three deliverables:

1. **`QualityBuilderBridgeTools`** — companion DLL, built and deployed exactly like
   `ZoneStorageBridgeTools` (`Mods\ZoneStorageBridgeTools`): references
   `RimBridgeServer.Sdk` (`Private=false`), game API from `Krafs.Rimworld.Ref`,
   `OutputPath` deploys the DLL to `RimWorldRoot\BridgeTools\QualityBuilderBridgeTools.dll`.
   All game access marshalled via `ctx.MainThread.InvokeAsync`. Dev-mode-gated for
   mutation tools (`Prefs.DevMode`), matching the ZoneStorage precedent.
2. **`qbtest`** — a Python package: GABP client, game lifecycle control, QB tool
   wrappers, the test modules, and a runner.
3. **`qb_test`** — a hand-made RimWorld save providing a known starting state.

### 3.1 Why call QB's real API (not reimplement)

QB's key entry points are already `public static`, so the companion DLL exercises
**the real code under test** rather than a copy:

- `QualityBuilder.QualityBuilder.setSkilled(Thing, QualityCategory?, bool)`
- `QualityBuilder.QualityBuilder.checkAndDesignateForRebuild(Building, CompQualityBuilder)`
- `QualityBuilder.QualityBuilder.getCompQualityBuilder(Thing)`
- `QualityBuilder.QualityBuilder.getBestConstructorSkill(Map)` / `pawnCanConstruct(Pawn)`
- `QualityBuilder._JobDriver_ConstructFinishFrame.afterFinishToil(CompQualityBuilder, Map, LocalTargetInfo)`
- `QualityBuilder._WorkGiver_ConstructFinishFrames.isPawnGoodEnoughToBuild(Pawn)`
- `CompQualityBuilder` public properties: `isSkilled`, `desiredMinQuality`,
  `pendingQualityRebuild`, `qualityRebuildAttempts`, `isDesiredMinQualityReached`.

**Binding approach:** the companion is a loose DLL in `BridgeTools\`, while QB loads
from its mod folder. To avoid assembly-resolution/load-order risk from a compile-time
reference, the companion resolves QB via **cached reflection**
(`GenTypes.GetTypeInAnyAssembly("QualityBuilder.QualityBuilder")` etc.), caching
`MethodInfo`/`PropertyInfo` on first use. RimWorld/Verse types are still used
directly through `Krafs.Rimworld.Ref`.

## 4. Component 1 — `QualityBuilderBridgeTools` tool surface

All tools return `{ success: bool, ... }` or `{ success: false, error: string }`,
matching the ZoneStorage convention. Mutation tools return `error` if
`!Prefs.DevMode`. `cell` params are `(x, z)`; the y/altitude is always 0.

### 4.1 Read-only (assertions)

| Tool | Params | Returns |
|---|---|---|
| `qb/get_building_state` | `thingId?` or `x,z` | `def`, `isBlueprint`, `isFrame`, `hasQuality`, `quality` (string or null), comp state (`isSkilled`, `desiredMinQuality`, `pendingQualityRebuild`, `qualityRebuildAttempts`, `isDesiredMinQualityReached`), `qbDesignation` (e.g. `SkilledBuilder3` or null), `hasDeconstructDesignation` |
| `qb/get_settings` | — | global settings + current-map settings (`defaultUseQualityBuilder`, `defaultMinQualitySetting`, `skillDifferenceFromBestBuilder`, `ignoreQualityBuilderAtSkill`, `maxQualityRebuildAttempts`, `bestConstructorOverride` label/id, `useMapSettings`), and computed `getBestConstructorSkill` |
| `qb/get_gizmo_info` | `thingId` | `commandOffered` (would `CompGetGizmosExtra` yield the QB button?), `floatMenuQualities` (the ordered quality options `RightClickFloatMenuOptions` would list for the current selection) |
| `qb/list_qb_things` | `x,z,width,height` | array of QB-comp blueprints/frames/buildings with `thingId`, `def`, kind, `quality`, cell |

`qb/get_gizmo_info` sets `Find.Selector` to the target thing (single selection),
enumerates the real gizmo/float-menu code paths, then restores selection.

### 4.2 Setup / mutation (dev-mode gated)

| Tool | Params | Effect |
|---|---|---|
| `qb/spawn_blueprint` | `def`, `x,z`, `rot?`, `stuff?` | `GenConstruct.PlaceBlueprintForBuild(...)`; returns new `thingId`. Exercises `PostSpawnSetup` auto-adopt. |
| `qb/spawn_finished_building` | `def`, `x,z`, `stuff?`, `quality`, `rot?` | Spawn a finished building, `CompQuality.SetQuality(quality, ArtGenerationContext.Colony)`; ensure QB comp present. **The force-quality primitive.** Returns `thingId`. |
| `qb/set_skilled` | `thingId`, `quality`, `add` | Calls `QualityBuilder.setSkilled` (real designation add/remove + forbidden preservation + finished-building rebuild check). |
| `qb/set_comp_state` | `thingId`, any of `isSkilled`, `desiredMinQuality`, `pendingQualityRebuild`, `qualityRebuildAttempts`, `isDesiredMinQualityReached` | Directly set comp fields to arrange preconditions. |
| `qb/invoke_check_rebuild` | `thingId` | Calls `checkAndDesignateForRebuild(building, comp)` directly. |
| `qb/invoke_after_finish_toil` | `x,z` (or frame `thingId`) | Calls `afterFinishToil(comp, map, target)` directly (def-disambiguation test). |
| `qb/set_pawn_skill` | `pawnId`, `level` | Set a colonist's Construction skill level. |
| `qb/set_pawn_flags` | `pawnId`, any of `drafted`, `downed` | Toggle transient pawn state for gate tests (prisoner/mental arranged via existing debug tools or skipped if not cleanly settable). |
| `qb/set_setting` | `key`, `value`, `scope` (`map`\|`global`) | Set one settings field, incl. `maxQualityRebuildAttempts` (accepts the unlimited sentinel), `useMapSettings`, `bestConstructorOverride` (by `pawnId` or clear). |
| `qb/clear_arena` | `x,z,width,height` | Despawn/destroy every thing and remove all designations in the rect (per-test cleanup). Returns counts. |
| `qb/load_save` | `name` | Load a save by name (`GameDataSaveLoader.LoadGame`), for persistence-test reload. |
| `qb/save_game` | `name` | Save current game to `name`. |

`qb/load_save` and `qb/save_game` run through `LongEventHandler`; the Python runner
polls readiness (a subsequent `qb/get_settings` succeeding) rather than assuming
synchronous completion.

## 5. Component 2 — `qb_test` save

Hand-made once and committed as a fixture (or documented for manual recreation):

- **Dev mode ON** (mutation tools require it).
- QualityBuilder + RimBridgeServer active; companion DLL present in `BridgeTools\`.
- A flat, buildable dirt area containing a **scratch arena rect** anchored at a
  fixed, documented cell (e.g. min corner recorded in `arena.py`). Large enough for
  several simultaneous blueprints without overlap.
- Reachable **stockpile of steel and wood** (materials for the integration builds).
- **Two colonists**, both with Construction work enabled:
  - `builder_hi` — high Construction skill (e.g. 15+).
  - `builder_lo` — low Construction skill (e.g. 2).
- Ideology enabled with at least one styleable precept, so the style-preservation
  assertion (D3) is meaningful.

The runner loads this save by name at startup and after crashes.

## 6. Component 3 — Python runner (`qbtest` package)

```
qbtest/
  gabp_client.py    # TCP GABP client: connect, handshake(token), call_tool(name, params), timeouts, reconnect
  discovery.py      # tail Player.log for RimBridge "listening on port / token" line; wait-for-ready
  game_control.py   # kill/launch RimWorldWin64.exe; wait for bridge; load qb_test
  qb.py             # typed wrappers over qb/* and the built-in RimBridge tools
  arena.py          # scratch-rect coordinates + clear_arena helper
  tests/
    a_designation.py
    b_persistence.py
    c_skill.py
    d_rebuild.py
    e_config.py
  runner.py         # lifecycle, ordering, per-test cleanup, reload-for-persistence, crash-restart, reporting
  __main__.py       # `python -m qbtest [--filter ...] [--no-restart]`
```

### 6.1 GABP transport (`gabp_client.py`)

- Connect TCP to `127.0.0.1:<port>`; perform the GABP handshake with the token.
- `call_tool(name, params, timeout)` → sends a request, awaits the matching response,
  raises `ToolError` on `success:false`, `ToolTimeout` on no response.
- A dropped socket / timeout is surfaced to the runner as a probable game crash.

Port/token source (direct mode): `discovery.py` tails
`...\AppData\LocalLow\Ludeon Studios\RimWorld*\Player.log` for RimBridge's
startup line announcing the port and token. (Confirm the exact log format during
implementation; fall back to a fixed default port if RimBridge exposes one.)

### 6.2 Game lifecycle (`game_control.py`)

- `ensure_running()`: if no bridge answers, `taskkill` any existing `RimWorldWin64`
  process, launch the exe, then poll `discovery` + a trivial tool call until ready
  (bounded timeout).
- `load_test_save()`: call `qb/load_save("qb_test")`, poll until `qb/get_settings`
  succeeds.
- New companion DLLs require a full game restart to load — handled on first launch.

### 6.3 Runner loop (`runner.py`)

1. `ensure_running()` → `load_test_save()`.
2. For each test (ordered by module): `clear_arena` → run body → collect result →
   `clear_arena`.
3. Persistence tests (`b_*`) instead do `qb/save_game` → `qb/load_save` and assert
   across the reload.
4. On `ToolTimeout`/dropped connection during a test: mark it `ERROR`, restart the
   game, reload `qb_test`, continue with the next test.
5. Emit live `PASS/FAIL/ERROR` lines to console and write a JSON summary to the
   scratchpad; exit non-zero if any test is not `PASS`.

### 6.4 Determinism

Quality is never left to the RNG roll except inside the ~3 integration tests, and
even those assert *outcomes* (a rebuild was designated; no reserve-error was logged),
not a specific rolled quality. All other tests set quality via
`qb/spawn_finished_building` or `qb/set_comp_state`.

## 7. Full test-case enumeration

Level: **L** = logic (direct tool invocation), **P** = persistence (save+reload),
**I** = real-pawn integration.

### A. Designation & gizmo state

- **A1 (L)** Auto-adopt: with `defaultUseQualityBuilder=true` and a chosen
  `defaultMinQuality`, `qb/spawn_blueprint` → `get_building_state` shows
  `isSkilled=true`, `desiredMinQuality` == default, matching `SkilledBuilder*`
  designation present.
- **A1b (L)** With `defaultUseQualityBuilder=false`, a new blueprint is NOT skilled
  and has no QB designation.
- **A2 (L)** Toggle on: `set_skilled(add=true)` adds the designation and sets
  `isSkilled`; toggle off: `set_skilled(add=false)` removes designation, clears
  `isSkilled`.
- **A3 (L)** Gizmo visibility: `get_gizmo_info` reports `commandOffered=true` for a
  blueprint, a frame, and a finished building below Legendary.
- **A4 (L)** Gizmo hidden at Legendary: a `spawn_finished_building(Legendary)` reports
  `commandOffered=false`.
- **A5 (L)** Float menu on a finished building excludes qualities ≤ its current
  quality; on a blueprint lists the full Awful..Legendary range.
- **A6 (L)** Designation↔quality mapping: for each `QualityCategory` Awful..Legendary,
  `set_skilled(quality)` yields the expected `SkilledBuilder{index}` designation
  (Awful→`SkilledBuilder`, …Legendary→`SkilledBuilder7`).
- **A7 (L)** Forbidden preserved: forbid a blueprint, `set_skilled`, assert still
  forbidden.
- **A8 (L)** Toggle-off cancels a pending QB deconstruct: arrange a finished building
  with `pendingQualityRebuild=true` + a `Deconstruct` designation, `set_skilled(add=false)`,
  assert the deconstruct designation is removed and `pendingQualityRebuild=false`.

### B. Save/load persistence

- **B1 (P)** Comp fields round-trip: set a distinctive comp state on a blueprint,
  `save_game`/`load_save`, assert all five fields match.
- **B2 (P)** Load adopts existing designation: with a `SkilledBuilder3` designation on
  a thing, reload, assert comp `isSkilled=true` and `desiredMinQuality=Good`.
- **B3 (P)** Map settings + `useMapSettings` persist: set a non-default map setting,
  reload, assert it survives and per-map vs global resolution is unchanged.

### C. Skill gating (WorkGiver)

- **C1 (L)** Low-skill denial: `builder_lo` fails `isPawnGoodEnoughToBuild` on a QB
  frame under the configured threshold; `builder_hi` passes.
- **C1b (I)** Real-pawn: only `builder_hi` takes the `FinishFrame` job on a QB frame;
  `builder_lo` does not (job denied).
- **C2 (L)** Forced bypass: with `forced=true` the WorkGiver postfix keeps the job for
  `builder_lo` (asserted via a real forced job or the postfix-level check).
- **C3 (L)** `bestConstructorOverride`: set override to `builder_hi` → only
  `builder_hi` passes; set override to a downed/off-map pawn → the gate ignores it
  (does not block `builder_hi`).
- **C4 (L)** `ignoreQualityBuilderAtSkill`: a pawn at/above the ignore skill always
  passes regardless of best-builder gap.
- **C5 (L)** `skillDifferenceFromBestBuilder`: with a set gap, a pawn below
  (best − gap) is denied, one at/above passes.
- **C6 (I)** Kick: start `builder_lo` on a QB `FinishFrame` job, then have
  `builder_hi` become available/scan; assert `builder_lo` is kicked off the job and a
  player-**forced** `builder_lo` is NOT kicked; re-kick respects the 600-tick cooldown.
- **C7 (L)** `getBestConstructorSkill` set membership: a downed / prisoner / in-mental
  / unspawned high-skill pawn does not raise the best skill; a drafted or mech pawn
  does count.

### D. Construction → quality-check → rebuild cycle

- **D1 (L)** At/above min → no rebuild: `spawn_finished_building(Excellent)`, desire
  `Good`, `invoke_check_rebuild` → `isDesiredMinQualityReached=true`, no Deconstruct
  designation, `qualityRebuildAttempts=0`, `pendingQualityRebuild=false`.
- **D2 (L)** Below min → deconstruct designated: `spawn_finished_building(Awful)`,
  desire `Excellent`, `invoke_check_rebuild` → `pendingQualityRebuild=true`, a
  `Deconstruct` designation is added, `qualityRebuildAttempts=1`.
- **D3 (I)** Full deconstruct→rebuild: build a real frame (forcing a below-min
  quality), let QB designate + a pawn deconstruct; assert a replacement blueprint
  appears at the same cell/rotation/stuff, with carried `desiredMinQuality`,
  incremented/kept `qualityRebuildAttempts`, and preserved ideology style.
- **D4 (L)** Loop-breaker cap: set `maxQualityRebuildAttempts=3` and
  `qualityRebuildAttempts=3` on a below-min building; `invoke_check_rebuild` →
  no new deconstruct, `pendingQualityRebuild=false`, give-up message emitted.
- **D5 (L)** Unlimited sentinel: `maxQualityRebuildAttempts=Unlimited`,
  `qualityRebuildAttempts` large → still designates deconstruct (never gives up).
- **D6 (I)** Player-ordered deconstruct not hijacked: a below-min QB building with a
  `Deconstruct` designation but `pendingQualityRebuild=false`; a pawn completes the
  player-ordered deconstruct → the real `_JobDriver_Deconstruct_FinishedRemoving`
  postfix runs and places **no** replacement blueprint. (Integration because the
  postfix needs a real `JobDriver_Deconstruct` instance; it cannot be invoked
  standalone the way `checkAndDesignateForRebuild`/`afterFinishToil` can.)
- **D7 (L)** Def disambiguation: place a QB wall and a wall-attached QB lamp sharing a
  cell; `invoke_after_finish_toil` for the lamp frame → the lamp (not the wall) is the
  building whose quality is checked.
- **D8 (I)** Reservation release / no error spam: run a deconstruct→rebuild cycle with
  a pawn and assert no `"Could not reserve"` error is logged during the handoff
  (scan `rimbridge/list_logs`).

### E. Config

- **E1 (L)** `maxQualityRebuildAttempts` value (incl. unlimited sentinel) is read back
  correctly and governs D4/D5.
- **E2 (L/P)** Per-map vs global: with `useMapSettings=false`, `get_settings` resolves
  to the global page; with `true`, to the map copy; a change to one does not leak to
  the other.
- **E3 (L)** `defaultMinQualitySetting` is applied to a newly spawned blueprint (ties
  to A1).

Total: 31 cases — L = 23, P = 3, I = 5 (C1b, C6, D3, D6, D8).

## 8. Build & deploy

- Companion DLL: `dotnet build QualityBuilderBridgeTools/Source/QualityBuilderBridgeTools.csproj -c Release`
  → deploys to `RimWorldRoot\BridgeTools\`. First run requires a game restart so
  RimBridge loads it.
- Python: no build; `python -m qbtest` from the package root. Standard library only
  where feasible (sockets, subprocess, json); document any third-party dependency.

## 9. Risks & things to confirm during implementation

1. **QB API binding from the loose companion DLL** — verify cached reflection resolves
   the QB types at runtime; if a direct assembly reference binds cleanly, prefer it.
2. **RimBridge port/token log format** — confirm the exact `Player.log` line;
   otherwise use RimBridge's configured/default port.
3. **`qb/get_gizmo_info` side effects** — enumerating gizmos/float-menus mutates
   `Find.Selector`; always snapshot and restore selection.
4. **Prisoner/mental-state pawn flags** — may not be cleanly settable via a simple
   tool; if so, C7 covers those sub-cases via `set_comp`-style synthesized inputs or
   is documented as partially covered.
5. **`load_save`/`save_game` timing** — asynchronous via `LongEventHandler`; the runner
   must poll readiness, not assume synchronous return.
6. **Integration-test flakiness** — the ~4 real-pawn tests depend on pathing/work
   assignment; give them generous wait-loops and assert outcomes, not timing.

## 10. Deliverable order (for the implementation plan)

1. `QualityBuilderBridgeTools` DLL — read-only tools first (spawn + inspect), then
   mutation tools, then `load/save/clear_arena`.
2. `qb_test` save (hand-made) + `arena.py` coordinates.
3. Python `gabp_client` + `discovery` + `game_control` (prove connect + load + one
   round-trip).
4. `qb.py` wrappers + `runner.py` skeleton with one A-test green end-to-end.
5. Fill in test modules A→E.
6. Crash-restart + JSON reporting hardening.
