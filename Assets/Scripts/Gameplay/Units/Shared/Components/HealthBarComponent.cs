using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine; // 必须引用这个命名空间！
namespace TMG.NFE_Tutorial
{
    public struct HealthBarRef : IComponentData
    {
        public Entity BarEntity;
    }
    public struct HealthBarScale : IComponentData
    {
        public float4 Value;
    }
    [MaterialProperty("_FillAmount")]
    public struct URPMaterialPropertyFillAmount : IComponentData
    {
        // 存储_FillAmount的值（Range(0,1)对应float类型）
        public float Value;
    }
    public class HealthBarUIReference : ICleanupComponentData
    {
        public GameObject Value;
    }

    public struct HealthBarOffset : IComponentData
    {
        public float3 Value;
    }
}