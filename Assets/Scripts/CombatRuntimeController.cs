using System.Collections.Generic;
using Assets.Scripts.Ecs;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public class CombatRuntimeController : MonoBehaviour
{
    private readonly Dictionary<Entity, GameObject> _projectileViews = new();
    private readonly Dictionary<Entity, GameObject> _pickupViews = new();
    private readonly Dictionary<Entity, RangedProjectilePresentation> _rangedProjectileViews = new();
    private readonly List<DamageNumberPresentation> _damageNumbers = new();

    private World _world;
    private EntityManager _entityManager;
    private Entity _playerEntity;
    private EntityQuery _projectileQuery;
    private EntityQuery _pickupQuery;
    private EntityQuery _tracerQuery;
    private EntityQuery _rangedProjectileQuery;
    private EntityQuery _damageNumberQuery;
    private CombatHudView _hud;
    private Transform _playerVisual;
    private float _defeatUntil = -1f;
    private int _damageNumberSequence;
    private GUIStyle _damageNumberStyle;
    private GUIStyle _damageNumberShadowStyle;

    private const float DamageNumberDuration = 0.9f;

    public void Initialize(World world, Entity playerEntity, Transform playerVisual)
    {
        _world = world;
        _entityManager = world.EntityManager;
        _playerEntity = playerEntity;
        _playerVisual = playerVisual;
        _projectileQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<ProjectileComponent>(), ComponentType.ReadOnly<LocalTransform>());
        _pickupQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<ExperiencePickupComponent>(), ComponentType.ReadOnly<LocalTransform>());
        _tracerQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<TracerEvent>());
        _rangedProjectileQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<RangedProjectileComponent>(), ComponentType.ReadOnly<LocalTransform>());
        _damageNumberQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<DamageNumberEvent>());
        _hud = FindAnyObjectByType<CombatHudView>();
        _hud?.ShowDefeat(false);
    }

    private void Update()
    {
        if (_world == null || _world.IsCreated == false || _entityManager.Exists(_playerEntity) == false)
        {
            return;
        }

        if (_defeatUntil > 0f)
        {
            if (Time.unscaledTime >= _defeatUntil)
            {
                SceneManager.LoadScene("Game");
            }

            return;
        }

        UpdateAimAndWeapon();
        PresentProjectiles();
        PresentExperiencePickups();
        PresentTracers();
        PresentDamageNumbers();
        PresentRangedProjectiles();
        RefreshHudAndCheckDefeat();
    }

    private void UpdateAimAndWeapon()
    {
        PlayerCombatState combat = _entityManager.GetComponentData<PlayerCombatState>(_playerEntity);
        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) combat.SelectedWeapon = WeaponSlot.Pistol;
            if (Keyboard.current.digit2Key.wasPressedThisFrame) combat.SelectedWeapon = WeaponSlot.MachineGun;
        }
        _entityManager.SetComponentData(_playerEntity, combat);

        Camera camera = Camera.main;
        if (camera == null || Mouse.current == null)
        {
            return;
        }

        Ray ray = camera.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane groundPlane = new(Vector3.up, Vector3.zero);
        if (groundPlane.Raycast(ray, out float distance) == false)
        {
            return;
        }

        Vector3 target = ray.GetPoint(distance);
        LocalTransform playerTransform = _entityManager.GetComponentData<LocalTransform>(_playerEntity);
        Vector3 playerPosition = new(playerTransform.Position.x, playerTransform.Position.y, playerTransform.Position.z);
        Vector3 direction = target - playerPosition;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        direction.Normalize();
        _entityManager.SetComponentData(_playerEntity, new PlayerAimComponent
        {
            Position = new Unity.Mathematics.float3(target.x, target.y, target.z),
            Direction = new Unity.Mathematics.float3(direction.x, direction.y, direction.z)
        });
        if (_playerVisual != null)
        {
            _playerVisual.forward = direction;
        }
        _hud?.SetAimReticle(camera.WorldToScreenPoint(target));
    }

    private void RefreshHudAndCheckDefeat()
    {
        HealthComponent health = _entityManager.GetComponentData<HealthComponent>(_playerEntity);
        PlayerCombatState combat = _entityManager.GetComponentData<PlayerCombatState>(_playerEntity);
        PlayerProgressionState progression = _entityManager.GetComponentData<PlayerProgressionState>(_playerEntity);
        _hud?.Refresh(health.Value, health.MaxValue, combat.Experience, (int)combat.SelectedWeapon, progression.PistolUpgradeCount, progression.MachineGunUpgradeCount);

        if (health.Value <= 0)
        {
            _defeatUntil = Time.unscaledTime + 1f;
            PlayerDefeatInfo defeatInfo = _entityManager.GetComponentData<PlayerDefeatInfo>(_playerEntity);
            _hud?.ShowDefeat(true, GetDefeatReason(defeatInfo.LastDamageSource), combat.Experience);
        }
    }

    private void PresentProjectiles()
    {
        NativeArray<Entity> entities = _projectileQuery.ToEntityArray(Allocator.Temp);
        foreach (Entity entity in entities)
        {
            if (_projectileViews.TryGetValue(entity, out GameObject view) == false)
            {
                ProjectileComponent projectile = _entityManager.GetComponentData<ProjectileComponent>(entity);
                view = CreateMarker(PrimitiveType.Sphere, new Color(projectile.Color.x, projectile.Color.y, projectile.Color.z, projectile.Color.w), projectile.VisualScale);
                _projectileViews.Add(entity, view);
            }

            LocalTransform transform = _entityManager.GetComponentData<LocalTransform>(entity);
            view.transform.position = new Vector3(transform.Position.x, transform.Position.y + 0.35f, transform.Position.z);
        }

        entities.Dispose();
        CleanViews(_projectileViews, typeof(ProjectileComponent));
    }

    private void PresentExperiencePickups()
    {
        NativeArray<Entity> entities = _pickupQuery.ToEntityArray(Allocator.Temp);
        foreach (Entity entity in entities)
        {
            if (_pickupViews.TryGetValue(entity, out GameObject view) == false)
            {
                view = CreateMarker(PrimitiveType.Cube, new Color(0.25f, 1f, 0.25f), 0.2f);
                _pickupViews.Add(entity, view);
            }

            LocalTransform transform = _entityManager.GetComponentData<LocalTransform>(entity);
            view.transform.position = new Vector3(transform.Position.x, transform.Position.y + 0.25f, transform.Position.z);
        }

        entities.Dispose();
        CleanViews(_pickupViews, typeof(ExperiencePickupComponent));
    }

    private void PresentTracers()
    {
        NativeArray<Entity> entities = _tracerQuery.ToEntityArray(Allocator.Temp);
        foreach (Entity entity in entities)
        {
            TracerEvent tracer = _entityManager.GetComponentData<TracerEvent>(entity);
            GameObject lineObject = new("CombatTracer");
            Vector3 start = new(tracer.Start.x, tracer.Start.y + 0.45f, tracer.Start.z);
            Vector3 end = new(tracer.End.x, tracer.End.y + 0.45f, tracer.End.z);
            Color color = new(tracer.Color.x, tracer.Color.y, tracer.Color.z, tracer.Color.w);
            ConfigureTracerLine(CreateTracerLayer(lineObject.transform, "Glow"), start, end, color, 0.09f, 0.035f, 0.28f);
            ConfigureTracerLine(CreateTracerLayer(lineObject.transform, "Core"), start, end, Color.Lerp(color, Color.white, 0.35f), 0.035f, 0.01f, 1f);
            Destroy(lineObject, 0.08f);
            _entityManager.DestroyEntity(entity);
        }

        entities.Dispose();
    }

    private static LineRenderer CreateTracerLayer(Transform parent, string name)
    {
        GameObject layer = new(name);
        layer.transform.SetParent(parent, false);
        LineRenderer line = layer.AddComponent<LineRenderer>();
        RuntimeRendererUtility.ConfigureLine(line);
        return line;
    }

    private static void ConfigureTracerLine(LineRenderer line, Vector3 start, Vector3 end, Color color, float startWidth, float endWidth, float alphaMultiplier)
    {
        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        line.startWidth = startWidth;
        line.endWidth = endWidth;
        line.numCapVertices = 2;
        line.numCornerVertices = 2;
        color.a *= alphaMultiplier;
        line.startColor = color;
        line.endColor = color;
    }

    private void PresentRangedProjectiles()
    {
        NativeArray<Entity> entities = _rangedProjectileQuery.ToEntityArray(Allocator.Temp);
        foreach (Entity entity in entities)
        {
            RangedProjectileComponent projectile = _entityManager.GetComponentData<RangedProjectileComponent>(entity);
            if (_rangedProjectileViews.TryGetValue(entity, out RangedProjectilePresentation presentation) == false)
            {
                presentation = new RangedProjectilePresentation
                {
                    Projectile = CreateMarker(PrimitiveType.Sphere, new Color(1f, 0.25f, 0.2f), 0.18f),
                    Marker = CreateMarker(PrimitiveType.Cylinder, new Color(1f, 0.2f, 0.15f, 0.35f), projectile.ImpactRadius * 2f),
                    Shadow = CreateMarker(PrimitiveType.Sphere, new Color(0f, 0f, 0f, 0.45f), 0.16f)
                };
                presentation.Marker.transform.localScale = new Vector3(projectile.ImpactRadius * 2f, 0.015f, projectile.ImpactRadius * 2f);
                presentation.Shadow.transform.localScale = new Vector3(0.2f, 0.015f, 0.2f);
                _rangedProjectileViews.Add(entity, presentation);
            }

            LocalTransform transform = _entityManager.GetComponentData<LocalTransform>(entity);
            presentation.Projectile.transform.position = new Vector3(transform.Position.x, transform.Position.y, transform.Position.z);
            float markerGroundHeight = GetGroundHeight(projectile.ImpactPoint);
            float shadowGroundHeight = GetGroundHeight(transform.Position);
            presentation.Marker.transform.position = new Vector3(projectile.ImpactPoint.x, markerGroundHeight + 0.02f, projectile.ImpactPoint.z);
            presentation.Shadow.transform.position = new Vector3(transform.Position.x, shadowGroundHeight + 0.02f, transform.Position.z);
        }

        entities.Dispose();
        CleanRangedProjectileViews();
    }

    private void PresentDamageNumbers()
    {
        NativeArray<Entity> entities = _damageNumberQuery.ToEntityArray(Allocator.Temp);
        foreach (Entity entity in entities)
        {
            DamageNumberEvent damageNumber = _entityManager.GetComponentData<DamageNumberEvent>(entity);
            int lane = _damageNumberSequence++ % 3 - 1;
            _damageNumbers.Add(new DamageNumberPresentation
            {
                Position = new Vector3(damageNumber.Position.x, damageNumber.Position.y, damageNumber.Position.z) + Vector3.up * 1.5f,
                Amount = damageNumber.Amount,
                StartedAt = Time.unscaledTime,
                HorizontalOffset = lane * 14f
            });
            _entityManager.DestroyEntity(entity);
        }

        entities.Dispose();

        float now = Time.unscaledTime;
        for (int index = _damageNumbers.Count - 1; index >= 0; index--)
        {
            if (now - _damageNumbers[index].StartedAt >= DamageNumberDuration)
            {
                _damageNumbers.RemoveAt(index);
            }
        }
    }

    private void OnGUI()
    {
        if (_damageNumbers.Count == 0)
        {
            return;
        }

        RuntimeGuiPresentation.ApplyFontToCurrentSkin();
        Camera camera = Camera.main;
        if (camera == null)
        {
            return;
        }

        _damageNumberStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 26,
            fontStyle = FontStyle.Bold
        };
        RuntimeGuiPresentation.ApplyFont(_damageNumberStyle);
        _damageNumberShadowStyle ??= new GUIStyle(_damageNumberStyle);

        float now = Time.unscaledTime;
        foreach (DamageNumberPresentation damageNumber in _damageNumbers)
        {
            float age = now - damageNumber.StartedAt;
            if (age < 0f || age >= DamageNumberDuration)
            {
                continue;
            }

            Vector3 screenPosition = camera.WorldToScreenPoint(damageNumber.Position);
            if (screenPosition.z <= 0f)
            {
                continue;
            }

            float progress = age / DamageNumberDuration;
            float alpha = 1f - Mathf.SmoothStep(0f, 1f, progress);
            float x = screenPosition.x + damageNumber.HorizontalOffset - 45f;
            float y = Screen.height - screenPosition.y - 25f - progress * 55f;
            Rect labelRect = new(x, y, 90f, 40f);
            string label = damageNumber.Amount.ToString();

            _damageNumberShadowStyle.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.9f);
            GUI.Label(new Rect(labelRect.x + 2f, labelRect.y + 2f, labelRect.width, labelRect.height), label, _damageNumberShadowStyle);
            _damageNumberStyle.normal.textColor = new Color(1f, 0.82f, 0.16f, alpha);
            GUI.Label(labelRect, label, _damageNumberStyle);
        }
    }

    private GameObject CreateMarker(PrimitiveType type, Color color, float scale)
    {
        GameObject marker = GameObject.CreatePrimitive(type);
        marker.name = "CombatRuntimeMarker";
        marker.transform.localScale = Vector3.one * scale;
        Destroy(marker.GetComponent<Collider>());
        RuntimeRendererUtility.ConfigureMesh(marker.GetComponent<Renderer>(), color);
        return marker;
    }

    private void CleanViews(Dictionary<Entity, GameObject> views, System.Type componentType)
    {
        var removed = new List<Entity>();
        foreach ((Entity entity, GameObject view) in views)
        {
            if (_entityManager.Exists(entity) && _entityManager.HasComponent(entity, ComponentType.ReadOnly(componentType)))
            {
                continue;
            }

            Destroy(view);
            removed.Add(entity);
        }

        foreach (Entity entity in removed)
        {
            views.Remove(entity);
        }
    }

    private void CleanRangedProjectileViews()
    {
        var removed = new List<Entity>();
        foreach ((Entity entity, RangedProjectilePresentation presentation) in _rangedProjectileViews)
        {
            if (_entityManager.Exists(entity) && _entityManager.HasComponent<RangedProjectileComponent>(entity))
            {
                continue;
            }

            Destroy(presentation.Projectile);
            Destroy(presentation.Marker);
            Destroy(presentation.Shadow);
            removed.Add(entity);
        }

        foreach (Entity entity in removed)
        {
            _rangedProjectileViews.Remove(entity);
        }
    }

    private static string GetDefeatReason(DamageSource source)
    {
        return source switch
        {
            DamageSource.EnemyRangedProjectile => "Дальний снаряд",
            DamageSource.EnemyContact => "Контакт с противником",
            _ => "Неизвестная угроза"
        };
    }

    private static float GetGroundHeight(Unity.Mathematics.float3 position)
    {
        Vector3 samplePosition = new(position.x, position.y, position.z);
        if (NavMesh.SamplePosition(samplePosition + Vector3.up * 2f, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            return hit.position.y;
        }

        return samplePosition.y;
    }

    private void OnDestroy()
    {
        foreach (GameObject view in _projectileViews.Values) Destroy(view);
        foreach (GameObject view in _pickupViews.Values) Destroy(view);
        foreach (RangedProjectilePresentation presentation in _rangedProjectileViews.Values)
        {
            Destroy(presentation.Projectile);
            Destroy(presentation.Marker);
            Destroy(presentation.Shadow);
        }
    }

    private sealed class RangedProjectilePresentation
    {
        public GameObject Projectile;
        public GameObject Marker;
        public GameObject Shadow;
    }

    private sealed class DamageNumberPresentation
    {
        public Vector3 Position;
        public int Amount;
        public float StartedAt;
        public float HorizontalOffset;
    }
}

internal static class RuntimeRendererUtility
{
    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");

    public static void ConfigureMesh(Renderer renderer, Color color)
    {
        if (renderer == null)
        {
            return;
        }

        RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
        Material material = color.a < 0.999f
            ? pipeline?.defaultParticleMaterial
            : pipeline?.defaultMaterial;
        if (material != null)
        {
            renderer.sharedMaterial = material;
        }

        SetColor(renderer, color);
    }

    public static void ConfigureLine(LineRenderer line)
    {
        if (line == null)
        {
            return;
        }

        Material material = GraphicsSettings.currentRenderPipeline?.defaultLineMaterial;
        if (material != null)
        {
            line.sharedMaterial = material;
        }
    }

    public static void SetColor(Renderer renderer, Color color)
    {
        if (renderer == null)
        {
            return;
        }

        MaterialPropertyBlock properties = new();
        renderer.GetPropertyBlock(properties);
        properties.SetColor(BaseColorProperty, color);
        properties.SetColor(ColorProperty, color);
        renderer.SetPropertyBlock(properties);
    }
}
