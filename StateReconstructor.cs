// Walks a RunHistory's per-floor entries up to the target node and
// folds the recorded deltas into a PlayerStateSnapshot. The result
// represents the player's state at the *start* of the target room
// (i.e. after the previous room's events resolved).
//
// Data sources per PlayerMapPointHistoryEntry:
//   • CardsGained / CardsRemoved      → deck mutations
//   • UpgradedCards / DowngradedCards → upgrade-level shifts
//   • CardsEnchanted                  → enchantment attached
//   • CardsTransformed                → in-place swap of card id
//   • RelicChoices (wasPicked)         → picked relic adds
//   • BoughtRelics                    → shop-bought relics
//   • RelicsRemoved                   → relic removals
//   • PotionChoices (wasPicked)        → picked potion adds
//   • BoughtPotions                   → shop-bought potions
//   • PotionDiscarded / PotionUsed    → potion removals
//   • CompletedQuests                 → quest carry-over
//
// Final HP / MaxHp / Gold are read from the entry just before the
// target rather than computed from deltas — the recorded numbers
// are authoritative and avoid drift from rounding edge cases.
using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Retry;

public static class StateReconstructor
{
    public static PlayerStateSnapshot ReconstructAtTarget(
        RunHistory history,
        int targetActIndex,
        int targetFloorIndex,
        RunHistoryPlayer player)
    {
        var snapshot = new PlayerStateSnapshot
        {
            NetId = player.Id,
            CharacterId = player.Character,
            MaxPotionSlotCount = player.MaxPotionSlotCount,
        };

        SeedFromCharacter(snapshot, player.Character);

        int globalFloor = 0;
        PlayerMapPointHistoryEntry? lastApplied = null;
        for (int act = 0; act < history.MapPointHistory.Count; act++)
        {
            var actEntries = history.MapPointHistory[act];
            int endFloor = (act == targetActIndex) ? targetFloorIndex : actEntries.Count;
            if (act > targetActIndex) break;
            for (int floor = 0; floor < endFloor; floor++)
            {
                globalFloor++;
                PlayerMapPointHistoryEntry entry;
                try { entry = actEntries[floor].GetEntry(player.Id); }
                catch { continue; }
                ApplyDelta(snapshot, entry, globalFloor);
                lastApplied = entry;
            }
        }

        // Override scalar fields from the last applied entry so HP /
        // gold are exactly what the recorded run reports rather than
        // a computed approximation.
        if (lastApplied != null)
        {
            snapshot.CurrentHp = lastApplied.CurrentHp;
            snapshot.MaxHp = lastApplied.MaxHp;
            snapshot.Gold = lastApplied.CurrentGold;
        }

        return snapshot;
    }

    private static void SeedFromCharacter(PlayerStateSnapshot s, ModelId characterId)
    {
        var character = ModelDb.GetByIdOrNull<CharacterModel>(characterId);
        if (character == null)
        {
            // Modded / removed character — leave the snapshot defaulted
            // and let the caller surface a helpful error.
            return;
        }
        s.CurrentHp = character.StartingHp;
        s.MaxHp = character.StartingHp;
        s.Gold = character.StartingGold;

        foreach (var card in character.StartingDeck)
        {
            s.Deck.Add(new SerializableCard
            {
                Id = card.Id,
                CurrentUpgradeLevel = 0,
                FloorAddedToDeck = 0,
            });
        }
        foreach (var relic in character.StartingRelics)
        {
            s.Relics.Add(new SerializableRelic
            {
                Id = relic.Id,
                FloorAddedToDeck = 0,
            });
        }
        int slot = 0;
        foreach (var potion in character.StartingPotions)
        {
            s.Potions.Add(new SerializablePotion { Id = potion.Id, SlotIndex = slot++ });
        }
    }

