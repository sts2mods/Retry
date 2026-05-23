// Drop a "View Acts" banner on the death/win screen so the player
// can jump straight into the act-map browser for the run that just
// ended. Mirrors the banner on NRunHistory but tinted gold and
// pinned to the left edge so it doesn't fight the game's existing
// Continue / View Run / Main Menu button column on the right.
//
// Banner click → grab the screen's _history private field (set by
// NGameOverScreen._Ready from RunManager.Instance.History) → open
// NActMapBrowser with it.
using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;

namespace Retry;

[HarmonyPatch(typeof(NGameOverScreen), "_Ready")]
public static class NGameOverScreen_Ready_Patch
{
    private const string ButtonName = "RetryViewActMapsButton";

    private static readonly FieldInfo? HistoryField =
        typeof(NGameOverScreen).GetField("_history",
            BindingFlags.Instance | BindingFlags.NonPublic);

    static void Postfix(NGameOverScreen __instance)
    {
        if (!RetryMod.Enabled) return;
        try
        {
            if (__instance.HasMeta("retry_gameover_button_wired")) return;
            __instance.SetMeta("retry_gameover_button_wired", true);
            AddBanner(__instance);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{RetryMod.LogPrefix}game over banner: {ex.Message}");
        }
    }

    private static void AddBanner(NGameOverScreen screen)
    {
        var captured = screen;
        var root = BannerButton.Create(new BannerButtonOptions
        {
            Name = ButtonName,
            Text = "View Acts",
            // Left side so it doesn't fight the right-anchored button
            // stack (Continue / View Run / Main Menu / Leaderboard).
            Side = BannerSide.Left,
            // Gold-ish — matches the "achievement" tone of a finished
            // run rather than the cool blue of the in-menu browser.
            Tint = new Color(0.88f, 0.66f, 0.18f, 1f),
            Brightness = 2.1f,
            // Pinned above mid-screen — far enough from the top banner
            // ("YOU DIED" / victory text) and the side button column.
            OffsetTop = 360,
            OnClick = () => OpenBrowser(captured),
        });
        screen.AddChild(root);
    }

    private static void OpenBrowser(NGameOverScreen screen)
    {
        try
        {
            // Prefer the run's archived history (written when the run
            // ended). Falls back to the live RunManager.History which
            // NGameOverScreen._Ready also reads.
            var history = HistoryField?.GetValue(screen) as RunHistory
                          ?? RunManager.Instance?.History;
            if (history == null)
            {
                GD.PrintErr($"{RetryMod.LogPrefix}game over banner: no history available");
                return;
            }
            if (history.Players.Count == 0)
            {
                GD.PrintErr($"{RetryMod.LogPrefix}game over banner: history has no players");
                return;
            }
            var player = history.Players[0];
            _ = OpenViaMainMenuAsync(history, player);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{RetryMod.LogPrefix}game over banner OpenBrowser: {ex.Message}");
        }
    }

    // End up in the exact same state as if the player had clicked
    // Main Menu → Compendium → Run History → this run → View Acts,
    // but with the visible transition: game-over → black → browser.
    //
    // Sequence (everything between FadeOut and Open is behind the
    // black NTransition overlay, so no menu flash):
    //   1. Transition.FadeOut(0.2s) — cover the game-over screen
    //   2. RunManager.CleanUp(graceful: false) — drop run state
    //      (so the just-finished run isn't in-progress anymore)
    //   3. SaveManager.DeleteCurrentRun — kill the disk save so
    //      RetryRunner.Begin's hasSave check returns false. Without
    //      this the retry click pops the abandon modal which the
    //      user shouldn't need to see; OnEnded usually deletes the
    //      save but doesn't if the screen popped before that ran.
    //   4. SetCurrentScene(NMainMenu.Create()) — switch scene
    //   5. PushSubmenuType<NRunHistory>() on NMainMenu.SubmenuStack —
    //      its OnSubmenuOpened auto-selects index 0 (latest run = the
    //      just-finished one). This is the same submenu state as
    //      manual navigation, so the browser's back button restores
    //      to it cleanly.
    //   6. NActMapBrowser.Open(history, player) — browser overlay
    //      covers the still-faded screen
    //   7. Transition.FadeIn(0.3s) fire-and-forget — clears the fade
    //      behind the browser so the user lands on a clean menu when
    //      they back out. NMainMenu._Ready also queues its own
    //      FadeIn(3f), but ours wins (kills the slower tween).
    private static async System.Threading.Tasks.Task OpenViaMainMenuAsync(
        RunHistory history,
        RunHistoryPlayer player)
    {
        try
        {
            var ng = MegaCrit.Sts2.Core.Nodes.NGame.Instance;
            if (ng == null)
            {
                GD.PrintErr($"{RetryMod.LogPrefix}game over banner: NGame.Instance null");
                return;
            }

            // 1) Quick fade-out to cover the upcoming scene swap.
            try { await ng.Transition.FadeOut(0.2f); }
            catch (Exception ex) { GD.PrintErr($"{RetryMod.LogPrefix}game over banner FadeOut: {ex.Message}"); }

            // 2) Drop the in-progress run state.
            try { RunManager.Instance?.CleanUp(graceful: false); }
            catch (Exception ex) { GD.PrintErr($"{RetryMod.LogPrefix}game over banner CleanUp: {ex.Message}"); }

            // 3) Make sure no disk save lingers — otherwise
            //    RetryRunner.Begin's hasSave guard pops the abandon
            //    modal on confirm, which is invisible behind our
            //    overlay and looks like "confirm does nothing".
            try { MegaCrit.Sts2.Core.Saves.SaveManager.Instance?.DeleteCurrentRun(); }
            catch (Exception ex) { GD.PrintErr($"{RetryMod.LogPrefix}game over banner DeleteCurrentRun: {ex.Message}"); }

            // 4) Switch the scene to NMainMenu.
            try
            {
                var menuScene = MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenu.Create(openTimeline: false);
                ng.RootSceneContainer?.SetCurrentScene(menuScene);
            }
            catch (Exception ex) { GD.PrintErr($"{RetryMod.LogPrefix}game over banner SetCurrentScene: {ex.Message}"); }

            // Give NMainMenu's _Ready a tick to wire itself up.
            await ng.ToSignal(ng.GetTree(), Godot.SceneTree.SignalName.ProcessFrame);

            // 5) Push NRunHistory; its OnSubmenuOpened auto-selects
            //    the most recent (just-finished) run.
            if (ng.RootSceneContainer?.CurrentScene
                is MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenu menu
                && menu.SubmenuStack != null)
            {
                try { menu.SubmenuStack.PushSubmenuType<MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen.NRunHistory>(); }
                catch (Exception ex) { GD.PrintErr($"{RetryMod.LogPrefix}game over banner submenu push: {ex.Message}"); }
            }
            await ng.ToSignal(ng.GetTree(), Godot.SceneTree.SignalName.ProcessFrame);

            // 6) Browser on top of still-faded screen — user only
            //    sees the browser.
            GD.Print($"{RetryMod.LogPrefix}game over banner: opening browser for seed={history.Seed}");
            NActMapBrowser.Open(history, player);

            // 7) Drop the fade behind the browser (fire-and-forget).
            //    When the user backs out the menu is already clear.
            _ = ng.Transition.FadeIn(0.3f);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{RetryMod.LogPrefix}game over banner OpenViaMainMenuAsync: {ex.Message}\n{ex.StackTrace}");
        }
    }
}
