using System;
using System.Collections.Generic;
using System.Text;
using Unity.Entities;

namespace Assets.Scripts.Ecs
{
    public struct HealthComponent : IComponentData
    {
        public int Value;
        public int MaxValue;
    }
}
