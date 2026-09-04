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
    private const string CounterCatchState = "Parry Catch";
    private const string CounterEndState = "Parry End";
    private const string ChainGuardState = "Parry Flip?";
    private const string ExitState = "Special End";
    private const float RecoveryAnimationDuration = 0.25f;
    private const int TerrainLayerMask = 8448;
    private const float LandingWallInset = 0.35f;
    private const float ForwardLandingFraction = 0.75f;

    private readonly object successInvulnerabilitySource = new();
    private readonly object actionInvulnerabilitySource = new();
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
    private HeroController? cancelableFsmMoveHero;
    private bool previousCancelableFsmMove;

    private HeroController? actionInvulnerabilityHero;
    private float actionInvulnerabilityEndsAt;

    private bool counterLandingPending;
    private bool counterEndpointValid;
    private bool counterAttackWasReleased;
    private HeroController? counterHero;
    private Fsm? counterFsm;
    private Vector2 counterOrigin;
    private Vector2 counterEndpoint;

    internal bool HasIndependentGuardBinding => betterBindings.IsRegistered;

    internal static bool IsCrossStitchTool(ToolItem? tool)
    {
        return tool is ToolItemSkill &&
               string.Equals(tool.name, ParryToolName, StringComparison.Ordinal) &&
               string.Equals(tool.Usage.FsmEventName, ParryEventName, StringComparison.Ordinal);
    }

    internal void Update()
    {
        UpdateActionInvulnerability();
        UpdateCounterLandingInput();
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
        ReleaseActionInvulnerability();

        successActive = true;
        successHero = hero;
        successFsm = fsm;
        successStartedAt = Time.time;
        recoveryAnimationStarted = false;
        AcquireCancelableFsmMove(hero);

        InputHandler? input = GetInputHandler();
        bufferedAttack = input?.inputActions.Attack.IsPressed ?? false;
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
            HeroController? hero = successHero;
            if (hero != null)
            {
                EndSuccessfulGuardForAction(hero);
            }
            else
            {
                EndSuccessfulGuard(clearParryAttack: true);
            }
        }

        if (!counterLandingPending || !ReferenceEquals(counterFsm, fsm))
        {
            return;
        }

        if (string.Equals(stateName, CounterState, StringComparison.Ordinal))
        {
            CaptureCounterEndpoint();
        }
        else if (!string.Equals(stateName, CounterLeftState, StringComparison.Ordinal) &&
                 !string.Equals(stateName, CounterRightState, StringComparison.Ordinal) &&
                 !string.Equals(stateName, CounterCatchState, StringComparison.Ordinal) &&
                 !string.Equals(stateName, CounterEndState, StringComparison.Ordinal))
        {
            ClearCounterLanding();
        }
    }

    internal void ApplyCounterLanding()
    {
        if (!counterLandingPending)
        {
            return;
        }

        HeroController? hero = counterHero;
        if (hero == null)
        {
            ClearCounterLanding();
            return;
        }

        InputHandler? input = GetInputHandler();
        bool attackIsPressed = input?.inputActions.Attack.IsPressed == true;
        bool landForward = !counterAttackWasReleased && counterEndpointValid;
        Vector2 requested = Vector2.Lerp(counterOrigin, counterEndpoint, ForwardLandingFraction);
        Vector2 target = landForward ? ClampLandingToTerrain(counterOrigin, requested) : counterOrigin;

        if (Plugin.DebugCounterWithoutPhantom.Value)
        {
            Plugin.Log.LogInfo(
                $"Counter landing: forward={landForward}, released={counterAttackWasReleased}, " +
                $"pressedNow={attackIsPressed}, endpointValid={counterEndpointValid}, " +
                $"origin={counterOrigin}, endpoint={counterEndpoint}, target={target}.");
        }

        Rigidbody2D body = hero.GetComponent<Rigidbody2D>();
        Vector3 currentPosition = hero.transform.position;
        hero.transform.position = new Vector3(target.x, target.y, currentPosition.z);
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.position = target;
        }
        Physics2D.SyncTransforms();

        if (Plugin.DebugCounterWithoutPhantom.Value)
        {
            Vector2 rigidbodyPosition = body != null ? body.position : (Vector2)hero.transform.position;
            Plugin.Log.LogInfo(
                $"Counter landing committed: transform={hero.transform.position}, rigidbody={rigidbodyPosition}.");
        }

        ClearCounterLanding();
    }

    internal void Dispose()
    {
        EndSuccessfulGuard(clearParryAttack: true);
        ReleaseActionInvulnerability();
        betterBindings.Dispose();
        ClearCounterLanding();
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
        PrepareCounterLanding(hero, fsm);
        EndSuccessfulGuardForAction(hero);
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
        ExitThroughSpecialEnd(hero, fsm);
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
            !CanStartIndependentGuard(heroController))
        {
            return;
        }

        if (heroController.cState.attacking)
        {
            heroController.CancelAttack();
        }
        heroController.ResetInputQueues();
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

        return successActive || CanStartIndependentGuard(hero);
    }

    private static bool CanStartIndependentGuard(HeroController hero)
    {
        if (!hero.CanInput() ||
            hero.controlReqlinquished ||
            hero.cState.dead ||
            hero.cState.hazardDeath ||
            hero.cState.hazardRespawning ||
            hero.cState.transitioning ||
            hero.cState.recoiling ||
            hero.hero_state == ActorStates.no_input ||
            hero.hero_state == ActorStates.hard_landing ||
            hero.hero_state == ActorStates.dash_landing ||
            InteractManager.BlockingInteractable != null)
        {
            return false;
        }

        if (hero.cState.attacking)
        {
            return !hero.cState.dashing;
        }

        return hero.CanThrowTool(checkGetWillThrow: false);
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
        ExitThroughSpecialEnd(hero, fsm);
    }

    private void CancelToBind(HeroController hero, Fsm fsm)
    {
        hero.ResetInputQueues();
        ExitThroughSpecialEnd(hero, fsm);
        hero.bellBindFSM.SendEvent("BUTTON DOWN");
    }

    private void CancelToSilkSpecialState(HeroController hero, Fsm fsm, string stateName)
    {
        hero.ResetInputQueues();
        EndSuccessfulGuardForAction(hero);
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

        ExitThroughSpecialEnd(hero, fsm);
    }

    private void CancelToFree(HeroController hero, Fsm fsm)
    {
        hero.ResetInputQueues();
        EndSuccessfulGuard(clearParryAttack: true);
        fsm.SetState(ExitState);
    }

    private void ExitThroughSpecialEnd(HeroController hero, Fsm fsm)
    {
        EndSuccessfulGuardForAction(hero);
        fsm.SetState(ExitState);
    }

    private void EndSuccessfulGuardForAction(HeroController hero)
    {
        EndSuccessfulGuard(clearParryAttack: true);
        StartActionInvulnerability(hero);
    }

    private void EndSuccessfulGuard(bool clearParryAttack)
    {
        HeroController? hero = successHero;
        ReleaseSuccessInvulnerability();
        ReleaseCancelableFsmMove();
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

    private void AcquireCancelableFsmMove(HeroController hero)
    {
        ReleaseCancelableFsmMove();
        cancelableFsmMoveHero = hero;
        previousCancelableFsmMove = hero.cState.isInCancelableFSMMove;
        hero.cState.isInCancelableFSMMove = true;
    }

    private void ReleaseCancelableFsmMove()
    {
        if (cancelableFsmMoveHero != null)
        {
            cancelableFsmMoveHero.cState.isInCancelableFSMMove = previousCancelableFsmMove;
        }

        cancelableFsmMoveHero = null;
        previousCancelableFsmMove = false;
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

    private void StartActionInvulnerability(HeroController hero)
    {
        ReleaseActionInvulnerability();
        if (Plugin.ActionInvulnerabilityCarry.Value <= 0f)
        {
            return;
        }

        actionInvulnerabilityHero = hero;
        actionInvulnerabilityEndsAt = Time.time + Plugin.ActionInvulnerabilityCarry.Value;
        hero.AddInvulnerabilitySource(actionInvulnerabilitySource);
    }

    private void UpdateActionInvulnerability()
    {
        if (actionInvulnerabilityHero == null)
        {
            actionInvulnerabilityHero = null;
            return;
        }

        if (Time.time >= actionInvulnerabilityEndsAt || actionInvulnerabilityHero.cState.dead)
        {
            ReleaseActionInvulnerability();
        }
    }

    private void ReleaseActionInvulnerability()
    {
        if (actionInvulnerabilityHero != null)
        {
            actionInvulnerabilityHero.RemoveInvulnerabilitySource(actionInvulnerabilitySource);
        }
        actionInvulnerabilityHero = null;
        actionInvulnerabilityEndsAt = 0f;
    }

    private void PrepareCounterLanding(HeroController hero, Fsm fsm)
    {
        Rigidbody2D body = hero.GetComponent<Rigidbody2D>();
        counterOrigin = body != null ? body.position : (Vector2)hero.transform.position;
        counterEndpoint = counterOrigin;
        counterEndpointValid = false;
        counterAttackWasReleased = false;
        counterLandingPending = true;
        counterHero = hero;
        counterFsm = fsm;
    }

    private void CaptureCounterEndpoint()
    {
        HeroController? hero = counterHero;
        Fsm? fsm = counterFsm;
        if (hero == null || fsm == null)
        {
            return;
        }

        GameObject? effect = fsm.Variables.FindFsmGameObject("Temp")?.Value;
        PolygonCollider2D? collider = effect != null
            ? effect.GetComponentInChildren<PolygonCollider2D>(includeInactive: true)
            : null;
        if (collider == null)
        {
            Plugin.Log.LogWarning("Could not resolve the active Cross Stitch slash collider; held attack will use the origin landing.");
            return;
        }

        float farthestDistance = 0f;
        Vector2 farthestPoint = counterOrigin;
        for (int pathIndex = 0; pathIndex < collider.pathCount; pathIndex++)
        {
            Vector2[] path = collider.GetPath(pathIndex);
            foreach (Vector2 point in path)
            {
                Vector3 world = collider.transform.TransformPoint(point);
                float distanceFromOrigin = Mathf.Abs(world.x - counterOrigin.x);
                if (distanceFromOrigin > farthestDistance)
                {
                    farthestDistance = distanceFromOrigin;
                    farthestPoint = new Vector2(world.x, counterOrigin.y);
                }
            }
        }

        if (farthestDistance > 0f)
        {
            counterEndpoint = farthestPoint;
            counterEndpointValid = true;
        }

        if (Plugin.DebugCounterWithoutPhantom.Value)
        {
            Plugin.Log.LogInfo(
                $"Counter endpoint: valid={counterEndpointValid}, collider={collider.name}, " +
                $"origin={counterOrigin}, endpoint={counterEndpoint}, distance={farthestDistance:0.###}.");
        }
    }

    private void UpdateCounterLandingInput()
    {
        if (!counterLandingPending || counterAttackWasReleased)
        {
            return;
        }

        InputHandler? input = GetInputHandler();
        if (input?.inputActions.Attack.WasReleased == true)
        {
            counterAttackWasReleased = true;
        }
    }

    private static Vector2 ClampLandingToTerrain(Vector2 origin, Vector2 requested)
    {
        Vector2 delta = requested - origin;
        float distance = Mathf.Abs(delta.x);
        if (distance <= Mathf.Epsilon)
        {
            return origin;
        }

        float direction = Mathf.Sign(delta.x);
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, Vector2.right * direction, distance, TerrainLayerMask);
        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider == null || hit.collider.isTrigger || hit.distance <= Mathf.Epsilon)
            {
                continue;
            }

            float safeDistance = Mathf.Max(0f, hit.distance - LandingWallInset);
            return new Vector2(origin.x + direction * safeDistance, origin.y);
        }

        return requested;
    }

    private void ClearCounterLanding()
    {
        counterLandingPending = false;
        counterEndpointValid = false;
        counterAttackWasReleased = false;
        counterHero = null;
        counterFsm = null;
        counterOrigin = Vector2.zero;
        counterEndpoint = Vector2.zero;
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
