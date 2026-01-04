using TMPro;
using UnityEngine;
using Unity.Entities;
using Unity.NetCode;
namespace TMG.NFE_Tutorial
{
    public class GoldUIController : MonoBehaviour
    {
        // 单例模式，方便 System 找到它
        // 注意：在复杂的 MPPM 场景中，这依然可能导致不同客户端抢夺同一个 UI 实例
        // 但对于学习阶段和简单调试，这是最快且正确的方案
        public static GoldUIController Instance;

        public TextMeshProUGUI GoldText;

        private void Awake()
        {
            Instance = this;
        }

        // 提供一个公开方法给外部调用
        public void UpdateGoldView(int amount)
        {
            GoldText.text = "Gold:"+amount;
        }
    }
    // 1. 只在客户端运行
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    // 2. 放在 Presentation 组（渲染/表现层），这是专门处理 UI 和视图的地方
    [UpdateInGroup(typeof(PresentationSystemGroup))] 
    public partial struct GoldUIPresentationSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            // 性能保护：如果 UI 单例还没初始化，或者被销毁了，就别跑了
            if (GoldUIController.Instance == null) return;

            // 查询本机玩家的金币
            // 因为这是 ClientSimulation，且我们用 GhostOwnerIsLocal 过滤
            // 所以这里一定只会查到一个实体（就是玩家自己）
            foreach (var gold in SystemAPI.Query<RefRO<PlayerGold>>()
                         .WithAll<GhostOwnerIsLocal>())
            {
                // 获取当前数值
                int currentGold = (int)gold.ValueRO.CurrentValue;
                
                // 推送给 UI
                GoldUIController.Instance.UpdateGoldView(currentGold);
            }
        }
    }
}