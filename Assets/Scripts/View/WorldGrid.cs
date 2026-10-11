using UnityEngine;
using Omen.Core;

namespace Omen.View
{
    /// <summary>
    /// 网格坐标 ↔ 世界坐标的唯一转换入口（架构铁律 4）。
    /// 整个项目里只允许在这一处出现"格子中心在世界哪里"的计算，
    /// 否则相机、Tile、生物、点击拾取会各自用不同的约定，偏移半格这类 bug 会到处冒。
    ///
    /// 约定：(0,0) 格的世界范围是 [0,1]×[0,1]，其中心在 (0.5, 0.5)。
    /// 于是 16x16 的地图占据世界空间 [0,16]×[0,16]。
    /// </summary>
    public static class WorldGrid
    {
        /// <summary>一格在世界单位下的边长。与 PPU 无关，PPU 由相机负责像素对齐。</summary>
        public const float CellSize = 1f;

        /// <summary>格子中心的世界坐标。</summary>
        public static Vector2 CellCenter(GridPos pos)
        {
            return new Vector2((pos.X + 0.5f) * CellSize, (pos.Y + 0.5f) * CellSize);
        }

        /// <summary>格子中心的世界坐标（按坐标分量传入，避免为了调用而构造 GridPos）。</summary>
        public static Vector2 CellCenter(int x, int y)
        {
            return new Vector2((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);
        }

        /// <summary>整张网格在世界空间中的中心点。相机用它来居中构图。</summary>
        public static Vector2 MapCenter(int width, int height)
        {
            return new Vector2(width * CellSize * 0.5f, height * CellSize * 0.5f);
        }

        /// <summary>整张网格的世界尺寸（宽, 高）。</summary>
        public static Vector2 MapSize(int width, int height)
        {
            return new Vector2(width * CellSize, height * CellSize);
        }

        /// <summary>世界坐标 → 格子坐标。落在网格外时返回的坐标可能是负数或超出范围，调用方自行用 InBounds 判断。</summary>
        public static GridPos WorldToCell(Vector2 world)
        {
            return new GridPos(Mathf.FloorToInt(world.x / CellSize), Mathf.FloorToInt(world.y / CellSize));
        }
    }
}
