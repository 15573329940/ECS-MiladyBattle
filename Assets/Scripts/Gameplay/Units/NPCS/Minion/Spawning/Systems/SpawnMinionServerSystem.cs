using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;

namespace TMG.NFE_Tutorial
{
    // 在客户端运行，把 Monobehaviour 的数据填入 Input Component
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    public partial class MinionInputSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            //return;
            var manager = MinionPlacementManager.Instance;
            // 如果 Manager 没准备好，或者没有处于放置模式，则重置输入
            if (manager == null) return;

            bool isSpawning = manager.IsClicking; // 只有这一帧点击了才为 true
            float3 spawnPos = manager.CurrentMousePosition;
            int typeId = manager.SelectedUnitIndex + 1; // 假设 Input 里的 ID 是从 1 开始 (0代表无)

            foreach (var input in SystemAPI.Query<RefRW<SpawnMinionInput>>().WithAll<GhostOwnerIsLocal>())
            {
                input.ValueRW.IsSpawning = isSpawning;
                if (isSpawning)
                {
                    input.ValueRW.SpawnPosition = new float2(spawnPos.x, spawnPos.z);
                    input.ValueRW.MinionTypeId = typeId;
                }
                
            }
        }
    }
}