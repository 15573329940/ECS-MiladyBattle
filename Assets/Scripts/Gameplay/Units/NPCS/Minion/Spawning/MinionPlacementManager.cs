using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections; // [新增]
using Unity.NetCode;
using UnityEngine;
using SO;
namespace TMG.NFE_Tutorial
{
    public class MinionPlacementManager : MonoBehaviour
    {
        public static MinionPlacementManager Instance;

        [Header("配置引用")]
        public MinionUpgradeConfig UpgradeConfig; // 拖入你的 SO 文件

        [Header("预览模型")]
        public GameObject[] PreviewPrefabs; 

        private List<GameObject> _previewInstances = new List<GameObject>(); // 改为 List
        private int _selectedUnitIndex = -1; 
        private Camera _mainCamera;
        private Plane _groundPlane = new Plane(Vector3.up, 0); 

        // ECS 数据
        private EntityManager _entityManager;
        private EntityQuery _localPlayerQuery;
        private EntityQuery _techQuery;
        public Vector3 CurrentMousePosition { get; private set; }
        public int SelectedUnitIndex => _selectedUnitIndex;
        public bool IsClicking { get; private set; } 
        public bool IsPlacing => _selectedUnitIndex != -1;

        private void Awake()
        {
            Instance = this;
            _mainCamera = Camera.main;
            _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            _localPlayerQuery = _entityManager.CreateEntityQuery(typeof(OwnerChampTag), typeof(MobaTeam));
            _techQuery = _entityManager.CreateEntityQuery(
                typeof(PlayerMinionTech), 
                typeof(GhostOwnerIsLocal)
            );
        }

        void Update()
        {
            // 1. 获取队伍
            TeamType currentTeam = TeamType.Blue; 
            if (!_localPlayerQuery.IsEmpty)
            {
                var entity = _localPlayerQuery.GetSingletonEntity();
                currentTeam = _entityManager.GetComponentData<MobaTeam>(entity).Value;
            }

            // 2. 输入处理
            HandleSelectionInput(KeyCode.Alpha1, 0);
            HandleSelectionInput(KeyCode.Alpha2, 1);
            HandleSelectionInput(KeyCode.Alpha3, 2);

            // 3. 核心：更新预览位置和旋转
            UpdatePreviewPosition(currentTeam);

            if (IsPlacing && Input.GetMouseButtonDown(0)) IsClicking = true;
            else IsClicking = false;

            if (Input.GetMouseButtonDown(1)) CancelSelection();
        }
        // [新增] 辅助函数：获取当前兵种等级
        private int GetCurrentLevel(int typeIndex)
        {
            if (_techQuery.IsEmpty) return 0; // 如果还没连上或者没初始化，默认0级

            // 安全获取实体
            using var entities = _techQuery.ToEntityArray(Allocator.Temp);
            if (entities.Length > 0)
            {
                var tech = _entityManager.GetComponentData<PlayerMinionTech>(entities[0]);
                return tech.GetLevel(typeIndex);
            }
            return 0;
        }
        private void HandleSelectionInput(KeyCode key, int index)
        {
            if (Input.GetKeyDown(key))
            {
                if (_selectedUnitIndex == index) CancelSelection();
                else SelectUnit(index);
            }
        }

        private void SelectUnit(int index)
        {
            if (index >= PreviewPrefabs.Length) return;
            CancelSelection();
            _selectedUnitIndex = index;
            int currentLevel = GetCurrentLevel(index);
            // 获取该兵种、当前等级（这里预览默认取Level 0）的配置数量
            // [修改] 2. 根据等级获取 UnitCount
            int count = 1;
            if (UpgradeConfig != null && index < UpgradeConfig.UnitTypes.Count)
            {
                var levelData = UpgradeConfig.UnitTypes[index].LevelDatas;
                // 确保配置表里有这个等级的数据，防止越界
                if (levelData != null && currentLevel < levelData.Count)
                {
                    count = levelData[currentLevel].UnitCount; // 读取当前等级的数量
                }
            }
            float scale = (currentLevel + 1) / 2f;
            if (PreviewPrefabs[index] != null)
            {
                for(int i=0; i<count; i++)
                {
                    var go = Instantiate(PreviewPrefabs[index]);
                    go.transform.localScale = Vector3.one * scale; // 应用缩放
                    _previewInstances.Add(go);
                }
            }
        }

        private void CancelSelection()
        {
            _selectedUnitIndex = -1;
            foreach (var go in _previewInstances) if (go != null) Destroy(go);
            _previewInstances.Clear();
        }

