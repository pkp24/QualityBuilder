# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

QualityBuilder is a RimWorld C# mod (package id `hatti.qualitybuilder`) that lets players set a minimum quality for constructed buildings. Active development target is **RimWorld 1.6**; versions 1.0–1.6 are supported.

## Build & deploy

- Source lives in `_PROJECT/`. `_PROJECT/QualityBuilder.csproj` is an **SDK-style** project. Build with: `dotnet build _PROJECT/QualityBuilder.csproj -c Release`
- Target framework is **.NET Framework 4.7.2** (`net472`), not modern .NET. Output type is a class library (DLL).
- RimWorld/Unity assemblies come from the **`Krafs.Rimworld.Ref`** NuGet package and Harmony from **`Lib.Harmony`** (both compile-only — `CopyLocalLockFileAssemblies=false` and Harmony's `ExcludeAssets=runtime` keep them out of the output). No hard-coded DLL paths; the project restores on any machine. RimWorld and Harmony are provided at runtime by the game/Harmony mod.
- Release output is `TargetAssemblies/QualityBuilder.dll` (repo root, gitignored) — only the mod DLL is emitted. There is **no post-build copy step**.
- The game loads version-specific DLLs from `Versions/v1.x/Assemblies/` per `LoadFolders.xml`. After building, the DLL must be copied from `TargetAssemblies/` into the right `Versions/v1.x/Assemblies/` folder (these committed DLLs are what ships). Run `/build` to compile and deploy to `Versions/v1.6/` in one step.

## Architecture

- Harmony is a hard dependency (`brrainz.harmony`); the mod must load after it. Patching is done via `[HarmonyPatch(...)]` attribute classes.
- Defs cannot be modified before the game loads, so the mod **injects at runtime** (`QualityBuilderStartup`): a `[StaticConstructorOnStartup]` class adds `CompQualityBuilder` to every building with `CompQuality` (plus its blueprint/frame defs) once after def load, and a Harmony postfix on `DesignationCategoryDef.ResolveDesignators` appends the skilled/unskilled designators to the Orders category on every resolve (the game clears and rebuilds that list via `DirtyCache`, so one-time injection would be lost). Be aware of this when something seems "missing" from the defs — it's added in code.
- Core construction logic is in the Harmony patches under `_PROJECT/QualityBuilder/` (`_JobDriver_*`, `_Toils_Construct`, `_WorkGiver_*`): a finished frame is checked against the desired quality and deconstructed/rebuilt if it falls short, preserving ideology style (`CompStyleable`) settings.
- Version differences (e.g. pre-1.6 vs 1.6 ideology style APIs) are handled with inline fallback code paths, not separate source trees. Keep new code version-tolerant.
- Shared helpers live in the static `QualityBuilder` class; prefer adding to it over duplicating logic.

## Code style

- All source is in `namespace QualityBuilder`.
- **Tabs** for indentation. PascalCase types/methods, camelCase locals.
- Harmony patch classes are prefixed with an underscore: `_Designator_`, `_JobDriver_`, `_Toils_`, `_WorkGiver_`.
- Wrap risky reflection/patch code in try-catch (e.g. optional Replace Stuff compatibility is patched only if that mod is present). Null-check defensively.
- Debug-only logging goes behind `#if DEBUG`.

## Known issues & investigation notes

### "Could not reserve Thing_Frame_… for … HaulToContainer … maxPawns 5 and stackCount 1" (red error during construction)

Rare, **frame-precise / not reliably reproducible**. Stack trace runs `_Toils_Construct.Postfix` → `vanillaInit` → `Toils_Construct.MakeSolidThingFromBlueprintIfNecessary` → `ReservationUtility.Reserve` → `LogCouldNotReserveError`. Below is a best-guess analysis (the bug could not be isolated by toggling mods); treat the fixes as hypotheses to trial, where "success" = the error never recurs in a long construction-heavy session.

**What the error actually is (confirmed by decompiling the shipped `Assembly-CSharp.dll`):**
- The failing call is **vanilla**, not QB. `Toils_Construct.MakeSolidThingFromBlueprintIfNecessary` does `actor.Reserve(createdThing, curJob, 5, 1)` right after a blueprint becomes a `Frame`. The `maxPawns 5 / stackCount 1` in the log matches this line exactly.
- QB's `_Toils_Construct.Postfix` only **wraps** this toil via `vanillaInit?.Invoke()`. It never calls `Reserve` and never changes its arguments — QB appears in the stack purely as the wrapping delegate.
- The reserve uses the default `errorOnFailed: true`, and the vanilla toil **ignores the bool return**, so the frame still builds. **This is benign log spam, not a functional failure.**
- The conflict: the **same pawn** already holds a `FinishFrame` reservation on that frame (`maxPawns 1, stackCount -1`) from an *earlier* job, while the *current* delivery job (`ConstructDeliverResourcesToBlueprints`) runs the `maxPawns 5` reserve. maxPawns 1 vs 5 are incompatible → logged.

**Best-guess root cause (ranked):**
1. A `FinishFrame` reservation from the **original** build of a building is not released when that job ends; then **QB's own deconstruct→rebuild cycle** (it deconstructs a too-low-quality finished building and places a replacement blueprint at the same cell) brings the same pawn back to deliver resources there. When the replacement blueprint becomes a frame, vanilla's `maxPawns 5` reserve collides with the stale `maxPawns 1` reservation. The non-release of the old reservation is the real defect.
2. `_WorkGiver_ConstructFinishFrames.kickUnqualifiedBuilders` force-ends another pawn's FinishFrame job via `EndCurrentJob(InterruptForced, true, true)`. If that path leaves a frame reservation behind, it can collide later.

**Ruled out — PartialReservationSystem.** It was initially suspected (loaded in the same save, patches `ReservationManager.Reserve`/`CanReserve`/`Release`/`EndCurrentJob`). Reading the PRS source disproves it: `Reserve_Prefix` and `CanReserve_Postfix` both bail unless the target is a *storage destination* (`IsStorageDestination` → `ISlotGroupParent`/`IHaulDestination`/slot-group cell). A construction `Frame`/`Blueprint` is none of those, so PRS never touches it. `Reserve_Prefix` also always `return true` (never sets `__result`/skips original), so vanilla reservation logic is unmodified for all targets, and `EndCurrentJob_Postfix` only cleans PRS's own ledger, not vanilla's `ReservationManager`. **This is a vanilla + QB interaction; the fix belongs in QB.**

**Candidate fixes (try one at a time, best-first):**
1. **Release lingering reservations at the deconstruct→rebuild handoff** (most targeted). In `_JobDriver_Deconstruct_FinishedRemoving.Postfix`, and in `_JobDriver_ConstructFinishFrame.afterFinishToil` at the point it adds the `Deconstruct` designation, clear reservations on the old frame/building/cell before the replacement blueprint is placed — e.g. `map.reservationManager.ReleaseAllForTarget(oldThing)` (`ReleaseAllForTarget` exists in 1.6; PRS already patches it). Stops a stale reservation from surviving into the rebuilt frame.
2. **Make the kick release reservations explicitly.** In `kickUnqualifiedBuilders`, after `EndCurrentJob`, call `frame.Map.reservationManager.ReleaseAllForTarget(frame)`. FinishFrame is `maxPawns 1`, so releasing the whole target is safe.
3. **Defensive release in the toil wrapper.** In `_Toils_Construct.Postfix`, before `vanillaInit?.Invoke()`, release `actor`'s existing reservations at `blueprint.Position` so the upcoming vanilla `maxPawns 5` reserve cannot collide. More invasive (touches the hot delivery path) — use only if 1–2 don't help.
4. **Cosmetic suppression (last resort).** Transpile `Toils_Construct.MakeSolidThingFromBlueprintIfNecessary` so the `Reserve` call passes `errorOnFailed: false`. Silences the red log without changing reservation logic, but hides any genuine problem and alters vanilla behavior for every build.

**Status:** Fix #1 implemented, then **consolidated during a code review** to a single release point. The deconstruct→rebuild reservation release now happens only in `_JobDriver_Deconstruct_FinishedRemoving`, right before the replacement blueprint is placed (`reservationManager.ReleaseAllForTarget(oldThing)` + helper `QualityBuilder.releaseConstructionReservations(cell)`). The earlier scattered releases were removed: the `ReleaseClaimedBy` in `_JobDriver_ConstructFinishFrame`'s finish action (redundant — vanilla job cleanup already releases it) and the releases in `afterFinishToil` (premature — nothing is rebuilt at the deconstruct-designation point). Candidate fix #2 is unnecessary: decompiling confirms `EndCurrentJob` already releases the kicked pawn's reservations via `CleanupCurrentJob(releaseReservations: true)`. Separately, `kickUnqualifiedBuilders` was hardened (snapshots `PawnsInFaction` before iterating, only fires for actual `FinishFrame` jobs, and uses `startNewJob: false` to avoid a re-entrant job search). Compiles and deployed; still awaiting in-game confirmation that the red error no longer recurs.
