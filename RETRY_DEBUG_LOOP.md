# Retry Mod Debug Loop — Self-Continuation Protocol

You are debugging the Retry mod (sts2mod/mod/retry). The user's complaint: clicking a node in the run-history view loads a retry that **doesn't reproduce the original map / encounters**. The user has asked you to iterate autonomously until it works.

If you wake to this file, **continue the loop** — do not ask the user what to do.

---

## Target Run

- **File**: `/Users/austin/Library/Application Support/SlayTheSpire2/steam/76561198066616066/modded/profile2/saves/history/1779070640.run`
- **Seed**: `TBESFXYS6G`
- **Char**: `CHARACTER.IRONCLAD`
- **Mode**: `standard`
- **Acts**: `OVERGROWTH, HIVE, GLORY`
- **17 floors** in act 0 (Overgrowth)
- **Test target**: act 0, floor 5 (Monster after the Event at floor 4). Vary if useful.

## Test Harness

`AutoTestHarness.cs` reads `~/Library/Application Support/SlayTheSpire2/retry_test_target.json` on main menu load. If `enabled:true`, it loads the named run-history file and calls `RetryRunner.Begin` with the target. **No clicking needed.**

Config example (already written):
```json
{
  "enabled": true,
  "run_file_basename": "1779070640.run",
  "act": 0,
  "floor": 5
}
```

Restart the game → harness auto-triggers retry. Logs land in `~/Library/Application Support/SlayTheSpire2/logs/godot.log`. All retry logs prefixed `[Retry]`.

## Loop Cycle

1. **Form a hypothesis** about what's wrong. Don't change blindly.
2. **Read logs** from the last run: `grep "\[Retry\]" ~/Library/Application Support/SlayTheSpire2/logs/godot.log | tail -50`
3. **Inspect comparison data**:
   - Generated map: `~/Library/Application Support/SlayTheSpire2/retry_debug_map.json`
   - Original path (from history file): types per floor
   - Walk decisions
4. **Change ONE thing** in the mod. Always include a `GD.Print($"{RetryMod.LogPrefix}...")` showing what you observed.
5. **Build**: `cd /Users/austin/Documents/GitHub/sts2mod/mod/retry && ./build.sh 2>&1 | tail -10`
6. **Restart game**: `PID=$(ps aux | grep -i "Slay the Spire 2" | grep -v grep | awk '{print $2}'); [ -n "$PID" ] && kill "$PID"; sleep 1; open -a "Slay the Spire 2"; sleep 6` (6s gives the harness time to trigger)
7. **Read logs again** — confirm hypothesis or refute.
8. **Loop**.

## Known State (update this section as you learn)

- Map RNG uses `seed_uint = StringHelper.GetDeterministicHashCode(stringSeed)` + per-act key `act_N_map`. Same seed → same map.
- Snapshots already exist for this seed (`retry_rng_snapshots.json` has many entries keyed by `TBESFXYS6G|0|row,col`). `TryApplyLive` fast-forwards live RNG.
- Queue counters advanced via `RoomQueueSimulator.SimulatePriorRooms`.
- `MapPointHistory` doesn't store coords — only types. The walk has to infer the column.
- `SkipNeowEntryPatch` suppresses the first auto-EnterMapCoord on StartingMapPoint so Neow doesn't re-prompt.
- The user reports the rendered map LAYOUT doesn't match the original run. This is the live bug.

## Hypotheses To Investigate (refute or confirm)

1. The seed string we read from history is corrupted somewhere.
2. `runState.Rng.Seed` (uint) differs between original and retry despite same string.
3. Map gen uses some other RNG-affecting input we're missing.
4. The retry's running with different `Players.Count` (multiplayer flag flip).
5. The replayed RngSnapshotCapture from a prior retry corrupted snapshots — the snapshot now reflects a retry's wrong RNG state, not the original.

## Heartbeat

Use `ScheduleWakeup` after every build+restart cycle with delaySeconds≥900 (cache friendly). Pass `<<autonomous-loop-dynamic>>` if no user prompt. The wakeup message should re-read this file and continue the loop.

## Stop Conditions

Stop iterating if:
- The user says to stop.
- You've made 20+ iterations without a single confirmed-by-evidence improvement (escalate).
- A code change you can't justify with a log observation.

## Status Log (append entries)

Format: `YYYY-MM-DDTHH:MM | hypothesis | change | observation`

