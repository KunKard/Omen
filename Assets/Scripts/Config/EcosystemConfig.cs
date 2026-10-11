using UnityEngine;
using Omen.Core;

namespace Omen.Config
{
    /// <summary>
    /// 生态系统配置资产 —— 把三个物种的行为参数暴露到 Inspector，
    /// 运行时转成纯 C# 的 <see cref="EcosystemConfigData"/> 交给模拟层。
    ///
    /// 创建方式：Project 窗口右键 → Create → Omen → Ecosystem Config
    /// 建议路径：Assets/Config/EcosystemConfig.asset
    ///
    /// 调参提示（这些默认值是批量模拟跑出来的，改动前建议先看 Docs/Design.md 的平衡记录）：
    ///   - 草太能长 → 调低 MaxGrassNeighbors；草长不起来 → 调高
    ///   - 兔群爆炸把草啃绝 → 调高繁殖成本 / 繁殖冷却
    ///   - 狼把兔吃绝 → 调低狼的吃兔回复量，或调高狼的繁殖门槛
    /// </summary>
    [CreateAssetMenu(fileName = "EcosystemConfig", menuName = "Omen/Ecosystem Config", order = 2)]
    public sealed class EcosystemConfig : ScriptableObject
    {
        [Header("兔")]
        [Tooltip("开局投放数量。")]
        [Range(0, 100)] [SerializeField] private int rabbitInitialCount = 12;

        [Tooltip("每个 tick 消耗的能量。归零即饿死。")]
        [Range(0f, 5f)] [SerializeField] private float rabbitEnergyDrain = 0.7f;

        [Tooltip("吃一口草回复的能量。")]
        [Range(0f, 30f)] [SerializeField] private float rabbitEnergyPerFood = 5f;

        [Tooltip("能量上限。繁殖门槛必须低于它，否则繁殖会变成\"满能量才能生\"，几乎免费。")]
        [Range(1f, 60f)] [SerializeField] private float rabbitMaxEnergy = 14f;

        [Tooltip("繁殖所需的能量下限。必须明显低于能量上限。")]
        [Range(0f, 30f)] [SerializeField] private float rabbitReproThreshold = 10f;

        [Tooltip("繁殖消耗的能量。")]
        [Range(0f, 30f)] [SerializeField] private float rabbitReproCost = 6f;

        [Tooltip("繁殖冷却（tick 数）。")]
        [Range(1f, 200f)] [SerializeField] private float rabbitReproCooldown = 14f;

        [Tooltip("种群上限。")]
        [Range(0, 500)] [SerializeField] private int rabbitPopulationCap = 60;

        [Tooltip("视野半径（格）。决定多远内能发现草或天敌。")]
        [Range(0, 32)] [SerializeField] private int rabbitVisionRadius = 5;

        [Header("狼")]
        [Range(0, 100)] [SerializeField] private int wolfInitialCount = 2;

        [Tooltip("每个 tick 消耗的能量。狼耗能应高于兔，否则捕食压力不足。")]
        [Range(0f, 5f)] [SerializeField] private float wolfEnergyDrain = 0.8f;

        [Tooltip("吃到一只兔回复的能量。")]
        [Range(0f, 60f)] [SerializeField] private float wolfEnergyPerFood = 25f;

        [Range(0f, 60f)] [SerializeField] private float wolfReproThreshold = 26f;

        [Range(0f, 60f)] [SerializeField] private float wolfReproCost = 16f;

        [Tooltip("狼的繁殖冷却。刻意远长于兔 —— 捕食者快速增殖会把猎物吃绝，然后自己饿死。")]
        [Range(1f, 400f)] [SerializeField] private float wolfReproCooldown = 50f;

        [Range(0, 500)] [SerializeField] private int wolfPopulationCap = 8;

        [Tooltip("狼的视野半径。")]
        [Range(0, 32)] [SerializeField] private int wolfVisionRadius = 4;

        [Header("草")]
        [Tooltip("开局草格数。")]
        [Range(0, 256)] [SerializeField] private int grassInitialCount = 20;

        [Tooltip("每格每 tick 尝试扩散的次数（可以是小数，表示概率性尝试）。")]
        [Range(0f, 8f)] [SerializeField] private float grassSpreadAttempts = 1.0f;

        [Tooltip("单次扩散尝试成功的概率。")]
        [Range(0f, 1f)] [SerializeField] private float grassSpreadChance = 0.75f;

        [Tooltip("目标格允许的草邻居数量上限。这是控制草覆盖率上限的关键旋钮：\n" +
                 "2 → 约 60%（推荐，让\"草≥50%\"这个生存条件有悬念）\n" +
                 "3 → 约 80%    4 → 约 93%    0 → 100%（不限制）")]
        [Range(0, 4)] [SerializeField] private int grassMaxNeighbors = 2;

        [Tooltip("是否要求目标格至少挨着一格草才能长草。")]
        [SerializeField] private bool grassRequireNeighbor = true;

        /// <summary>转成 Core 层可用的纯 C# 数据。</summary>
        public EcosystemConfigData ToData()
        {
            var data = new EcosystemConfigData();

            data.Rabbit.InitialCount = rabbitInitialCount;
            data.Rabbit.MaxEnergy = rabbitMaxEnergy;
            data.Rabbit.EnergyDrainPerTick = rabbitEnergyDrain;
            data.Rabbit.EnergyPerFood = rabbitEnergyPerFood;
            data.Rabbit.ReproductionEnergyThreshold = rabbitReproThreshold;
            data.Rabbit.ReproductionEnergyCost = rabbitReproCost;
            data.Rabbit.ReproductionCooldownTicks = rabbitReproCooldown;
            data.Rabbit.PopulationCap = rabbitPopulationCap;
            data.Rabbit.VisionRadius = rabbitVisionRadius;

            data.Wolf.InitialCount = wolfInitialCount;
            data.Wolf.EnergyDrainPerTick = wolfEnergyDrain;
            data.Wolf.EnergyPerFood = wolfEnergyPerFood;
            data.Wolf.ReproductionEnergyThreshold = wolfReproThreshold;
            data.Wolf.ReproductionEnergyCost = wolfReproCost;
            data.Wolf.ReproductionCooldownTicks = wolfReproCooldown;
            data.Wolf.PopulationCap = wolfPopulationCap;
            data.Wolf.VisionRadius = wolfVisionRadius;

            data.Grass.InitialCount = grassInitialCount;
            data.Grass.SpreadAttemptsPerTick = grassSpreadAttempts;
            data.Grass.SpreadChance = grassSpreadChance;
            data.Grass.MaxGrassNeighbors = grassMaxNeighbors;
            data.Grass.RequireGrassNeighbor = grassRequireNeighbor;

            data.Sanitize();
            return data;
        }

        private void OnValidate()
        {
            // 门槛 ≥ 能量上限是无效配置：Sanitize 会把它夹下来，但在 Inspector 里先提示更直观，
            // 也避免"参数明明填了却没生效"这种困惑。
            if (rabbitReproThreshold >= rabbitMaxEnergy)
            {
                rabbitReproThreshold = Mathf.Max(0f, rabbitMaxEnergy - 1f);
            }

            rabbitInitialCount = Mathf.Max(0, rabbitInitialCount);
            wolfInitialCount = Mathf.Max(0, wolfInitialCount);
            grassInitialCount = Mathf.Max(0, grassInitialCount);
        }
    }
}
