using System;

namespace Omen.Core
{
    /// <summary>
    /// 时钟参数的纯数据载体，不引用 UnityEngine。
    ///
    /// 与 <see cref="IslandConfigData"/> 同样的分层思路：Unity 侧的
    /// <c>Omen.Config.GameClockConfig</c>（ScriptableObject）负责在 Inspector 里配置，
    /// 运行时转成本类喂给 <c>Omen.Systems.GameClock</c>。
    /// </summary>
    [Serializable]
    public class GameClockData
    {
        /// <summary>一局的总天数。第 15 天结算。</summary>
        public int TotalDays = 15;

        /// <summary>游戏内 1 天对应多少真实秒。</summary>
        public double SecondsPerDay = 12.0;

        /// <summary>一次生物行动（tick）对应多少真实秒。</summary>
        public double SecondsPerTick = 1.0;

        /// <summary>一天包含多少个 tick。由前两者推导，不单独配置，避免出现互相矛盾的参数。</summary>
        public int TicksPerDay
        {
            get
            {
                if (SecondsPerTick <= 0.0)
                {
                    return 1;
                }

                int ticks = (int)Math.Round(SecondsPerDay / SecondsPerTick);
                return ticks < 1 ? 1 : ticks;
            }
        }

        /// <summary>一局的总 tick 数。</summary>
        public int TotalTicks
        {
            get
            {
                long total = (long)TotalDays * TicksPerDay;
                return total > int.MaxValue ? int.MaxValue : (int)total;
            }
        }

        /// <summary>一局的总真实时长（秒）。</summary>
        public double TotalSeconds
        {
            get { return TotalDays * SecondsPerDay; }
        }

        /// <summary>把参数夹到合理范围，防止 Inspector 里手滑填出除零或倒流的时间。</summary>
        public void Sanitize()
        {
            TotalDays = Clamp(TotalDays, 1, 999);
            SecondsPerDay = Clamp(SecondsPerDay, 0.5, 600.0);
            SecondsPerTick = Clamp(SecondsPerTick, 0.05, 600.0);

            // 不允许 tick 比一天还长，否则一天里一个 tick 都跑不完，天数永远不会推进。
            if (SecondsPerTick > SecondsPerDay)
            {
                SecondsPerTick = SecondsPerDay;
            }
        }

        public GameClockData Clone()
        {
            return new GameClockData
            {
                TotalDays = TotalDays,
                SecondsPerDay = SecondsPerDay,
                SecondsPerTick = SecondsPerTick,
            };
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        private static double Clamp(double value, double min, double max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
