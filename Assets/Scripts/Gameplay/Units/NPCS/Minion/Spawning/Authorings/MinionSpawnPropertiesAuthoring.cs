using SO;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
namespace TMG.NFE_Tutorial
{
    // 最底层：单级数据
    public struct MinionLevelBlobData
    {
        public int Attack;
        public int Hp;
        public float MoveSpeed;
        
        // [新增]
        public int UnitCount;
        public float ModelRadius;
        public int UpgradeCost;
        public int SpawnCost;
        public int KillGold;
        public float ProjectileScale;
    }

    // 中间层：兵种数据 (包含一串等级)
    public struct MinionTypeBlobData
    {
        public BlobArray<MinionLevelBlobData> Levels;
    }

    // 最顶层：总配置 (包含一串兵种)
    public struct MinionUpgradeBlob
    {
        public BlobArray<MinionTypeBlobData> UnitTypes;
    }
    public struct MinionUpgradeData : IComponentData
    {
        public BlobAssetReference<MinionUpgradeBlob> BlobRef; // 最终逻辑层直接用的组件
    }
    
    public class MinionSpawnPropertiesAuthoring : MonoBehaviour
    {
        public float TimeBetweenWaves;
        public float TimeBetweenMinions;
        public int CountToSpawnInWave;
        public MinionUpgradeConfig upgradeConfig;
        public class MinionSpawnPropertiesBaker : Baker<MinionSpawnPropertiesAuthoring>
        {
            public override void Bake(MinionSpawnPropertiesAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new MinionSpawnProperties
                {
                    TimeBetweenWaves = authoring.TimeBetweenWaves,
                    TimeBetweenMinions = authoring.TimeBetweenMinions,
                    CountToSpawnInWave = authoring.CountToSpawnInWave
                });
                AddComponent(entity, new MinionSpawnTimers
                {
                    TimeToNextWave = authoring.TimeBetweenWaves,
                    TimeToNextMinion = 0f,
                    CountSpawnedInWave = 0
                });
                DependsOn(authoring.upgradeConfig);
                if (authoring.upgradeConfig != null)
                {
                    var builder = new BlobBuilder(Allocator.Temp);
                    ref var root = ref builder.ConstructRoot<MinionUpgradeBlob>();
                
                    // 1. 分配兵种数组 
                    var typesList = authoring.upgradeConfig.UnitTypes;
                    var typesBuilder = builder.Allocate(ref root.UnitTypes, typesList.Count);

                    for (int i = 0; i < typesList.Count; i++)
                    {
                        var levelsList = typesList[i].LevelDatas;
                        var levelsBuilder = builder.Allocate(ref typesBuilder[i].Levels, levelsList.Count);

                        for (int j = 0; j < levelsList.Count; j++)
                        {
                            levelsBuilder[j] = new MinionLevelBlobData
                            {
                                Attack = levelsList[j].Attack,
                                Hp = levelsList[j].Hp,
                                MoveSpeed = levelsList[j].MoveSpeed,
                                
                                // [新增] 传递数据
                                UnitCount = levelsList[j].UnitCount,
                                ModelRadius = levelsList[j].ModelRadius,
                                UpgradeCost = levelsList[j].UpgradeCost,
                                SpawnCost = levelsList[j].SpawnCost,
                                KillGold = levelsList[j].KillGold,
                                ProjectileScale = levelsList[j].ProjectileScale
                            };
                        }
                    }

                    // 生成 BlobAssetReference
                    var blobRef = builder.CreateBlobAssetReference<MinionUpgradeBlob>(Allocator.Persistent);
                    builder.Dispose();

                    // --- 添加组件 ---
                    AddComponent(entity, new MinionUpgradeData { BlobRef = blobRef });
                }
            }
        }
    }
}