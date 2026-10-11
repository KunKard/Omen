using System;

namespace Omen.Core
{
    /// <summary>
    /// 岛屿生成参数 —— 纯 C# 数据载体，不引用 UnityEngine。
    ///
    /// 为什么不让生成器直接吃 ScriptableObject：<see cref="IslandGenerator"/> 属于 Core 层，
    /// 必须保持"零 Unity 依赖"才能被单元测试和存档系统复用。Unity 侧的
    /// <c>Omen.Config.IslandConfig</c>（ScriptableObject）负责在 Inspector 里配置，
    /// 运行时再转成本类喂给生成器。
    /// </summary>
    [Serializable]
    public class IslandConfigData
    {
        // ---------------------------------------------------------------- 网格
        /// <summary>网格宽度。</summary>
        public int Width = 16;

        /// <summary>网格高度。</summary>
        public int Height = 16;

        // ---------------------------------------------------------------- 大陆形状
        /// <summary>目标陆地占比（0~1）。算法会二分搜索阈值逼近它；实际值会略低（孤岛被裁掉）。</summary>
        public double LandRatio = 0.45;

        /// <summary>
        /// 半岛 / 海湾的数量。这是决定岛屿"长相"最重要的参数：
        /// 3 = 三叶形，4 = 经典小岛（推荐），8 = 高度破碎。
        /// </summary>
        public int ShapeLobes = 4;

        /// <summary>
        /// 各方向半径的随机落差（0~0.85）。越大，伸出的半岛越长、海湾越深。
        /// 0.55 时最长的半岛与最短的方向相差半个岛半径 —— 长度差约 2.8 格，肉眼非常明显。
        /// </summary>
        public double ShapeRadiusJitter = 0.55;

        /// <summary>
        /// 海岸线起伏幅度（0~0.5）。用乘性遮罩实现，所以实际偏移量随岛半径等比缩放：
        /// 0.4 在 16x16 网格上约等于半岛与海湾相差 2.4 格。
        /// 上限 0.5 是刻意留的安全边界：遮罩下限 0.5 能保证等值线始终是凸的，陆地不会被撕成两块。
        /// </summary>
        public double ShapeInfluence = 0.40;

        // ---------------------------------------------------------------- 海岸线细节
        /// <summary>海岸线噪声的影响权重（0~0.4）。给轮廓叠加小尺度锯齿，避免看起来像几何图形。</summary>
        public double CoastNoiseInfluence = 0.18;

        /// <summary>噪声频率倍率。0.25 对应约 4 格一个起伏；越小地形特征越大块。</summary>
        public double CoastNoiseScale = 0.25;

        /// <summary>噪声倍频程数。1 = 只有大起伏；3 = 带细节的海岸线（推荐）。</summary>
        public int NoiseOctaves = 3;

        /// <summary>每层倍频程的频率放大倍数。</summary>
        public double NoiseLacunarity = 2.0;

        /// <summary>每层倍频程的振幅衰减。越小则高频细节越弱、海岸线越平滑。</summary>
        public double NoiseGain = 0.45;

        // ---------------------------------------------------------------- 配色
        // 放在 Core 里而不是 View 里，是为了让"颜色"成为可存档的数据而不是硬编码常量：
        // 存档回放时能还原玩家当时看到的配色，Phase 7 换美术时也只改一处。
        // 用 uint 的 0xRRGGBB 表示，避免 Core 层依赖 UnityEngine.Color。

        /// <summary>海水颜色，0xRRGGBB。</summary>
        public uint WaterColor = 0x2E5C8A;

        /// <summary>空陆地颜色。</summary>
        public uint LandColor = 0x6B8E4E;

        /// <summary>草地颜色。</summary>
        public uint GrassColor = 0x8FBF4A;

        /// <summary>兔的颜色。</summary>
        public uint RabbitColor = 0xF2E8D5;

        /// <summary>狼的颜色。</summary>
        public uint WolfColor = 0x8C2F39;

        // ---------------------------------------------------------------- 显示
        /// <summary>每单位像素数（像素风基准）。</summary>
        public int PixelsPerUnit = 16;

