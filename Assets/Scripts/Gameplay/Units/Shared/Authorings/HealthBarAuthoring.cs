using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
namespace TMG.NFE_Tutorial
{
    public class HealthBarAuthoring : MonoBehaviour
{
    // public Vector2 Scale; //这一行可以删掉了，我们不再需要手动填

    public class Baker : Baker<HealthBarAuthoring>
    {
        public override void Bake(HealthBarAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            // 1. 获取同一物体上的 MeshRenderer
            var renderer = GetComponent<MeshRenderer>();
            
            // 防御性编程：确保有材质球
            if (renderer == null || renderer.sharedMaterial == null) 
                return;

            // 2. 从材质球中读取 _ScaleXY 属性
            // 注意：必须用 sharedMaterial (编辑器下读共享材质)
            // GetVector 返回的是 Vector4
//            Vector4 matScale = renderer.sharedMaterial.GetVector("_ScaleXY");

            // 3. 把读到的值填入 ECS 组件
           // AddComponent(entity, new HealthBarScale
           // {
                // 直接把材质球里的 X, Y 拿过来用
            //    Value = new float4(matScale.x, matScale.y, 0, 0)
           // });

            // 添加颜色组件
            AddComponent(entity, new URPMaterialPropertyBaseColor 
            { 
                Value = new float4(1, 0, 0, 0) 
            });
            AddComponent(entity, new URPMaterialPropertyFillAmount
        {
            Value = 1.0f // 初始填充100%（对应血条满值）
        });
        }
    }
}
}
