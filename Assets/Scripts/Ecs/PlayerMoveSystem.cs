using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PlayerInputSystem))]
public partial struct PlayerMoveSystem : ISystem 
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerMoveInput>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float2 input = SystemAPI.GetSingleton<PlayerMoveInput>().Value;
        float3 movement = new float3(input.x, 0, input.y);
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach(var (transform, moveSpeed) in SystemAPI.Query<RefRW<LocalTransform>, RefRO<MoveSpeed>>().WithAll<PlayerTag>())
        {
            transform.ValueRW.Position += movement * moveSpeed.ValueRO.Value * deltaTime;
        }
    }
}
