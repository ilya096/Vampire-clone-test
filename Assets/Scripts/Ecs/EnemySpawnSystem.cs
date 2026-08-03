using UnityEngine;
using System.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.AI;

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
			var random = new Unity.Mathematics.Random(spawnState.ValueRO.RandomState == 0 ? 1u : spawnState.ValueRO.RandomState);
			float3 spawnPosition = GetSpawnPosition(playerPosition, config.SpawnRadius, ref random);
			if (NavMesh.SamplePosition(new Vector3(spawnPosition.x, spawnPosition.y, spawnPosition.z), out NavMeshHit navMeshHit, 2f, NavMesh.AllAreas) == false)
			{
				spawnState.ValueRW.RandomState = random.state;
				spawnState.ValueRW.TimeToNextSpawn = 0.1f;
				return;
			}
			spawnPosition = new float3(navMeshHit.position.x, navMeshHit.position.y, navMeshHit.position.z);
			EnemyArchetype archetype = GetArchetype(ref random);
			spawnState.ValueRW.RandomState = random.state;
			CreateEnemy(ref state, spawnPosition, archetype);
        }

		private float3 GetPlayerPosition(ref SystemState state)
		{
			var player = SystemAPI.GetSingletonEntity<PlayerTag>();
			var transform = SystemAPI.GetComponent<LocalTransform>(player);

			return transform.Position;
		}

		private float3 GetSpawnPosition(float3 playerPosition, float spawnRadius, ref Unity.Mathematics.Random random)
		{
			float angle = random.NextFloat(0f, math.PI * 2f);

			float3 direction = new(math.cos(angle), 0, math.sin(angle));

			return playerPosition + direction * spawnRadius;
		}

		private EnemyArchetype GetArchetype(ref Unity.Mathematics.Random random)
		{
			float roll = random.NextFloat();
			if (roll < 0.6f) return EnemyArchetype.Normal;
			if (roll < 0.8f) return EnemyArchetype.Swarm;
			if (roll < 0.9f) return EnemyArchetype.Heavy;
			return EnemyArchetype.Ranged;
		}

		private void CreateEnemy(ref SystemState state, float3 positon, EnemyArchetype archetype)
		{
			EnemyBalance balance = CombatBalance.GetEnemy(archetype);
			Entity enemy = state.EntityManager.CreateEntity(
				typeof(EnemyTag),
				typeof(EnemyArchetypeComponent),
				typeof(EnemyBehaviourComponent),
				typeof(MoveSpeed),
				typeof(LocalTransform),
				typeof(HealthComponent),
				typeof(AttackComponent)
				);

			state.EntityManager.SetComponentData<EnemyArchetypeComponent>(enemy, new EnemyArchetypeComponent { Value = archetype });
			state.EntityManager.SetComponentData<EnemyBehaviourComponent>(enemy, new EnemyBehaviourComponent
			{
				BaseSpeed = balance.Speed,
				PreferredDistance = archetype == EnemyArchetype.Ranged ? 6f : 0f,
				DashCooldown = archetype == EnemyArchetype.Heavy ? 3f : 0f
			});
			state.EntityManager.SetComponentData<MoveSpeed>(enemy, new MoveSpeed() {Value = balance.Speed });
			state.EntityManager.SetComponentData<LocalTransform>(enemy, LocalTransform.FromPosition(positon));
			state.EntityManager.SetComponentData<HealthComponent>(enemy, new HealthComponent() { Value = balance.Health, MaxValue = balance.Health });
			state.EntityManager.SetComponentData<AttackComponent>(enemy, new AttackComponent()
			{
				Damage = balance.Damage,
				Inverval = balance.AttackInterval,
				Range = balance.AttackRange,
			});
		}
	}
}
