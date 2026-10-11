using System;

namespace Omen.Core
{
    /// <summary>
    /// 岛屿生成器：把"随机种子 + 参数"变成一张确定性的岛屿网格。
    ///
    /// 算法（5 步）：
    ///   1) 径向形状函数 —— 在若干方向扇区上随机取半径，再用循环 Catmull-Rom 插值平滑。
    ///      这是产生"半岛 / 海湾"的关键：方向 A 半径大就是伸出去的半岛，方向 B 半径小就是凹进去的海湾。
    ///   2) 径向距离场 —— 基础团块，保证陆地天然是连通的（不会碎成一堆孤岛）。
    ///   3) 值噪声扰动 —— 在轮廓上叠加小尺度锯齿，海岸线不会像几何图形。
    ///   4) 阈值二分 —— 自动逼近目标陆地占比，不需要手工调参。
    ///   5) 连通性裁剪 —— 只保留最大的陆地块，兜底清理噪声产生的零碎孤岛。
    ///
    /// 纯 C#，不引用 UnityEngine。同参数 ⇒ 同结果，可安全用于复现 bug 与回放。
    /// </summary>
    public sealed class IslandGenerator
    {
        private const int ShapeSampleCount = 24;

        private readonly double[] _score;
        private readonly double[] _distance;
        private readonly double[] _shape;
        private readonly double[] _noise;
        private readonly bool[] _isLand;
        private readonly bool[] _visited;
        private readonly bool[] _keepMask;
        private readonly double[] _shapeRadius;
        private readonly System.Collections.Generic.List<GridPos> _component = new System.Collections.Generic.List<GridPos>(512);
        private readonly System.Collections.Generic.Queue<int> _queue = new System.Collections.Generic.Queue<int>(512);

        public IslandGenerator(int width, int height)
        {
            if (width < 3 || height < 3)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "网格至少 3x3 才能生成岛屿（需要一圈海水边界）。");
            }

            Width = width;
            Height = height;

            int cellCount = width * height;
            _score = new double[cellCount];
            _distance = new double[cellCount];
            _shape = new double[cellCount];
            _noise = new double[cellCount];
            _isLand = new bool[cellCount];
            _visited = new bool[cellCount];
            _keepMask = new bool[cellCount];
            _shapeRadius = new double[ShapeSampleCount];
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>上一次生成实际达到的陆地占比（裁剪连通块之后的真实值）。</summary>
        public double LastLandRatio { get; private set; }

        /// <summary>上一次生成中，被当作孤岛裁掉的陆地格数。调试用。</summary>
        public int LastTrimmedCellCount { get; private set; }

        /// <summary>上一次生成使用的阈值。调试用。</summary>
        public double LastThreshold { get; private set; }

        /// <summary>
        /// 生成岛屿并写入 <paramref name="grid"/>。
        /// </summary>
        /// <param name="grid">目标网格，尺寸必须与生成器一致（会被完全重建）。</param>
        /// <param name="seed">随机种子，同种子同结果。</param>
        /// <param name="config">生成参数。</param>
        /// <param name="scoreOut">可选：写入每格的"陆地倾向分"（越小越像陆地），供调试可视化。</param>
        public void Generate(IslandGrid grid, int seed, IslandConfigData config, double[] scoreOut = null)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            if (grid.Width != Width || grid.Height != Height)
            {
                throw new ArgumentException(
                    "网格尺寸 " + grid.Width + "x" + grid.Height + " 与生成器 " + Width + "x" + Height + " 不一致。",
                    nameof(grid));
            }

            BuildScoreField(seed, config);

            if (scoreOut != null)
            {
                Array.Copy(_score, scoreOut, Math.Min(scoreOut.Length, _score.Length));
            }

            LastThreshold = SolveThreshold(config.LandRatio);
            MarkLandByThreshold(LastThreshold);
            EnforceWaterBorder();

            int before = CountTrue(_isLand);
            KeepLargestLandmass();
            int after = CountTrue(_isLand);

            var positions = new System.Collections.Generic.List<GridPos>(after);
            for (int i = 0; i < _isLand.Length; i++)
            {
                if (_isLand[i])
                {
                    positions.Add(GridPos.FromIndex(i, Width));
                }
            }

