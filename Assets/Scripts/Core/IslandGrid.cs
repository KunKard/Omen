using System;
using System.Text;

namespace Omen.Core
{
    /// <summary>
    /// 岛格地图数据层 —— <b>整个游戏唯一的真相来源</b>。
    ///
    /// 架构铁律：
    ///   1. 任何系统想改变格子状态，必须调用本类的方法，禁止绕过它直接改画面 / 坐标。
    ///   2. 本类不引用 UnityEngine，保证 JSON 存档、回放、单元测试、快照对比全部可行。
    ///   3. 本类不发事件、不持有委托。View 层通过对比 <see cref="Version"/> 自行同步。
    ///
    /// 约定：一维数组按行优先存储，index = Y * Width + X。
    /// </summary>
    public sealed class IslandGrid
    {
        private CellState[] _cells;
        private int _landCount;
        private int _grassCount;

        public IslandGrid(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "网格尺寸必须为正数。");
            }

            Width = width;
            Height = height;
            _cells = new CellState[width * height];
            RebuildFromPositions(null);
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>格子总数。</summary>
        public int CellCount
        {
            get { return _cells.Length; }
        }

        /// <summary>陆地格数（含长草的和站着生物的）。</summary>
        public int LandCount
        {
            get { return _landCount; }
        }

        /// <summary>长草的格子数。</summary>
        public int GrassCount
        {
            get { return _grassCount; }
        }

        /// <summary>海水格数。</summary>
        public int WaterCount
        {
            get { return _cells.Length - _landCount; }
        }

        /// <summary>
        /// 数据版本号。每次状态变更 +1。
        /// View 层只需记住上次同步的版本号，就能以最低成本发现"该刷新了"。
        /// </summary>
        public int Version { get; private set; }

        /// <summary>按坐标读写格子。读取返回副本，写入会更新统计与版本号。</summary>
        public CellState this[int x, int y]
        {
            get { return _cells[GridUtility.ToIndex(x, y, Width)]; }
            set
            {
                int index = GridUtility.ToIndex(x, y, Width);
                WriteCell(index, value);
            }
        }

        /// <summary>按坐标读取格子（带边界检查，越界返回海水）。</summary>
        public CellState Get(int x, int y)
        {
            if (!GridUtility.InBounds(x, y, Width, Height))
            {
                return CellState.Water();
            }

            return _cells[GridUtility.ToIndex(x, y, Width)];
        }

        /// <summary>按一维索引读取格子（不做边界检查，供内部与性能敏感路径使用）。</summary>
        public CellState GetByIndex(int index)
        {
            return _cells[index];
        }

        public bool InBounds(int x, int y)
        {
            return GridUtility.InBounds(x, y, Width, Height);
        }

        // ---------------------------------------------------------------- 生成与重建

