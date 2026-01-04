using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
namespace TMG.NFE_Tutorial
{
    
    // 配置单例：存材质引用
    public class FloatingTextAuthoring : MonoBehaviour
    {
        public Material TextMaterial; // 拖入做好的 Shader Graph 材质
        public class Baker : Baker<FloatingTextAuthoring>
        {
            public override void Bake(FloatingTextAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponentObject(entity, new FloatingTextConfig
                {
                    Material = authoring.TextMaterial,
                    QuadMesh = CreateQuadMesh()
                });
            }

            private Mesh CreateQuadMesh()
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
                DestroyImmediate(go);
                return mesh;
            }
        }
    }
    public class FloatingTextConfig : IComponentData
    {
        public Material Material;
        public Mesh QuadMesh;
    }
}