            grid.RebuildFromPositions(positions);
            LastLandRatio = grid.CellCount > 0 ? (double)grid.LandCount / grid.CellCount : 0.0;
        }

        // ---------------------------------------------------------------- 第 1~3 步：打分场

        private void BuildScoreField(int seed, IslandConfigData config)
        {
            int cellCount = Width * Height;
            var rng = new Random(seed);

            double centerX = (Width - 1) * 0.5;
            double centerY = (Height - 1) * 0.5;

            // 半径基准取"中心到角落"的距离，于是角落的 d 恰好等于 1，整张图都落在单位圆内。
            double radiusBase = Math.Sqrt(centerX * centerX + centerY * centerY);
            if (radiusBase <= 0.0)
            {
                radiusBase = 1.0;
            }

            BuildShapeRadius(rng, config);
            var noise = new ValueNoise(seed)
            {
                Lacunarity = config.NoiseLacunarity,
                Gain = config.NoiseGain,
            };

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    double dx = x - centerX;
                    double dy = y - centerY;
                    double distance = Math.Sqrt(dx * dx + dy * dy) / radiusBase;

                    // atan2 返回值域 (-π, π]，加 π 后映射到 [0, 2π) 再归一化到 [0,1) 作为扇区参数。
                    double angle = Math.Atan2(dy, dx) + Math.PI;
                    double t = angle / (Math.PI * 2.0);
                    double shape = SampleShape(t);

                    double n = noise.SampleFractal(x * config.CoastNoiseScale, y * config.CoastNoiseScale, config.NoiseOctaves);

                    int index = y * Width + x;
                    _distance[index] = distance;
                    _shape[index] = shape;
                    _noise[index] = n;
                }
            }

            Normalize(_shape, cellCount);
            Normalize(_noise, cellCount);

            // 形状函数用"乘性遮罩"而不是加法项 —— 这是关键设计。
            //
            // 若写成 d - shape * influence，形状场的梯度（约 0.3）远小于距离场的梯度（约 1.0），
            // 轮廓几乎还是正圆，只是整体平移了一点点，看不出半岛。
            //
            // 改写成 d / mask 后：
            //   半径大的方向 mask → 1，边界停在最外圈；
            //   半径小的方向 mask → 1 - influence，等值线被同比例拉向圆心。
            // 于是"边界偏移量"在圆周上的最大落差约等于 influence × 岛半径（16x16 时约 2.4 格），
            // 半岛与海湾肉眼清晰可辨，而且偏移量随岛屿尺寸等比缩放，换网格大小也不用重调参数。
            //
            // mask 下限 0.5 保证除出来的等值线始终是凸的 → 陆地结构上一定连通。
            for (int i = 0; i < cellCount; i++)
            {
                double mask = 1.0 - _shape[i] * config.ShapeInfluence;
                if (mask < 0.5)
                {
                    mask = 0.5;
                }

                _score[i] = _distance[i] / mask - _noise[i] * config.CoastNoiseInfluence;
            }
        }

        /// <summary>
        /// 生成径向形状遮罩：在圆周上均匀取 <see cref="ShapeSampleCount"/> 个方向作为采样点，
        /// 半径由 <paramref name="config"/> 的 <c>ShapeLobes</c> 个主方向半径经 smoothstep 过渡得到。
        /// 半径范围恒为 [1 - ShapeRadiusJitter, 1]，所以曲线始终内切于单位圆。
        /// </summary>
        private void BuildShapeRadius(Random rng, IslandConfigData config)
        {
            int lobes = config.ShapeLobes;
            if (lobes < 2)
            {
                lobes = 2;
            }

            if (lobes > ShapeSampleCount / 2)
            {
                lobes = ShapeSampleCount / 2;
            }

            double jitter = config.ShapeRadiusJitter;
            if (jitter < 0.0)
            {
                jitter = 0.0;
            }

            if (jitter > 0.85)
            {
                jitter = 0.85;
            }

            // 先定出 lobes 个"主方向"的半径，再在它们之间平滑过渡，
            // 这样得到的是明确可数的半岛数量，而不是一团随机噪声。
            double[] lobeRadius = new double[lobes];
            for (int i = 0; i < lobes; i++)
            {
                lobeRadius[i] = 1.0 - rng.NextDouble() * jitter;
            }

            for (int i = 0; i < ShapeSampleCount; i++)
            {
                double u = (double)i / ShapeSampleCount * lobes;
                int i0 = (int)u;
                double f = u - i0;

                double r0 = lobeRadius[WrapIndex(i0, lobes)];
                double r1 = lobeRadius[WrapIndex(i0 + 1, lobes)];

                // 用 smoothstep 在相邻主方向之间过渡，避免出现折线感的尖角。
                double s = f * f * (3.0 - 2.0 * f);
                _shapeRadius[i] = r0 + (r1 - r0) * s;
            }
        }

        /// <summary>按参数 t ∈ [0,1) 对形状函数做循环线性插值（采样点足够密，线性插值已足够平滑）。</summary>
        private double SampleShape(double t)
        {
            double u = t * ShapeSampleCount;
            int i0 = (int)u;
            if (i0 < 0)
            {
                i0 = 0;
            }

            if (i0 >= ShapeSampleCount)
            {
                i0 = ShapeSampleCount - 1;
            }

            double f = u - i0;
            double r0 = _shapeRadius[i0];
            double r1 = _shapeRadius[(i0 + 1) % ShapeSampleCount];
            return r0 + (r1 - r0) * f;
        }

        private static int WrapIndex(int index, int count)
        {
            int wrapped = index % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }

        /// <summary>把数组极值归一化到 [0,1]，让强度参数的含义与种子无关。</summary>
        private static void Normalize(double[] values, int count)
        {
            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                if (values[i] < min)
                {
                    min = values[i];
                }

                if (values[i] > max)
                {
                    max = values[i];
                }
            }

            double range = max - min;
            if (range <= 1e-9)
            {
                for (int i = 0; i < count; i++)
                {
                    values[i] = 0.5;
                }

                return;
            }

            double scale = 1.0 / range;
            for (int i = 0; i < count; i++)
            {
                values[i] = (values[i] - min) * scale;
            }
        }

        // ---------------------------------------------------------------- 第 4 步：阈值二分

        /// <summary>
        /// 二分搜索阈值，使"低于阈值的格子数 / 总格数"尽量接近目标陆地占比。
        /// 因为最终会被强制海边界与连通性裁剪，实际占比会略低于目标值，这是预期行为。
        /// </summary>
        private double SolveThreshold(double targetRatio)
        {
            if (targetRatio <= 0.0)
            {
                return double.NegativeInfinity;
            }

            if (targetRatio >= 1.0)
            {
                return double.PositiveInfinity;
            }

            double lo = double.PositiveInfinity;
            double hi = double.NegativeInfinity;
            for (int i = 0; i < _score.Length; i++)
            {
                double s = _score[i];
                if (s < lo)
                {
                    lo = s;
                }

                if (s > hi)
                {
                    hi = s;
                }
            }

            if (lo >= hi)
            {
                return lo;
            }

            int targetCount = (int)Math.Round(targetRatio * _score.Length);
            double low = lo;
            double high = hi;

            // 40 次二分足以把区间压到浮点精度极限，成本可忽略（生成只在开局 / 换种子时发生）。
            for (int iteration = 0; iteration < 40; iteration++)
            {
                double mid = (low + high) * 0.5;
                int count = CountBelow(mid);
                if (count < targetCount)
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            return (low + high) * 0.5;
        }

        private int CountBelow(double threshold)
        {
            int count = 0;
            for (int i = 0; i < _score.Length; i++)
            {
                if (_score[i] < threshold)
                {
                    count++;
                }
            }

            return count;
        }

        private void MarkLandByThreshold(double threshold)
        {
            for (int i = 0; i < _score.Length; i++)
            {
                _isLand[i] = _score[i] < threshold;
            }
        }

        // ---------------------------------------------------------------- 第 5 步：边界与连通性

        private void EnforceWaterBorder()
        {
            for (int x = 0; x < Width; x++)
            {
                _isLand[x] = false;                              // 下边界
                _isLand[(Height - 1) * Width + x] = false;        // 上边界
            }

            for (int y = 0; y < Height; y++)
            {
                _isLand[y * Width] = false;                       // 左边界
                _isLand[y * Width + (Width - 1)] = false;         // 右边界
            }
        }

        /// <summary>
        /// 只保留最大的陆地块。噪声会在近岸产生零碎的孤立小岛，
        /// 而那些格子面积太小，支撑不了任何生态，反而会让"陆地覆盖率"统计产生误导。
        /// </summary>
        private void KeepLargestLandmass()
        {
            int largest = GridUtility.MarkLargestComponent(_isLand, Width, Height, _queue, _visited, _keepMask);
            if (largest <= 0)
            {
                return;
            }

            for (int i = 0; i < _isLand.Length; i++)
            {
                if (_isLand[i] && !_keepMask[i])
                {
                    _isLand[i] = false;
                }
            }
        }

        private static int CountTrue(bool[] flags)
        {
            int count = 0;
            for (int i = 0; i < flags.Length; i++)
            {
                if (flags[i])
                {
                    count++;
                }
            }

            return count;
        }
    }
}