        /// <summary>
        /// 用一批陆地坐标完全重建网格（其余为海水）。
        /// 传 null 表示全海水；这也会重置统计与版本号。
        /// </summary>
        public void RebuildFromPositions(System.Collections.Generic.List<GridPos> landPositions)
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i] = CellState.Water();
            }

            if (landPositions != null)
            {
                for (int i = 0; i < landPositions.Count; i++)
                {
                    GridPos p = landPositions[i];
                    if (!InBounds(p.X, p.Y))
                    {
                        continue;
                    }

                    _cells[GridUtility.ToIndex(p.X, p.Y, Width)] = CellState.Land();
                }
            }

            RecountAll();
            Version++;
        }

        // ---------------------------------------------------------------- 状态变更（唯一入口）

        /// <summary>
        /// 就地修改一格。传入的委托会收到当前状态的副本，返回修改后的状态。
        /// 例：<c>grid.Mutate(p, c =&gt; { c.HasGrass = true; return c; });</c>
        /// </summary>
        public void Mutate(GridPos pos, Func<CellState, CellState> mutator)
        {
            if (!InBounds(pos.X, pos.Y))
            {
                return;
            }

            int index = GridUtility.ToIndex(pos.X, pos.Y, Width);
            CellState next = mutator(_cells[index]);
            WriteCell(index, next);
        }

        /// <summary>设置某格是否有草。只能长在陆地上；海水调用无效。</summary>
        public void SetGrass(GridPos pos, bool hasGrass)
        {
            if (!InBounds(pos.X, pos.Y))
            {
                return;
            }

            int index = GridUtility.ToIndex(pos.X, pos.Y, Width);
            CellState cell = _cells[index];
            if (cell.Type != TileType.Land || cell.HasGrass == hasGrass)
            {
                return;
            }

            cell.HasGrass = hasGrass;
            WriteCell(index, cell);
        }

        /// <summary>
        /// 兔吃掉某格的草。返回是否真的吃到了（用来判断"移动后是否获得能量"，逻辑放在调用方）。
        /// </summary>
        public bool TryConsumeGrass(GridPos pos)
        {
            if (!InBounds(pos.X, pos.Y))
            {
                return false;
            }

            int index = GridUtility.ToIndex(pos.X, pos.Y, Width);
            CellState cell = _cells[index];
            if (!cell.HasGrass)
            {
                return false;
            }

            cell.HasGrass = false;
            WriteCell(index, cell);
            return true;
        }

        /// <summary>把某格标记为被生物占据。传 <see cref="CellState.NoCreature"/> 表示清空。</summary>
        public void SetOccupant(GridPos pos, int creatureId)
        {
            if (!InBounds(pos.X, pos.Y))
            {
                return;
            }

            int index = GridUtility.ToIndex(pos.X, pos.Y, Width);
            CellState cell = _cells[index];
            if (cell.OccupantId == creatureId)
            {
                return;
            }

            cell.OccupantId = creatureId;
            WriteCell(index, cell);
        }

        /// <summary>
        /// 把生物从 <paramref name="from"/> 移动到 <paramref name="to"/>。
        /// 目标格原有的占据者会被覆盖（战斗力结算由 Phase 2 的系统层负责，本类不管规则）。
        /// 坐标越界或生物 ID 非法时不做任何事。
        /// </summary>
        public void MoveOccupant(GridPos from, GridPos to, int creatureId)
        {
            if (creatureId < 0 || !InBounds(from.X, from.Y) || !InBounds(to.X, to.Y) || from == to)
            {
                return;
            }

            int fromIndex = GridUtility.ToIndex(from.X, from.Y, Width);
            int toIndex = GridUtility.ToIndex(to.X, to.Y, Width);

            CellState source = _cells[fromIndex];
            if (source.OccupantId == creatureId)
            {
                source.OccupantId = CellState.NoCreature;
                _cells[fromIndex] = source;
                Version++;
            }

            CellState target = _cells[toIndex];
            target.OccupantId = creatureId;
            _cells[toIndex] = target;
            Version++;
        }

        /// <summary>统计当前占据格子的生物数量（不区分物种；物种由 Phase 2 的生物表负责）。</summary>
        public int CountOccupiedCells()
        {
            int count = 0;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i].HasCreature)
                {
                    count++;
                }
            }

            return count;
        }

        // ---------------------------------------------------------------- 调试

        /// <summary>
        /// 转成 ASCII 图，方便在 Console 里肉眼检查。
        /// 图例：<c>~</c> 海水、<c>.</c> 空陆地、<c>"</c> 草地、<c>@</c> 有生物的格子、<c>*</c> 有生物且长草。
        /// 行序为从上到下（Y 从大到小），与屏幕一致。
        /// </summary>
        public string ToAsciiMap()
        {
            var sb = new StringBuilder((Width + 1) * Height + 32);
            sb.Append("    ");
            for (int x = 0; x < Width; x++)
            {
                sb.Append(x % 10);
            }

            sb.AppendLine();

            for (int y = Height - 1; y >= 0; y--)
            {
                sb.Append(y.ToString("D2")).Append(" |");
                for (int x = 0; x < Width; x++)
                {
                    sb.Append(SymbolFor(_cells[GridUtility.ToIndex(x, y, Width)]));
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static char SymbolFor(CellState cell)
        {
            if (cell.Type == TileType.Water)
            {
                return '~';
            }

            if (cell.HasCreature)
            {
                return cell.HasGrass ? '*' : '@';
            }

            return cell.HasGrass ? '"' : '.';
        }

        // ---------------------------------------------------------------- 内部

        /// <summary>统一的写入出口：更新统计、递增版本号。</summary>
        private void WriteCell(int index, CellState next)
        {
            CellState previous = _cells[index];
            if (previous.Type == next.Type && previous.HasGrass == next.HasGrass && previous.OccupantId == next.OccupantId)
            {
                return;
            }

            if (previous.Type == TileType.Land)
            {
                _landCount--;
            }

            if (previous.HasGrass)
            {
                _grassCount--;
            }

            _cells[index] = next;

            if (next.Type == TileType.Land)
            {
                _landCount++;
            }

            if (next.HasGrass)
            {
                _grassCount++;
            }

            Version++;
        }

        private void RecountAll()
        {
            _landCount = 0;
            _grassCount = 0;

            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i].Type == TileType.Land)
                {
                    _landCount++;
                }

                if (_cells[i].HasGrass)
                {
                    _grassCount++;
                }
            }
        }
    }
}
