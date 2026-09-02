using Assets.Scripts.Ecs;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Assets.Scripts
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(EnemyNavMeshMoveSystem))]
    public partial class EnemyAttackPresentationSystem : SystemBase
    {
        private EnemyViewSynchronizator _enemyViews;

        protected override void OnCreate()
        {
            RequireForUpdate<EnemyAttackPresentationEvent>();
        }

        protected override void OnUpdate()
        {
            _enemyViews ??= ServiceLocator.Get<EnemyViewSynchronizator>();
            EntityCommandBuffer commandBuffer = new(Allocator.Temp);

            foreach ((RefRO<EnemyAttackPresentationEvent> presentationEvent, Entity eventEntity) in
                SystemAPI.Query<RefRO<EnemyAttackPresentationEvent>>().WithEntityAccess())
            {
                EnemyAttackPresentationEvent attack = presentationEvent.ValueRO;
                _enemyViews.PlayAttackGesture(
                    attack.Enemy,
                    attack.Kind,
                    new Vector3(attack.Target.x, attack.Target.y, attack.Target.z));
                commandBuffer.DestroyEntity(eventEntity);
            }

            commandBuffer.Playback(EntityManager);
            commandBuffer.Dispose();
        }
    }
}
