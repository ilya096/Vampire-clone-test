using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Ecs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Runtime-only final encounter for the central O arena. The boss is represented
/// by a lightweight ECS damage target while movement, bounded presentation and
/// deterministic attack sequencing remain in this controller.
/// </summary>
public sealed class FinalBossRuntimeController : MonoBehaviour
{
    public enum EncounterState
    {
        Dormant,
        PhaseOne,
        Transition,
        PhaseTwo,
        Victory,
        Defeated
    }

    private enum AttackKind
    {
        Sector,
        Radial,
        Beam
    }

    private sealed class RadialProjectile
    {
        public GameObject View;
        public Vector3 Position;
        public Vector3 Velocity;
        public float RemainingSeconds;
    }

    private sealed class HazardZone
    {
        public GameObject View;
        public Vector3 Center;
        public float RemainingSeconds;
        public float DamageAccumulator;
    }

    public const int BossMaxHealth = 8000;
    public const float PhaseTwoThreshold = 0.5f;
    public const float PhaseTransitionSeconds = 2f;
    public const float BossMoveSpeed = 1.5f;
    public const int ContactDamage = 15;
    public const float ContactCooldownSeconds = 1f;
    public const float TelegraphSeconds = 1.2f;
    public const float AttackCooldownSeconds = 2.5f;
    public const int SectorDamage = 20;
    public const float SectorAngleDegrees = 70f;
    public const float SectorRange = 8f;
    public const int RadialDamage = 20;
    public const int RadialDirectionCount = 16;
    public const float RadialSafeGapDegrees = 45f;
    public const int BeamDamage = 25;
    public const float BeamRotationSeconds = 8f;
    public const int HazardZoneCount = 3;
    public const float HazardRadius = 1.5f;
    public const float HazardDurationSeconds = 6f;
    public const int HazardDamagePerSecond = 10;

    private const float RadialProjectileSpeed = 8f;
    private const float RadialProjectileLifetime = 3f;
    private const float RadialCollisionRadius = 0.55f;
    private const float BeamCollisionRadius = 0.65f;
    private const float BeamDamageCooldownSeconds = 1f;
    private const float VictoryFadeSeconds = 1.1f;

    private readonly List<RadialProjectile> _radialProjectiles = new();
    private readonly List<HazardZone> _hazards = new();
    private readonly List<Entity> _victoryEnemies = new();
    private readonly Dictionary<int, AudioClip> _audioCues = new();

    private World _world;
    private EntityManager _entityManager;
    private Entity _playerEntity;
    private Entity _bossEntity;
    private ArenaRouteController _arenaRoute;
    private ArenaRouteLayout _layout;
    private GameObject _bossVisual;
    private Transform _coreVisual;
    private Transform _shellVisual;
    private NavMeshAgent _agent;
    private AudioSource _audioSource;
    private GameObject _telegraphRoot;
    private LineRenderer _beamLine;
    private AttackKind _telegraphedAttack;
    private Vector3 _lockedAttackDirection = Vector3.forward;
    private float _radialGapCenterDegrees;
    private float _telegraphRemaining;
    private float _attackCooldown;
    private float _transitionRemaining;
    private float _contactCooldown;
    private float _beamElapsed;
    private float _beamDamageCooldown;
    private float _beamStartAngle;
    private float _victoryCleanupRemaining;
    private float _arenaRadius = 12f;
    private Vector3 _arenaCenter;
    private int _attackSequence;
    private bool _initialized;
    private bool _started;
    private bool _telegraphActive;
    private bool _beamActive;
    private bool _victoryPublished;
    private bool _victoryCleanupPublished;
    private bool _defeatCleanupPublished;
    private string _announcement = string.Empty;
    private float _announcementRemaining;

    public EncounterState State { get; private set; } = EncounterState.Dormant;
    public bool IsActive => State is EncounterState.PhaseOne or EncounterState.Transition or EncounterState.PhaseTwo;
    public int CurrentHealth => BossExists
        ? _entityManager.GetComponentData<HealthComponent>(_bossEntity).Value
        : 0;
    public event Action VictoryPublished;
    public event Action VictoryCleanupCompleted;
    public event Action DefeatCleanupCompleted;

    private bool BossExists => _world != null
        && _world.IsCreated
        && _bossEntity != Entity.Null
        && _entityManager.Exists(_bossEntity);

    public void Initialize(World world, Entity playerEntity, ArenaRouteController arenaRoute)
    {
        _world = world;
        _entityManager = world.EntityManager;
        _playerEntity = playerEntity;
        _arenaRoute = arenaRoute;
        _victoryPublished = false;
        _victoryCleanupPublished = false;
        _defeatCleanupPublished = false;
        _layout = arenaRoute != null ? arenaRoute.Layout : FindAnyObjectByType<ArenaRouteLayout>();

        if (_arenaRoute == null || _layout == null || _layout.BossBoundaryO == null)
        {
            Debug.LogError("Final boss is disabled: arena O boundary configuration is missing.");
            enabled = false;
            return;
        }

        _arenaRoute.BossArenaOpened += HandleBossArenaOpened;
        _initialized = true;
        if (_arenaRoute.Phase == ArenaRouteController.RoutePhase.BossArena)
        {
            HandleBossArenaOpened();
        }
    }

    public bool DamageBossForDebug(int amount)
    {
        if (BossExists == false || State is EncounterState.Transition or EncounterState.Victory or EncounterState.Defeated)
        {
            return false;
        }

        HealthComponent health = _entityManager.GetComponentData<HealthComponent>(_bossEntity);
        health.Value = math.max(0, health.Value - math.max(0, amount));
        _entityManager.SetComponentData(_bossEntity, health);
        return true;
    }

