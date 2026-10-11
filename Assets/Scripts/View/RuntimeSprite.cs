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
    }
}
