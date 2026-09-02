using System.Collections.Generic;
using UnityEngine;

namespace CrossStitchRework;

internal sealed class StableParryContactTracker : MonoBehaviour, CustomPlayerLoop.ILateFixedUpdate
{
    private const float ReleasePadding = 0.5f;
    private static readonly Vector2 ReleaseBoxOffset = new(-0.017f, -0.3799f);
    private static readonly Vector2 ReleaseBoxSize = new(
        1.18f + ReleasePadding * 2f,
        2.2498f + ReleasePadding * 2f);

    private readonly List<Collider2D> overlaps = new(32);
    private readonly HashSet<int> overlappingSources = new();
    private ContactFilter2D contactFilter;

    private void Awake()
    {
        contactFilter = new ContactFilter2D();
        contactFilter.NoFilter();
        CustomPlayerLoop.RegisterSuperLateFixedUpdate(this);
    }

    private void OnDestroy()
    {
        CustomPlayerLoop.UnregisterSuperLateFixedUpdate(this);
    }

    public void LateFixedUpdate()
    {
        Vector3 scale = transform.lossyScale;
        Vector2 worldSize = new(
            ReleaseBoxSize.x * Mathf.Abs(scale.x),
            ReleaseBoxSize.y * Mathf.Abs(scale.y));
        Vector2 worldCenter = transform.TransformPoint(ReleaseBoxOffset);

        overlaps.Clear();
        Physics2D.OverlapBox(
            worldCenter,
            worldSize,
            transform.eulerAngles.z,
            contactFilter,
            overlaps);

        overlappingSources.Clear();
        foreach (Collider2D overlap in overlaps)
        {
            GameObject source = overlap.gameObject;
            if (source.activeInHierarchy)
            {
                overlappingSources.Add(source.GetInstanceID());
            }
        }

        ParryContactRegistry.UpdateContacts(overlappingSources);
    }
}
