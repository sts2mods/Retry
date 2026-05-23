// Force the target room's card reward to match the historical
// 3-card offer the original player saw. Without this, even if the
// retry reaches the right combat with the right encounter, the
// post-combat card reward is rolled fresh from CombatCardGeneration
// RNG → almost certainly different cards from the original.
//
// History (`PlayerMapPointHistoryEntry.CardChoices`) records all
// offered cards plus which one was picked. We extract the offered
// IDs from the target's entry, stash them in RetryContext, and
// intercept the next `CardFactory.CreateForReward` call to return
// our substitute list. The Prefix is one-shot — clears the context
// after firing so subsequent rewards (if any) use vanilla RNG.
//
// Limitations:
//   • Only forces the target room's reward, not rewards from prior
//     floors (those didn't happen — we skipped them).
//   • If a card in history is no longer in ModelDb (modded run), we
//     warn and skip; the reward falls back to whatever RNG produces.
//   • CardCreationOptions still flows through Hook.TryModify... so
//     relic-driven mutations (e.g. "rare cards more likely") still
//     get applied to our substitute list. Probably fine — the
//     player's relics at retry-time match historical relics, so
//     the modifications are the same.
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;

namespace Retry;

[HarmonyPatch(typeof(CardFactory), nameof(CardFactory.CreateForReward),
    typeof(Player), typeof(int), typeof(CardCreationOptions))]
public static class CardFactory_CreateForReward_Patch
{
    static bool Prefix(Player player, int cardCount, ref IEnumerable<CardCreationResult> __result)
    {
        if (!RetryMod.Enabled) return true;
        var forced = RetryContext.TargetCardChoices;
        if (forced == null || forced.Count == 0) return true;
        RetryContext.TargetCardChoices = null; // one-shot

        var list = new List<CardCreationResult>(cardCount);
        foreach (var sc in forced.Take(cardCount))
        {
            if (sc.Id == null) continue;
            var canonical = ModelDb.GetByIdOrNull<CardModel>(sc.Id);
            if (canonical == null)
            {
                GD.PrintErr($"{RetryMod.LogPrefix}force card-reward: id {sc.Id} not in ModelDb — skipping");
                continue;
            }
            // CreateCard mints the per-combat instance (with upgrade
            // level applied). We use the player's CombatState scope
            // so the reward attaches to the active combat.
            try
            {
                var card = player.Creature.CombatState.CreateCard(canonical, player);
                for (int u = 0; u < sc.CurrentUpgradeLevel; u++)
                {
                    try { card.UpgradeInternal(); }
                    catch { break; }
                }
                list.Add(new CardCreationResult(card));
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"{RetryMod.LogPrefix}force card-reward: failed {sc.Id}: {ex.Message}");
            }
        }
        if (list.Count == 0) return true; // give up, let vanilla run
        GD.Print($"{RetryMod.LogPrefix}forced card reward: {string.Join(",", list.Select(r => r.Card?.Id.ToString()))}");
        __result = list;
        return false;
    }
}
