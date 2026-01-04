using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace TMG.NFE_Tutorial
{
    // 存储玩家三个兵种的当前等级
    [GhostComponent(PrefabType = GhostPrefabType.AllPredicted)]
    public struct PlayerMinionTech : IComponentData
    {
        // 假设索引 0:坦克, 1:射手, 2:刺客
        // 这里用定长数组或者三个字段都可以，为了方便对应 typeIndex，建议用 DynamicBuffer 或者 FixedBlob，
        // 但为了简单演示，用三个字段：
        [GhostField] public int LevelType0; // 坦克等级
        [GhostField] public int LevelType1; // 射手等级
        [GhostField] public int LevelType2; // 刺客等级

        public int GetLevel(int typeIndex)
        {
            switch (typeIndex)
            {
                case 0: return LevelType0;
                case 1: return LevelType1;
                case 2: return LevelType2;
                default: return 0;
            }
        }

        public void SetLevel(int typeIndex, int level)
        {
            switch (typeIndex)
            {
                case 0: LevelType0 = level; break;
                case 1: LevelType1 = level; break;
                case 2: LevelType2 = level; break;
            }
        }
    }

    // 升级请求 RPC
    public struct UpgradeMinionRpc : IRpcCommand
    {
        public int MinionTypeIndex; // 0, 1, 2
        public Entity TargetPlayer; // [新增] 是哪个玩家发起的请求
    }
    public class MinionUpgradeAuthoring : MonoBehaviour
    {
        class Baker : Baker<MinionUpgradeAuthoring>
        {
            public override void Bake(MinionUpgradeAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new PlayerMinionTech()
                {
                    LevelType0 = 0,
                    LevelType1 = 0,
                    LevelType2 = 0,
                });
                
            }
        }
    }
}