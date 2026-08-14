using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Controls one physical route gate. Disabling the renderers, colliders and
/// carving obstacles makes an open gate both visually and physically absent.
/// </summary>
public sealed class ArenaGate : MonoBehaviour
{
    [SerializeField] private Renderer[] _visuals = Array.Empty<Renderer>();
    [SerializeField] private Collider[] _colliders = Array.Empty<Collider>();
    [SerializeField] private NavMeshObstacle[] _obstacles = Array.Empty<NavMeshObstacle>();
    [SerializeField] private bool _initiallyClosed = true;

    public bool IsOpen { get; private set; }

    public void Configure(
        Renderer[] visuals,
        Collider[] colliders,
        NavMeshObstacle[] obstacles,
        bool initiallyClosed)
    {
        _visuals = visuals ?? Array.Empty<Renderer>();
        _colliders = colliders ?? Array.Empty<Collider>();
        _obstacles = obstacles ?? Array.Empty<NavMeshObstacle>();
        _initiallyClosed = initiallyClosed;
    }

    private void Awake()
    {
        SetOpen(_initiallyClosed == false, force: true);
    }

    public void SetOpen(bool open)
    {
        SetOpen(open, force: false);
    }

    private void SetOpen(bool open, bool force)
    {
        if (force == false && IsOpen == open)
        {
            return;
        }

        IsOpen = open;
        bool blocked = open == false;
        foreach (Renderer visual in _visuals)
        {
            if (visual != null)
            {
                visual.enabled = blocked;
            }
        }

        foreach (Collider gateCollider in _colliders)
        {
            if (gateCollider != null)
            {
                gateCollider.enabled = blocked;
            }
        }

        foreach (NavMeshObstacle obstacle in _obstacles)
        {
            if (obstacle != null)
            {
                obstacle.enabled = blocked;
            }
        }
    }
}
