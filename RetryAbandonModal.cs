// Custom 3-option abandon confirmation for retry-mod entrypoints.
// Built by instantiating the game's NAbandonRunConfirmPopup scene
// (so we inherit its layout / styling / EnterTree animation) and
// then duplicating the YesButton to get a third NPopupYesNoButton.
//
// The third option is "Abandon (no save)" — skips writing the
// run_history entry so a flurry of test retries doesn't bloat the
// player's history. It uses an inline two-press confirm flow: first
// click re-labels the button to "Click again", second click within
// a few seconds fires for real. Reverts on timeout or on a click
// elsewhere in the popup.
//
// Why NOT NVerticalPopup direct: that only has YesButton/NoButton
// and there's no clean way to add a third without breaking the
// game's signal wiring on Close. Wrapping with the NAbandonRunConfirmPopup
// scene gives us its TreeExited cleanup for free.
using System;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace Retry;

public static class RetryAbandonModal
{
    public enum Choice { Cancel, AbandonSave, AbandonNoSave }

    public static void Show(
        string title,
        string body,
        Action<Choice> onChoice)
    {
        try
        {
            var modal = NModalContainer.Instance;
            if (modal == null)
            {
                GD.PrintErr($"{RetryMod.LogPrefix}abandon modal: no NModalContainer.Instance — falling back to AbandonSave");
                onChoice(Choice.AbandonSave);
                return;
            }

            // Wrap the game's standard abandon popup so we get its
            // intro animation + modal-clear glue for free, then mutate.
            var popup = NAbandonRunConfirmPopup.Create(mainMenu: null);
            if (popup == null)
            {
                GD.PrintErr($"{RetryMod.LogPrefix}abandon modal: popup Create returned null (TestMode?) — defaulting to AbandonSave");
                onChoice(Choice.AbandonSave);
                return;
            }

            // Wire AFTER the popup's _Ready runs (which resolves
            // _verticalPopup and wires its default Yes/No handlers).
            // TreeEntered would be too early — it fires before _Ready.
            popup.Connect(Node.SignalName.Ready,
                Callable.From(() => InitAfterReady(popup, title, body, onChoice)),
                (uint)GodotObject.ConnectFlags.OneShot);
            modal.Add(popup);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{RetryMod.LogPrefix}abandon modal show: {ex.Message}");
            onChoice(Choice.AbandonSave); // fail safe — keep current behavior
        }
    }

    private static void InitAfterReady(
        NAbandonRunConfirmPopup popup,
        string title,
        string body,
        Action<Choice> onChoice)
    {
        try
        {
            var vp = popup.GetNode<NVerticalPopup>("VerticalPopup");
            vp.SetText(title, body);

            // Disconnect popup's own _Ready-time wiring so our buttons
            // are the only callbacks. NAbandonRunConfirmPopup._Ready
            // already InitYesButton'd with its own handler; re-Init
            // here overwrites the text + adds extra connections.
            vp.DisconnectSignals();

            bool choiceMade = false;
            void Pick(Choice c)
            {
                if (choiceMade) return;
                choiceMade = true;
                onChoice(c);
            }

            // Cancel (No) — left button. Standard "dismiss".
            vp.InitNoButton(new LocString("main_menu_ui", "GENERIC_POPUP.cancel"),
                _ => Pick(Choice.Cancel));

            // Abandon + save (Yes) — center button. Matches existing
            // 2-option flow exactly.
            vp.InitYesButton(new LocString("main_menu_ui", "GENERIC_POPUP.confirm"),
                _ => Pick(Choice.AbandonSave));

            // Add a third "Abandon (no save)" button by duplicating
            // the YesButton. Positioned to the right of YesButton.
            try { AddNoSaveButton(vp, () => Pick(Choice.AbandonNoSave)); }
            catch (Exception ex) { GD.PrintErr($"{RetryMod.LogPrefix}abandon modal third btn: {ex.Message}"); }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{RetryMod.LogPrefix}abandon modal init: {ex.Message}");
        }
    }

    private static void AddNoSaveButton(NVerticalPopup vp, Action onConfirmed)
    {
        var src = vp.YesButton;
        if (src == null || !GodotObject.IsInstanceValid(src)) return;

        // Duplicate with everything-but-signals so the dup keeps its
        // groups + script (= NPopupYesNoButton type) but starts with
        // no signal connections — our Released handler is the only one.
        const int dupFlags = (int)(Node.DuplicateFlags.Groups | Node.DuplicateFlags.Scripts | Node.DuplicateFlags.UseInstantiation);
        var dup = (NPopupYesNoButton)src.Duplicate(dupFlags);
        var parent = src.GetParent();
        parent.AddChild(dup);

        // Place to the right of YesButton with a small gap. Both
        // buttons in NVerticalPopup are anchored, so we anchor the
        // dup the same way and offset.
        dup.Name = "AbandonNoSaveButton";
        dup.IsYes = true;
        dup.SetText("Abandon (no save)");
        dup.Position = src.Position + new Vector2(src.Size.X + 30f, 0f);
        dup.Visible = true;

        bool armed = false;
        void Revert()
        {
            if (!GodotObject.IsInstanceValid(dup)) return;
            armed = false;
            dup.SetText("Abandon (no save)");
        }

        dup.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ =>
        {
            if (!armed)
            {
                armed = true;
                dup.SetText("Click again to confirm");
                var tt = dup.GetTree()?.CreateTimer(3.0);
                tt?.Connect("timeout", Callable.From(Revert));
            }
            else
            {
                onConfirmed();
                NModalContainer.Instance.Clear();
            }
        }));
    }
}
