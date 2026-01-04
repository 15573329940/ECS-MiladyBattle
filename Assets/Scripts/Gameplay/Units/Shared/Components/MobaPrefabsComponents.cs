using Unity.Entities;
using UnityEngine;
namespace TMG.NFE_Tutorial
{
    public struct MobaPrefabs:IComponentData
    {
        public Entity Champion;
        //public Entity Minion;
        public Entity GameOverEntity;
        public Entity RespawnEntity;
    }

    public class UIPrefabs : IComponentData
    {
        public GameObject HealthBar;
        public GameObject SkillShot;
    }
    [InternalBufferCapacity(5)] // 预分配容量，根据实际需求调整
    public struct MinionPrefabElement : IBufferElementData
    {
        public Entity Value;
    }
}