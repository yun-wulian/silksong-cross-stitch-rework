using System;
using System.Collections.Generic;
using GlobalEnums;
using HutongGames.PlayMaker;
using UnityEngine;

namespace CrossStitchRework;

internal sealed class CrossStitchRuntime
{
    private const string ParryToolName = "Parry";
    private const string ParryEventName = "PARRY";
    private const string SilkSpecialsFsmName = "Silk Specials";
    private const string ParryClashState = "Parry Clash";
    private const string CounterReadyState = "Change Facing?";
    private const string CounterState = "Parry Cross Slash";
    private const string CounterLeftState = "CrossSlash L";
    private const string CounterRightState = "CrossSlash R";
    private const string ChainGuardState = "Parry Flip?";
    private const string ExitState = "Special End";
    private const float RecoveryAnimationDuration = 0.25f;

    private readonly object successInvulnerabilitySource = new();
    private readonly object movementInvulnerabilitySource = new();
    private readonly BetterBindingsBridge betterBindings = new();

    private PlayerData? accessPlayerData;
    private bool checkedInitialEquip;
    private float nextAccessCheck;

    private bool successActive;
    private bool successInvulnerabilityHeld;
    private bool recoveryAnimationStarted;
    private bool bufferedAttack;
    private bool bufferedQuickCast;
    private bool bufferedCast;
    private bool bufferedDreamNail;
    private bool bufferedTaunt;
    private float successStartedAt;
    private HeroController? successHero;
    private Fsm? successFsm;

    private HeroController? movementInvulnerabilityHero;
    private float movementInvulnerabilityEndsAt;

    internal bool HasIndependentGuardBinding => betterBindings.IsRegistered;

    internal static bool IsCrossStitchTool(ToolItem? tool)
    {
        return tool is ToolItemSkill &&
               string.Equals(tool.name, ParryToolName, StringComparison.Ordinal) &&
               string.Equals(tool.Usage.FsmEventName, ParryEventName, StringComparison.Ordinal);
    }

    internal void Update()
    {
        UpdateMovementInvulnerability();
        EnsureInitialAccess();
        UpdateSuccessfulGuard();
    }

    internal void InitializeIndependentGuardBinding()
    {
        if (!betterBindings.TryRegister(OnIndependentGuardPressed, CanInvokeIndependentGuard))
        {
            Plugin.Log.LogInfo("Better Bindings shortcut unavailable; Cross Stitch requires the equipped skill input.");
        }
    }

    internal void BeginSuccessfulGuard(Fsm fsm)
    {
        HeroController hero = HeroController.instance;
        if (hero == null || !string.Equals(fsm.Name, SilkSpecialsFsmName, StringComparison.Ordinal))
        {
            return;
        }

        EndSuccessfulGuard(clearParryAttack: true);
        ReleaseMovementInvulnerability();

        successActive = true;
        successHero = hero;
        successFsm = fsm;
        successStartedAt = Time.time;
        recoveryAnimationStarted = false;

        InputHandler? input = GetInputHandler();
        bufferedAttack = input?.inputActions.Attack.WasPressed ?? false;
        bufferedQuickCast = input?.inputActions.QuickCast.WasPressed ?? false;
        bufferedCast = input?.inputActions.Cast.WasPressed ?? false;
        bufferedDreamNail = input?.inputActions.DreamNail.WasPressed ?? false;
        bufferedTaunt = input?.inputActions.Taunt.WasPressed ?? false;

        hero.ResetInputQueues();
        hero.AddSilk(3, heroEffect: true);
        hero.AddInvulnerabilitySource(successInvulnerabilitySource);
        successInvulnerabilityHeld = true;
    }

    internal void OnSilkSpecialStateEntered(Fsm fsm, string stateName)
    {
        if (successActive &&
            ReferenceEquals(successFsm, fsm) &&
            !string.Equals(stateName, ParryClashState, StringComparison.Ordinal) &&
            !string.Equals(stateName, CounterReadyState, StringComparison.Ordinal))
        {
            EndSuccessfulGuard(clearParryAttack: true);
        }
    }

    internal void Dispose()
    {
        EndSuccessfulGuard(clearParryAttack: true);
        ReleaseMovementInvulnerability();
        betterBindings.Dispose();
    }