    public bool AdvanceForDebug()
    {
        if (BossExists == false)
        {
            return false;
        }

        if (State == EncounterState.PhaseOne)
        {
            HealthComponent health = _entityManager.GetComponentData<HealthComponent>(_bossEntity);
            health.Value = math.min(health.Value, BossMaxHealth / 2);
            _entityManager.SetComponentData(_bossEntity, health);
            return true;
        }

        if (State == EncounterState.Transition)
        {
            _transitionRemaining = 0f;
            return true;
        }

        if (State == EncounterState.PhaseTwo)
        {
            return DamageBossForDebug(BossMaxHealth);
        }

        return false;
    }

    public bool StopForPlayerDefeat()
    {
        if (IsActive == false)
        {
            return false;
        }

        StopForDefeat();
        return true;
    }

    public static bool ValidateDefaults(out string error)
    {
        int safeDirections = 0;
        for (int index = 0; index < RadialDirectionCount; index++)
        {
            if (FinalBossAttackMath.IsDirectionInsideSafeGap(index, RadialDirectionCount, 0f, RadialSafeGapDegrees))
            {
                safeDirections++;
            }
        }

        if (safeDirections < 2 || safeDirections >= RadialDirectionCount)
        {
            error = $"Radial safe-gap configuration leaves {safeDirections}/{RadialDirectionCount} safe directions.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void HandleBossArenaOpened()
    {
        if (_started)
        {
            Debug.LogWarning("[Logo Survivor][FinalBoss] Repeated boss-start signal ignored.");
            return;
        }

        _started = true;
        SpawnBoss();
    }

    private void SpawnBoss()
    {
        Vector3 desiredCenter = _layout.BossBoundaryO.transform.position;
        _arenaCenter = desiredCenter;
        Vector3 spawnPosition = desiredCenter;
        bool onNavMesh = NavMesh.SamplePosition(desiredCenter + Vector3.up * 2f, out NavMeshHit hit, 8f, NavMesh.AllAreas);
        if (onNavMesh)
        {
            spawnPosition = hit.position;
        }
        else
        {
            Debug.LogError($"[Logo Survivor][FinalBoss] Boss center is not on NavMesh: {desiredCenter}.");
        }

        Vector3 radiusX = _layout.BossBoundaryO.transform.TransformVector(Vector3.right * _layout.BossBoundaryO.LocalRadius);
        Vector3 radiusZ = _layout.BossBoundaryO.transform.TransformVector(Vector3.forward * _layout.BossBoundaryO.LocalRadius);
        _arenaRadius = Mathf.Clamp(Mathf.Max(radiusX.magnitude, radiusZ.magnitude), 8f, 30f);

        _bossEntity = _entityManager.CreateEntity(
            typeof(EnemyTag),
            typeof(BossTag),
            typeof(HealthComponent),
            typeof(LocalTransform));
        _entityManager.SetComponentData(_bossEntity, new HealthComponent
        {
            Value = BossMaxHealth,
            MaxValue = BossMaxHealth
        });
        _entityManager.SetComponentData(_bossEntity, LocalTransform.FromPosition(new float3(spawnPosition.x, spawnPosition.y, spawnPosition.z)));

        CreateBossPresentation(spawnPosition, onNavMesh);
        State = EncounterState.PhaseOne;
        _attackCooldown = 1.25f;
        Announce("ЦЕНТР ОТКРЫТ · КРАСНОЕ ЯДРО", 2.5f);
        PlayCue(510f, 0.2f);
        Debug.Log($"[Logo Survivor][FinalBoss] Started at {spawnPosition}; HP={BossMaxHealth}; arenaRadius={_arenaRadius:F1}.");
    }

    private void CreateBossPresentation(Vector3 position, bool navMeshAvailable)
    {
        _bossVisual = new GameObject("FinalBoss_RedCore");
        _bossVisual.transform.position = position;

        GameObject core = CreatePrimitive(PrimitiveType.Cube, "Core", _bossVisual.transform, new Color(0.95f, 0.05f, 0.04f), new Vector3(1.7f, 1.7f, 1.7f));
        core.transform.localPosition = Vector3.up * 1.1f;
        core.transform.localRotation = Quaternion.Euler(35f, 45f, 20f);
        _coreVisual = core.transform;

        GameObject shell = CreatePrimitive(PrimitiveType.Cube, "OuterShell", _bossVisual.transform, new Color(1f, 0.35f, 0.12f, 0.28f), new Vector3(2.25f, 2.25f, 2.25f));
        shell.transform.localPosition = Vector3.up * 1.1f;
        shell.transform.localRotation = Quaternion.Euler(-25f, 18f, 45f);
        _shellVisual = shell.transform;
        CreateCircleLine("CoreOutline", _bossVisual.transform, Vector3.up * 0.12f, 1.55f, 40, new Color(1f, 0.8f, 0.25f, 0.9f), 0.09f);

        _audioSource = _bossVisual.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0.15f;
        _audioSource.volume = 0.35f;

        if (navMeshAvailable)
        {
            _agent = _bossVisual.AddComponent<NavMeshAgent>();
            _agent.speed = BossMoveSpeed;
            _agent.acceleration = 7f;
            _agent.angularSpeed = 360f;
            _agent.radius = 0.8f;
            _agent.height = 2.2f;
            _agent.stoppingDistance = 2.25f;
            _agent.autoBraking = true;
            if (_agent.isOnNavMesh == false && _agent.Warp(position) == false)
            {
                Debug.LogError($"[Logo Survivor][FinalBoss] NavMeshAgent could not warp to {position}.");
                _agent.enabled = false;
            }
        }
    }

    private void Update()
    {
        if (_initialized == false
            || _started == false
            || _world == null
            || _world.IsCreated == false
            || _entityManager.Exists(_playerEntity) == false
            || Time.timeScale <= 0f
            || Application.isFocused == false)
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        _announcementRemaining = Mathf.Max(0f, _announcementRemaining - deltaTime);

        if (State == EncounterState.Victory)
        {
            UpdateVictoryCleanup(deltaTime);
            return;
        }
        if (State == EncounterState.Defeated || BossExists == false)
        {
            return;
        }

        HealthComponent playerHealth = _entityManager.GetComponentData<HealthComponent>(_playerEntity);
        if (playerHealth.Value <= 0)
        {
            StopForDefeat();
            return;
        }

        HealthComponent bossHealth = _entityManager.GetComponentData<HealthComponent>(_bossEntity);
        if (bossHealth.Value <= 0)
        {
            CompleteVictory();
            return;
        }

        if (State == EncounterState.PhaseOne && bossHealth.Value <= BossMaxHealth * PhaseTwoThreshold)
        {
            BeginPhaseTransition();
            return;
        }

        AnimateBoss(deltaTime);
        if (State == EncounterState.Transition)
        {
            UpdatePhaseTransition(deltaTime);
            return;
        }

        UpdateBossMovement();
        UpdateContactDamage(deltaTime);
        UpdateRadialProjectiles(deltaTime);
        UpdateHazards(deltaTime);

        if (_beamActive)
        {
            UpdateBeam(deltaTime);
            return;
        }

        if (_telegraphActive)
        {
            UpdateTelegraph(deltaTime);
            return;
        }

        _attackCooldown -= deltaTime;
        if (_attackCooldown <= 0f)
        {
            BeginNextAttack();
        }
    }

    private void UpdateBossMovement()
    {
        if (_bossVisual == null || _telegraphActive || _beamActive)
        {
            SetAgentStopped(true);
            return;
        }

        LocalTransform playerTransform = _entityManager.GetComponentData<LocalTransform>(_playerEntity);
        Vector3 playerPosition = new(playerTransform.Position.x, playerTransform.Position.y, playerTransform.Position.z);
        float movementRadius = Mathf.Max(2f, _arenaRadius - 1.8f);
        playerPosition = FinalBossAttackMath.ClampToCircleXZ(playerPosition, _arenaCenter, movementRadius);
        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            _agent.isStopped = false;
            _agent.SetDestination(playerPosition);
        }

        Vector3 position = _bossVisual.transform.position;
        _entityManager.SetComponentData(_bossEntity, LocalTransform.FromPosition(new float3(position.x, position.y, position.z)));
    }

    private void UpdateContactDamage(float deltaTime)
    {
        _contactCooldown = Mathf.Max(0f, _contactCooldown - deltaTime);
        if (_contactCooldown > 0f)
        {
            return;
        }

        Vector3 playerPosition = GetPlayerPosition();
        if (PlanarDistanceSquared(playerPosition, GetBossPosition()) <= 1.8f * 1.8f)
        {
            RequestPlayerDamage(ContactDamage, DamageSource.BossContact);
            _contactCooldown = ContactCooldownSeconds;
        }
    }

    private void BeginNextAttack()
    {
        _telegraphedAttack = State == EncounterState.PhaseOne
            ? (_attackSequence % 2 == 0 ? AttackKind.Sector : AttackKind.Radial)
            : (_attackSequence % 3) switch
            {
                0 => AttackKind.Sector,
                1 => AttackKind.Radial,
                _ => AttackKind.Beam
            };
        _attackSequence++;

        Vector3 toPlayer = GetPlayerPosition() - GetBossPosition();
        toPlayer.y = 0f;
        _lockedAttackDirection = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : Vector3.forward;
        _radialGapCenterDegrees = Mathf.Repeat((_attackSequence - 1) * 22.5f, 180f);
        _telegraphRemaining = TelegraphSeconds;
        _telegraphActive = true;
        SetAgentStopped(true);
        CreateTelegraph(_telegraphedAttack);

        float frequency = _telegraphedAttack switch
        {
            AttackKind.Sector => 330f,
            AttackKind.Radial => 440f,
            _ => 220f
        };
        PlayCue(frequency, 0.14f);
        Debug.Log($"[Logo Survivor][FinalBoss] Telegraph {_telegraphedAttack}; phase={State}.");
    }

    private void UpdateTelegraph(float deltaTime)
    {
        _telegraphRemaining -= deltaTime;
        if (_telegraphRoot != null)
        {
            _telegraphRoot.transform.position = GetBossPosition() + Vector3.up * 0.04f;
            float pulse = 1f + Mathf.Sin((TelegraphSeconds - _telegraphRemaining) * 16f) * 0.05f;
            _telegraphRoot.transform.localScale = Vector3.one * pulse;
        }

        if (_telegraphRemaining > 0f)
        {
            return;
        }

        _telegraphActive = false;
        ResolveTelegraphedAttack();
    }

    private void ResolveTelegraphedAttack()
    {
        Vector3 bossPosition = GetBossPosition();
        switch (_telegraphedAttack)
        {
            case AttackKind.Sector:
                if (FinalBossAttackMath.IsInsideSector(bossPosition, _lockedAttackDirection, GetPlayerPosition(), SectorAngleDegrees, SectorRange))
                {
                    RequestPlayerDamage(SectorDamage, DamageSource.BossSector);
                }
                DestroyTelegraph();
                _attackCooldown = AttackCooldownSeconds;
                break;

            case AttackKind.Radial:
                SpawnRadialVolley(bossPosition);
                DestroyTelegraph();
                _attackCooldown = AttackCooldownSeconds;
                break;

            case AttackKind.Beam:
                DestroyTelegraph();
                BeginBeam();
                break;
        }
    }

    private void SpawnRadialVolley(Vector3 origin)
    {
        for (int index = 0; index < RadialDirectionCount; index++)
        {
            if (FinalBossAttackMath.IsDirectionInsideSafeGap(index, RadialDirectionCount, _radialGapCenterDegrees, RadialSafeGapDegrees))
            {
                continue;
            }

            float angle = index / (float)RadialDirectionCount * 360f;
            Quaternion volleyRotation = Quaternion.LookRotation(_lockedAttackDirection, Vector3.up);
            Vector3 direction = volleyRotation * (Quaternion.Euler(0f, angle, 0f) * Vector3.forward);
            GameObject view = CreatePrimitive(
                PrimitiveType.Sphere,
                $"BossRadial_{index:00}",
                transform,
                new Color(1f, 0.18f, 0.05f),
                Vector3.one * 0.34f);
            Vector3 start = origin + Vector3.up * 0.35f + direction * 1.2f;
            view.transform.position = start;
            _radialProjectiles.Add(new RadialProjectile
            {
                View = view,
                Position = start,
                Velocity = direction * RadialProjectileSpeed,
                RemainingSeconds = RadialProjectileLifetime
            });
        }

        PlayCue(620f, 0.1f);
    }

    private void UpdateRadialProjectiles(float deltaTime)
    {
        Vector3 playerPosition = GetPlayerPosition();
        for (int index = _radialProjectiles.Count - 1; index >= 0; index--)
        {
            RadialProjectile projectile = _radialProjectiles[index];
            Vector3 start = projectile.Position;
            Vector3 end = start + projectile.Velocity * deltaTime;
            projectile.Position = end;
            projectile.RemainingSeconds -= deltaTime;
            if (projectile.View != null)
            {
                projectile.View.transform.position = end;
            }

            bool hit = FinalBossAttackMath.DistanceToSegmentXZ(playerPosition, start, end) <= RadialCollisionRadius;
            if (hit)
            {
                RequestPlayerDamage(RadialDamage, DamageSource.BossRadial);
            }
            if (hit || projectile.RemainingSeconds <= 0f)
            {
                if (projectile.View != null)
                {
                    Destroy(projectile.View);
                }
                _radialProjectiles.RemoveAt(index);
            }
        }
    }

    private void BeginBeam()
    {
        _beamActive = true;
        _beamElapsed = 0f;
        _beamDamageCooldown = 0f;
        _beamStartAngle = Mathf.Atan2(_lockedAttackDirection.x, _lockedAttackDirection.z) * Mathf.Rad2Deg;
        GameObject lineObject = new("BossRotatingBeam");
        lineObject.transform.SetParent(transform, false);
        _beamLine = lineObject.AddComponent<LineRenderer>();
        RuntimeRendererUtility.ConfigureLine(_beamLine);
        _beamLine.positionCount = 2;
        _beamLine.startWidth = 0.85f;
        _beamLine.endWidth = 0.45f;
        _beamLine.numCapVertices = 4;
        _beamLine.startColor = new Color(1f, 0.1f, 0.03f, 0.92f);
        _beamLine.endColor = new Color(1f, 0.75f, 0.12f, 0.72f);
        SpawnHazards();
        PlayCue(250f, 0.24f);
    }

    private void UpdateBeam(float deltaTime)
    {
        _beamElapsed += deltaTime;
        _beamDamageCooldown = Mathf.Max(0f, _beamDamageCooldown - deltaTime);
        float angle = _beamStartAngle + FinalBossAttackMath.GetBeamAngle(_beamElapsed, BeamRotationSeconds);
        Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
        Vector3 start = GetBossPosition() + Vector3.up * 0.45f;
        Vector3 end = start + direction * Mathf.Max(10f, _arenaRadius * 1.05f);
        if (_beamLine != null)
        {
            _beamLine.SetPosition(0, start);
            _beamLine.SetPosition(1, end);
        }

        if (_beamDamageCooldown <= 0f
            && FinalBossAttackMath.DistanceToSegmentXZ(GetPlayerPosition(), start, end) <= BeamCollisionRadius)
        {
            RequestPlayerDamage(BeamDamage, DamageSource.BossBeam);
            _beamDamageCooldown = BeamDamageCooldownSeconds;
        }

        if (_beamElapsed < BeamRotationSeconds)
        {
            return;
        }

        _beamActive = false;
        if (_beamLine != null)
        {
            Destroy(_beamLine.gameObject);
            _beamLine = null;
        }
        _attackCooldown = AttackCooldownSeconds;
    }

    private void SpawnHazards()
    {
        Vector3 center = _layout.BossBoundaryO.transform.position;
        float distance = Mathf.Min(6f, _arenaRadius * 0.42f);
        float rotation = Mathf.Repeat(_attackSequence * 37f, 120f);
        for (int index = 0; index < HazardZoneCount; index++)
        {
            float angle = rotation + index * (360f / HazardZoneCount);
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Vector3 position = center + direction * distance;
            if (NavMesh.SamplePosition(position + Vector3.up * 2f, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                position = hit.position;
            }

            GameObject view = CreatePrimitive(
                PrimitiveType.Cylinder,
                $"BossHazard_{index + 1}",
                transform,
                new Color(1f, 0.12f, 0.02f, 0.38f),
                new Vector3(HazardRadius * 2f, 0.035f, HazardRadius * 2f));
            view.transform.position = position + Vector3.up * 0.035f;
            CreateCircleLine("HazardOutline", view.transform, Vector3.up * 0.1f, 0.5f, 28, new Color(1f, 0.85f, 0.15f, 0.95f), 0.04f);
            _hazards.Add(new HazardZone
            {
                View = view,
                Center = position,
                RemainingSeconds = HazardDurationSeconds,
                DamageAccumulator = 0f
            });
        }

        PlayCue(560f, 0.12f);
    }

    private void UpdateHazards(float deltaTime)
    {
        Vector3 playerPosition = GetPlayerPosition();
        for (int index = _hazards.Count - 1; index >= 0; index--)
        {
            HazardZone hazard = _hazards[index];
            hazard.RemainingSeconds -= deltaTime;
            bool playerInside = PlanarDistanceSquared(playerPosition, hazard.Center) <= HazardRadius * HazardRadius;
            if (playerInside)
            {
                hazard.DamageAccumulator += deltaTime;
                while (hazard.DamageAccumulator >= 1f)
                {
                    hazard.DamageAccumulator -= 1f;
                    RequestPlayerDamage(HazardDamagePerSecond, DamageSource.BossHazard);
                }
            }
            else
            {
                hazard.DamageAccumulator = 0f;
            }

            if (hazard.View != null)
            {
                float pulse = 0.96f + Mathf.Sin(hazard.RemainingSeconds * 7f) * 0.04f;
                hazard.View.transform.localScale = new Vector3(HazardRadius * 2f * pulse, 0.035f, HazardRadius * 2f * pulse);
            }

            if (hazard.RemainingSeconds <= 0f)
            {
                if (hazard.View != null)
                {
                    Destroy(hazard.View);
                }
                _hazards.RemoveAt(index);
            }
        }
    }

    private void BeginPhaseTransition()
    {
        State = EncounterState.Transition;
        _transitionRemaining = PhaseTransitionSeconds;
        if (_entityManager.HasComponent<BossInvulnerableTag>(_bossEntity) == false)
        {
            _entityManager.AddComponent<BossInvulnerableTag>(_bossEntity);
        }
        ClearBossAttacks();
        SetAgentStopped(true);
        Announce("ЯДРО ПЕРЕСТРАИВАЕТСЯ", 2f);
        PlayCue(170f, 0.35f);
        Debug.Log("[Logo Survivor][FinalBoss] Phase transition started at 50% HP.");
    }

    private void UpdatePhaseTransition(float deltaTime)
    {
        _transitionRemaining -= deltaTime;
        if (_shellVisual != null)
        {
            float pulse = 1f + Mathf.Sin((PhaseTransitionSeconds - _transitionRemaining) * 18f) * 0.16f;
            _shellVisual.localScale = Vector3.one * (2.25f * pulse);
        }
        if (_transitionRemaining > 0f)
        {
            return;
        }

        if (_entityManager.HasComponent<BossInvulnerableTag>(_bossEntity))
        {
            _entityManager.RemoveComponent<BossInvulnerableTag>(_bossEntity);
        }
        if (_shellVisual != null)
        {
            _shellVisual.localScale = Vector3.one * 2.55f;
        }
        State = EncounterState.PhaseTwo;
        _attackCooldown = 0.8f;
        Announce("ФАЗА 2", 1.8f);
        PlayCue(700f, 0.22f);
        Debug.Log("[Logo Survivor][FinalBoss] Phase two started.");
    }

    private void CompleteVictory()
    {
        State = EncounterState.Victory;
        if (_entityManager.Exists(_playerEntity)
            && _entityManager.HasComponent<SessionCombatStats>(_playerEntity))
        {
            SessionCombatStats stats = _entityManager.GetComponentData<SessionCombatStats>(_playerEntity);
            stats.ConfirmedKills++;
            _entityManager.SetComponentData(_playerEntity, stats);
        }
        if (_entityManager.HasComponent<BossInvulnerableTag>(_bossEntity) == false)
        {
            _entityManager.AddComponent<BossInvulnerableTag>(_bossEntity);
        }
        ClearBossAttacks();
        ClearCombatProjectilesAndPendingDamage();
        SetAgentStopped(true);
        DisableAndFadeCarryOverEnemies();
        _victoryCleanupRemaining = VictoryFadeSeconds;
        Announce("ПОБЕДА", 3f);
        PlayCue(760f, 0.45f);
        Debug.Log("[Logo Survivor][FinalBoss] Victory published; hazards cleared and carry-over enemies disabled.");

        if (_victoryPublished == false)
        {
            _victoryPublished = true;
            VictoryPublished?.Invoke();
        }
    }

    private void StopForDefeat()
    {
        State = EncounterState.Defeated;
        ClearBossAttacks();
        SetAgentStopped(true);
        Debug.Log("[Logo Survivor][FinalBoss] Defeat detected; boss hazards stopped for session result.");
        if (_defeatCleanupPublished == false)
        {
            _defeatCleanupPublished = true;
            DefeatCleanupCompleted?.Invoke();
        }
    }

    private void DisableAndFadeCarryOverEnemies()
    {
        EntityQuery query = _entityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<EnemyTag>() },
            None = new[] { ComponentType.ReadOnly<BossTag>() }
        });
        NativeArray<Entity> enemies = query.ToEntityArray(Allocator.Temp);
        EnemyViewSynchronizator synchronizator = ServiceLocator.Get<EnemyViewSynchronizator>();
        foreach (Entity enemy in enemies)
        {
            if (_entityManager.HasComponent<CombatDisabledTag>(enemy) == false)
            {
                _entityManager.AddComponent<CombatDisabledTag>(enemy);
            }
            synchronizator.FadeAndReturnToPool(enemy, VictoryFadeSeconds);
            _victoryEnemies.Add(enemy);
        }
        enemies.Dispose();
        query.Dispose();
    }

    private void UpdateVictoryCleanup(float deltaTime)
    {
        _victoryCleanupRemaining -= deltaTime;
        if (_bossVisual != null)
        {
            float scale = Mathf.Clamp01(_victoryCleanupRemaining / VictoryFadeSeconds);
            _bossVisual.transform.localScale = Vector3.one * scale;
        }
        if (_victoryCleanupRemaining > 0f)
        {
            return;
        }

        foreach (Entity enemy in _victoryEnemies)
        {
            if (_entityManager.Exists(enemy))
            {
                _entityManager.DestroyEntity(enemy);
            }
        }
        _victoryEnemies.Clear();
        if (BossExists)
        {
            _entityManager.DestroyEntity(_bossEntity);
            _bossEntity = Entity.Null;
        }
        if (_bossVisual != null)
        {
            Destroy(_bossVisual);
            _bossVisual = null;
        }
        _victoryCleanupRemaining = float.PositiveInfinity;
        if (_victoryCleanupPublished == false)
        {
            _victoryCleanupPublished = true;
            VictoryCleanupCompleted?.Invoke();
        }
    }

    private void ClearCombatProjectilesAndPendingDamage()
    {
        DestroyEntitiesWith<ProjectileComponent>();
        DestroyEntitiesWith<RangedProjectileComponent>();
        DestroyEntitiesWith<DamageRequest>();
    }

    private void DestroyEntitiesWith<T>() where T : unmanaged, IComponentData
    {
        EntityQuery query = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
        _entityManager.DestroyEntity(query);
        query.Dispose();
    }

    private void RequestPlayerDamage(int amount, DamageSource source)
    {
        if (_entityManager.Exists(_playerEntity) == false)
        {
            return;
        }
        Entity request = _entityManager.CreateEntity(typeof(DamageRequest));
        _entityManager.SetComponentData(request, new DamageRequest
        {
            Target = _playerEntity,
            Amount = amount,
            Source = source
        });
    }

    private void AnimateBoss(float deltaTime)
    {
        if (_coreVisual != null)
        {
            _coreVisual.Rotate(new Vector3(29f, 47f, 17f) * deltaTime, Space.Self);
        }
        if (_shellVisual != null && State != EncounterState.Transition)
        {
            _shellVisual.Rotate(new Vector3(-19f, 31f, 43f) * deltaTime, Space.Self);
        }
    }

    private void CreateTelegraph(AttackKind kind)
    {
        DestroyTelegraph();
        _telegraphRoot = new GameObject($"BossTelegraph_{kind}");
        _telegraphRoot.transform.SetParent(transform, false);
        _telegraphRoot.transform.position = GetBossPosition() + Vector3.up * 0.04f;
        _telegraphRoot.transform.forward = _lockedAttackDirection;

        switch (kind)
        {
            case AttackKind.Sector:
                CreateSectorTelegraph();
                break;
            case AttackKind.Radial:
                CreateRadialTelegraph();
                break;
            case AttackKind.Beam:
                CreateBeamTelegraph();
                break;
        }
    }

    private void CreateSectorTelegraph()
    {
        GameObject fill = new("SectorFill");
        fill.transform.SetParent(_telegraphRoot.transform, false);
        MeshFilter filter = fill.AddComponent<MeshFilter>();
        MeshRenderer renderer = fill.AddComponent<MeshRenderer>();
        filter.sharedMesh = CreateSectorMesh(SectorAngleDegrees, SectorRange, 18);
        RuntimeRendererUtility.ConfigureMesh(renderer, new Color(1f, 0.16f, 0.02f, 0.34f));

        LineRenderer outline = CreateLine("SectorOutline", _telegraphRoot.transform, new Color(1f, 0.9f, 0.18f, 0.95f), 0.11f);
        int arcSegments = 18;
        outline.positionCount = arcSegments + 3;
        outline.SetPosition(0, Vector3.zero);
        for (int index = 0; index <= arcSegments; index++)
        {
            float angle = -SectorAngleDegrees * 0.5f + SectorAngleDegrees * index / arcSegments;
            outline.SetPosition(index + 1, Quaternion.Euler(0f, angle, 0f) * Vector3.forward * SectorRange);
        }
        outline.SetPosition(arcSegments + 2, Vector3.zero);
    }

    private void CreateRadialTelegraph()
    {
        CreateCircleLine("RadialOutline", _telegraphRoot.transform, Vector3.zero, 1.25f, 36, new Color(1f, 0.9f, 0.2f, 0.95f), 0.1f);
        for (int index = 0; index < RadialDirectionCount; index++)
        {
            if (FinalBossAttackMath.IsDirectionInsideSafeGap(index, RadialDirectionCount, _radialGapCenterDegrees, RadialSafeGapDegrees))
            {
                continue;
            }
            float angle = index / (float)RadialDirectionCount * 360f;
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            LineRenderer spoke = CreateLine($"RadialSpoke_{index:00}", _telegraphRoot.transform, new Color(1f, 0.22f, 0.04f, 0.72f), 0.055f);
            spoke.positionCount = 2;
            spoke.SetPosition(0, direction * 1.2f);
            spoke.SetPosition(1, direction * Mathf.Min(7f, _arenaRadius * 0.5f));
        }
    }

    private void CreateBeamTelegraph()
    {
        LineRenderer outer = CreateLine("BeamWarningOuter", _telegraphRoot.transform, new Color(1f, 0.12f, 0.02f, 0.3f), 0.8f);
        outer.positionCount = 2;
        outer.SetPosition(0, Vector3.zero);
        outer.SetPosition(1, Vector3.forward * Mathf.Max(10f, _arenaRadius));
        LineRenderer core = CreateLine("BeamWarningCore", _telegraphRoot.transform, new Color(1f, 0.95f, 0.25f, 0.95f), 0.08f);
        core.positionCount = 2;
        core.SetPosition(0, Vector3.zero);
        core.SetPosition(1, Vector3.forward * Mathf.Max(10f, _arenaRadius));
    }

    private static Mesh CreateSectorMesh(float angleDegrees, float range, int segments)
    {
        Vector3[] vertices = new Vector3[segments + 2];
        int[] triangles = new int[segments * 3];
        vertices[0] = Vector3.zero;
        for (int index = 0; index <= segments; index++)
        {
            float angle = -angleDegrees * 0.5f + angleDegrees * index / segments;
            vertices[index + 1] = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * range;
            if (index < segments)
            {
                int triangle = index * 3;
                triangles[triangle] = 0;
                triangles[triangle + 1] = index + 1;
                triangles[triangle + 2] = index + 2;
            }
        }
        Mesh mesh = new() { name = "FinalBossSectorTelegraph" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    private static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent, Color color, Vector3 scale)
    {
        GameObject result = GameObject.CreatePrimitive(type);
        result.name = name;
        result.transform.SetParent(parent, false);
        result.transform.localScale = scale;
        Collider collider = result.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }
        RuntimeRendererUtility.ConfigureMesh(result.GetComponent<Renderer>(), color);
        return result;
    }

    private static LineRenderer CreateLine(string name, Transform parent, Color color, float width)
    {
        GameObject lineObject = new(name);
        lineObject.transform.SetParent(parent, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        RuntimeRendererUtility.ConfigureLine(line);
        line.useWorldSpace = false;
        line.startWidth = width;
        line.endWidth = width;
        line.numCapVertices = 3;
        line.startColor = color;
        line.endColor = color;
        return line;
    }

    private static LineRenderer CreateCircleLine(string name, Transform parent, Vector3 localCenter, float radius, int segments, Color color, float width)
    {
        LineRenderer line = CreateLine(name, parent, color, width);
        line.loop = true;
        line.positionCount = segments;
        for (int index = 0; index < segments; index++)
        {
            float angle = index / (float)segments * Mathf.PI * 2f;
            line.SetPosition(index, localCenter + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
        return line;
    }

    private void PlayCue(float frequency, float seconds)
    {
        if (_audioSource == null)
        {
            return;
        }

        int key = Mathf.RoundToInt(frequency * 10f + seconds * 1000f);
        if (_audioCues.TryGetValue(key, out AudioClip clip) == false)
        {
            const int sampleRate = 22050;
            int sampleCount = Mathf.CeilToInt(sampleRate * seconds);
            float[] samples = new float[sampleCount];
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)sampleRate;
                float envelope = Mathf.Sin(Mathf.PI * index / Mathf.Max(1, sampleCount - 1));
                samples[index] = Mathf.Sin(Mathf.PI * 2f * frequency * time) * envelope * 0.28f;
            }
            clip = AudioClip.Create($"BossCue_{key}", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            _audioCues.Add(key, clip);
        }
        _audioSource.PlayOneShot(clip);
    }

    private void ClearBossAttacks()
    {
        _telegraphActive = false;
        _beamActive = false;
        DestroyTelegraph();
        if (_beamLine != null)
        {
            Destroy(_beamLine.gameObject);
            _beamLine = null;
        }
        foreach (RadialProjectile projectile in _radialProjectiles)
        {
            if (projectile.View != null)
            {
                Destroy(projectile.View);
            }
        }
        _radialProjectiles.Clear();
        foreach (HazardZone hazard in _hazards)
        {
            if (hazard.View != null)
            {
                Destroy(hazard.View);
            }
        }
        _hazards.Clear();
    }

    private void DestroyTelegraph()
    {
        if (_telegraphRoot != null)
        {
            Destroy(_telegraphRoot);
            _telegraphRoot = null;
        }
    }

    private void SetAgentStopped(bool stopped)
    {
        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            _agent.isStopped = stopped;
            if (stopped)
            {
                _agent.ResetPath();
            }
        }
    }

    private Vector3 GetPlayerPosition()
    {
        LocalTransform playerTransform = _entityManager.GetComponentData<LocalTransform>(_playerEntity);
        return new Vector3(playerTransform.Position.x, playerTransform.Position.y, playerTransform.Position.z);
    }

    private Vector3 GetBossPosition()
    {
        if (_bossVisual != null)
        {
            return _bossVisual.transform.position;
        }
        if (BossExists)
        {
            LocalTransform bossTransform = _entityManager.GetComponentData<LocalTransform>(_bossEntity);
            return new Vector3(bossTransform.Position.x, bossTransform.Position.y, bossTransform.Position.z);
        }
        return _layout != null && _layout.BossBoundaryO != null
            ? _layout.BossBoundaryO.transform.position
            : Vector3.zero;
    }

    private static float PlanarDistanceSquared(Vector3 a, Vector3 b)
    {
        float x = a.x - b.x;
        float z = a.z - b.z;
        return x * x + z * z;
    }

    private void Announce(string text, float seconds)
    {
        _announcement = text;
        _announcementRemaining = seconds;
    }

    private void OnGUI()
    {
        if (_started == false || State is EncounterState.Dormant or EncounterState.Defeated)
        {
            return;
        }

        RuntimeGuiPresentation.ApplyFontToCurrentSkin();
        float width = Mathf.Clamp(Screen.width * 0.58f, 360f, 760f);
        Rect frame = new(Screen.width * 0.5f - width * 0.5f, 12f, width, 54f);
        GUI.Box(frame, string.Empty);
        Rect bar = new(frame.x + 10f, frame.y + 26f, frame.width - 20f, 18f);
        Color previousColor = GUI.color;
        GUI.color = new Color(0.12f, 0.025f, 0.02f, 0.95f);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        float normalizedHealth = BossExists ? Mathf.Clamp01(CurrentHealth / (float)BossMaxHealth) : 0f;
        GUI.color = State == EncounterState.Transition
            ? new Color(1f, 0.65f, 0.08f, 0.95f)
            : new Color(0.95f, 0.08f, 0.035f, 0.95f);
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * normalizedHealth, bar.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(bar.x + bar.width * 0.5f - 1f, bar.y - 2f, 2f, bar.height + 4f), Texture2D.whiteTexture);
        GUI.color = previousColor;

        string phase = State switch
        {
            EncounterState.PhaseOne => "ФАЗА 1",
            EncounterState.Transition => "ПЕРЕСТРОЙКА",
            EncounterState.PhaseTwo => "ФАЗА 2",
            EncounterState.Victory => "ПОБЕЖДЁН",
            _ => string.Empty
        };
        string health = BossExists ? $"{CurrentHealth} / {BossMaxHealth}" : "0 / 8000";
        GUI.Label(new Rect(frame.x + 12f, frame.y + 3f, frame.width - 24f, 22f), $"КРАСНОЕ ЯДРО  ·  {phase}  ·  {health}");

        if (_announcementRemaining > 0f)
        {
            GUI.Box(new Rect(Screen.width * 0.5f - 220f, Screen.height * 0.24f, 440f, 46f), _announcement);
        }
    }

    private void OnDestroy()
    {
        if (_arenaRoute != null)
        {
            _arenaRoute.BossArenaOpened -= HandleBossArenaOpened;
        }
        ClearBossAttacks();
        if (BossExists)
        {
            _entityManager.DestroyEntity(_bossEntity);
        }
        foreach (AudioClip clip in _audioCues.Values)
        {
            if (clip != null)
            {
                Destroy(clip);
            }
        }
    }
}