- 2026-05-19T05:06 | TruncateHistory excluded target so walk landed at floor-1 | TruncateHistory takes target+1; queue sim and history-prepop slice strictly before target | Auto-test for `1779070640.run` act 0 floor 5 → landed in combat vs VINE_SHAMBLER_NORMAL (matches history). Queue sim log: SHRINKER_BEETLE_WEAK / FUZZY_WURM_CRAWLER_WEAK / SLIMES_WEAK / WELLSPRING (event) / next-monster=VINE_SHAMBLER_NORMAL — all match history.
- 2026-05-19T05:06 | v1 snapshot file is corrupted with retry-walk pollution | Skip `RngSnapshotCapture` while `RetryContext.IsRetrying`; rewrote store to v2 schema with floor-keyed entries and (row,col) inline; backed up old file as `retry_rng_snapshots.v1.bak.json`. | Snapshot=miss on retry (expected — needs a fresh playthrough to repopulate v2 store).
- 2026-05-19T05:10 | Event queue ordering differs because ActModel.GenerateRooms filters events by current UnlockState | `EventListPatcher.AlignToHistory` reorders `_rooms.events` so historical event IDs appear at the front in original order, before queue sim runs. | Floor 10 retry → "Byrdonis Nest" event, exactly matching `EVENT.BYRDONIS_NEST` from history. Previously got WELLSPRING.
- 2026-05-19T05:15 | Greedy walk picked leftmost-matching child → couldn't reach historical type sequence beyond a few floors | `MapPathSearch.FindPath` DFS over the map with type constraints; falls back to greedy if no exact-type path found. | Floor 12 walk: DFS found path (0,3)→(1,5)→(2,6)→…→(12,5,Unknown). Without DFS the greedy walk shifted off-track at row 5 and ended at (12,0) Elite.
- 2026-05-19T05:19 | Unknown target node rolled via Odds.UnknownMapPoint; live odds state ≠ original so different concrete RoomType | `RoomTypeRollPatch` Prefix on `RunManager.RollRoomTypeFor` substitutes `RetryContext.TargetExpectedRoomType` (set from history entry's `Rooms[0].RoomType`) when pointType is Unknown during retry. | Floor 12 retry → fought FOGMOG (matches `ENCOUNTER.FOGMOG_NORMAL`). Previously got WELLSPRING event.
- 2026-05-19T05:21 | Verified cross-act retries | Tested act 1 floor 5 (Lost Wisp event, correct) + act 2 floor 5 (Frog Knight, correct) + act 2 floor 14 (Queen Boss, correct). All major mechanisms (map walk via DFS, event queue, encounter queue, Unknown room override, cross-act state) are working end-to-end.
- 2026-05-19T05:33 | Suspected reconstruction bug at boss floor 16 (HP 80 vs expected 55) | Investigated — original run's relic list includes PANTOGRAPH, whose Pantograph.cs uses `new HealVar(25m)` to heal 25 HP at boss combat start. 55+25=80 matches in-game. No bug. | The retry IS faithful to the original gameplay rule.
- 2026-05-19T05:34 | Add fidelity report + drift warnings | `FidelityReport` emits per-retry log line; `RoomQueueSimulator.VerifyMatch` warns when simulated encounter ID ≠ historical ID; `InventoryInjector` warns on missing relic/card IDs (mod removed). | Floor 16 retry log: `fidelity-verdict: APPROXIMATE — map/encounter match, but combat RNG diverges`. Zero drift warnings on 16 simulated floors.
- 2026-05-19T05:51 | `SkipNextNeowEntry` flag persisted past EnterAct(0); when target was the act > 0 StartingMapPoint (Ancient at floor 0 of act 1+), our explicit `EnterMapCoord(target)` got suppressed by the same patch → player stuck on map screen. | Clear `SkipNextNeowEntry` right after `StartNewSingleplayerRun` returns — by then EnterAct(0) has already run, so any later `EnterMapCoord(StartingMapPoint)` is ours and should pass through. | Act 1 floor 0 (TEZCATARA Ancient) retry → fired correctly; "Welcome back, sweetie..." event shown. Re-verified act 0 floor 5 still works (Vine Shambler).
- 2026-05-19T05:53 | DumpVisited diagnostic | New `DebugDump.DumpVisited` writes `retry_debug_visited.json` with the live `runState.VisitedMapCoords` after retry. | Shop test (act 1 floor 2) → visited = `[(0,3), (1,6), (2,6)]` — confirms path painting matches DFS path output.
- 2026-05-19T06:13 | Post-combat card reward rolled fresh from CombatCardGeneration RNG → cards different from what original player saw, even when seed is the same | `CardRewardForce` Prefix on `CardFactory.CreateForReward` consumes `RetryContext.TargetCardChoices` (list of `SerializableCard` extracted from target's `PlayerStats[0].CardChoices`) and returns a substitute `CardCreationResult` list once. One-shot — clears itself. | Floor 1 (Shrinker Beetle) retry log: `prime card-reward: CARD.CINDER,CARD.BLOOD_WALL,CARD.FIGHT_ME` — exactly matches the historical 3-card offer. Won't visually verify until end-of-combat reward screen but code path is wired.
- 2026-05-19T06:45 | Relic reward rolled from RelicGrabBag → different relic than original on treasure/elite/boss | `RelicRewardForce` Prefix on `RelicGrabBag.PullFromFront(rarity, filter, runState)` — this is the lowest-level call point that BOTH `RelicFactory.PullNextRelicFromFront` (used by `RelicReward.Populate` for elite/boss) AND `TreasureRoomRelicSynchronizer.BeginRelicPicking` (which calls `_sharedGrabBag.PullFromFront` directly) route through. One-shot via `RetryContext.TargetExpectedRelic`. | Floor 9 (treasure) retry log: `prime relic-reward: RELIC.PANTOGRAPH` then `forced relic: RELIC.PANTOGRAPH` — Prefix fires and substitutes. Earlier attempt patching `RelicFactory.PullNextRelicFromFront` directly didn't catch treasure rooms because the synchronizer bypasses the factory.
- 2026-05-19T07:08 | User reports: relics don't display in HUD, deck count badge stuck at 10 | (1) Deck operations were `silent: true` → no `CardAdded`/`CardRemoved` events → HUD badge stays at character-default count. Changed to `silent: false` with explicit `RemoveInternal` for old cards. (2) `NRelicInventory.Initialize` reads `player.Relics` at `NRun._Ready` time — race with our injection means events fire before subscribers wire. New `HudResync` rebuilds the relic visuals AFTER `EnterMapCoord` (when NRun.Instance is alive) by clearing `NRelicInventory._relicNodes` via reflection then calling `Initialize` again. Also re-inits `NTopBarDeckButton` for the deck-count badge. | Floor 12 (Unknown→FOGMOG) retry: HUD shows all 5 relics + deck count 18 (was 10). `inject summary: relics=5 deck=18 potions=1` matches expectations.
- 2026-05-19T07:31 | User reports: current-node arrow stuck at Ancient on the map; walked path lines aren't darkened | `NMapScreen.SetMap` darkens visited path lines (`item.Modulate = MapTraveledColor`) but that loop runs ONCE during `RunManager.GenerateMap`, before our path-painting adds VisitedMapCoords. The marker (`_marker.SetMapPoint`) was last touched by `EnterAct(0)`'s pre-Neow `InitMarker(StartingMapPoint)` call. Extended `HudResync.Apply` to re-call `NMapScreen.SetMap(map, seed, false)` (re-runs the path-darken loop with the now-populated VisitedMapCoords) and then `InitMarker(visited.Last())` to put the arrow on the target. | Floor 8 retry: map view shows darkened dotted line tracing (0,3)→(1,?)→…→(8,5) and red arrow indicator sits on (8,5).

## Remaining Issues / Known Gaps

- **Combat RNG parity** — `snapshot=miss` on every test (v1 file backed up; need a fresh playthrough to repopulate v2 store). Once player plays even one combat with the new mod, snapshots get captured and future retries to those nodes will match opening hands.
- **~~HP at boss floor 16~~** — RESOLVED: was Pantograph's +25 boss-start heal (`new HealVar(25m)`), not a reconstruction bug.
- **Card rewards / treasure contents** — driven by CombatCardSelection / TreasureRoomRelics counters. Only correct when snapshot is applied (i.e. after v2 data exists).
- **~~Modded content drift~~** — RESOLVED: now warns via `DRIFT floor N` for encounter mismatches and `DRIFT relic/card X not in ModelDb` for missing IDs.
- **Stateful relic props lost** — `RelicChoices` in `RunHistory` only stores the relic ID, not `Props` (e.g. `GoldenCompass.GoldenPathAct`). Stateful relics get default state on injection, so e.g. a GoldenPath-modified map wouldn't reproduce. Out of scope for this iteration; would need a parallel capture of per-relic state during play.

---
