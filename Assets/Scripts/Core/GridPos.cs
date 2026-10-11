using System;

namespace Omen.Core
{
    /// <summary>
    /// 网格坐标。纯 C# 值类型，不引用 UnityEngine，可安全用于存档 / 回放 / 单元测试。
    /// 坐标约定：(0,0) 在左下角，X 向右，Y 向上。
    /// </summary>
    [Serializable]
    public struct GridPos : IEquatable<GridPos>
    {
        public int X;
        public int Y;

        public GridPos(int x, int y)
        {
            X = x;
            Y = y;
        }

        /// <summary>转换为长度 = width * height 的一维数组索引。约定：行优先 index = Y * width + X。</summary>
        public int ToIndex(int width)
        {
            return Y * width + X;
        }

        /// <summary>从一维数组索引还原坐标。</summary>
        public static GridPos FromIndex(int index, int width)
        {
            return new GridPos(index % width, index / width);
        }

        public bool Equals(GridPos other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is GridPos other && Equals(other);
        }

        /// <summary>无 GC 的哈希（不装箱）。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Y;
            }
        }

        public static bool operator ==(GridPos a, GridPos b)
        {
            return a.X == b.X && a.Y == b.Y;
        }

        public static bool operator !=(GridPos a, GridPos b)
        {
            return a.X != b.X || a.Y != b.Y;
        }

        public override string ToString()
        {
            return "(" + X + "," + Y + ")";
        }
    }
}