        private void UpdatePreviewPosition(TeamType team)
        {
            if (!IsPlacing) return;

            Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);
            if (_groundPlane.Raycast(ray, out float enter))
            {
                Vector3 rawHitPoint = ray.GetPoint(enter);
                
                // 1. 保持你原有的三角形校验逻辑 (不动)
                Vector3 centerPos = GetClampedPosition(rawHitPoint, team);
                CurrentMousePosition = centerPos;

                // 2. 计算阵列和朝向
                CalculateFormationAndRotation(centerPos, team);
            }
        }

        // --- 核心：阵列计算 (与 ECS System 保持逻辑一致) ---
        private void CalculateFormationAndRotation(Vector3 center, TeamType team)
        {
            int count = _previewInstances.Count;
            if (count == 0) return;

            // A. 计算朝向
            float yRotation = (team == TeamType.Blue) ? -135f : 45f;
            Quaternion rotation = Quaternion.Euler(0, yRotation, 0);

            // B. 阵列参数
            // 每排个数 = Sqrt(Total) 向上取整
            int rowCapacity = Mathf.CeilToInt(Mathf.Sqrt(count));
            float spacing = 1.5f; // 可以从 Config 读取: UpgradeConfig.UnitTypes[idx].LevelDatas[0].ModelRadius * 2

            // C. 轴向向量 (归一化)
            // 横排 y = -x -> (1, 0, -1)
            Vector3 rowDir = new Vector3(1, 0, -1).normalized;
            // 纵排 y = x  -> (1, 0, 1)
            Vector3 colDir = new Vector3(1, 0, 1).normalized;

            // D. 计算整体偏移以居中
            // 总行数
            int totalRows = Mathf.CeilToInt((float)count / rowCapacity);
            // 最后一行的列数 (用于更精细的居中，这里简化为矩形居中)
            float totalWidth = (rowCapacity - 1) * spacing;
            float totalHeight = (totalRows - 1) * spacing;
            Vector3 startOffset = center - (rowDir * totalWidth * 0.5f) - (colDir * totalHeight * 0.5f);

            for (int i = 0; i < count; i++)
            {
                // 计算行列索引
                int rowIndex = i / rowCapacity; // 第几行 (纵向)
                int colIndex = i % rowCapacity; // 第几列 (横向)

                Vector3 pos = startOffset + (colDir * rowIndex * spacing) + (rowDir * colIndex * spacing);

                var instance = _previewInstances[i];
                if (instance != null)
                {
                    instance.transform.position = pos;
                    instance.transform.rotation = rotation;
                }
            }
        }

        // --- 几何算法：三角形钳制 ---
        private Vector3 GetClampedPosition(Vector3 target, TeamType team)
        {
            float x = target.x;
            float z = target.z;
            Vector2 p1, p2, p3;

            if (team == TeamType.Blue)
            {
                // 蓝方范围: x > -50, z > -50, x + z < -20
                p1 = new Vector2(-50, -50); // 左下
                p2 = new Vector2(-50, 30);  // 左上交点
                p3 = new Vector2(30, -50);  // 右下交点
            }
            else
            {
                // 红方范围: x < 50, z < 50, x + z > 20
                p1 = new Vector2(50, 50);   // 右上
                p2 = new Vector2(50, -30);  // 右下交点
                p3 = new Vector2(-30, 50);  // 左上交点
            }

            Vector2 point = new Vector2(x, z);

            // 1. 在内部则直接返回
            if (IsPointInTriangle(point, p1, p2, p3)) return target;

            // 2. 在外部则吸附到最近边
            Vector2 closestOnP1P2 = GetClosestPointOnSegment(point, p1, p2);
            Vector2 closestOnP2P3 = GetClosestPointOnSegment(point, p2, p3);
            Vector2 closestOnP3P1 = GetClosestPointOnSegment(point, p3, p1);

            float d1 = Vector2.SqrMagnitude(point - closestOnP1P2);
            float d2 = Vector2.SqrMagnitude(point - closestOnP2P3);
            float d3 = Vector2.SqrMagnitude(point - closestOnP3P1);

            Vector2 finalPos = closestOnP1P2;
            float minDst = d1;

            if (d2 < minDst) { minDst = d2; finalPos = closestOnP2P3; }
            if (d3 < minDst) { minDst = d3; finalPos = closestOnP3P1; }

            return new Vector3(finalPos.x, 0, finalPos.y);
        }

        private bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b);
            float d2 = Sign(p, b, c);
            float d3 = Sign(p, c, a);
            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(hasNeg && hasPos);
        }

        private float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        private Vector2 GetClosestPointOnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ap = p - a;
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / ab.sqrMagnitude);
            return a + ab * t;
        }
    }
}