        // ---------------------------------------------------------------- 动画时长（纯表现，不影响模拟）
        /// <summary>草从裸地长出来时的颜色渐变时长（真实秒）。</summary>
        public float GrassGrowSeconds = 0.45f;

        /// <summary>动物进食时的高亮闪烁时长（真实秒）。</summary>
        public float EatFlashSeconds = 0.35f;

        /// <summary>进食高亮的亮度倍数。1 = 不亮，1.6 = 明显发亮。</summary>
        public float EatFlashBrightness = 1.7f;

        /// <summary>
        /// 生物移动动画的时长 = 一个 tick 的秒数 × 这个系数。
        ///
        /// 为什么默认大于 1：模拟是"每 tick 瞬移一格"，而 tick = 1 秒。
        /// 若动画正好占满 1 秒，两次移动之间没有重叠，看起来就是"停 1 秒、走一格"。
        /// 让动画略长于一个 tick，上一次的滑动还没结束下一次就开始了，
        /// 画面就变成连续滑行 —— 即使某 tick 被挡住（实测约 12%），观感也不会断。
        ///
        /// 代价：生物位置在画面上会比模拟滞后最多 (系数-1) 个 tick。
        /// 对"观察生态"这个玩法完全可接受；若以后要精确点击生物，把它的 sortOrder 提上去即可。
        /// </summary>
        public float MoveAnimationScale = 1.6f;

        /// <summary>
        /// 相机放大倍数（<b>整数</b>）。屏幕上 1 格 = PixelsPerUnit × CameraZoom 个像素。
        /// 必须整数：非整数倍时相邻像素会占 1 个或 2 个屏幕像素，出现间隔条纹。
        /// 与 PixelsPerUnit 分开是有意的 —— 改画面缩放不应该污染美术采图标准。
        /// </summary>
        public int CameraZoom = 2;

        /// <summary>复制一份，避免调用方后续修改影响到已经传给生成器的数据。</summary>
        public IslandConfigData Clone()
        {
            return new IslandConfigData
            {
                Width = Width,
                Height = Height,
                LandRatio = LandRatio,
                ShapeLobes = ShapeLobes,
                ShapeRadiusJitter = ShapeRadiusJitter,
                ShapeInfluence = ShapeInfluence,
                CoastNoiseInfluence = CoastNoiseInfluence,
                CoastNoiseScale = CoastNoiseScale,
                NoiseOctaves = NoiseOctaves,
                NoiseLacunarity = NoiseLacunarity,
                NoiseGain = NoiseGain,
                WaterColor = WaterColor,
                LandColor = LandColor,
                GrassColor = GrassColor,
                RabbitColor = RabbitColor,
                WolfColor = WolfColor,
                PixelsPerUnit = PixelsPerUnit,
                CameraZoom = CameraZoom,
                GrassGrowSeconds = GrassGrowSeconds,
                EatFlashSeconds = EatFlashSeconds,
                EatFlashBrightness = EatFlashBrightness,
                MoveAnimationScale = MoveAnimationScale,
            };
        }

        /// <summary>把参数夹到合理范围，防止 Inspector 里手滑填出无法生成的值。</summary>
        public void Sanitize()
        {
            Width = Clamp(Width, 8, 128);
            Height = Clamp(Height, 8, 128);
            LandRatio = Clamp(LandRatio, 0.05, 0.90);
            ShapeLobes = Clamp(ShapeLobes, 2, 12);
            ShapeRadiusJitter = Clamp(ShapeRadiusJitter, 0.0, 0.85);
            ShapeInfluence = Clamp(ShapeInfluence, 0.0, 0.60);
            CoastNoiseInfluence = Clamp(CoastNoiseInfluence, 0.0, 0.40);
            CoastNoiseScale = Clamp(CoastNoiseScale, 0.02, 1.00);
            NoiseOctaves = Clamp(NoiseOctaves, 1, 6);
            NoiseLacunarity = Clamp(NoiseLacunarity, 1.1, 4.0);
            NoiseGain = Clamp(NoiseGain, 0.05, 0.95);
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        private static double Clamp(double value, double min, double max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
