namespace Assets.Scripts.Ecs
{
    public readonly struct EnemyBalance
    {
        public readonly int Health;
        public readonly int Damage;
        public readonly float Speed;
        public readonly float AttackRange;
        public readonly float AttackInterval;
        public readonly int Experience;

        public EnemyBalance(int health, int damage, float speed, float attackRange, float attackInterval, int experience)
        {
            Health = health;
            Damage = damage;
            Speed = speed;
            AttackRange = attackRange;
            AttackInterval = attackInterval;
            Experience = experience;
        }
    }

    public static class CombatBalance
    {
        public const int PlayerMaxHealth = 100;
        public const int PistolDamage = 20;
        public const float PistolIntervalSeconds = 0.5f;
        public const float PistolSpeed = 18f;
        public const float PistolRange = 12f;
        public const float MachineGunRange = 10f;
        public const float MachineGunSpeed = 40f;
        public const int MachineGunDamage = 8;
        public const float MachineGunIntervalSeconds = 0.15f;
        public const float MachineGunSpreadDegrees = 4f;
        public const float ExperienceAttractionRadius = 2f;
        public const float ExperienceAttractionSpeed = 8f;
        public const float RangedProjectileFlightSeconds = 0.65f;
        public const float RangedProjectileLeadLimit = 1.5f;
        public const float RangedProjectileArcHeight = 1.5f;
        public const float RangedProjectileImpactRadius = 0.5f;

        public static EnemyBalance GetEnemy(EnemyArchetype archetype)
        {
            return archetype switch
            {
                EnemyArchetype.Swarm => new EnemyBalance(10, 3, 4f, 1.25f, 0.9f, 1),
                EnemyArchetype.Heavy => new EnemyBalance(100, 15, 1.35f, 1.7f, 1.1f, 5),
                EnemyArchetype.Ranged => new EnemyBalance(30, 10, 2.2f, 8f, 1.2f, 5),
                _ => new EnemyBalance(20, 5, 2.5f, 1.5f, 1f, 1)
            };
        }
    }
}
