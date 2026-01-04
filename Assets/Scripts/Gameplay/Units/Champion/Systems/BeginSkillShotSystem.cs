using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
// 注意：这里不需要引用 UnityEngine 了，因为我们不操作 GameObject

namespace TMG.NFE_Tutorial
{
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    public partial struct BeginSkillShotSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            // 必须使用 System 单例的 ECB，不要自己 new
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 1. 获取 ECB (用于在帧末尾应用更改，防止破坏预测)
            var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            
            var netTime = SystemAPI.GetSingleton<NetworkTime>();
            if (!netTime.IsFirstTimeFullyPredictingTick) return;
            var currentTick = netTime.ServerTick;
            var isServer = state.WorldUnmanaged.IsServer();

            // ========================================================================
            // 第一步：按下 W 键 -> 开启瞄准状态
            // ========================================================================
            // WithNone<AimSkillShotTag> 在组件继承 IEnableableComponent 后，
            // 会自动匹配到 "组件存在但被禁用 (Disabled)" 的实体。
            foreach (var skillShot in SystemAPI.Query<SkillShotAspect>()
                         .WithAll<Simulate>()
                         .WithNone<AimSkillShotTag>()
                         .WithNone<DeadTag>()) 
            {
                // 【重要改动 1】权限卫兵
                // 只有“服务器”和“本机玩家(Owner)”才有资格读取输入。
                // 敌方客户端(Ghost)没有输入数据，读了也是错的，所以直接跳过。
                bool isOwner = SystemAPI.HasComponent<GhostOwnerIsLocal>(skillShot.ChampionEntity);
                if (!isServer && !isOwner) continue;

                var isOnCooldown = true;
                var curTargetTicks = new AbilityCooldownTargetTicks();

                // 必须在预测批次中循环检查，以确保预测正确
                for (var i = 0u; i < netTime.SimulationStepBatchSize; i++)
                {
                    var testTick = currentTick;
                    testTick.Subtract(i);

                    // 从 InputBuffer 获取历史数据
                    if (!skillShot.CooldownTargetTicks.GetDataAtTick(testTick, out curTargetTicks))
                    {
                        curTargetTicks.SkillShotAbility = NetworkTick.Invalid;
                    }

                    // 检查 SkillShotAbility 的冷却时间戳
                    // 如果 Invalid 或者 当前时间 比 目标解锁时间 新 (IsNewerThan)，说明冷却好了
                    if (curTargetTicks.SkillShotAbility == NetworkTick.Invalid ||
                        !curTargetTicks.SkillShotAbility.IsNewerThan(currentTick))
                    {
                        isOnCooldown = false;
                        break;
                    }
                }

                // 如果还在冷却中，直接跳过，不开指示器
                if (isOnCooldown) continue;
                // ---------------------------

                // 检测玩家是否按下了 W
                if (!skillShot.BeginAttack) continue;

                // 【重要改动 2】只改数据状态，不加组件
                // 我们不再 AddComponent，而是把早就挂在身上的组件“启用 (Enable)”。
                // 这样不会改变内存结构，预测极其稳定。
                ecb.SetComponentEnabled<AimSkillShotTag>(skillShot.ChampionEntity, true);

                // 【重要改动 3】删除了所有 UI 代码！
                // 以前这里写的 Instantiate(UI) 和 AddComponent(UIReference) 全部删掉！
                // 那些脏活累活现在交给 SkillShotVisualSystem 去做。
            }

            // ========================================================================
            // 第二步：按下鼠标左键 -> 发射技能
            // ========================================================================
            // WithAll<AimSkillShotTag> 会自动匹配到 "组件被启用 (Enabled)" 的实体。
            foreach (var skillShot in SystemAPI.Query<SkillShotAspect>()
                         .WithAll<AimSkillShotTag, Simulate>()
                         .WithNone<DeadTag>())
            {
                // 【重要改动 1】权限卫兵再次出现
                bool isOwner = SystemAPI.HasComponent<GhostOwnerIsLocal>(skillShot.ChampionEntity);
                if (!isServer && !isOwner) continue;

                // 检测玩家是否点击了鼠标
                if (!skillShot.ConfirmAttack) continue;

                // 1. 生成子弹实体 (这是纯数据操作，是安全的)
                var skillShotAbility = ecb.Instantiate(skillShot.AbilityPrefab);
                // 设置子弹位置
                ecb.SetComponent(skillShotAbility, skillShot.SpawnPosition);
                ecb.SetComponent(skillShotAbility, skillShot.Team);
                
                // 【重要改动 2】只改数据状态，不移除组件
                // 以前是 RemoveComponent，现在改为禁用 (Disable)。
                ecb.SetComponentEnabled<AimSkillShotTag>(skillShot.ChampionEntity, false);
                
                // 【重要改动 3】删除了所有 UI 销毁代码！
                // 以前这里写的 Object.Destroy(UI) 和 RemoveComponent(UIReference) 全部删掉！
                // UI 系统发现 Tag 被禁用后，会自动把 UI 关掉。

                // --- 客户端预测修正逻辑 (CommandData) ---
                if (isServer) continue;
                
                skillShot.CooldownTargetTicks.GetDataAtTick(currentTick, out var curTargetTicks);
                var newCooldownTargetTick = currentTick;
                newCooldownTargetTick.Add(skillShot.CooldownTicks);
                curTargetTicks.SkillShotAbility = newCooldownTargetTick;
                    
                var nextTick = currentTick;
                nextTick.Add(1u);
                curTargetTicks.Tick = nextTick;

                skillShot.CooldownTargetTicks.AddCommandData(curTargetTicks);
            }
            
            // 【重要改动 4】删掉了最后的 UI 清理循环
            // 以前最后的 foreach (SkillShotUIReference...) 整个删掉。
            
            // 【重要改动 5】严禁使用 ecb.Playback
            // ecb.Playback(state.EntityManager); // 这行绝对不能有！
        }
    }
}