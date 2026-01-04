using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace TMG.NFE_Tutorial
{
    [GhostComponent(PrefabType = GhostPrefabType.All)]
    public struct MaxHitPoints : IComponentData
    {
        [GhostField]
        public int Value;// 只要数值不变，它就不会耗费带宽
    }
    [GhostComponent(PrefabType = GhostPrefabType.All)]
    public struct CurrentHitPoints : IComponentData
    {
        [GhostField] public int Value;
    }
    [GhostComponent(PrefabType = GhostPrefabType.AllPredicted)]
    public struct DamageBufferElement : IBufferElementData
    {
        public int Value;
    }
    [GhostComponent(PrefabType = GhostPrefabType.AllPredicted, OwnerSendType = SendToOwnerType.SendToNonOwner)]
    public struct DamageThisTick : ICommandData
    {
        public NetworkTick Tick { get; set; }
        public int Value;
    }

    public struct AttackDamage : IComponentData
    {
        [GhostField]public int Value;
    }
    public struct AbilityPrefabs : IComponentData
    {
        public Entity AoeAbility;
        public Entity SkillShotAbility;
    }
    public struct DestroyOnTimer : IComponentData
    {
        public float Value;
    }

    public struct DestroyAtTick : IComponentData
    {
        [GhostField] public NetworkTick Value;
    }
    public struct DestroyEntityTag :IComponentData{}
    public struct DamageOnTrigger : IComponentData
    {
        [GhostField]public int Value;
    }
    // 用于错峰执行寻敌逻辑
    public struct NpcTargetCheckTimer : IComponentData
    {
        public float Value; // 倒计时
    }
    [GhostComponent(PrefabType = GhostPrefabType.AllPredicted)]
    public struct AlreadyDamagedEntity : IBufferElementData
    {
        public Entity Value;
    }
    public struct AbilityCooldownTicks : IComponentData
    {
        public uint AoeAbility;
        public uint SkillShotAbility;
    }

    [GhostComponent(PrefabType = GhostPrefabType.AllPredicted)]
    public struct AbilityCooldownTargetTicks : ICommandData
    {
        public NetworkTick Tick { get; set; }
        public NetworkTick AoeAbility;
        public NetworkTick SkillShotAbility;
    }
    [GhostComponent(PrefabType = GhostPrefabType.AllPredicted)]
    public struct AimSkillShotTag : IComponentData, IEnableableComponent 
    { 
    }
    public struct AbilityMoveSpeed : IComponentData
    {
        public float Value;
    }
    //npc
    public struct NpcTargetRange : IComponentData
    {
        public float Value;
    }
    public struct NpcAttackRange : IComponentData
    {
        public float Value;
    }
    public struct NpcTargetEntity : IComponentData
    {
        [GhostField] public Entity Value;
    }
    
    public struct NpcAttackProperties : IComponentData
    {
        public float3 FirePointOffset;
        public uint CooldownTickCount;
        public Entity AttackPrefab;
    }

    public struct NpcAttackCooldown : ICommandData
    {
        public NetworkTick Tick { get; set; }
        public NetworkTick Value;
    }
    
    public struct GameOverOnDestroyTag : IComponentData {}
}