    private void EnsureInitialAccess()
    {
        if (Time.unscaledTime < nextAccessCheck)
        {
            return;
        }

        nextAccessCheck = Time.unscaledTime + 0.5f;
        if (!PlayerData.HasInstance)
        {
            return;
        }

        PlayerData playerData = PlayerData.instance;
        if (!ReferenceEquals(accessPlayerData, playerData))
        {
            accessPlayerData = playerData;
            checkedInitialEquip = false;
        }

        ToolItem parry = ToolItemManager.GetToolByName(ParryToolName);
        if (parry == null || checkedInitialEquip)
        {
            return;
        }

        if (HasIndependentGuardBinding)
        {
            checkedInitialEquip = true;
            return;
        }

        string crestId = playerData.CurrentCrestID;
        ToolCrest crest = ToolItemManager.GetCrestByName(crestId);
        if (string.IsNullOrEmpty(crestId) || crest == null)
        {
            return;
        }

        List<ToolItem> equipped = ToolItemManager.GetEquippedToolsForCrest(crestId) ?? new List<ToolItem>();
        for (int i = 0; i < crest.Slots.Length && i < equipped.Count; i++)
        {
            if (crest.Slots[i].Type == ToolItemType.Skill && equipped[i] != null)
            {
                checkedInitialEquip = true;
                return;
            }
        }

        List<string> equippedNames = new(crest.Slots.Length);
        int skillSlot = -1;
        for (int i = 0; i < crest.Slots.Length; i++)
        {
            ToolItem? existing = i < equipped.Count ? equipped[i] : null;
            equippedNames.Add(existing != null ? existing.name : string.Empty);
            if (skillSlot < 0 &&
                crest.Slots[i].Type == ToolItemType.Skill &&
                crest.Slots[i].AttackBinding == AttackToolBinding.Neutral)
            {
                skillSlot = i;
            }
        }

        if (skillSlot >= 0)
        {
            equippedNames[skillSlot] = parry.name;
            ToolItemManager.SetEquippedTools(crestId, equippedNames);
            Plugin.Log.LogInfo($"Added Cross Stitch guard access to crest '{crestId}' without setting hasParry.");
        }

        checkedInitialEquip = true;
    }

    private void UpdateSuccessfulGuard()
    {
        if (!successActive)
        {
            return;
        }

        HeroController hero = successHero!;
        Fsm fsm = successFsm!;
        if (hero == null || hero.cState.dead || hero.cState.hazardDeath)
        {
            EndSuccessfulGuard(clearParryAttack: true);
            return;
        }

        string stateName = fsm.ActiveStateName;
        if (!string.Equals(stateName, ParryClashState, StringComparison.Ordinal) &&
            !string.Equals(stateName, CounterReadyState, StringComparison.Ordinal))
        {
            EndSuccessfulGuard(clearParryAttack: true);
            return;
        }

        GameManager gameManager = GameManager.instance;
        if (gameManager == null || gameManager.isPaused)
        {
            return;
        }

        InputHandler? input = GetInputHandler();
        if (input == null)
        {
            return;
        }

        HeroActions actions = input.inputActions;
        if (Consume(ref bufferedAttack) || actions.Attack.WasPressed)
        {
            HandleAttack(hero, fsm, actions);
            return;
        }

        if (Consume(ref bufferedQuickCast) || actions.QuickCast.WasPressed)
        {
            HandleQuickCast(hero, fsm, actions);
            return;
        }

        if (Consume(ref bufferedCast) || actions.Cast.WasPressed)
        {
            CancelToBind(hero, fsm);
            return;
        }

        if (Consume(ref bufferedDreamNail) || actions.DreamNail.WasPressed)
        {
            CancelToSilkSpecialState(hero, fsm, "Needolin Sub");
            return;
        }

        if (Consume(ref bufferedTaunt) || actions.Taunt.WasPressed)
        {
            CancelToSilkSpecialState(hero, fsm, "Taunt Check");
            return;
        }

        float elapsed = Time.time - successStartedAt;
        if (elapsed >= Plugin.MovementCancelDelay.Value && HasHeldMovement(actions))
        {
            CancelToMovement(hero, fsm, actions);
            return;
        }

        float recoveryStart = Math.Max(0f, Plugin.CounterWindow.Value - RecoveryAnimationDuration);
        if (!recoveryAnimationStarted &&
            string.Equals(stateName, CounterReadyState, StringComparison.Ordinal) &&
            elapsed >= recoveryStart)
        {
            recoveryAnimationStarted = true;
            string clipName = hero.cState.onGround ? "Parry Recover Ground" : "Parry Recover";
            hero.AnimCtrl.animator.Play(clipName);
        }

        if (elapsed >= Plugin.CounterWindow.Value)
        {
            CancelToFree(hero, fsm);
        }
    }

