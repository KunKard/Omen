using UnityEngine;
using Omen.Core;

namespace Omen.View
{
    /// <summary>
    /// 单个格子的视觉表现。
    ///
    /// 架构铁律 2：View 只读。本类<b>不持有业务状态</b>，只把传进来的
    /// <see cref="CellState"/> 翻译成"什么颜色"。它不能反过来被玩法逻辑查询。
    ///
    /// 每个 Tile 是一个 GameObject + SpriteRenderer。16x16 = 256 个，
    /// 由 <see cref="GridView"/> 一次性建好后长期复用，不做增删。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TileView : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Color _lastColor;

        /// <summary>这个 Tile 对应的格子坐标。只用于调试定位，玩法逻辑不应该读它。</summary>
        public GridPos Position { get; private set; }

        /// <summary>由 <see cref="GridView"/> 在创建时装配。</summary>
        public void Initialize(GridPos position, SpriteRenderer spriteRenderer, Color initialColor)
        {
            Position = position;
            _renderer = spriteRenderer;
            _lastColor = initialColor;

            if (_renderer != null)
            {
                _renderer.sprite = RuntimeSprite.Shared;
                _renderer.color = initialColor;
                _renderer.drawMode = SpriteDrawMode.Simple;
            }

            transform.position = WorldGrid.CellCenter(position);
        }

        /// <summary>
        /// 按格子状态刷新颜色。颜色没变化时不触碰 SpriteRenderer —— 这就是"只刷新变了的格子"的落点。
        /// </summary>
        public void Apply(CellState cell, in CellPalette palette)
        {
            Color color = ResolveColor(cell, palette);
            if (color.r == _lastColor.r && color.g == _lastColor.g && color.b == _lastColor.b && color.a == _lastColor.a)
            {
                return;
            }

            _lastColor = color;
            if (_renderer != null)
            {
                _renderer.color = color;
            }
        }

        /// <summary>
        /// 决定这一格该显示什么颜色。Phase 1 只有三种状态；Phase 2 加入生物后会在这里分派到生物 View。
        /// </summary>
        public static Color ResolveColor(CellState cell, in CellPalette palette)
        {
            switch (cell.Type)
            {
                case TileType.Water:
                    return palette.Water;

                case TileType.Land:
                    return cell.HasGrass ? palette.Grass : palette.Land;

                default:
                    return CellPalette.Fallback;
            }
        }
    }
}
