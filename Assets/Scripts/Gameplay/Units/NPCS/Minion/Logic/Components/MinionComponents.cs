using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using SO;
namespace TMG.NFE_Tutorial
{
    public struct MinionTag : IComponentData {}
    public struct NewMinionTag : IComponentData {}
    
    public struct MinionPathPosition : IBufferElementData
    {
        [GhostField(Quantization = 0)] public float3 Value;
    }
    public struct MinionLevel : IComponentData
    {
        public int Value; // 当前等级 (比如 0, 1, 2...)
    }
    public struct MinionPathIndex : IComponentData
    {
        [GhostField] public byte Value;
    }
    public struct MinionTypeIndex : IComponentData
    {
        public int Value;
    }
}