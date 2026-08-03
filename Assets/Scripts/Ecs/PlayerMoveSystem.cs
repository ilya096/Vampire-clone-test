using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Assets.Scripts.Ecs;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PlayerInputSystem))]
public partial struct PlayerMoveSystem : ISystem 
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerMoveInput>();
    }

    public void OnUpdate(ref SystemState state)
    {
        float2 input = SystemAPI.GetSingleton<PlayerMoveInput>().Value;
        float3 movement = new float3(input.x, 0, input.y);
        float deltaTime = SystemAPI.Time.DeltaTime;
        GameplayTuningComponent tuning = SystemAPI.GetSingleton<GameplayTuningComponent>();

        foreach(var (transform, moveSpeed, progression) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<MoveSpeed>, RefRW<PlayerProgressionState>>().WithAll<PlayerTag>())
        {
            progression.ValueRW.DashCooldownRemaining = math.max(0f, progression.ValueRO.DashCooldownRemaining - deltaTime);
            progression.ValueRW.DashRemaining = math.max(0f, progression.ValueRO.DashRemaining - deltaTime);
            progression.ValueRW.InvulnerabilityRemaining = math.max(0f, progression.ValueRO.InvulnerabilityRemaining - deltaTime);
            float dashMultiplier = progression.ValueRO.DashRemaining > 0f ? tuning.DashSpeedMultiplier : 1f;
            moveSpeed.ValueRW.Value = tuning.PlayerBaseSpeed * progression.ValueRO.MoveSpeedMultiplier * dashMultiplier;
            transform.ValueRW.Position += movement * moveSpeed.ValueRO.Value * deltaTime;
        }
    }
}
