using UnityEngine;

public enum ArenaId
{
    P,
    R,
    O
}

/// <summary>
/// An oriented, scene-editable logical arena volume. The volume follows its
/// transform, so it can be parented directly to the imported logo floor.
/// </summary>
[ExecuteAlways]
public sealed class ArenaZone : MonoBehaviour
{
    [SerializeField] private ArenaId _arena;
    [SerializeField] private Vector2 _localSize = new(0.5f, 0.8f);
    [SerializeField] private Color _gizmoColor = new(0.15f, 0.75f, 1f, 0.3f);

    public ArenaId Arena => _arena;
    public Vector2 LocalSize => _localSize;

    public void Configure(ArenaId arena, Vector2 localSize, Color gizmoColor)
    {
        _arena = arena;
        _localSize = new Vector2(Mathf.Max(0.01f, localSize.x), Mathf.Max(0.01f, localSize.y));
        _gizmoColor = gizmoColor;
    }

    public bool Contains(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        return Mathf.Abs(local.x) <= _localSize.x * 0.5f
            && Mathf.Abs(local.z) <= _localSize.y * 0.5f;
    }

    private void OnDrawGizmos()
    {
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = _gizmoColor;
        Gizmos.DrawCube(Vector3.zero, new Vector3(_localSize.x, 0.01f, _localSize.y));
        Gizmos.color = new Color(_gizmoColor.r, _gizmoColor.g, _gizmoColor.b, 1f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(_localSize.x, 0.02f, _localSize.y));
        Gizmos.matrix = previousMatrix;
    }
}
