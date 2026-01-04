using Unity.Entities;
using UnityEngine;

namespace TMG.NFE_Tutorial
{

    public class MinimapAuthoring : MonoBehaviour
    {
        public Mesh DotMesh;
        public Material DotMaterial;
        public Material BGMaterial;
        public float MapSize = 100f;
        public float BaseScale = 5f;

        class Baker : Baker<MinimapAuthoring>
        {
            public override void Bake(MinimapAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponentObject(entity, new MinimapConfig
                {
                    DotMesh = authoring.DotMesh,
                    DotMaterial = authoring.DotMaterial,
                    BGMaterial = authoring.BGMaterial, // 【新增】
                    MapSize = authoring.MapSize,
                    BaseScale = authoring.BaseScale
                });
            }
        }
    }
    public class MinimapConfig : IComponentData
    {
        public Mesh DotMesh;
        public Material DotMaterial;
        public Material BGMaterial;  // 【新增】用于画背景
        public float MapSize;   // 地图边长 (比如 100)
        public float BaseScale; // 点的大小 (像素)
    }
}