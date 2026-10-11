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
    /// 动画（纯表现，不影响模拟）：
    ///   - 草长出来时，颜色从裸地色渐变到草色，而不是"啪"地变绿
    ///   - 动画由 <see cref="Advance"/> 按真实时间推进，与模拟的 tick 节奏<b>无关</b> ——
    ///     所以即使把一局的时间调快调慢，草的生长动画时长也保持不变
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TileView : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Color _lastColor;
        private Color _currentColor;
        private Color _animationSource;
        private float _animationProgress = 1f;
        private float _animationSeconds = 0.45f;

        /// <summary>这个 Tile 对应的格子坐标。只用于调试定位，玩法逻辑不应该读它。</summary>
        public GridPos Position { get; private set; }

        /// <summary>这个格子正在做颜色过渡动画。</summary>
        public bool IsAnimating
        {
            get { return _animationProgress < 1f; }
        }

        /// <summary>由 <see cref="GridView"/> 在创建时装配。</summary>
        public void Initialize(GridPos position, SpriteRenderer spriteRenderer, Color initialColor, float animationSeconds)
        {
            Position = position;
            _renderer = spriteRenderer;
            _lastColor = initialColor;
            _currentColor = initialColor;
            _animationSource = initialColor;
            _animationProgress = 1f;
            _animationSeconds = Mathf.Max(0f, animationSeconds);

            if (_renderer != null)
            {
                _renderer.sprite = RuntimeSprite.Shared;
                _renderer.color = initialColor;
                _renderer.drawMode = SpriteDrawMode.Simple;
            }

            transform.position = WorldGrid.CellCenter(position);
        }

        /// <summary>换配色后直接落定，不做动画（避免整张图一起闪一下）。</summary>
        public void SetColorImmediate(Color color)
        {
            _lastColor = color;
            _currentColor = color;
            _animationSource = color;
            _animationProgress = 1f;

            if (_renderer != null)
            {
                _renderer.color = color;
            }
        }

        /// <summary>
        /// 按格子状态刷新颜色。目标色没变化时什么都不做 —— 这是"只刷新变了的格子"的落点。
        /// 目标色变化时启动一段从"当前显示色"到"目标色"的过渡。
        /// </summary>
        public void Apply(CellState cell, in CellPalette palette)
        {
            Color target = ResolveColor(cell, palette);

            if (SameColor(target, _lastColor))
            {
                return;
            }

            _lastColor = target;
            _animationSource = _currentColor;

            if (_animationSeconds <= 0f)
            {
                SetColorImmediate(target);
                return;
            }

            _animationProgress = 0f;
        }

        /// <summary>
        /// 按真实时间推进动画。返回 true 表示这一帧颜色有变化（需要写回 SpriteRenderer）。
        /// 没有动画在跑时开销就是一次比较。
        /// </summary>
        public bool Advance(float deltaSeconds)
        {
            if (_animationProgress >= 1f)
            {
                return false;
            }

            if (_animationSeconds <= 0f)
            {
                SetColorImmediate(_lastColor);
                return true;
            }

            _animationProgress = Mathf.Clamp01(_animationProgress + deltaSeconds / _animationSeconds);

            // 缓动，让"长出来"这一步有加速感而不是匀速。
            float eased = Mathf.SmoothStep(0f, 1f, _animationProgress);
            _currentColor = CellPalette.Blend(_animationSource, _lastColor, eased);

            if (_renderer != null)
            {
                _renderer.color = _currentColor;
            }

            if (_animationProgress >= 1f)
            {
                _currentColor = _lastColor;
            }

            return true;
        }

        /// <summary>
        /// 决定这一格该显示什么颜色。Phase 2 起，有生物站着的格子显示生物的颜色 ——
        /// 生物精灵会盖在上面，但把格子本身也染色能让边缘更整齐，避免"圆点悬空"的观感。
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

        private static bool SameColor(Color a, Color b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }
    }
}
