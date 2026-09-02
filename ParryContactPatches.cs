using GlobalEnums;
using HarmonyLib;
using UnityEngine;

namespace CrossStitchRework;

[HarmonyPatch(typeof(HeroBox), "Awake")]
internal static class StableParryContactTrackerPatch
{
    private static void Postfix(HeroBox __instance)
    {
        HeroController hero = __instance.GetComponentInParent<HeroController>();
        if (hero != null &&
            hero.GetComponentInChildren<StableParryContactTracker>(includeInactive: true) == null)
        {
            __instance.gameObject.AddComponent<StableParryContactTracker>();
        }
    }
}

[HarmonyPatch(typeof(HeroBox), nameof(HeroBox.CheckForDamage))]
internal static class ConsumedHeroBoxContactPatch
{
    private static bool Prefix(GameObject otherGameObject)
    {
        return !ParryContactRegistry.ShouldIgnore(otherGameObject);
    }
}

[HarmonyPatch(typeof(HeroController), nameof(HeroController.CheckParry))]
internal static class CheckParryContactPatch
{
    private readonly struct ParryAttempt
    {
        internal ParryAttempt(DamageHero source, HeroController hero)
        {
            Source = source;
            WasParrying = hero.cState.parrying;
            WasParryAttack = hero.cState.parryAttack;
        }

        internal DamageHero Source { get; }
        internal bool WasParrying { get; }
        internal bool WasParryAttack { get; }
    }

    private static bool Prefix(
        HeroController __instance,
        DamageHero damageHero,
        out ParryAttempt __state)
    {
        __state = new ParryAttempt(damageHero, __instance);
        return !ParryContactRegistry.ShouldIgnore(damageHero);
    }

    private static void Postfix(HeroController __instance, ParryAttempt __state)
    {
        if (__state.Source != null &&
            __state.WasParrying &&
            !__state.WasParryAttack &&
            !__instance.cState.parrying &&
            __instance.cState.parryAttack)
        {
            ParryContactRegistry.Consume(__state.Source);
        }
    }
}

[HarmonyPatch(
    typeof(HeroController),
    nameof(HeroController.TakeDamage),
    new[]
    {
        typeof(GameObject),
        typeof(CollisionSide),
        typeof(int),
        typeof(HazardType),
        typeof(DamagePropertyFlags)
    })]
internal static class TakeDamageParryContactPatch
{
    private readonly struct ParryAttempt
    {
        internal ParryAttempt(GameObject? source, HeroController hero)
        {
            Source = source;
            WasParrying = hero.cState.parrying;
            WasParryAttack = hero.cState.parryAttack;
        }

        internal GameObject? Source { get; }
        internal bool WasParrying { get; }
        internal bool WasParryAttack { get; }
    }

    private static bool Prefix(
        HeroController __instance,
        GameObject go,
        out ParryAttempt __state)
    {
        __state = new ParryAttempt(go, __instance);
        return go == null || !ParryContactRegistry.ShouldIgnore(go);
    }

    private static void Postfix(HeroController __instance, ParryAttempt __state)
    {
        if (__state.Source != null &&
            __state.WasParrying &&
            !__state.WasParryAttack &&
            !__instance.cState.parrying &&
            __instance.cState.parryAttack)
        {
            ParryContactRegistry.Consume(__state.Source);
        }
    }
}
