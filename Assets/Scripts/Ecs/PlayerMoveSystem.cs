using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
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
            movement = math.normalizesafe(movement);
            float3 currentPosition = transform.ValueRO.Position;
            Vector3 currentNavPosition = new(currentPosition.x, currentPosition.y, currentPosition.z);
            if (NavMesh.SamplePosition(currentNavPosition, out NavMeshHit currentHit, 5f, NavMesh.AllAreas))
            {
                currentNavPosition = currentHit.position;
            }

            Vector3 requestedPosition = currentNavPosition + new Vector3(movement.x, movement.y, movement.z) * moveSpeed.ValueRO.Value * deltaTime;
            Vector3 resolvedPosition = currentNavPosition;
            if (NavMesh.Raycast(currentNavPosition, requestedPosition, out NavMeshHit boundaryHit, NavMesh.AllAreas))
            {
                resolvedPosition = boundaryHit.position;
            }
            else if (NavMesh.SamplePosition(requestedPosition, out NavMeshHit requestedHit, 0.75f, NavMesh.AllAreas))
            {
                resolvedPosition = requestedHit.position;
            }

            transform.ValueRW.Position = new float3(resolvedPosition.x, resolvedPosition.y, resolvedPosition.z);
        }
    }
}
