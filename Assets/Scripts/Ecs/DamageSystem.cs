
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

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
                    RefRW<HealthComponent> health = SystemAPI.GetComponentRW<HealthComponent>(target);
                    health.ValueRW.Value = math.max(0, health.ValueRO.Value - damage.ValueRO.Amount);

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
