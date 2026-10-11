using System.Collections.Generic;

namespace Omen.Core
{
    /// <summary>
    /// 网格底层工具：索引换算、边界判定、四邻域邻接、连通块统计。
    /// 纯 C# 静态类，不引用 UnityEngine，也不持有任何状态。
    /// 所有内部缓冲区都由调用方传入（见 <see cref="NeighborBuffer"/>），保证零 GC。
    /// </summary>
    public static class GridUtility
    {
        /// <summary>四邻域方向数组，配合 <see cref="FillNeighbors"/> 使用，长度 4。</summary>
        public static readonly int[] Directions4X = { 0, 0, -1, 1 };
        public static readonly int[] Directions4Y = { -1, 1, 0, 0 };

        /// <summary>行优先索引换算：index = y * width + x。</summary>
        public static int ToIndex(int x, int y, int width)
        {
            return y * width + x;
        }

        /// <summary>判断坐标是否落在网格内。</summary>
        public static bool InBounds(int x, int y, int width, int height)
        {
            return x >= 0 && x < width && y >= 0 && y < height;
        }

        /// <summary>
        /// 取四邻域。越界的邻居被丢弃，所以结果数量在 2（角落）~ 4（内部）之间。
        /// 返回实际写入的数量，写入到调用方提供的缓冲区里，不产生任何分配。
        /// </summary>
        public static int FillNeighbors(int x, int y, int width, int height, NeighborBuffer buffer)
        {
            int count = 0;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + Directions4X[d];
                int ny = y + Directions4Y[d];
                if (!InBounds(nx, ny, width, height))
                {
                    continue;
                }

                buffer.X[count] = nx;
                buffer.Y[count] = ny;
                count++;
            }

            return count;
        }

        /// <summary>
        /// 四邻域洪水填充：从 <paramref name="startIndex"/> 出发，把 <paramref name="component"/>
        /// 中布尔值为 true 且连通的格子数出来。用于连通性判定与孤岛裁剪。
        /// 调用方需保证 <paramref name="component"/> 长度 = width * height。
        /// </summary>
        public static int FloodFillCount(bool[] component, int width, int height, int startIndex,
            Queue<int> queue, bool[] visited)
        {
            if (startIndex < 0 || startIndex >= component.Length || !component[startIndex])
            {
                return 0;
            }

            int count = 0;
            queue.Clear();
            queue.Enqueue(startIndex);
            visited[startIndex] = true;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                count++;

                int cx = current % width;
                int cy = current / width;

                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + Directions4X[d];
                    int ny = cy + Directions4Y[d];
                    if (!InBounds(nx, ny, width, height))
                    {
                        continue;
                    }

                    int nIndex = ny * width + nx;
                    if (visited[nIndex] || !component[nIndex])
                    {
                        continue;
                    }

                    visited[nIndex] = true;
                    queue.Enqueue(nIndex);
                }
            }

            return count;
        }

        /// <summary>收集从 <paramref name="startIndex"/> 出发能到达的所有连通格子（含起点）。</summary>
        public static void CollectComponent(bool[] component, int width, int height, int startIndex,
            Queue<int> queue, bool[] visited, List<GridPos> result)
        {
            result.Clear();
            if (startIndex < 0 || startIndex >= component.Length || !component[startIndex])
            {
                return;
            }

            queue.Clear();
            queue.Enqueue(startIndex);
            visited[startIndex] = true;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                result.Add(new GridPos(current % width, current / width));

                int cx = current % width;
                int cy = current / width;

                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + Directions4X[d];
                    int ny = cy + Directions4Y[d];
                    if (!InBounds(nx, ny, width, height))
                    {
                        continue;
                    }

                    int nIndex = ny * width + nx;
                    if (visited[nIndex] || !component[nIndex])
                    {
                        continue;
                    }

                    visited[nIndex] = true;
                    queue.Enqueue(nIndex);
                }
            }
        }

        /// <summary>
        /// 在 <paramref name="component"/> 中标出最大的连通块（就地修改 <paramref name="keepMask"/>）。
        /// <paramref name="keepMask"/> 需与 <paramref name="component"/> 等长，函数不负责清空它，调用方自行处理。
        /// 返回最大连通块的大小；没有任何连通块时返回 0。
        /// </summary>
        public static int MarkLargestComponent(bool[] component, int width, int height,
            Queue<int> queue, bool[] visited, bool[] keepMask)
        {
            for (int i = 0; i < visited.Length; i++)
            {
                visited[i] = false;
            }

            int bestSize = 0;
            int bestStart = -1;

            for (int i = 0; i < component.Length; i++)
            {
                if (!component[i] || visited[i])
                {
                    continue;
                }

                int size = FloodFillCount(component, width, height, i, queue, visited);
                if (size > bestSize)
                {
                    bestSize = size;
                    bestStart = i;
                }
            }

            if (bestStart < 0)
            {
                return 0;
            }

            // 复用 visited 作为"最大块"标记，再拷回 keepMask。
            for (int i = 0; i < visited.Length; i++)
            {
                visited[i] = false;
            }

            FloodFillCount(component, width, height, bestStart, queue, visited);
            for (int i = 0; i < keepMask.Length; i++)
            {
                keepMask[i] = visited[i];
            }

            return bestSize;
        }
    }

    /// <summary>
    /// 四邻域结果的可复用缓冲区，避免每 tick 分配数组导致 GC。
    /// 用一个 struct 包住两个定长数组；不要在不同线程间共用同一个实例。
    /// </summary>
    public sealed class NeighborBuffer
    {
        public readonly int[] X = new int[4];
        public readonly int[] Y = new int[4];

        public int Count;
    }
}
