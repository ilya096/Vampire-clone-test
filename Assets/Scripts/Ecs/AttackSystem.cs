using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Assets.Scripts.Ecs
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EnemySpawnSystem))]
    [UpdateBefore(typeof(DamageSystem))]
    public partial struct AttackSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<AttackComponent>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var player = SystemAPI.GetSingletonEntity<PlayerTag>();
            var playerTransform = SystemAPI.GetComponent<LocalTransform>(player);
            float deltaTime = SystemAPI.Time.DeltaTime;
            EntityCommandBuffer commandBuffer = new(Allocator.Temp);

            foreach ((RefRW<AttackComponent> attack, RefRO<LocalTransform> attackerTransform, Entity attacker) in
                SystemAPI.Query<RefRW<AttackComponent>, RefRO<LocalTransform>>().WithEntityAccess())
            {
                attack.ValueRW.TimeToNextAttack -= deltaTime;

                if (attack.ValueRO.TimeToNextAttack > 0)
                {
                    continue;
                }

                Entity target = GetAttackTarget(ref state, attacker, attackerTransform.ValueRO.Position, player, playerTransform.Position, attack.ValueRO.Range);

                if(target == Entity.Null)
                {
                    continue;
                }

                attack.ValueRW.TimeToNextAttack = attack.ValueRO.Inverval;

                CreateDamgeRequest(commandBuffer, target, attack.ValueRO.Damage);
            }

            commandBuffer.Playback(state.EntityManager);
            commandBuffer.Dispose();
        }

        private Entity GetAttackTarget(ref SystemState state, Entity attacker, float3 attackerPosition, Entity player, float3 playerPosition, float range)
        {
            if(SystemAPI.HasComponent<PlayerTag>(attacker))
            {
                return GetNearestEnemyInRange(ref state, playerPosition, range);
            }

            if(SystemAPI.HasComponent<EnemyTag>(attacker) && (math.distancesq(attackerPosition, playerPosition) <= range * range))
            {
                return player;
            }

            return Entity.Null;
        }

        private Entity GetNearestEnemyInRange(ref SystemState state, float3 position, float range)
        {
            float rangeSq = range * range;
            float nearestDistSq = rangeSq;
            Entity nearestEnemy = Entity.Null;

            foreach((RefRO<LocalTransform> transform, Entity enemy) in 
                SystemAPI.Query<RefRO<LocalTransform>>().WithAll<EnemyTag, HealthComponent>().WithEntityAccess())
            {
                float distanceSq = math.distancesq(position, transform.ValueRO.Position);

                if (distanceSq <= nearestDistSq)
                {
                    nearestDistSq = distanceSq;
                    nearestEnemy = enemy;
                }
            }

            return nearestEnemy;
        }

        private void CreateDamgeRequest(EntityCommandBuffer commandBuffer, Entity target, int amount)
        {
            commandBuffer.AddComponent(target, new DamageRequest() { Amount = amount });
        }
    }
}
