using System;

namespace Omen.Core
{
    /// <summary>
    /// 一只动物（兔或狼）的完整状态。纯 C# 值类型，不引用 UnityEngine，可直接 JSON 序列化。
    ///
    /// 草不走这个结构：草长在格子上，状态就是 <see cref="CellState.HasGrass"/> 那一个布尔值。
    /// 给草单独建生物对象是过度设计 —— 16x16 里最多 256 格草，用格子状态表示最省。
    /// </summary>
    [Serializable]
    public struct Creature
    {
        /// <summary>本槽位的代际号。每次复用槽位时 +1，用于识别"过期的句柄"。</summary>
        public uint Generation;

        /// <summary>存活标记。死亡后槽位进入空闲列表，等待被复用。</summary>
        public bool Alive;

        /// <summary>物种。</summary>
        public SpeciesKind Kind;

        /// <summary>所在格子。</summary>
        public GridPos Position;

        /// <summary>
        /// 能量。
        /// 兔：吃一次草回复；归零则饿死。
        /// 狼：吃一只兔回复；归零则饿死。
        /// </summary>
        public float Energy;

        /// <summary>距离下一次可繁殖还剩多少能量单位。归零即可繁殖。</summary>
        public float ReproductionCooldown;

        /// <summary>已经活了多久（tick 数）。仅用于调试与统计。</summary>
        public int Age;

        /// <summary>
        /// 繁殖倒计时。&gt; 0 表示正在繁殖中，还需要这么多 tick 才会生出后代。
        /// 0 表示当前没有在繁殖。
        ///
        /// 兔与狼的繁殖耗时不同（兔 1 回合、狼 2 回合），这段时间它们不移动 ——
        /// 这正是"繁殖要花时间"这个规则在数据上的体现。
        /// </summary>
        public int BreedingTicks;

        /// <summary>
        /// 繁殖搭档的槽位下标；-1 表示没有搭档。
        /// 倒计时结束时需要再确认一次搭档还在旁边，否则取消这次繁殖
        /// （搭档可能在半途被吃掉或走开了）。
        /// </summary>
        public int BreedingPartner;

        public bool IsAnimal
        {
            get { return Kind.IsAnimal(); }
        }

        public override string ToString()
        {
            return "#" + Generation + " " + Kind + " @(" + Position.X + "," + Position.Y + ")"
                + " E=" + Energy.ToString("F1") + " R=" + ReproductionCooldown.ToString("F1");
        }
    }

    /// <summary>
    /// 生物句柄：对外代表"某一只具体的生物"，而不是数组下标。
    ///
    /// 为什么需要它：生物池会复用死亡生物的槽位。如果外部只存下标，
    /// 那么"第 3 号槽位"在原来的生物死后会指向新出生的生物 —— 这是极难排查的 bug。
    /// 句柄同时带代际号，槽位被复用时代际号会变，于是过期句柄会被识别为 <see cref="IsNone"/>。
    /// </summary>
    [Serializable]
    public struct CreatureHandle : IEquatable<CreatureHandle>
    {
        /// <summary>槽位下标；-1 表示空句柄。</summary>
        public int Index;

        /// <summary>该槽位当时的代际号。</summary>
        public uint Generation;

        /// <summary>空句柄。</summary>
        public static readonly CreatureHandle None = new CreatureHandle { Index = -1, Generation = 0u };

        public bool IsNone
        {
            get { return Index < 0; }
        }

        public bool Equals(CreatureHandle other)
        {
            return Index == other.Index && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is CreatureHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Index * 397) ^ (int)Generation;
            }
        }

        public static bool operator ==(CreatureHandle a, CreatureHandle b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(CreatureHandle a, CreatureHandle b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return IsNone ? "<none>" : ("#" + Index + "g" + Generation);
        }
    }
}
