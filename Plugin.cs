using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;

namespace CrossStitchRework;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(BetterBindingsGuid, BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "modcraft.silksong.cross-stitch-rework";
    public const string PluginName = "Cross Stitch Rework";
    public const string PluginVersion = "0.5.0";
    public const string BetterBindingsGuid = "modcraft.silksong.better-bindings";

    internal static Plugin? Instance { get; private set; }
    internal static ManualLogSource Log { get; private set; } = null!;

    internal static ConfigEntry<float> CounterWindow { get; private set; } = null!;
    internal static ConfigEntry<float> MovementCancelDelay { get; private set; } = null!;
    internal static ConfigEntry<float> MovementInvulnerabilityCarry { get; private set; } = null!;
    internal static ConfigEntry<bool> DebugCounterWithoutPhantom { get; private set; } = null!;

    private readonly CrossStitchRuntime runtime = new();

    internal static bool HasIndependentGuardBinding => Instance?.runtime.HasIndependentGuardBinding ?? false;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        CounterWindow = Config.Bind(
            "Timing",
            "CounterWindow",
            1.00f,
            new ConfigDescription(
                "Seconds after a successful guard during which attack can trigger the counter.",
                new AcceptableValueRange<float>(0.15f, 1.0f)));
        MovementCancelDelay = Config.Bind(
            "Timing",
            "MovementCancelDelay",
            0.10f,
            new ConfigDescription(
                "Delay before held movement, jump, or dash may cancel a successful guard.",
                new AcceptableValueRange<float>(0f, 0.5f)));
        MovementInvulnerabilityCarry = Config.Bind(
            "Timing",
            "MovementInvulnerabilityCarry",
            0.50f,
            new ConfigDescription(
                "Invulnerability carried into movement after movement-cancelling the recovery.",
                new AcceptableValueRange<float>(0f, 0.5f)));
        DebugCounterWithoutPhantom = Config.Bind(
            "Debug",
            "UnlockCounterWithoutPhantom",
            false,
            "Allow the counterattack before defeating Phantom. Guard availability is unaffected.");

        new Harmony(PluginGuid).PatchAll(Assembly.GetExecutingAssembly());
        runtime.InitializeIndependentGuardBinding();
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void Update()
    {
        runtime.Update();
    }

    private void OnDestroy()
    {
        runtime.Dispose();
        ParryContactRegistry.Reset();
        Instance = null;
    }

    internal void BeginSuccessfulGuard(HutongGames.PlayMaker.Fsm fsm)
    {
        runtime.BeginSuccessfulGuard(fsm);
    }

    internal void OnSilkSpecialStateEntered(HutongGames.PlayMaker.Fsm fsm, string stateName)
    {
        runtime.OnSilkSpecialStateEntered(fsm, stateName);
    }

    internal void ApplyCounterLanding()
    {
        runtime.ApplyCounterLanding();
    }
}