    private void HandleAttack(HeroController hero, Fsm fsm, HeroActions actions)
    {
        PlayerData playerData = PlayerData.instance;
        int cost = playerData.SilkSkillCost;
        bool counterUnlocked = playerData.hasParry || Plugin.DebugCounterWithoutPhantom.Value;
        if (!counterUnlocked || playerData.silk < cost)
        {
            if (counterUnlocked && playerData.silk < cost)
            {
                EventRegister.SendEvent(EventRegisterEvents.BindFailedNotEnough);
            }

            CancelToAttack(hero, fsm);
            return;
        }

        hero.TakeSilk(cost);
        hero.ResetInputQueues();
        EndSuccessfulGuard(clearParryAttack: true);
        hero.cState.parryAttack = true;

        if (actions.Right.IsPressed)
        {
            fsm.SetState(CounterRightState);
        }
        else if (actions.Left.IsPressed)
        {
            fsm.SetState(CounterLeftState);
        }
        else
        {
            fsm.SetState(CounterState);
        }
    }

    private void HandleQuickCast(HeroController hero, Fsm fsm, HeroActions actions)
    {
        ToolItem? selectedTool = GetSelectedQuickCastTool(actions);
        if (IsCrossStitchTool(selectedTool))
        {
            ChainGuard(hero, fsm);
            return;
        }

        hero.ResetInputQueues();
        hero.SetStartWithToolThrow();
        ExitThroughSpecialEnd(hero, fsm, carryMovementInvulnerability: false);
    }

    private void OnIndependentGuardPressed()
    {
        if (successActive)
        {
            HeroController? hero = successHero;
            Fsm? fsm = successFsm;
            if (hero != null &&
                fsm != null &&
                (string.Equals(fsm.ActiveStateName, ParryClashState, StringComparison.Ordinal) ||
                 string.Equals(fsm.ActiveStateName, CounterReadyState, StringComparison.Ordinal)))
            {
                ChainGuard(hero, fsm);
            }
            return;
        }

        HeroController heroController = HeroController.instance;
        GameManager gameManager = GameManager.instance;
        if (heroController == null ||
            gameManager == null ||
            gameManager.GameState != GameState.PLAYING ||
            !heroController.CanThrowTool(checkGetWillThrow: false))
        {
            return;
        }

        EventRegister.SendEvent(EventRegisterEvents.FsmCancel);
        heroController.silkSpecialFSM.SendEvent(ParryEventName);
    }

    private bool CanInvokeIndependentGuard()
    {
        GameManager gameManager = GameManager.instance;
        HeroController hero = HeroController.instance;
        if (gameManager == null || hero == null || gameManager.GameState != GameState.PLAYING)
        {
            return false;
        }

        return successActive || hero.CanThrowTool(checkGetWillThrow: false);
    }

    private void ChainGuard(HeroController hero, Fsm fsm)
    {
        hero.ResetInputQueues();
        EndSuccessfulGuard(clearParryAttack: true);
        fsm.SetState(ChainGuardState);
    }

    private void CancelToAttack(HeroController hero, Fsm fsm)
    {
        hero.ResetInputQueues();
        hero.SetStartWithAttack();
        ExitThroughSpecialEnd(hero, fsm, carryMovementInvulnerability: false);
    }

    private void CancelToBind(HeroController hero, Fsm fsm)
    {
        hero.ResetInputQueues();
        ExitThroughSpecialEnd(hero, fsm, carryMovementInvulnerability: false);
        hero.bellBindFSM.SendEvent("BUTTON DOWN");
    }

