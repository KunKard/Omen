using System;

namespace Omen.Core
{
    /// <summary>
    /// 纯 C# 值噪声（Value Noise）。用整数哈希做随机源，不依赖 UnityEngine，
    /// 因此跨平台、跨 Unity 版本都完全确定 —— 同一种子永远得到同一张图。
    /// </summary>
    public sealed class ValueNoise
    {
        private readonly int _seedOffset0;
        private readonly int _seedOffset1;

        /// <summary>频率倍率：每一层倍频程的频率放大倍数（经典取值 2.0）。</summary>
        public double Lacunarity = 2.0;

        /// <summary>振幅衰减：每一层倍频程的振幅缩小倍数，越小白噪声越少。</summary>
        public double Gain = 0.45;

        public ValueNoise(int seed)
        {
            // 用 System.Random 展开种子，比手工位混淆更不容易撞车。
            var rng = new Random(seed);
            _seedOffset0 = rng.Next(int.MinValue, int.MaxValue);
            _seedOffset1 = rng.Next(int.MinValue, int.MaxValue);
        }

        /// <summary>单层值噪声，输出范围 [0, 1]。</summary>
        public double Sample(double x, double y)
        {
            int x0 = FloorToInt(x);
            int y0 = FloorToInt(y);
            int x1 = x0 + 1;
            int y1 = y0 + 1;

            double tx = SmoothStep(x - x0);
            double ty = SmoothStep(y - y0);

            double v00 = HashToUnit(x0, y0);
            double v10 = HashToUnit(x1, y0);
            double v01 = HashToUnit(x0, y1);
            double v11 = HashToUnit(x1, y1);

            double a = v00 + (v10 - v00) * tx;
            double b = v01 + (v11 - v01) * tx;
            return a + (b - a) * ty;
        }

        /// <summary>
        /// 分形叠加（fBm），输出范围约 [0, 1]（已按总振幅归一化）。
        /// </summary>
        public double SampleFractal(double x, double y, int octaves)
        {
            if (octaves < 1)
            {
                octaves = 1;
            }

            double amplitude = 1.0;
            double frequency = 1.0;
            double sum = 0.0;
            double amplitudeSum = 0.0;

            for (int i = 0; i < octaves; i++)
            {
                sum += Sample(x * frequency, y * frequency) * amplitude;
                amplitudeSum += amplitude;
                frequency *= Lacunarity;
                amplitude *= Gain;
            }

            return amplitudeSum > 0.0 ? sum / amplitudeSum : 0.0;
        }

        /// <summary>平滑插值曲线 6t^5 - 15t^4 + 10t^3，保证一阶导在格点处连续（海岸线不会出现方格棱角）。</summary>
        private static double SmoothStep(double t)
        {
            return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
        }

        private static int FloorToInt(double v)
        {
            int i = (int)v;
            return v < i ? i - 1 : i;
        }

        /// <summary>整数哈希 → [0, 1)。全程整数运算，无 GC、无平台差异。</summary>
        private double HashToUnit(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393) + (uint)(y * 668265263) + (uint)_seedOffset0;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                h += (uint)_seedOffset1;
                h = (h ^ (h >> 15)) * 2246822519u;
                h ^= h >> 13;
                return h / 4294967296.0;
            }
        }
    }
}
