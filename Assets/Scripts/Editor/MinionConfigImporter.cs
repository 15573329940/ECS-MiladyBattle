using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using SO; // 你的 namespace
namespace Editor
{
    
    public class MinionConfigImporter : EditorWindow
{
    // 配置 CSV 路径和 SO 输出路径
    private const string CSVPath = "Assets/Settings/Excels/MinionConfig.csv";
    private const string SOPath = "Assets/Settings/MinionUpgradeConfig.asset";

    [MenuItem("Tools/导表工具/导入小兵属性 (Import Minion Config)")]
    public static void ImportConfig()
    {
        // 1. 确保 CSV 存在
        if (!File.Exists(CSVPath))
        {
            Debug.LogError($"[导表失败] 找不到CSV文件: {CSVPath}。请检查路径或将Excel另存为CSV。");
            return;
        }

        // 2. 加载或创建 ScriptableObject
        MinionUpgradeConfig configSO = AssetDatabase.LoadAssetAtPath<MinionUpgradeConfig>(SOPath);
        if (configSO == null)
        {
            configSO = ScriptableObject.CreateInstance<MinionUpgradeConfig>();
            AssetDatabase.CreateAsset(configSO, SOPath);
        }

        // 3. 读取 CSV 内容
        string[] lines = File.ReadAllLines(CSVPath);
        if (lines.Length <= 1) return; // 只有表头或为空

        // 清空旧数据，准备重新填充
        configSO.UnitTypes = new List<MinionTypeData>();

        // 字典辅助构建： Key=兵种名, Value=兵种数据
        Dictionary<string, MinionTypeData> typeDict = new Dictionary<string, MinionTypeData>();

        // 4. 解析每一行 (从第1行开始，跳过表头)
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] cols = line.Split(','); // 简单 CSV 解析
            
            // 数据映射 (一定要对应 Excel 的列顺序)
            // UnitName, Level, Hp, Attack, MoveSpeed, UpgradeCost, UnitCount, ModelRadius
            string unitName = cols[0].Trim();
            if(!int.TryParse(cols[1], out int level)) continue;
            int hp = int.Parse(cols[2]);
            int attack = int.Parse(cols[3]);
            float moveSpeed = float.Parse(cols[4]);
            int upgradeCost = int.Parse(cols[5]);
            int unitCount = int.Parse(cols[6]);
            float radius = float.Parse(cols[7]);
            // [新增] 解析新字段 (假设在第 8, 9, 10 列)
            // 建议加个 Length 检查防止旧表报错
            int spawnCost = cols.Length > 8 ? int.Parse(cols[8]) : 3;
            int killGold = cols.Length > 9 ? int.Parse(cols[9]) : 3;
            float projScale = cols.Length > 10 ? float.Parse(cols[10]) : 1.0f;
            // --- 构建嵌套结构 ---
            
            // A. 如果是新兵种，先创建兵种容器
            if (!typeDict.ContainsKey(unitName))
            {
                var newType = new MinionTypeData
                {
                    Name = unitName,
                    LevelDatas = new List<MinionLevelData>()
                };
                typeDict.Add(unitName, newType);
                configSO.UnitTypes.Add(newType); // 保持列表顺序
            }

            // B. 创建等级数据
            var levelData = new MinionLevelData
            {
                Level = level,
                Hp = hp,
                Attack = attack,
                MoveSpeed = moveSpeed,
                UpgradeCost = upgradeCost,
                UnitCount = unitCount,
                ModelRadius = radius,
                SpawnCost = spawnCost,
                KillGold = killGold,
                ProjectileScale = projScale
            };

            // C. 加入到对应兵种的等级列表中
            typeDict[unitName].LevelDatas.Add(levelData);
        }

        // 5. 数据后处理 (可选：按等级排序，防止 Excel 乱序)
        foreach (var unitType in configSO.UnitTypes)
        {
            unitType.LevelDatas = unitType.LevelDatas.OrderBy(x => x.Level).ToList();
        }

        // 6. 保存资产
        EditorUtility.SetDirty(configSO);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"<color=green>[导表成功]</color> 已更新 {configSO.UnitTypes.Count} 个兵种的数据到 {SOPath}");
    }
}
    public class MinionConfigAutoReloader : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (string str in importedAssets)
            {
                // 如果检测到是那个 CSV 文件变了
                if (str.Contains("MinionConfig.csv"))
                {
                    // 自动调用导入逻辑
                    MinionConfigImporter.ImportConfig();
                    return;
                }
            }
        }
    }
}