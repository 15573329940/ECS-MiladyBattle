using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using UnityEngine;

namespace TMG.NFE_Tutorial
{
    public struct SpawnMinionInput : IInputComponentData
    {
        public float2 SpawnPosition; // 只需要 XZ 平面坐标
        public int MinionTypeId;       // 0=无操作, 1=兵种A, 2=兵种B, 3=兵种C
        public bool IsSpawning;      // 这一帧是否按下了鼠标左键
    }
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    public partial class MobaInputSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<NetworkId>();
            RequireForUpdate<GamePlayingTag>(); // 建议加一个Tag控制是否允许输入
        }

        protected override void OnUpdate()
        {
            // Mono 桥接器
            if (MinionPlacementManager.Instance == null) return;
            
            var manager = MinionPlacementManager.Instance;
            
            // 构造输入数据
            var input = new SpawnMinionInput
            {
                // 注意：UI里的索引是 0,1,2，我们传给服务端时 +1 变成 1,2,3，0代表无操作
                MinionTypeId = manager.SelectedUnitIndex + 1, 
                SpawnPosition = new float2(manager.CurrentMousePosition.x, manager.CurrentMousePosition.z),
                IsSpawning = manager.IsClicking
            };

            // 获取本地玩家的输入 Buffer
            foreach (var playerInput in SystemAPI.Query<RefRW<SpawnMinionInput>>()
                         .WithAll<GhostOwnerIsLocal>())
            {
                playerInput.ValueRW = input;
            }
        }
    }
    
}