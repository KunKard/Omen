using System;

namespace Omen.Core
{
    /// <summary>
    /// 一次 tick 之后的生态统计快照。纯数据，供 HUD、日志、平衡分析使用。
    /// </summary>
    public struct EcosystemStats
    {
        /// <summary>累计 tick 数。</summary>
        public int Tick;

        /// <summary>兔的数量。</summary>
        public int RabbitCount;

        /// <summary>狼的数量。</summary>
        public int WolfCount;

        /// <summary>长草的格子数。</summary>
        public int GrassCells;

        /// <summary>陆地格子数。</summary>
        public int LandCells;

        /// <summary>草的陆地覆盖率，0~1。这是草的存活判定依据。</summary>
        public float GrassCoverage
        {
            get { return LandCells > 0 ? (float)GrassCells / LandCells : 0f; }
        }

        /// <summary>本 tick 内兔的出生数。</summary>
        public int RabbitBirths;

        /// <summary>本 tick 内兔的死亡数。</summary>
        public int RabbitDeaths;

        /// <summary>本 tick 内狼的出生数。</summary>
        public int WolfBirths;

        /// <summary>本 tick 内狼的死亡数。</summary>
        public int WolfDeaths;

        /// <summary>本 tick 内被吃掉的草格数。</summary>
        public int GrassEaten;

        // ---------------------------------------------------------------- 移动诊断
        // 这四项用于回答"为什么有的生物长时间不动"。
        // 它们是纯统计量，不参与任何规则判断，因此可以随时关掉而不影响模拟。

        /// <summary>本 tick 内成功移动的动物次数。</summary>
        public int MovesSucceeded;

        /// <summary>本 tick 内想动但四个方向全被挡住（水/别的生物）而失败的次数。</summary>
        public int MovesBlocked;

        /// <summary>本 tick 内因为"没有值得追求的目标"而干脆没尝试移动的次数（草食动物吃饱时的随机游走也算一次尝试）。</summary>
        public int MovesNotAttempted;

        /// <summary>本 tick 内成功吃到东西的次数（兔吃草 + 狼吃兔）。</summary>
        public int MealsEaten;

        public override string ToString()
        {
            return "tick " + Tick
                + " 兔 " + RabbitCount + " 狼 " + WolfCount
                + " 草 " + GrassCells + "/" + LandCells + " (" + (GrassCoverage * 100f).ToString("F1") + "%)"
                + " 移动 " + MovesSucceeded + "/阻挡 " + MovesBlocked;
        }
    }

    /// <summary>
    /// 一次 tick 里发生的、值得记录的事件。Phase 5 的"转折点回溯"会消费这些数据。
    /// </summary>
    public struct EcosystemEvent
    {
        public int Tick;
        public SpeciesKind Kind;
        public EcosystemEventType Type;
        public GridPos Position;

        public override string ToString()
        {
            return "[" + Tick + "] " + Kind + " " + Type + " @(" + Position.X + "," + Position.Y + ")";
        }
    }

    /// <summary>事件类型。</summary>
    public enum EcosystemEventType
    {
        /// <summary>出生（草用"扩散"表示）。</summary>
        Born = 0,

        /// <summary>饿死。</summary>
        Starved = 1,

        /// <summary>被吃掉。</summary>
        Eaten = 2,

        /// <summary>老死 / 因年龄以外的规则移除。</summary>
        Died = 3,
    }
}
