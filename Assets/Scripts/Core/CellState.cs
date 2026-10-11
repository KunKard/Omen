using System;

namespace Omen.Core
{
    /// <summary>
    /// 单个格子的完整状态。纯 C# 值类型，不引用 UnityEngine。
    /// 语义约定：草可以被生物踩在脚下（草是地面附着物，生物是站立者），两者互不冲突。
    /// </summary>
    [Serializable]
    public struct CellState
    {
        /// <summary>地形。</summary>
        public TileType Type;

        /// <summary>这一格是否长着草。</summary>
        public bool HasGrass;

        /// <summary>占据这一格的生物 ID；<see cref="NoCreature"/> 表示空。</summary>
        public int OccupantId;

        /// <summary>
        /// 这一格的草正在被啃：还需要几个 tick 才会被吃光。
        /// 0 表示没有被正在进行的啃食。
        ///
        /// 为什么把"啃食进度"放在格子上而不是生物身上：
        /// 它是"这棵草被啃到什么程度"的属性，生物走开后进度自然作废。
        /// 放在格子上，兔子离开时只需清掉这一格的状态，不用满世界找那只兔子。
        /// </summary>
        public int EatingTicks;

        /// <summary>表示"没有生物"的占位值。</summary>
        public const int NoCreature = -1;

        /// <summary>这一格是否有生物站着。</summary>
        public bool HasCreature
        {
            get { return OccupantId >= 0; }
        }

        /// <summary>是否是可通行的陆地（不含草与生物的判断）。</summary>
        public bool IsLand
        {
            get { return Type == TileType.Land; }
        }

        /// <summary>构造一格海水。</summary>
        public static CellState Water()
        {
            return new CellState { Type = TileType.Water, HasGrass = false, OccupantId = NoCreature };
        }

        /// <summary>构造一格空的陆地（无草无生物）。</summary>
        public static CellState Land()
        {
            return new CellState { Type = TileType.Land, HasGrass = false, OccupantId = NoCreature };
        }

        public override string ToString()
        {
            return Type + "(grass=" + HasGrass + ", occupant=" + OccupantId + ")";
        }
    }
}
