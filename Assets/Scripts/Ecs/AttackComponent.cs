using System;
using System.Collections.Generic;
using System.Text;
using Unity.Entities;

namespace Assets.Scripts.Ecs
{
    public struct AttackComponent : IComponentData
    {
        public int Damage;
        public float Range;
        public float Inverval;
        public float TimeToNextAttack;
    }
}