    private void CancelToSilkSpecialState(HeroController hero, Fsm fsm, string stateName)
    {
        hero.ResetInputQueues();
        EndSuccessfulGuard(clearParryAttack: true);
        hero.RegainControl();
        hero.StartAnimationControlToIdle();
        fsm.SetState(stateName);
    }

    private void CancelToMovement(HeroController hero, Fsm fsm, HeroActions actions)
    {
        hero.ResetInputQueues();
        if (actions.Jump.IsPressed)
        {
            hero.SetStartWithAnyJump();
        }
        else if (actions.Dash.IsPressed)
        {
            hero.SetStartWithDash();
        }

        ExitThroughSpecialEnd(hero, fsm, carryMovementInvulnerability: true);
    }

    private void CancelToFree(HeroController hero, Fsm fsm)
    {
        hero.ResetInputQueues();
        ExitThroughSpecialEnd(hero, fsm, carryMovementInvulnerability: false);
    }

    private void ExitThroughSpecialEnd(HeroController hero, Fsm fsm, bool carryMovementInvulnerability)
    {
        EndSuccessfulGuard(clearParryAttack: true);
        if (carryMovementInvulnerability)
        {
            StartMovementInvulnerability(hero);
        }
        fsm.SetState(ExitState);
    }

    private void EndSuccessfulGuard(bool clearParryAttack)
    {
        HeroController? hero = successHero;
        ReleaseSuccessInvulnerability();
        if (clearParryAttack && hero != null)
        {
            hero.cState.parrying = false;
            hero.cState.parryAttack = false;
        }

        successActive = false;
        successHero = null;
        successFsm = null;
        recoveryAnimationStarted = false;
        bufferedAttack = false;
        bufferedQuickCast = false;
        bufferedCast = false;
        bufferedDreamNail = false;
        bufferedTaunt = false;
    }

    private void ReleaseSuccessInvulnerability()
    {
        if (!successInvulnerabilityHeld)
        {
            return;
        }

        if (successHero != null)
        {
            successHero.RemoveInvulnerabilitySource(successInvulnerabilitySource);
        }
        successInvulnerabilityHeld = false;
    }

    private void StartMovementInvulnerability(HeroController hero)
    {
        ReleaseMovementInvulnerability();
        if (Plugin.MovementInvulnerabilityCarry.Value <= 0f)
        {
            return;
        }

        movementInvulnerabilityHero = hero;
        movementInvulnerabilityEndsAt = Time.time + Plugin.MovementInvulnerabilityCarry.Value;
        hero.AddInvulnerabilitySource(movementInvulnerabilitySource);
    }

    private void UpdateMovementInvulnerability()
    {
        if (movementInvulnerabilityHero == null)
        {
            movementInvulnerabilityHero = null;
            return;
        }

        if (Time.time >= movementInvulnerabilityEndsAt || movementInvulnerabilityHero.cState.dead)
        {
            ReleaseMovementInvulnerability();
        }
    }

    private void ReleaseMovementInvulnerability()
    {
        if (movementInvulnerabilityHero != null)
        {
            movementInvulnerabilityHero.RemoveInvulnerabilitySource(movementInvulnerabilitySource);
        }
        movementInvulnerabilityHero = null;
        movementInvulnerabilityEndsAt = 0f;
    }

    private static InputHandler? GetInputHandler()
    {
        GameManager gameManager = GameManager.instance;
        return gameManager != null ? gameManager.GetComponent<InputHandler>() : null;
    }

    private static ToolItem? GetSelectedQuickCastTool(HeroActions actions)
    {
        AttackToolBinding binding = actions.Up.IsPressed
            ? AttackToolBinding.Up
            : actions.Down.IsPressed
                ? AttackToolBinding.Down
                : AttackToolBinding.Neutral;
        return ToolItemManager.GetBoundAttackTool(binding, ToolEquippedReadSource.Active);
    }

    private static bool HasHeldMovement(HeroActions actions)
    {
        return actions.Left.IsPressed ||
               actions.Right.IsPressed ||
               actions.Up.IsPressed ||
               actions.Down.IsPressed ||
               actions.Jump.IsPressed ||
               actions.Dash.IsPressed ||
               actions.Evade.IsPressed ||
               actions.SuperDash.IsPressed;
    }

    private static bool Consume(ref bool buffered)
    {
        bool result = buffered;
        buffered = false;
        return result;
    }
}
