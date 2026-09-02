using System;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace CrossStitchRework;

[HarmonyPatch(typeof(PlayMakerFSM), "Awake")]
internal static class SilkSpecialsFsmPatch
{
    private static void Postfix(PlayMakerFSM __instance)
    {
        if (!string.Equals(__instance.FsmName, "Silk Specials", StringComparison.Ordinal))
        {
            return;
        }

        FsmState counterReady = __instance.Fsm.GetState("Change Facing?");
        FsmState parryClash = __instance.Fsm.GetState("Parry Clash");
        if (counterReady == null || parryClash == null)
        {
            Plugin.Log.LogError("Could not find the Cross Stitch states required for the rework.");
            return;
        }

        FsmStateAction[] clashActions = parryClash.Actions;
        int clashAnimationIndex = Array.FindIndex(
            clashActions,
            action => action is Tk2dPlayAnimationWithEvents or SuccessfulGuardPoseAction);
        if (clashAnimationIndex < 0)
        {
            Plugin.Log.LogError("Could not find the Cross Stitch success animation action; no FSM changes were applied.");
            return;
        }

        if (clashActions[clashAnimationIndex] is not SuccessfulGuardPoseAction)
        {
            clashActions[clashAnimationIndex] = new SuccessfulGuardPoseAction();
            parryClash.Actions = clashActions;
        }

        if (counterReady.Actions.Length != 1 || counterReady.Actions[0] is not CounterReadyBlocker)
        {
            counterReady.Actions = new FsmStateAction[] { new CounterReadyBlocker() };
            counterReady.Transitions = Array.Empty<FsmTransition>();
        }

        Plugin.Log.LogInfo("Installed the stable success pose and manual Cross Stitch counter-ready state.");
    }
}

internal sealed class CounterReadyBlocker : FsmStateAction
{
    public override void OnEnter()
    {
        // The runtime controller owns this state and deliberately leaves this action unfinished.
    }
}

internal sealed class SuccessfulGuardPoseAction : FsmStateAction
{
    private const float PoseDuration = 0.15f;

    private float elapsed;

    public override void OnEnter()
    {
        elapsed = 0f;
        HeroController hero = HeroController.instance;
        if (hero == null)
        {
            Finish();
            return;
        }

        string clipName = hero.cState.onGround ? "Parry Stance Ground" : "Parry Stance";
        tk2dSpriteAnimationClip clip = hero.AnimCtrl.animator.GetClipByName(clipName);
        if (clip == null || clip.frames.Length == 0)
        {
            Finish();
            return;
        }

        hero.AnimCtrl.animator.PlayFromFrame(clip, clip.frames.Length - 1);
    }

    public override void OnUpdate()
    {
        elapsed += Time.deltaTime;
        if (elapsed >= PoseDuration)
        {
            Finish();
        }
    }
}

[HarmonyPatch(typeof(FsmState), nameof(FsmState.OnEnter))]
internal static class SuccessfulGuardEntryPatch
{
    private static void Postfix(FsmState __instance)
    {
        Fsm? fsm = __instance.Fsm;
        if (fsm != null && string.Equals(fsm.Name, "Silk Specials", StringComparison.Ordinal))
        {
            if (string.Equals(__instance.Name, "Parry Clash", StringComparison.Ordinal))
            {
                Plugin.Instance?.BeginSuccessfulGuard(fsm);
            }
            else
            {
                Plugin.Instance?.OnSilkSpecialStateEntered(fsm, __instance.Name);
            }
        }
    }
}

[HarmonyPatch(
    typeof(HeroController),
    "CanThrowTool",
    new[] { typeof(ToolItem), typeof(AttackToolBinding), typeof(bool) })]
internal static class FreeGuardSilkGatePatch
{
    private static bool Prefix(ToolItem tool, ref bool __result)
    {
        if (!CrossStitchRuntime.IsCrossStitchTool(tool))
        {
            return true;
        }

        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(TakeSilk), nameof(TakeSilk.OnEnter))]
internal static class FreeGuardSilkCostPatch
{
    private static bool Prefix(TakeSilk __instance)
    {
        if (!string.Equals(__instance.Fsm?.Name, "Silk Specials", StringComparison.Ordinal) ||
            !string.Equals(__instance.State?.Name, "Parry Start", StringComparison.Ordinal))
        {
            return true;
        }

        __instance.Finish();
        return false;
    }
}

[HarmonyPatch(typeof(ToolItem), "get_IsUnlockedNotHidden")]
internal static class InitialGuardInventoryPatch
{
    private static void Postfix(ToolItem __instance, ref bool __result)
    {
        if (!__result &&
            CrossStitchRuntime.IsCrossStitchTool(__instance) &&
            !__instance.SavedData.IsHidden)
        {
            __result = true;
        }
    }
}

[HarmonyPatch(typeof(ToolHudIcon), nameof(ToolHudIcon.GetIsEmpty))]
internal static class GuardHudAvailabilityPatch
{
    private static void Postfix(ToolHudIcon __instance, ref bool __result)
    {
        if (CrossStitchRuntime.IsCrossStitchTool(__instance.CurrentTool))
        {
            __result = false;
        }
    }
}
