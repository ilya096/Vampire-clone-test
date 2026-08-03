using Assets.Scripts.Ecs;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Assets.Scripts
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class EnemyNavMeshMoveSystem : SystemBase
    {
        private EnemyViewSynchronizator _enemyViewSynchronizator;

        protected override void OnCreate()
        {
            RequireForUpdate<PlayerTag>();
        }

        protected override void OnUpdate()
        {
            _enemyViewSynchronizator = _enemyViewSynchronizator != null ? _enemyViewSynchronizator : ServiceLocator.Get<EnemyViewSynchronizator>();

            var playerPosition = GetPlayerPosition();
            EntityManager entityManager = EntityManager;

            float deltaTime = SystemAPI.Time.DeltaTime;

            foreach((RefRW<LocalTransform> transform, RefRW<EnemyBehaviourComponent> behaviour, RefRO<EnemyArchetypeComponent> archetype, Entity enemy) in
                SystemAPI.Query<RefRW<LocalTransform>, RefRW<EnemyBehaviourComponent>, RefRO<EnemyArchetypeComponent>>().WithAll<EnemyTag>().WithEntityAccess())
            {
                var agent = _enemyViewSynchronizator.CreateEnemyView(enemy, transform.ValueRO.Position);
                _enemyViewSynchronizator.ConfigureEnemyView(enemy, archetype.ValueRO.Value);
                if (agent.isOnNavMesh == false && _enemyViewSynchronizator.TryPlaceOnNavMesh(agent, transform.ValueRO.Position) == false)
                {
                    continue;
                }

                float3 toPlayer = playerPosition - transform.ValueRO.Position;
                float distance = math.length(toPlayer);
                float speed = behaviour.ValueRO.BaseSpeed;
                float3 destination = playerPosition;

                if (archetype.ValueRO.Value == EnemyArchetype.Swarm && distance < 4f)
                {
                    speed *= 1.5f;
                }

                behaviour.ValueRW.SlowRemaining = math.max(0f, behaviour.ValueRO.SlowRemaining - deltaTime);
                if (behaviour.ValueRO.SlowRemaining > 0f)
                {
                    speed *= 0.55f;
                }

                if (archetype.ValueRO.Value == EnemyArchetype.Heavy)
                {
                    behaviour.ValueRW.DashCooldown -= deltaTime;
                    behaviour.ValueRW.DashRemaining -= deltaTime;
                    if (behaviour.ValueRO.DashCooldown <= 0f && distance > 2f)
                    {
                        behaviour.ValueRW.DashRemaining = 0.45f;
                        behaviour.ValueRW.DashCooldown = 3f;
                    }

                    if (behaviour.ValueRO.DashRemaining > 0f)
                    {
                        speed *= 2.5f;
                    }
                }

                if (archetype.ValueRO.Value == EnemyArchetype.Ranged)
                {
                    if (distance < behaviour.ValueRO.PreferredDistance && distance > 0.01f)
                    {
                        destination = transform.ValueRO.Position - math.normalize(toPlayer) * 2f;
                    }
                    else if (distance <= behaviour.ValueRO.PreferredDistance + 0.5f)
                    {
                        agent.ResetPath();
                        transform.ValueRW.Position = new float3(agent.transform.position.x, 0f, agent.transform.position.z);
                        continue;
                    }
                }

                agent.speed = speed;
                agent.SetDestination(destination);

                transform.ValueRW.Position = new float3(agent.transform.position.x, 0f, agent.transform.position.z);
            }
        }

        private float3 GetPlayerPosition()
        {
            var player = SystemAPI.GetSingletonEntity<PlayerTag>();
            var transform = SystemAPI.GetComponent<LocalTransform>(player);

            return transform.Position;
        }

    }
}
