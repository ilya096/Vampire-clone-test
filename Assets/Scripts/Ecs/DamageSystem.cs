
using Unity.Entities;
using UnityEngine;

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
            foreach((RefRO<DamageRequest> damage, RefRW<HealthComponent> health) in 
                SystemAPI.Query<RefRO<DamageRequest>, RefRW<HealthComponent>>())
            {
                health.ValueRW.Value -= damage.ValueRO.Amount;

                Debug.Log($"{health.ValueRO.Value}");
            }
        }
    }
}
