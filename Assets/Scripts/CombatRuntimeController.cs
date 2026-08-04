using System.Collections.Generic;
using Assets.Scripts.Ecs;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class CombatRuntimeController : MonoBehaviour
{
    private readonly Dictionary<Entity, GameObject> _projectileViews = new();
    private readonly Dictionary<Entity, GameObject> _pickupViews = new();
    private readonly Dictionary<Entity, RangedProjectilePresentation> _rangedProjectileViews = new();

    private World _world;
    private EntityManager _entityManager;
    private Entity _playerEntity;
    private EntityQuery _projectileQuery;
    private EntityQuery _pickupQuery;
    private EntityQuery _tracerQuery;
    private EntityQuery _rangedProjectileQuery;
    private CombatHudView _hud;
    private Transform _playerVisual;
    private float _defeatUntil = -1f;

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
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.SetPosition(0, new Vector3(tracer.Start.x, tracer.Start.y + 0.35f, tracer.Start.z));
            line.SetPosition(1, new Vector3(tracer.End.x, tracer.End.y + 0.35f, tracer.End.z));
            line.startWidth = 0.035f;
            line.endWidth = 0.015f;
            line.startColor = new Color(tracer.Color.x, tracer.Color.y, tracer.Color.z, tracer.Color.w);
            line.endColor = line.startColor;
            Destroy(lineObject, 0.06f);
            _entityManager.DestroyEntity(entity);
        }

        entities.Dispose();
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

    private GameObject CreateMarker(PrimitiveType type, Color color, float scale)
    {
        GameObject marker = GameObject.CreatePrimitive(type);
        marker.name = "CombatRuntimeMarker";
        marker.transform.localScale = Vector3.one * scale;
        Destroy(marker.GetComponent<Collider>());
        marker.GetComponent<Renderer>().material.color = color;
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
}
