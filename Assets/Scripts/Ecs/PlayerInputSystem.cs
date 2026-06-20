using Unity.Entities;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(PlayerMoveSystem))]
public partial class PlayerInputSystem : SystemBase
{
    private InputService _inputService;

    protected override void OnCreate()
    {
        RequireForUpdate<PlayerMoveInput>();
    }

    protected override void OnUpdate()
    {
        _inputService ??= ServiceLocator.Get<InputService>();

        Vector2 move = _inputService.Move;
        RefRW<PlayerMoveInput> input = SystemAPI.GetSingletonRW<PlayerMoveInput>();
        input.ValueRW.Value = new Unity.Mathematics.float2(move.x, move.y);
    }
}