    private static void ApplyDelta(PlayerStateSnapshot s, PlayerMapPointHistoryEntry e, int floorNum)
    {
        // ----- Cards -----
        foreach (var c in e.CardsGained)
        {
            s.Deck.Add(CloneCard(c, floorNum));
        }
        foreach (var c in e.CardsRemoved)
        {
            int idx = FindMatching(s.Deck, c);
            if (idx >= 0) s.Deck.RemoveAt(idx);
        }
        foreach (var id in e.UpgradedCards)
        {
            var card = FindFirstById(s.Deck, id);
            if (card != null) card.CurrentUpgradeLevel++;
        }
        foreach (var id in e.DowngradedCards)
        {
            var card = FindFirstById(s.Deck, id);
            if (card != null) card.CurrentUpgradeLevel = Math.Max(0, card.CurrentUpgradeLevel - 1);
        }
        foreach (var ench in e.CardsEnchanted)
        {
            if (ench.Card.Id == null) continue;
            var card = FindFirstById(s.Deck, ench.Card.Id);
            if (card == null) continue;
            // Prefer the embedded post-enchant SerializableEnchantment
            // from ench.Card.Enchantment — it carries Amount (and any
            // other future fields) which the bare `ench.Enchantment`
            // id doesn't. Constructing a fresh SerializableEnchantment
            // with only Id loses the block/damage value, which is why
            // retried enchanted cards showed "Gain 0 Block" etc.
            card.Enchantment = ench.Card.Enchantment
                ?? new SerializableEnchantment { Id = ench.Enchantment };
        }
        foreach (var trans in e.CardsTransformed)
        {
            int idx = FindMatching(s.Deck, trans.OriginalCard);
            if (idx >= 0)
            {
                s.Deck[idx] = CloneCard(trans.FinalCard, floorNum);
            }
        }

        // ----- Relics -----
        // RelicChoices already covers shop purchases (every bought
        // relic shows up there with wasPicked=true) — iterating
        // BoughtRelics on top of that produced two copies of every
        // shop-bought relic. Verified across 20 SP runs: bought_relics
        // is always a strict subset of relic_choices[wasPicked].
        foreach (var pick in e.RelicChoices)
        {
            if (!pick.wasPicked) continue;
            s.Relics.Add(new SerializableRelic { Id = pick.choice, FloorAddedToDeck = floorNum });
        }
        foreach (var id in e.RelicsRemoved)
        {
            int idx = s.Relics.FindIndex(r => r.Id != null && r.Id.Equals(id));
            if (idx >= 0) s.Relics.RemoveAt(idx);
        }

        // ----- Potions -----
        // Same shape as relics — bought_potions is redundant with
        // potion_choices[wasPicked].
        foreach (var pick in e.PotionChoices)
        {
            if (!pick.wasPicked) continue;
            AddPotion(s, pick.choice);
        }
        foreach (var id in e.PotionDiscarded) RemovePotion(s, id);
        foreach (var id in e.PotionUsed) RemovePotion(s, id);

        // ----- Bought colorless cards (shop) -----
        // BoughtColorless is redundant with CardsGained for the same
        // shop floor — the CardsGained pass at the top already added
        // the purchase, so we deliberately don't iterate it here.

        // ----- Completed quests -----
        foreach (var id in e.CompletedQuests)
        {
            if (!s.CompletedQuests.Contains(id)) s.CompletedQuests.Add(id);
        }
    }

    // SerializableCard equality includes Id + CurrentUpgradeLevel +
    // Enchantment — that's how the game distinguishes "remove the
    // upgraded Strike but keep the base Strike". Find the first
    // matching deck card.
    private static int FindMatching(List<SerializableCard> deck, SerializableCard target)
    {
        for (int i = 0; i < deck.Count; i++)
        {
            if (deck[i].Equals(target)) return i;
        }
        // Fall back to id-only if no exact match (the historical card
        // may have been recorded with stale upgrade info).
        for (int i = 0; i < deck.Count; i++)
        {
            if (CardIdEquals(deck[i].Id, target.Id)) return i;
        }
        return -1;
    }

    private static SerializableCard? FindFirstById(List<SerializableCard> deck, ModelId id)
    {
        foreach (var c in deck)
            if (CardIdEquals(c.Id, id)) return c;
        return null;
    }

    private static bool CardIdEquals(ModelId? a, ModelId? b) =>
        a != null && b != null && a.Equals(b);

    private static SerializableCard CloneCard(SerializableCard src, int floorNum) => new()
    {
        Id = src.Id,
        CurrentUpgradeLevel = src.CurrentUpgradeLevel,
        Enchantment = src.Enchantment,
        Props = src.Props,
        FloorAddedToDeck = floorNum,
    };

    private static void AddPotion(PlayerStateSnapshot s, ModelId id)
    {
        // Find first free slot 0..MaxPotionSlotCount-1.
        var occupied = new HashSet<int>(s.Potions.Select(p => p.SlotIndex));
        for (int slot = 0; slot < s.MaxPotionSlotCount; slot++)
        {
            if (occupied.Contains(slot)) continue;
            s.Potions.Add(new SerializablePotion { Id = id, SlotIndex = slot });
            return;
        }
        // All slots full — drop. The game would have refused this in
        // the original run, so historical state shouldn't reach here.
    }

    private static void RemovePotion(PlayerStateSnapshot s, ModelId id)
    {
        int idx = s.Potions.FindIndex(p => p.Id != null && p.Id.Equals(id));
        if (idx >= 0) s.Potions.RemoveAt(idx);
    }
}
