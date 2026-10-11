using System;

namespace Omen.Core
{
    /// <summary>
    /// 确定性伪随机数发生器（xorshift32）。
    ///
    /// 为什么不用 <c>System.Random</c>：它的实现在不同 .NET / Mono 版本之间不保证一致，
    /// 而这个游戏的"同种子复现"是核心需求（复现 bug、回放、日志对照）。
    /// 自实现的整数算法跨平台、跨版本结果完全一致。
    ///
    /// 用法约定：<b>每个子系统持有自己的实例</b>（生成岛屿一个、生态模拟一个）。
    /// 共用实例会让"改一处调用顺序"意外改变另一处的随机序列，制造难查的连带 bug。
    /// </summary>
    public sealed class DeterministicRandom
    {
        private uint _state;

        /// <summary>用一个整数种子构造。种子为 0 时会被替换成一个非零常量（xorshift 不能从 0 出发）。</summary>
        public DeterministicRandom(int seed)
        {
            _state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);
        }

        /// <summary>当前内部状态。用于存档 / 快照，配合 <see cref="DeterministicRandom(int)"/> 还原序列。</summary>
        public uint State
        {
            get { return _state; }
        }

        /// <summary>推进一次并返回原始 32 位随机值。</summary>
        public uint NextUInt()
        {
            // xorshift32：周期 2^32-1，分布足够均匀，且完全没有平台差异。
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>返回 [0, maxExclusive) 的整数。maxExclusive &lt;= 0 时返回 0。</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                return 0;
            }

            // 用取模会有极轻微的偏置，但对"随机挑一个邻居"这种用途完全够用，
            // 而且比拒绝采样的实现简单得多、永远不会死循环。
            return (int)(NextUInt() % (uint)maxExclusive);
        }

        /// <summary>返回 [minInclusive, maxExclusive) 的整数。</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                return minInclusive;
            }

            return minInclusive + NextInt(maxExclusive - minInclusive);
        }

        /// <summary>返回 [0, 1) 的浮点数。</summary>
        public float NextFloat()
        {
            // 取高 24 位：float 的尾数只有 24 位，用低位会产生可察觉的规律。
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        /// <summary>以给定概率返回 true。probability &lt;= 0 恒为 false，&gt;= 1 恒为 true。</summary>
        public bool Chance(float probability)
        {
            if (probability <= 0.0f)
            {
                return false;
            }

            if (probability >= 1.0f)
            {
                return true;
            }

            return NextFloat() < probability;
        }

        /// <summary>
        /// 原地洗牌（Fisher-Yates）。用于"随机挑一个邻居"这类需求：
        /// 洗牌后按顺序尝试，比"随机挑 + 失败重试"更容易保证确定性与有界耗时。
        /// </summary>
        public void Shuffle(int[] items, int count)
        {
            if (items == null || count <= 1)
            {
                return;
            }

            if (count > items.Length)
            {
                count = items.Length;
            }

            for (int i = count - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                int tmp = items[i];
                items[i] = items[j];
                items[j] = tmp;
            }
        }

        /// <summary>复制一份当前状态的随机源，用于"预演分支"（例如试算某个预兆的效果）。</summary>
        public DeterministicRandom Clone()
        {
            return new DeterministicRandom(0) { _state = _state };
        }
    }
}
