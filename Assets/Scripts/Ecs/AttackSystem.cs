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
            state.RequireForUpdate<PlayerMoveInput>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var player = SystemAPI.GetSingletonEntity<PlayerTag>();
            var playerTransform = SystemAPI.GetComponent<LocalTransform>(player);
            float deltaTime = SystemAPI.Time.DeltaTime;
            EntityCommandBuffer commandBuffer = new(Allocator.Temp);

            foreach ((RefRW<AttackComponent> attack, RefRO<LocalTransform> attackerTransform, RefRO<EnemyArchetypeComponent> archetype) in
                SystemAPI.Query<RefRW<AttackComponent>, RefRO<LocalTransform>, RefRO<EnemyArchetypeComponent>>())
            {
                attack.ValueRW.TimeToNextAttack -= deltaTime;

                if (attack.ValueRO.TimeToNextAttack > 0)
                {
                    continue;
                }

                if (math.distancesq(attackerTransform.ValueRO.Position, playerTransform.Position) > attack.ValueRO.Range * attack.ValueRO.Range)
                {
                    continue;
                }

                attack.ValueRW.TimeToNextAttack = attack.ValueRO.Inverval;

                if (archetype.ValueRO.Value == EnemyArchetype.Ranged)
                {
                    CreateRangedProjectile(ref state, commandBuffer, attackerTransform.ValueRO.Position, playerTransform.Position);
                }
                else
                {
                    CreateDamageRequest(commandBuffer, player, attack.ValueRO.Damage, DamageSource.EnemyContact);
                }
            }

            commandBuffer.Playback(state.EntityManager);
            commandBuffer.Dispose();
        }

        private void CreateDamageRequest(EntityCommandBuffer commandBuffer, Entity target, int amount, DamageSource source)
        {
            Entity request = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(request, new DamageRequest { Target = target, Amount = amount, Source = source });
        }

        private void CreateRangedProjectile(ref SystemState state, EntityCommandBuffer commandBuffer, float3 start, float3 playerPosition)
        {
            PlayerMoveInput input = SystemAPI.GetSingleton<PlayerMoveInput>();
            Entity player = SystemAPI.GetSingletonEntity<PlayerTag>();
            float playerSpeed = SystemAPI.GetComponent<MoveSpeed>(player).Value;
            float3 velocity = new(input.Value.x * playerSpeed, 0f, input.Value.y * playerSpeed);
            float3 lead = velocity * CombatBalance.RangedProjectileFlightSeconds;
            lead = math.normalizesafe(lead) * math.min(math.length(lead), CombatBalance.RangedProjectileLeadLimit);

            Entity projectile = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(projectile, LocalTransform.FromPosition(start));
            commandBuffer.AddComponent(projectile, new RangedProjectileComponent
            {
                Start = start,
                ImpactPoint = playerPosition + lead,
                Duration = CombatBalance.RangedProjectileFlightSeconds,
                ArcHeight = CombatBalance.RangedProjectileArcHeight,
                ImpactRadius = CombatBalance.RangedProjectileImpactRadius,
                Damage = CombatBalance.GetEnemy(EnemyArchetype.Ranged).Damage
            });
        }
    }
}
