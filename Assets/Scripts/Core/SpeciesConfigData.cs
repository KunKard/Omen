using System;

namespace Omen.Core
{
    /// <summary>
    /// 单个物种的行为参数。纯 C# 数据载体，不引用 UnityEngine。
    ///
    /// 设计原则：模拟逻辑里<b>不出现</b>"如果是兔就…"这类硬编码分支，
    /// 只读这里的数值。于是 Phase 6 加预兆、Phase 8 做平衡时只改数据不改代码。
    /// </summary>
    [Serializable]
    public class SpeciesConfigData
    {
        // ---------------------------------------------------------------- 通用
        /// <summary>物种。</summary>
        public SpeciesKind Kind = SpeciesKind.Rabbit;

        /// <summary>开局投放数量。</summary>
        public int InitialCount = 12;

        /// <summary>能量上限。</summary>
        public float MaxEnergy = 10f;

        /// <summary>开局能量。</summary>
        public float InitialEnergy = 6f;

        /// <summary>每个 tick 消耗的能量。归零即饿死。</summary>
        public float EnergyDrainPerTick = 0.5f;

        /// <summary>吃一次食物回复的能量。</summary>
        public float EnergyPerFood = 4f;

        /// <summary>繁殖冷却（tick 数）。冷却结束且能量充足即可繁殖。</summary>
        public float ReproductionCooldownTicks = 6f;

        /// <summary>繁殖所需的能量下限。</summary>
        public float ReproductionEnergyThreshold = 6f;

        /// <summary>繁殖时消耗的能量（从亲代身上扣）。</summary>
        public float ReproductionEnergyCost = 3f;

        /// <summary>全局繁殖上限。达到后不再有新个体，防止数量爆炸。0 表示不限。</summary>
        public int PopulationCap = 120;

        // ---------------------------------------------------------------- 移动
        /// <summary>狼的视野半径（格）。0 表示逐格搜索全场。</summary>
        public int VisionRadius = 5;

        /// <summary>每次移动尝试的步数。1 = 每 tick 走一格。</summary>
        public int MoveStepsPerTick = 1;

        // ---------------------------------------------------------------- 掠食关系
        /// <summary>这个物种会捕食谁。None 表示不吃动物（兔子吃草，靠草规则处理）。</summary>
        public SpeciesKind PreyKind = SpeciesKind.None;

        /// <summary>这个物种会躲避谁。None 表示没有天敌。</summary>
        public SpeciesKind PredatorKind = SpeciesKind.None;

        // ---------------------------------------------------------------- 草专用
        /// <summary>
        /// 每个 tick 每个草格尝试扩散的次数。
        /// 这是控制"草多快占满全岛"的关键旋钮 —— 数值越大越快，详见模拟验证结论。
        /// </summary>
        public float SpreadAttemptsPerTick = 1f;

        /// <summary>单次扩散尝试成功的概率。</summary>
        public float SpreadChance = 1f;

        /// <summary>
        /// 一个草格允许的草邻居数量上限。超过则不再扩散（自限）。
        /// 0 表示不限制 —— 那样草会在十几个 tick 内铺满全岛，让"覆盖 50%"这个生存条件失去意义。
        /// </summary>
        public int MaxGrassNeighbors = 0;

        /// <summary>草的"能量"永不消耗，此字段对它无意义；保留是为了让配置结构统一。</summary>
        public bool IsPlant
        {
            get { return Kind.IsPlant(); }
        }

        /// <summary>把一个族群的参数复制出来，方便做"预兆只改某一个物种"的效果。</summary>
        public SpeciesConfigData Clone()
        {
            return (SpeciesConfigData)MemberwiseClone();
        }

        /// <summary>把参数夹到合理范围。</summary>
        public void Sanitize()
        {
            InitialCount = Clamp(InitialCount, 0, 2000);
            MaxEnergy = Clamp(MaxEnergy, 0.1f, 10000f);
            InitialEnergy = Clamp(InitialEnergy, 0f, MaxEnergy);
            EnergyDrainPerTick = Clamp(EnergyDrainPerTick, 0f, 1000f);
            EnergyPerFood = Clamp(EnergyPerFood, 0f, MaxEnergy);
            ReproductionCooldownTicks = Clamp(ReproductionCooldownTicks, 1f, 10000f);
            ReproductionEnergyThreshold = Clamp(ReproductionEnergyThreshold, 0f, MaxEnergy);
            ReproductionEnergyCost = Clamp(ReproductionEnergyCost, 0f, MaxEnergy);
            PopulationCap = Clamp(PopulationCap, 0, 100000);
            VisionRadius = Clamp(VisionRadius, 0, 128);
            MoveStepsPerTick = Clamp(MoveStepsPerTick, 1, 8);
            SpreadAttemptsPerTick = Clamp(SpreadAttemptsPerTick, 0f, 8f);
            SpreadChance = Clamp(SpreadChance, 0f, 1f);
            MaxGrassNeighbors = Clamp(MaxGrassNeighbors, 0, 4);
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
