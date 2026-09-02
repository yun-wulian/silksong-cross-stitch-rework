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
        }

        internal GameObject Source { get; }
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
            Plugin.Log.LogInfo($"Consumed parry contact '{source.name}' ({instanceId}).");
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
            if (state.Source == null ||
                !state.Source.activeInHierarchy ||
                !overlappingSources.Contains(instanceId))
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
                Plugin.Log.LogInfo($"Rearmed parry contact '{state.Source.name}' ({instanceId}) after separation.");
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
