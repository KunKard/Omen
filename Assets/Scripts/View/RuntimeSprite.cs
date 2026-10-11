using UnityEngine;

namespace Omen.View
{
    /// <summary>
    /// 运行时 Sprite 工厂。
    ///
    /// Phase 1 还没有美术资源，用一张 1x1 白色贴图生成的 Sprite 当"纯色块"。
    /// 所有 Tile 共用同一个 Sprite，靠 <see cref="SpriteRenderer.color"/> 染色区分地形
    /// —— 这样材质与贴图只有一份，SpriteRenderer 之间可以合批。
    ///
    /// Phase 7 换成真正的像素美术时，只要把 <see cref="Shared"/> 换成图集里的 Sprite 即可，
    /// TileView 不需要改动。
    /// </summary>
    public static class RuntimeSprite
    {
        private static Sprite _shared;
        private static Sprite _disc;

        /// <summary>1 世界单位大小的白色方块 Sprite（PPU = 1，因此 1 格 = 1 单位）。</summary>
        public static Sprite Shared
        {
            get
            {
                if (_shared == null)
                {
                    _shared = CreateSolid();
                }

                return _shared;
            }
        }

        /// <summary>
        /// 白色实心圆。生物用它 —— 圆点比方块更容易一眼区分"这是活物"而不是地形。
        /// 直径 12 像素，边缘保留一圈抗锯齿过渡（虽然像素风用 Point 过滤，但缩小时更干净）。
        /// </summary>
        public static Sprite Disc
        {
            get
            {
                if (_disc == null)
                {
                    _disc = CreateDisc(12);
                }

                return _disc;
            }
        }

        private static Sprite CreateSolid()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "OmenSolid",
                // 像素风必须用 Point 过滤，否则方块边缘会被插值糊掉。
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false, false);

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f,
                0,
                SpriteMeshType.FullRect);

            sprite.name = "OmenSolidSprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Sprite CreateDisc(int diameter)
        {
            if (diameter < 2)
            {
                diameter = 2;
            }

            var texture = new Texture2D(diameter, diameter, TextureFormat.RGBA32, false)
            {
                name = "OmenDisc",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            float radius = diameter * 0.5f;
            var pixels = new Color32[diameter * diameter];
            for (int y = 0; y < diameter; y++)
            {
                for (int x = 0; x < diameter; x++)
                {
                    float dx = x + 0.5f - radius;
                    float dy = y + 0.5f - radius;
                    // 用略微收紧的半径，让圆形在像素网格上看起来更圆。
                    bool inside = dx * dx + dy * dy <= radius * radius;
                    pixels[y * diameter + x] = inside
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, diameter, diameter),
                new Vector2(0.5f, 0.5f),
                diameter,
                0,
                SpriteMeshType.FullRect);

            sprite.name = "OmenDiscSprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
