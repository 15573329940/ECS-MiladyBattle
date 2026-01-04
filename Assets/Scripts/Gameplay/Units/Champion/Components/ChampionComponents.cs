using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
namespace TMG.NFE_Tutorial
{
    public struct ChampTag : IComponentData
    {
        
    }
    public struct DeadTag : IComponentData, IEnableableComponent 
    { 
    }
    public struct NewChampTag : IComponentData {}//进行初始化用的
    public struct OwnerChampTag : IComponentData {}
    public struct MobaTeam : IComponentData
    {
        [GhostField] public TeamType Value;
    }
    public struct OriCharacterMoveSpeed : IComponentData
    {
        public float Value;
    }
    
    [GhostComponent(PrefabType = GhostPrefabType.AllPredicted)]
    public struct CharacterMoveSpeed : IComponentData
    {
        [GhostField] public float Value;
    }

    [GhostComponent(PrefabType = GhostPrefabType.AllPredicted)]
    public struct ChampMoveTargetPosition : IInputComponentData
    {
        [GhostField(Quantization = 0)] public float3 Value;
    }
    [GhostComponent(PrefabType = GhostPrefabType.AllPredicted)]
    public struct AbilityInput : IInputComponentData
    {
        [GhostField] public InputEvent AoeAbility;
        [GhostField] public InputEvent SkillShotAbility;
        [GhostField] public InputEvent ConfirmSkillShotAbility;
    }

    [GhostComponent(PrefabType = GhostPrefabType.AllPredicted)]
    public struct AimInput : IInputComponentData
    {
        [GhostField(Quantization = 0)]public float3 Value;
    }
    
}