using UnityEngine;
using System.Collections;
using Unity.Entities;

namespace Assets.Scripts.Ecs
{
	public struct EnemySpawnStateComponent: IComponentData
	{
		public float TimeToNextSpawn;
	}
}