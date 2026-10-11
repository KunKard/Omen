using UnityEngine;
using Omen.Core;

namespace Omen.View
{
    /// <summary>
    /// 配色表（View 层）。
    ///
    /// 颜色数据本身存在 Core 层的 <see cref="IslandConfigData"/> 里（用 0xRRGGBB 表示，不依赖 UnityEngine），
    /// 这一类只负责把它翻译成 <see cref="Color"/> 供渲染使用。
    /// 好处：颜色可以进存档、能被单元测试校验，Phase 7 换美术时也只改配置资产。
    /// </summary>
    public readonly struct CellPalette
    {
        public readonly Color Water;
        public readonly Color Land;
        public readonly Color Grass;
        public readonly Color Rabbit;
        public readonly Color Wolf;

        /// <summary>地形无法识别时的兜底色（洋红色），出现即说明数据有问题。</summary>
        public static readonly Color Fallback = new Color(1f, 0f, 1f, 1f);

        private CellPalette(Color water, Color land, Color grass, Color rabbit, Color wolf)
        {
            Water = water;
            Land = land;
            Grass = grass;
            Rabbit = rabbit;
            Wolf = wolf;
        }

        public static CellPalette FromConfig(IslandConfigData data)
        {
            if (data == null)
            {
                var fallback = new IslandConfigData();
                fallback.Sanitize();
                data = fallback;
            }

            return new CellPalette(
                ToColor(data.WaterColor),
                ToColor(data.LandColor),
                ToColor(data.GrassColor),
                ToColor(data.RabbitColor),
                ToColor(data.WolfColor));
        }

        /// <summary>按物种取颜色。</summary>
        public Color ForSpecies(SpeciesKind kind)
        {
            switch (kind)
            {
                case SpeciesKind.Rabbit:
                    return Rabbit;
                case SpeciesKind.Wolf:
                    return Wolf;
                case SpeciesKind.Grass:
                    return Grass;
                default:
                    return Fallback;
            }
        }

        /// <summary>把颜色整体提亮（用于"进食闪烁"这类瞬时高亮）。factor 1 = 不变，2 = 两倍亮。</summary>
        public static Color Brighten(Color color, float factor)
        {
            return new Color(color.r * factor, color.g * factor, color.b * factor, color.a);
        }

        /// <summary>把颜色向某个目标混合。amount 0 = 原色，1 = 目标色。</summary>
        public static Color Blend(Color from, Color to, float amount)
        {
            return new Color(
                from.r + (to.r - from.r) * amount,
                from.g + (to.g - from.g) * amount,
                from.b + (to.b - from.b) * amount,
                from.a + (to.a - from.a) * amount);
        }

        /// <summary>把 0xRRGGBB 还原成 Color。</summary>
        public static Color ToColor(uint rgb24)
        {
            return new Color(
                ((rgb24 >> 16) & 0xFF) / 255f,
                ((rgb24 >> 8) & 0xFF) / 255f,
                (rgb24 & 0xFF) / 255f,
                1f);
        }
    }
}
