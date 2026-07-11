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

        protected void onCreate()
        {
            RequireForUpdate<PlayerTag>();
        }

        protected override void OnUpdate()
        {
            _enemyViewSynchronizator = _enemyViewSynchronizator != null ? _enemyViewSynchronizator : ServiceLocator.Get<EnemyViewSynchronizator>();

            var playerPosition = GetPlayerPosition();
            EntityManager entityManager = EntityManager;

            foreach((RefRW<LocalTransform> transform, RefRO<MoveSpeed> moveSpeed, Entity enemy) in  
                SystemAPI.Query<RefRW<LocalTransform>, RefRO<MoveSpeed>>().WithAll<EnemyTag>().WithEntityAccess())
            {
                var agent = _enemyViewSynchronizator.CreateEnemyView(enemy, transform.ValueRO.Position);

                agent.speed = moveSpeed.ValueRO.Value;
                agent.SetDestination(playerPosition);

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
