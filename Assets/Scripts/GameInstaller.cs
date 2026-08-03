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
    private CombatRuntimeController _combatRuntimeController;

    private void Awake()
    {
        ServiceLocator.Register(_inputService);
        ServiceLocator.Register(_enemyViewSynchronizator);

        _world = World.DefaultGameObjectInjectionWorld;
        _entityManager = _world.EntityManager;

        _playerInputEntity = _entityManager.CreateEntity(typeof(PlayerMoveInput));

        CreatePlayer();
        CreateEnemySpawner();

        _combatRuntimeController = GetComponent<CombatRuntimeController>();
        if (_combatRuntimeController == null)
        {
            _combatRuntimeController = gameObject.AddComponent<CombatRuntimeController>();
        }

        _combatRuntimeController.Initialize(_world, _playerEntity, _player.transform);

        _player.Initialize(_world, _playerEntity);
        _cameraFollow.SetPlayer(_player.transform);
    }

    private void OnDestroy()
    {
        ServiceLocator.Unregister<InputService>();

        DestroyEntity(_playerEntity);
        DestroyEntity(_playerInputEntity);
        DestroyEntity(_enemySpawnerEntity);

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
        _entityManager.SetComponentData(_playerEntity, new PlayerAimComponent { Direction = new float3(0f, 0f, 1f) });
        _entityManager.SetComponentData(_playerEntity, new PlayerDefeatInfo { LastDamageSource = DamageSource.None });
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
