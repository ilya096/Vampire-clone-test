
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;

namespace Assets.Scripts.Ecs
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AttackSystem))]
    public partial struct DamageSystem: ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DamageRequest>();
            state.RequireForUpdate<HealthComponent>();
            state.RequireForUpdate<SessionCombatStats>();
        }

        public void OnUpdate(ref SystemState state)
        {
            EntityCommandBuffer commandBuffer = new(Allocator.Temp);

            foreach((RefRO<DamageRequest> damage, Entity request) in
                SystemAPI.Query<RefRO<DamageRequest>>().WithEntityAccess())
            {
                Entity target = damage.ValueRO.Target;

                if (state.EntityManager.Exists(target) && SystemAPI.HasComponent<HealthComponent>(target))
                {
                    if (SystemAPI.HasComponent<BossInvulnerableTag>(target))
                    {
                        commandBuffer.DestroyEntity(request);
                        continue;
                    }

                    if (SystemAPI.HasComponent<PlayerTag>(target) && SystemAPI.HasComponent<PlayerProgressionState>(target))
                    {
                        RefRW<PlayerProgressionState> progression = SystemAPI.GetComponentRW<PlayerProgressionState>(target);
                        if (progression.ValueRO.InvulnerabilityRemaining > 0f)
                        {
                            commandBuffer.DestroyEntity(request);
                            continue;
                        }

                        if (progression.ValueRO.DashUnlocked && progression.ValueRO.DashCooldownRemaining <= 0f)
                        {
                            GameplayTuningComponent tuning = SystemAPI.GetSingleton<GameplayTuningComponent>();
                            progression.ValueRW.DashCooldownRemaining = tuning.DashCooldownSeconds;
                            progression.ValueRW.DashRemaining = tuning.DashDurationSeconds;
                            progression.ValueRW.InvulnerabilityRemaining = tuning.DashInvulnerabilitySeconds;
                        }
                    }

                    RefRW<HealthComponent> health = SystemAPI.GetComponentRW<HealthComponent>(target);
                    int actualDamage = math.min(
                        math.max(0, damage.ValueRO.Amount),
                        math.max(0, health.ValueRO.Value));
                    health.ValueRW.Value = math.max(0, health.ValueRO.Value - actualDamage);

                    if (actualDamage > 0 && SystemAPI.HasComponent<EnemyTag>(target))
                    {
                        RefRW<SessionCombatStats> stats = SystemAPI.GetSingletonRW<SessionCombatStats>();
                        stats.ValueRW.ActualDamage += actualDamage;
                    }

                    if (actualDamage > 0 &&
                        SystemAPI.HasComponent<EnemyTag>(target) &&
                        SystemAPI.HasComponent<LocalTransform>(target))
                    {
                        Entity damageNumberEvent = commandBuffer.CreateEntity();
                        commandBuffer.AddComponent(damageNumberEvent, new DamageNumberEvent
                        {
                            Position = SystemAPI.GetComponent<LocalTransform>(target).Position,
                            Amount = actualDamage
                        });
                    }

                    if (SystemAPI.HasComponent<PlayerTag>(target) && damage.ValueRO.Source != DamageSource.None)
                    {
                        RefRW<PlayerDefeatInfo> defeatInfo = SystemAPI.GetComponentRW<PlayerDefeatInfo>(target);
                        defeatInfo.ValueRW.LastDamageSource = damage.ValueRO.Source;
                    }
                }

                commandBuffer.DestroyEntity(request);
            }

            commandBuffer.Playback(state.EntityManager);
            commandBuffer.Dispose();
        }
    }
}
