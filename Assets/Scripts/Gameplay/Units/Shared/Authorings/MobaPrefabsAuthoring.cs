using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace TMG.NFE_Tutorial
{
    
    public class MobaPrefabsAuthoring : MonoBehaviour
    {
        [Header("Entities")]
        public GameObject Champion;

        //public GameObject Minion;
        public List<GameObject> MinionPrefabs; 
        public GameObject GameOverEntity;
        public GameObject RespawnEntity;
        [Header("GameObjects")] 
        public GameObject HealthBarPrefab;
        public GameObject SkillShotAimPrefab;
        public class MobaPrefabsBaker : Baker<MobaPrefabsAuthoring>
        {
            public override void Bake(MobaPrefabsAuthoring authoring)
            {
                var prefabContainerEntity = GetEntity(TransformUsageFlags.None);
                AddComponent(prefabContainerEntity, new MobaPrefabs
                {
                    Champion = GetEntity(authoring.Champion, TransformUsageFlags.Dynamic),
                    //Minion = GetEntity(authoring.Minion, TransformUsageFlags.Dynamic),
                    GameOverEntity = GetEntity(authoring.GameOverEntity, TransformUsageFlags.None),
                    RespawnEntity = GetEntity(authoring.RespawnEntity, TransformUsageFlags.None)
                });
                // 为动态缓冲区添加小兵预制体实体
                DynamicBuffer<MinionPrefabElement> minionPrefabsBuffer = AddBuffer<MinionPrefabElement>(prefabContainerEntity);
                foreach (var minionPrefab in authoring.MinionPrefabs)
                {
                    if (minionPrefab != null)
                    {
                        minionPrefabsBuffer.Add(new MinionPrefabElement
                        {
                            Value = GetEntity(minionPrefab, TransformUsageFlags.Dynamic)
                        });
                    }
                }
                AddComponentObject(prefabContainerEntity, new UIPrefabs
                {
                    HealthBar = authoring.HealthBarPrefab,
                    SkillShot = authoring.SkillShotAimPrefab
                });
                
            }
        }
    }
}