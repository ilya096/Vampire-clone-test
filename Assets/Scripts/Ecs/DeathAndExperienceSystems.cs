using Assets.Scripts;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Assets.Scripts.Ecs
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DamageSystem))]
    public partial struct EnemyDeathSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            EntityCommandBuffer commandBuffer = new(Allocator.Temp);
            EnemyViewSynchronizator synchronizator = null;

            foreach ((RefRO<HealthComponent> health, RefRO<LocalTransform> transform, RefRO<EnemyArchetypeComponent> archetype, Entity enemy) in
                SystemAPI.Query<RefRO<HealthComponent>, RefRO<LocalTransform>, RefRO<EnemyArchetypeComponent>>().WithAll<EnemyTag>().WithEntityAccess())
            {
                if (health.ValueRO.Value > 0)
                {
                    continue;
                }

                synchronizator ??= ServiceLocator.Get<EnemyViewSynchronizator>();
                synchronizator.ReturnToPool(enemy);
                CreateExperiencePickup(commandBuffer, transform.ValueRO.Position, CombatBalance.GetEnemy(archetype.ValueRO.Value).Experience);
                commandBuffer.DestroyEntity(enemy);
            }

            commandBuffer.Playback(state.EntityManager);
            commandBuffer.Dispose();
        }

        private void CreateExperiencePickup(EntityCommandBuffer commandBuffer, float3 position, int value)
        {
            Entity pickup = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(pickup, LocalTransform.FromPosition(position));
            commandBuffer.AddComponent(pickup, new ExperiencePickupComponent
            {
                Value = value,
                AttractionRadius = CombatBalance.ExperienceAttractionRadius,
                AttractionSpeed = CombatBalance.ExperienceAttractionSpeed
            });
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EnemyDeathSystem))]
    public partial struct ExperiencePickupSystem : ISystem
    {
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<PlayerTag>();

        public void OnUpdate(ref SystemState state)
        {
            Entity player = SystemAPI.GetSingletonEntity<PlayerTag>();
            LocalTransform playerTransform = SystemAPI.GetComponent<LocalTransform>(player);
            float deltaTime = SystemAPI.Time.DeltaTime;
            EntityCommandBuffer commandBuffer = new(Allocator.Temp);

            foreach ((RefRW<LocalTransform> transform, RefRO<ExperiencePickupComponent> pickup, Entity entity) in
                SystemAPI.Query<RefRW<LocalTransform>, RefRO<ExperiencePickupComponent>>().WithEntityAccess())
            {
                float3 toPlayer = playerTransform.Position - transform.ValueRO.Position;
                float distance = math.length(toPlayer);
                if (distance > pickup.ValueRO.AttractionRadius)
                {
                    continue;
                }

                if (distance <= 0.35f)
                {
                    RefRW<PlayerCombatState> combat = SystemAPI.GetComponentRW<PlayerCombatState>(player);
                    combat.ValueRW.Experience += pickup.ValueRO.Value;
                    commandBuffer.DestroyEntity(entity);
                    continue;
                }

                transform.ValueRW.Position += math.normalize(toPlayer) * pickup.ValueRO.AttractionSpeed * deltaTime;
            }

            commandBuffer.Playback(state.EntityManager);
            commandBuffer.Dispose();
        }
    }
}
