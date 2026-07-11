using UnityEngine;
using System.Collections;
using Unity.Entities;

namespace Assets.Scripts.Ecs
{
	public struct EnemySpawnConfigComponent: IComponentData
	{
        public float Interval;
        public float SpawnRadius;
        public float EnemySpeed;
        public float EnemyHealth;
        public int MaxEnemies;
        public int EnemyAttack;
        public float EnemyRange;
        public float EnemyAttackInterval;
    }
}