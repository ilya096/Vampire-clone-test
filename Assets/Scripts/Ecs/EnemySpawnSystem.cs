using UnityEngine;
using System.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Assets.Scripts.Ecs
{
	[UpdateInGroup(typeof(SimulationSystemGroup))]
	[UpdateAfter(typeof(PlayerMoveSystem))]
	public partial struct EnemySpawnSystem: ISystem
	{
		private EntityQuery _enemyQuery;

		public void OnCreate(ref SystemState systemState)
		{
			_enemyQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<EnemyTag>());

			systemState.RequireForUpdate<PlayerTag>();
			systemState.RequireForUpdate<EnemySpawnConfigComponent>();
			systemState.RequireForUpdate<EnemySpawnStateComponent>();
		}

		public void OnUpdate(ref SystemState state)
		{
			RefRW<EnemySpawnStateComponent> spawnState = SystemAPI.GetSingletonRW<EnemySpawnStateComponent>();

			spawnState.ValueRW.TimeToNextSpawn -= SystemAPI.Time.DeltaTime;

			if(spawnState.ValueRO.TimeToNextSpawn > 0f)
			{
				return;
			}

			var config = SystemAPI.GetSingleton<EnemySpawnConfigComponent>();
			spawnState.ValueRW.TimeToNextSpawn = config.Interval;

			if(_enemyQuery.CalculateEntityCount() >= config.MaxEnemies)
			{
				return;
			}

			float3 playerPosition = GetPlayerPosition(ref state);
			float3 spawnPosition = GetSpawnPosition(playerPosition, config.SpawnRadius);
			CreateEnemy(ref state, spawnPosition, config);
        }

		private float3 GetPlayerPosition(ref SystemState state)
		{
			var player = SystemAPI.GetSingletonEntity<PlayerTag>();
			var transform = SystemAPI.GetComponent<LocalTransform>(player);

			return transform.Position;
		}

		private float3 GetSpawnPosition(float3 playerPosition, float spawnRadius)
		{
			var random = new Unity.Mathematics.Random(1u);
			float angle = random.NextFloat(0f, math.PI * 2f);

			float3 direction = new(math.cos(angle), 0, math.sin(angle));

			return playerPosition + direction * spawnRadius;
		}

		private void CreateEnemy(ref SystemState state, float3 positon, EnemySpawnConfigComponent config)
		{
			Entity enemy = state.EntityManager.CreateEntity(
				typeof(EnemyTag),
				typeof(MoveSpeed),
				typeof(LocalTransform),
				typeof(HealthComponent),
				typeof(AttackComponent)
				);

			state.EntityManager.SetComponentData<MoveSpeed>(enemy, new MoveSpeed() {Value = config.EnemySpeed });
			state.EntityManager.SetComponentData<LocalTransform>(enemy, LocalTransform.FromPosition(positon));
			state.EntityManager.SetComponentData<HealthComponent>(enemy, new HealthComponent() { Value = 100 });
			state.EntityManager.SetComponentData<AttackComponent>(enemy, new AttackComponent()
			{
				Damage = config.EnemyAttack,
				Inverval = config.EnemyAttackInterval,
				Range = config.EnemyRange,
			});
		}
	}
}