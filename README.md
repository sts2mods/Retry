# Retry

I wanted to be able to redo a run from any point without losing what I'd
built up — try a different path at a key floor, retry the elite that
killed me, etc. This mod lets you click any node in your run history
and reload the game with the same seed and your deck, relics, HP, gold,
and potions from that floor restored.

## What it does

- Adds a **View Acts** banner to the Run History page, the game-over /
  win screen, and any run history visible mid-run.
- Click it to open a visual act-map browser. Hover any node to preview
  the inventory you'd reload with.
- Click a node and Confirm to drop into a fresh same-seed run at that
  floor with your historical state.
- Treasure rooms reload with the same relic that was originally
  offered.
- Multiplayer runs open with the right map (player count affects map
  gen). Click the top-left character portrait to swap which player's
  inventory + path you're viewing.
- The in-run timer starts at a sensible offset (proportional to floor
  position, or the full original time if you pick the death node)
  instead of `0:00`.
- Custom abandon prompt with three options: back out, abandon + save
  (writes a normal run-history entry), or abandon without saving
  (skips the history write so iterating doesn't fill your history with
  noise — that one wants a click-again confirmation).
- The new run is flagged Custom so it doesn't end up on the standard /
  daily leaderboards.

## Known limits

- The seed reproduces the map and most encounters, but combat-internal
  RNG (shuffles, crit rolls) can diverge unless the mod recorded a
  snapshot for that floor during the original run. Most of the time
  it's exact; sometimes it's only approximate.
- Saved snapshots are written on the fly while you play with the mod
  installed. Runs played before installing the mod won't have them.
- STS2 disables achievements while any mod is loaded — uninstall if
  you're chasing those.

## Install

### Steam Workshop

Subscribe via the game's Workshop page. Launch the game and enable the
mod from the in-game Mods screen.

### Manual

1. Download the zip from the [Releases page](../../releases).
2. Extract so the folder structure is
   `<game>/mods/Retry/{Retry.dll, mod_manifest.json}`.
   - Mac: `<game>/SlayTheSpire2.app/Contents/MacOS/mods/Retry/`
   - Windows/Linux: `<game>/mods/Retry/`
3. Launch the game and enable Retry on the in-game Mods screen.

## Build from source

Requires .NET 9 SDK and a local copy of Slay the Spire 2.

```
./build.sh
```

The build script compiles `Retry.dll` and copies it + the manifest into
your game's `mods/` folder.

## Companion mods

- [Run Table](https://github.com/sts2mods/RunTable) — searchable table
  of your past runs. Pairs with Retry: find a run in the table, click
  in, click View Acts, replay any floor.
- [Enemy Cycle](https://github.com/sts2mods/EnemyCycle) — see enemy
  move cycles.
- [Timeline](https://github.com/sts2mods/Timeline) — in-combat
  timeline of every event.

## License

MIT.