public static class FinalBossAttackMath
{
    public static bool IsInsideSector(Vector3 origin, Vector3 forward, Vector3 point, float angleDegrees, float range)
    {
        Vector3 offset = point - origin;
        offset.y = 0f;
        if (offset.sqrMagnitude > range * range)
        {
            return false;
        }
        if (offset.sqrMagnitude <= 0.0001f)
        {
            return true;
        }
        forward.y = 0f;
        return Vector3.Angle(forward.normalized, offset.normalized) <= angleDegrees * 0.5f;
    }

    public static bool IsDirectionInsideSafeGap(int directionIndex, int directionCount, float firstGapCenterDegrees, float gapDegrees)
    {
        if (directionCount <= 0)
        {
            return false;
        }
        float direction = directionIndex / (float)directionCount * 360f;
        float halfGap = Mathf.Max(0f, gapDegrees * 0.5f - 0.001f);
        return Mathf.Abs(Mathf.DeltaAngle(direction, firstGapCenterDegrees)) < halfGap
            || Mathf.Abs(Mathf.DeltaAngle(direction, firstGapCenterDegrees + 180f)) < halfGap;
    }

    public static float GetBeamAngle(float elapsed, float rotationSeconds)
    {
        return Mathf.Clamp01(elapsed / Mathf.Max(0.001f, rotationSeconds)) * 360f;
    }

    public static float DistanceToSegmentXZ(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector2 point2 = new(point.x, point.z);
        Vector2 start2 = new(start.x, start.z);
        Vector2 end2 = new(end.x, end.z);
        Vector2 segment = end2 - start2;
        float lengthSquared = segment.sqrMagnitude;
        float progress = lengthSquared <= 0.0001f
            ? 0f
            : Mathf.Clamp01(Vector2.Dot(point2 - start2, segment) / lengthSquared);
        return Vector2.Distance(point2, start2 + segment * progress);
    }

    public static Vector3 ClampToCircleXZ(Vector3 point, Vector3 center, float radius)
    {
        Vector3 offset = point - center;
        float originalY = point.y;
        offset.y = 0f;
        float safeRadius = Mathf.Max(0f, radius);
        if (offset.sqrMagnitude > safeRadius * safeRadius && offset.sqrMagnitude > 0.0001f)
        {
            point = center + offset.normalized * safeRadius;
            point.y = originalY;
        }
        return point;
    }
}
