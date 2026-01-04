using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace TMG.NFE_Tutorial
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct InitializeDestroyOnTimerSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var config = NetCodeConfig.Global;
            if (config == null) config = UnityEngine.Resources.Load<NetCodeConfig>("NetcodeConfig");
            if (config == null) return;

            var simulationTickRate = config.ClientServerTickRate.SimulationTickRate;
            var currentTick = SystemAPI.GetSingleton<NetworkTime>().ServerTick;

            // 【优化】查询已有 DestroyAtTick 但还没有设置过过期时间 (Value == 0) 的实体
            // 假设 NetworkTick.Invalid 或者 0 代表未初始化
            foreach (var (timer, destroyAt) in SystemAPI.Query<RefRO<DestroyOnTimer>, RefRW<DestroyAtTick>>())
            {
                // 如果已经被初始化过了（Tick > 0），就跳过
                // 注意：NetworkTick 是结构体，需要检查其内部值，或者根据你的逻辑判断
                if (destroyAt.ValueRO.Value.IsValid) continue; 

                var lifetimeInTicks = (uint)(timer.ValueRO.Value * simulationTickRate);
                var targetTick = currentTick;
                targetTick.Add(lifetimeInTicks);

                // 【关键】直接修改数据，不使用 ECB，不产生结构性变化
                destroyAt.ValueRW.Value = targetTick;
            }
        }
    }
}