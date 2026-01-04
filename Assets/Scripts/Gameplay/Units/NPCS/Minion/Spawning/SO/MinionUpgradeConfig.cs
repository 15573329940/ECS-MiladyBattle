
using System.Collections.Generic;
using UnityEngine;

namespace SO
{
    [CreateAssetMenu(fileName = "MinionUpgradeConfig", menuName = "DOTSAutoChess/MinionUpgradeConfig")]
    public class MinionUpgradeConfig : ScriptableObject
    {
        [Header("按顺序对应：1.坦克 2.射手 3.刺客")]
        public List<MinionTypeData> UnitTypes; 
    }

    [System.Serializable]
    public class MinionTypeData
    {
        public string Name; // 方便在 Inspector 里看是谁
        public List<MinionLevelData> LevelDatas; // 该兵种的等级表
    }

    [System.Serializable]
    public class MinionLevelData
    {
        public int Level;
        public int Attack;
        public int Hp;
        public float MoveSpeed;
        [Header("经济配置")]
        public int UpgradeCost; // 升到下一级花费
        public int SpawnCost;   // [新增] 生成该等级单位的单价 (兵阵总价 = 单价 * 数量)
        public int KillGold;    // [新增] 被击杀提供的金币奖励
        [Header("兵阵与战斗配置")]
        [Min(1)] public int UnitCount = 1; 
        public float ModelRadius = 0.5f;   
        public float ProjectileScale = 1.0f; // [新增] 技能/子弹模型缩放
    }
}