using System;

namespace Omen.Core
{
    /// <summary>
    /// 生态系统配置：三个物种的参数 + 随机种子。纯 C# 数据载体，不引用 UnityEngine。
    /// </summary>
    [Serializable]
    public class EcosystemConfigData
    {
        /// <summary>随机种子。同种子 + 同岛屿 ⇒ 完全相同的演化过程。</summary>
        public int Seed = 1;

        /// <summary>兔。</summary>
        public SpeciesConfigData Rabbit = CreateRabbitDefaults();

        /// <summary>狼。</summary>
        public SpeciesConfigData Wolf = CreateWolfDefaults();

        /// <summary>草。</summary>
        public SpeciesConfigData Grass = CreateGrassDefaults();

        /// <summary>兔的默认参数。</summary>
        public static SpeciesConfigData CreateRabbitDefaults()
        {
            return new SpeciesConfigData
            {
                Kind = SpeciesKind.Rabbit,
                InitialCount = 12,
                MaxEnergy = 14f,
                InitialEnergy = 12f,
                EnergyDrainPerTick = 0.7f,
                EnergyPerFood = 5f,
                // 繁殖参数是兔草平衡的核心：
                //   门槛必须明显低于 MaxEnergy。若门槛被 Sanitize 夹到等于 MaxEnergy，
                //   就变成"满能量才能生"，而吃两口草就满了 —— 繁殖几乎免费，兔群会指数爆炸。
                //   成本 6 / 冷却 14 让"生一胎"要付出约 9 个 tick 的觅食代价，兔群才有平衡点。
                ReproductionEnergyThreshold = 10f,
                ReproductionEnergyCost = 6f,
                ReproductionCooldownTicks = 14f,
                PopulationCap = 60,
                VisionRadius = 5,
                MoveStepsPerTick = 1,
                PreyKind = SpeciesKind.None,
                PredatorKind = SpeciesKind.Wolf,
            };
        }

        /// <summary>狼的默认参数。</summary>
        public static SpeciesConfigData CreateWolfDefaults()
        {
            return new SpeciesConfigData
            {
                Kind = SpeciesKind.Wolf,
                InitialCount = 2,
                MaxEnergy = 30f,
                InitialEnergy = 24f,
                EnergyDrainPerTick = 0.8f,
                EnergyPerFood = 25f,
                // 狼的繁殖刻意做得比兔慢很多：捕食者一旦快速增殖就会把猎物吃绝，
                // 然后自己饿死 —— 这是最难看的一种崩盘。
                ReproductionEnergyThreshold = 26f,
                ReproductionEnergyCost = 16f,
                ReproductionCooldownTicks = 50f,
                PopulationCap = 8,
                VisionRadius = 4,
                MoveStepsPerTick = 1,
                PreyKind = SpeciesKind.Rabbit,
                PredatorKind = SpeciesKind.None,
            };
        }

        /// <summary>草的默认参数。</summary>
        public static SpeciesConfigData CreateGrassDefaults()
        {
            return new SpeciesConfigData
            {
                Kind = SpeciesKind.Grass,
                InitialCount = 20,
                // 关键平衡结论（由批量模拟得出）：
                //   自限检查的是"目标格"的草邻居数。上限 2 时草会长到约 60% 覆盖率后停住，
                //   既不铺满全岛（那会让"覆盖 ≥50%"这个生存条件永久成立、失去意义），
                //   又能持续再生（被兔子啃掉的格子草邻居少，可以长回来）。
                //   上限 3 → 约 80%，上限 4 → 约 93%，上限 0 → 100%。
                SpreadAttemptsPerTick = 1.0f,
                SpreadChance = 0.75f,
                MaxGrassNeighbors = 2,
                RequireGrassNeighbor = true,
            };
        }

        public EcosystemConfigData Clone()
        {
            return new EcosystemConfigData
            {
                Seed = Seed,
                Rabbit = Rabbit.Clone(),
                Wolf = Wolf.Clone(),
                Grass = Grass.Clone(),
            };
        }

        public void Sanitize()
        {
            Rabbit.Sanitize();
            Wolf.Sanitize();
            Grass.Sanitize();
        }
    }
}
