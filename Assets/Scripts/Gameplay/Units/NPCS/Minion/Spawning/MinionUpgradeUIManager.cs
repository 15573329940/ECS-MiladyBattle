using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;
using Unity.NetCode;
using TMG.NFE_Tutorial;
using Unity.Collections;
using SO; // 引用配置命名空间
using TMPro;

public class MinionUpgradeUIManager : MonoBehaviour
{
    [System.Serializable]
    public struct UpgradeButtonInfo
    {
        public Button Btn;
        public Image Icon;
        public TextMeshProUGUI InfoText; // 显示 "Lv.0 -> Lv.1\nCost: 50"
        public int TypeIndex; // 0:坦克, 1:射手, 2:刺客
    }

    public MinionUpgradeConfig Config; // 拖入 SO
    public UpgradeButtonInfo[] UpgradeButtons; // 在 Inspector 配置3个按钮

    private EntityManager _entityManager;
    private EntityQuery _localPlayerQuery;

    void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        
        // 查找本地玩家：必须是本地拥有 (GhostOwnerIsLocal) 且有 PlayerGold/Tech 组件
        _localPlayerQuery = _entityManager.CreateEntityQuery(
            typeof(PlayerGold), 
            typeof(PlayerMinionTech), 
            typeof(GhostOwnerIsLocal)
        );

        // 绑定点击事件
        foreach (var btnInfo in UpgradeButtons)
        {
            int idx = btnInfo.TypeIndex;
            btnInfo.Btn.onClick.AddListener(() => OnUpgradeClick(idx));
        }
    }

    void Update()
    {
        var entity = GetLocalPlayerEntity();
        if (entity == Entity.Null) return;
        
        var gold = _entityManager.GetComponentData<PlayerGold>(entity);
        var tech = _entityManager.GetComponentData<PlayerMinionTech>(entity);

        foreach (var btnInfo in UpgradeButtons)
        {
            UpdateSingleButton(btnInfo, gold.CurrentValue, tech);
        }
    }
    private Entity GetLocalPlayerEntity()
    {
        // 因为 GhostOwnerIsLocal 是 Enableable 组件，不能用 GetSingletonEntity
        // 必须用 ToEntityArray
        using var entities = _localPlayerQuery.ToEntityArray(Allocator.Temp);
        if (entities.Length > 0)
        {
            return entities[0];
        }
        return Entity.Null;
    }
    void UpdateSingleButton(UpgradeButtonInfo info, float currentGold, PlayerMinionTech tech)
    {
        int currentLv = tech.GetLevel(info.TypeIndex);
        var typeData = Config.UnitTypes[info.TypeIndex];

        // 检查是否已满级 (假设最大级是2)
        if (currentLv >= typeData.LevelDatas.Count - 1)
        {
            info.Btn.interactable = false;
            info.InfoText.text = $"MAX LEVEL\nLv.{currentLv}";
            return;
        }

        // 获取下一级数据
        var nextLvData = typeData.LevelDatas[currentLv]; // 注意：配置里的 UpgradeCost 应该配在当前级，表示升到下一级的钱
        // 或者看你的配置逻辑，如果是 levelData[1].Cost 代表升到1级的钱，这里要注意索引

        // 假设：LevelDatas[0].UpgradeCost 是 0级升1级的钱
        int cost = nextLvData.UpgradeCost; 

        bool canAfford = currentGold >= cost;
        info.Btn.interactable = canAfford;

        // 只有当有钱且未满级时，按钮才亮
        // 这里可以加颜色变化逻辑，Unity Button Disable 会自动变灰

        info.InfoText.text = $"{typeData.Name}\nLv.{currentLv} -> Lv.{currentLv + 1}\nCost: {cost}";
    }

    void OnUpgradeClick(int typeIndex)
    {
        // 1. 获取玩家实体（只为了传参，不修改它）
        var playerEntity = GetLocalPlayerEntity();
        if (playerEntity == Entity.Null) return;

        var ecb = _entityManager.World.GetExistingSystemManaged<BeginSimulationEntityCommandBufferSystem>()
            .CreateCommandBuffer();
        
        // 2. 【核心修改】创建一个全新的临时实体 (相当于一封信)
        var requestEntity = ecb.CreateEntity();
        
        // 3. 填写信的内容
        var rpc = new UpgradeMinionRpc 
        { 
            MinionTypeIndex = typeIndex,
            TargetPlayer = playerEntity // 告诉服务端：是这个玩家要升级
        };
        
        // 4. 把 RPC 组件和发送请求加在这个临时实体上
        ecb.AddComponent(requestEntity, rpc);
        ecb.AddComponent<SendRpcCommandRequest>(requestEntity); 
        
        // 玩家实体 playerEntity 完全未受影响，结构未变，不会导致 Ghost 错误！
    }
}