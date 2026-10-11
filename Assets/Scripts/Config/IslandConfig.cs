using UnityEngine;
using Omen.Core;

namespace Omen.Config
{
    /// <summary>
    /// 岛屿配置资产 —— 在 Inspector 里可视化调参，运行时转成纯 C# 的 <see cref="IslandConfigData"/> 交给 Core 层。
    ///
    /// 创建方式：Project 窗口右键 → Create → Omen → Island Config
    /// 建议路径：Assets/Config/IslandConfig.asset
    /// </summary>
    [CreateAssetMenu(fileName = "IslandConfig", menuName = "Omen/Island Config", order = 0)]
    public sealed class IslandConfig : ScriptableObject
    {
        [Header("网格")]
        [Tooltip("网格宽度（格）。改动后需要重新生成岛屿。")]
        [SerializeField] private int width = 16;

        [Tooltip("网格高度（格）。")]
        [SerializeField] private int height = 16;

        [Tooltip("随机种子。0 表示每次运行随机取一个（实际使用的种子会打到 Console 上，方便复现）。")]
        [SerializeField] private int seed = 0;

        [Header("大陆形状（决定半岛与海湾）")]
        [Tooltip("目标陆地占比。算法会二分搜索阈值逼近它；实际值会略低，因为孤岛会被裁掉。")]
        [Range(0.05f, 0.90f)]
        [SerializeField] private float landRatio = 0.45f;

        [Tooltip("半岛/海湾的数量。3 = 三叶形，4 = 经典小岛，8 = 高度破碎。")]
        [Range(2, 12)]
        [SerializeField] private int shapeLobes = 4;

        [Tooltip("各方向半径的随机落差。越大，伸出的半岛越长、海湾越深。")]
        [Range(0.0f, 0.85f)]
        [SerializeField] private float shapeRadiusJitter = 0.55f;

        [Tooltip("大陆形状的影响力。0 = 正圆；0.4 = 明显的半岛与海湾。上限 0.6 是保证陆地连通的安全值。")]
        [Range(0.0f, 0.6f)]
        [SerializeField] private float shapeInfluence = 0.40f;

        [Header("海岸线细节")]
        [Tooltip("海岸线噪声的影响力。给轮廓叠加小尺度锯齿，避免看起来像几何图形。")]
        [Range(0.0f, 0.4f)]
        [SerializeField] private float coastNoiseInfluence = 0.18f;

        [Tooltip("噪声频率倍率。0.25 对应约 4 格一个起伏；越小地形特征越大块。")]
        [Range(0.02f, 1.0f)]
        [SerializeField] private float coastNoiseScale = 0.25f;

        [Tooltip("噪声倍频程数。1 = 只有大起伏，3 = 带细节的海岸线。")]
        [Range(1, 6)]
        [SerializeField] private int noiseOctaves = 3;

        [Tooltip("每层倍频程的频率放大倍数。")]
        [Range(1.1f, 4.0f)]
        [SerializeField] private float noiseLacunarity = 2.0f;

        [Tooltip("每层倍频程的振幅衰减。越小则高频细节越弱、海岸线越平滑。")]
        [Range(0.05f, 0.95f)]
        [SerializeField] private float noiseGain = 0.45f;

        [Header("配色")]
        [Tooltip("海水颜色。")]
        [SerializeField] private Color waterColor = new Color(0.18f, 0.36f, 0.54f);

        [Tooltip("空陆地颜色。")]
        [SerializeField] private Color landColor = new Color(0.42f, 0.56f, 0.31f);

        [Tooltip("草地颜色。")]
        [SerializeField] private Color grassColor = new Color(0.56f, 0.75f, 0.29f);

        [Tooltip("兔的颜色。")]
        [SerializeField] private Color rabbitColor = new Color(0.95f, 0.91f, 0.84f);

        [Tooltip("狼的颜色。")]
        [SerializeField] private Color wolfColor = new Color(0.55f, 0.18f, 0.22f);

        [Header("显示")]
        [Tooltip("美术采图标准：一格按多少像素绘制。长期契约，改动会影响以后所有美术资源，尽量不要动。")]
        [Range(1, 128)]
        [SerializeField] private int pixelsPerUnit = 16;

        [Tooltip("相机放大倍数（整数）。屏幕上 1 格 = PixelsPerUnit × 本值 个像素。\n" +
                 "1 = 原始大小（1080p 下岛约占屏幕高度 24%）；2 = 推荐；3 = 约 71%；4 = 约 95%（贴边）。\n" +
                 "必须是整数，否则相邻像素大小不一会出现条纹。屏幕装不下时会自动降档。")]
        [Range(1, 8)]
        [SerializeField] private int cameraZoom = 2;

        [Header("动画（纯表现，不影响模拟结果）")]
        [Tooltip("草从裸地长出来时的颜色渐变时长（秒）。0 = 瞬间变绿。")]
        [Range(0f, 2f)]
        [SerializeField] private float grassGrowSeconds = 0.45f;

        [Tooltip("动物进食时的高亮闪烁时长（秒）。0 = 不闪。")]
        [Range(0f, 2f)]
        [SerializeField] private float eatFlashSeconds = 0.35f;

        [Tooltip("进食高亮的亮度倍数。1 = 不变，1.7 = 明显发亮。")]
        [Range(1f, 4f)]
        [SerializeField] private float eatFlashBrightness = 1.7f;

        public int Width { get { return width; } }

        public int Height { get { return height; } }

        /// <summary>配置的种子。0 表示"运行时随机"。</summary>
        public int Seed { get { return seed; } }

        /// <summary>转成 Core 层可用的纯 C# 数据。</summary>
        public IslandConfigData ToData()
        {
            var data = new IslandConfigData
            {
                Width = width,
                Height = height,
                LandRatio = landRatio,
                ShapeLobes = shapeLobes,
                ShapeRadiusJitter = shapeRadiusJitter,
                ShapeInfluence = shapeInfluence,
                CoastNoiseInfluence = coastNoiseInfluence,
                CoastNoiseScale = coastNoiseScale,
                NoiseOctaves = noiseOctaves,
                NoiseLacunarity = noiseLacunarity,
                NoiseGain = noiseGain,
                WaterColor = ToRgb24(waterColor),
                LandColor = ToRgb24(landColor),
                GrassColor = ToRgb24(grassColor),
                RabbitColor = ToRgb24(rabbitColor),
                WolfColor = ToRgb24(wolfColor),
                PixelsPerUnit = pixelsPerUnit,
                CameraZoom = cameraZoom,
                GrassGrowSeconds = grassGrowSeconds,
                EatFlashSeconds = eatFlashSeconds,
                EatFlashBrightness = eatFlashBrightness,
            };

            data.Sanitize();
            return data;
        }

        /// <summary>把 Unity 的 Color 转成 0xRRGGBB，供不依赖 UnityEngine 的 Core 层使用。</summary>
        private static uint ToRgb24(Color color)
        {
            uint r = (uint)Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            uint g = (uint)Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            uint b = (uint)Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            return (r << 16) | (g << 8) | b;
        }

        private void OnValidate()
        {
            width = Mathf.Max(8, width);
            height = Mathf.Max(8, height);
        }
    }
}
