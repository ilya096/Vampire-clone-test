using Assets.Scripts;
using Assets.Scripts.Ecs;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

public class GameInstaller : MonoBehaviour
{
    [SerializeField] private float _playerSpeed = 5f;
    [SerializeField] private InputService _inputService;
    [SerializeField] private PlayerView _player;
    [SerializeField] private CameraFollow _cameraFollow;
    [SerializeField] private EnemyViewSynchronizator _enemyViewSynchronizator;
    [SerializeField] private EnemySpawnConfig _enemySpawnConfig;

    private World _world;
    private EntityManager _entityManager;
    private Entity _playerEntity;
    private Entity _playerInputEntity;
    private Entity _enemySpawnerEntity;
    private Entity _gameplayTuningEntity;
    private CombatRuntimeController _combatRuntimeController;
    private WaveRuntimeController _waveRuntimeController;
    private PlayerProgressionController _playerProgressionController;
    private DebugAdminPanel _debugAdminPanel;

    private void Awake()
    {
        ServiceLocator.Register(_inputService);
        ServiceLocator.Register(_enemyViewSynchronizator);

        _world = World.DefaultGameObjectInjectionWorld;
        _entityManager = _world.EntityManager;

        _playerInputEntity = _entityManager.CreateEntity(typeof(PlayerMoveInput));

        CreatePlayer();
        CreateGameplayTuning();
        CreateEnemySpawner();

        _combatRuntimeController = GetComponent<CombatRuntimeController>();
        if (_combatRuntimeController == null)
        {
            _combatRuntimeController = gameObject.AddComponent<CombatRuntimeController>();
        }

        _combatRuntimeController.Initialize(_world, _playerEntity, _player.transform);

        _waveRuntimeController = GetComponent<WaveRuntimeController>();
        if (_waveRuntimeController == null)
        {
            _waveRuntimeController = gameObject.AddComponent<WaveRuntimeController>();
        }

        _waveRuntimeController.Initialize(_world, _playerEntity, _player.transform);

        _playerProgressionController = GetComponent<PlayerProgressionController>();
        if (_playerProgressionController == null)
        {
            _playerProgressionController = gameObject.AddComponent<PlayerProgressionController>();
        }
        _playerProgressionController.Initialize(_world, _playerEntity);

        _debugAdminPanel = GetComponent<DebugAdminPanel>();
        if (_debugAdminPanel == null)
        {
            _debugAdminPanel = gameObject.AddComponent<DebugAdminPanel>();
        }
        _debugAdminPanel.Initialize(_world, _playerEntity);

        _player.Initialize(_world, _playerEntity);
        _cameraFollow.SetPlayer(_player.transform);
    }

    private void OnDestroy()
    {
        ServiceLocator.Unregister<InputService>();

        DestroyEntity(_playerEntity);
        DestroyEntity(_playerInputEntity);
        DestroyEntity(_enemySpawnerEntity);
        DestroyEntity(_gameplayTuningEntity);

        DestroyEntitiesWith<EnemyTag>();
        DestroyEntitiesWith<ProjectileComponent>();
        DestroyEntitiesWith<RangedProjectileComponent>();
        DestroyEntitiesWith<ExperiencePickupComponent>();
        DestroyEntitiesWith<DamageRequest>();
        DestroyEntitiesWith<TracerEvent>();
    }

    private void CreatePlayer()
    {
        Vector3 playerPos = _player.transform.position;
        float3 initPos = new float3(playerPos.x, 0, playerPos.z);

        _playerEntity = _entityManager.CreateEntity(
            typeof(PlayerTag),
            typeof(MoveSpeed),
            typeof(LocalTransform),
            typeof(HealthComponent),
            typeof(PlayerCombatState),
            typeof(PlayerProgressionState),
            typeof(PlayerAimComponent),
            typeof(PlayerDefeatInfo)
            );

        _entityManager.SetComponentData(_playerEntity, new MoveSpeed()
        {
            Value = _playerSpeed
        });

        _entityManager.SetComponentData(_playerEntity, LocalTransform.FromPosition(initPos));
        _entityManager.SetComponentData(_playerEntity, new HealthComponent() { Value = CombatBalance.PlayerMaxHealth, MaxValue = CombatBalance.PlayerMaxHealth});
        _entityManager.SetComponentData(_playerEntity, new PlayerCombatState { SelectedWeapon = WeaponSlot.Pistol });
        _entityManager.SetComponentData(_playerEntity, new PlayerProgressionState
        {
            Level = 1,
            NextLevelExperience = 10,
            MoveSpeedMultiplier = 1f,
            ExperienceRadiusMultiplier = 1f,
            ExperienceValueMultiplier = 1f
        });
        _entityManager.SetComponentData(_playerEntity, new PlayerAimComponent { Direction = new float3(0f, 0f, 1f) });
        _entityManager.SetComponentData(_playerEntity, new PlayerDefeatInfo { LastDamageSource = DamageSource.None });
    }

    private void CreateGameplayTuning()
    {
        _gameplayTuningEntity = _entityManager.CreateEntity(typeof(GameplayTuningComponent));
        _entityManager.SetComponentData(_gameplayTuningEntity, new GameplayTuningComponent
        {
            PistolDamage = CombatBalance.PistolDamage,
            PistolIntervalSeconds = CombatBalance.PistolIntervalSeconds,
            MachineGunDamage = CombatBalance.MachineGunDamage,
            MachineGunIntervalSeconds = CombatBalance.MachineGunIntervalSeconds,
            PlayerBaseSpeed = _playerSpeed,
            ExperienceRadius = CombatBalance.ExperienceAttractionRadius,
            ExperienceValueMultiplier = 1f,
            DashCooldownSeconds = 7f,
            DashDurationSeconds = 0.75f,
            DashSpeedMultiplier = 3f,
            DashInvulnerabilitySeconds = 0.5f
        });
    }

    private void CreateEnemySpawner()
    {
        _enemySpawnerEntity = _entityManager.CreateEntity(
            typeof(EnemySpawnConfigComponent),
            typeof(EnemySpawnStateComponent)
            );

        _entityManager.SetComponentData(_enemySpawnerEntity, new EnemySpawnConfigComponent()
        {
            EnemyHealth = _enemySpawnConfig.EnemyHealth,
            EnemySpeed = _enemySpawnConfig.EnemySpeed,
            Interval = _enemySpawnConfig.Interval,
            MaxEnemies = _enemySpawnConfig.MaxEnemies,
            SpawnRadius = _enemySpawnConfig.SpawnRadius,
            EnemyAttack = _enemySpawnConfig.EnemyAttack,
            EnemyAttackInterval = _enemySpawnConfig.EnemyAttackInterval,
            EnemyRange = _enemySpawnConfig.EnemyRange
        });
        _entityManager.SetComponentData(_enemySpawnerEntity, new EnemySpawnStateComponent { RandomState = 0x51A5EEDu });

    }

    private void DestroyEntity(Entity entity)
    {
        if(_world == null || _world.IsCreated == false)
        {
            return;
        }

        if(entity != Entity.Null && _entityManager.Exists(entity))
        {
            _entityManager.DestroyEntity(entity);
        }
    }

    private void DestroyEntitiesWith<TComponent>() where TComponent : unmanaged, IComponentData
    {
        if (_world == null || _world.IsCreated == false)
        {
            return;
        }

        EntityQuery query = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<TComponent>());
        _entityManager.DestroyEntity(query);
    }
}
