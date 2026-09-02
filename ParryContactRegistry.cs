using System.Collections.Generic;
using UnityEngine;

namespace CrossStitchRework;

internal static class ParryContactRegistry
{
    private sealed class ContactState
    {
        internal ContactState(GameObject source)
        {
            Source = source;
            SeenThisFixedStep = true;
        }

        internal GameObject Source { get; }
        internal bool SeenThisFixedStep { get; set; }
        internal int MissingFixedSteps { get; set; }
    }

    private static readonly Dictionary<int, ContactState> ConsumedContacts = new();
    private static readonly List<int> ContactsToRemove = new();

    internal static void Consume(DamageHero source)
    {
        if (source == null)
        {
            return;
        }

        Consume(source.gameObject);
    }

    internal static void Consume(GameObject source)
    {
        if (source == null)
        {
            return;
        }

        int instanceId = source.GetInstanceID();
        ConsumedContacts[instanceId] = new ContactState(source);

        if (Plugin.DebugCounterWithoutPhantom.Value)
        {
            Plugin.Log.LogInfo(
                $"Consumed parry contact '{source.name}' ({instanceId}) " +
                $"at frame {Time.frameCount}, time {Time.time:0.000}.");
        }
    }

    internal static bool ShouldIgnore(DamageHero source)
    {
        if (source == null)
        {
            return false;
        }

        return ShouldIgnore(source.gameObject);
    }

    internal static bool ShouldIgnore(GameObject source)
    {
        if (source == null)
        {
            return false;
        }

        int instanceId = source.GetInstanceID();
        if (!ConsumedContacts.TryGetValue(instanceId, out ContactState? state))
        {
            return false;
        }

        if (ReferenceEquals(state.Source, source))
        {
            state.SeenThisFixedStep = true;
            return true;
        }

        ConsumedContacts.Remove(instanceId);
        return false;
    }

    internal static void UpdateContacts(ISet<int> overlappingSources)
    {
        ContactsToRemove.Clear();
        foreach ((int instanceId, ContactState state) in ConsumedContacts)
        {
            if (state.Source == null || !state.Source.activeInHierarchy)
            {
                ContactsToRemove.Add(instanceId);
                continue;
            }

            bool hasContactEvidence = state.SeenThisFixedStep || overlappingSources.Contains(instanceId);
            state.SeenThisFixedStep = false;
            if (hasContactEvidence)
            {
                state.MissingFixedSteps = 0;
                continue;
            }

            state.MissingFixedSteps++;
            if (state.MissingFixedSteps >= 2)
            {
                ContactsToRemove.Add(instanceId);
            }
        }

        foreach (int instanceId in ContactsToRemove)
        {
            if (Plugin.DebugCounterWithoutPhantom.Value &&
                ConsumedContacts.TryGetValue(instanceId, out ContactState? state) &&
                state.Source != null)
            {
                Plugin.Log.LogInfo(
                    $"Rearmed parry contact '{state.Source.name}' ({instanceId}) after stable release-box separation " +
                    $"at frame {Time.frameCount}, time {Time.time:0.000}.");
            }

            ConsumedContacts.Remove(instanceId);
        }
    }

    internal static void Reset()
    {
        ConsumedContacts.Clear();
        ContactsToRemove.Clear();
    }
}
