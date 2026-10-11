using System;

namespace Omen.Core
{
    /// <summary>
    /// 动物对象池（兔与狼共用）。
    ///
    /// 内部结构（这是本类最需要理解的地方）：
    ///   <c>_slots</c>     固定槽位数组。句柄里的下标指的是它，<b>生物一生都不会换槽位</b>。
    ///   <c>_alive</c>     存活槽位列表，紧凑排列在 [0, AliveCount)。
    ///                     遍历用这个，避免在槽位数组上扫描空洞。
    ///   <c>_aliveIndexOf</c>  槽位 → 存活列表下标的反查表，让"从存活列表里摘掉一个"是 O(1)。
    ///   <c>_freeSlots</c> 空闲槽位栈，新生物优先复用，所以容量按"历史峰值"而不是"历史总数"增长。
    ///
    /// 为什么不能只用一个紧凑数组 + 交换删除：
    ///   交换删除会把末尾生物搬到被删位置，那个生物的"下标"就变了。
    ///   而外部持有的句柄记的是下标 —— 于是句柄会指向错误的生物。
    ///   最初的实现犯过这个错，被单测抓了出来。教训：句柄的稳定性优先于遍历的局部性。
    ///
    /// 迭代约定：<see cref="AliveCount"/> 在遍历过程中可能变化。
    /// 若遍历中杀死或新增了生物，请 <b>break 并重新开始遍历</b>（模拟层就是这么做的），
    /// 不要在遍历中继续推进下标 —— 那会漏掉或重复访问生物。
    ///
    /// 本类只管"生物集合"。移动、捕食、繁殖的规则全部在模拟层。
    /// </summary>
    public sealed class CreaturePool
    {
        private Creature[] _slots;
        private int[] _alive;
        private int[] _aliveIndexOf;
        private int[] _freeSlots;
        private int _freeCount;
        private int _generationCounter;

        public CreaturePool(int initialCapacity = 128)
        {
            if (initialCapacity < 1)
            {
                initialCapacity = 1;
            }

            _slots = new Creature[initialCapacity];
            _alive = new int[initialCapacity];
            _aliveIndexOf = new int[initialCapacity];
            _freeSlots = new int[initialCapacity];
        }

        /// <summary>活着的生物数量。存活槽位占据 [0, <see cref="AliveCount"/>) 区间。</summary>
        public int AliveCount { get; private set; }

        /// <summary>
        /// 池的版本号。任何一次生成或死亡都会 +1。
        /// View 层靠它判断"要不要重新同步"，于是静止时每帧开销为零 ——
        /// 只在真正发生过 tick 的那一帧才遍历。
        /// </summary>
        public int PoolVersion { get; private set; }

        /// <summary>某个槽位当前是否活着（不校验代际号）。供 View 层按槽位扫描使用。</summary>
        public bool IsSlotAlive(int slot)
        {
            return slot >= 0 && slot < _slots.Length && _slots[slot].Alive;
        }

        /// <summary>按槽位直接读取生物（不管死活）。供 View 层按槽位扫描使用。</summary>
        public Creature GetSlot(int slot)
        {
            return _slots[slot];
        }

        /// <summary>当前槽位数组容量（由历史峰值决定，不是活着的数量）。</summary>
        public int Capacity
        {
            get { return _slots.Length; }
        }

        /// <summary>空闲槽位数量。</summary>
        public int FreeSlotCount
        {
            get { return _freeCount; }
        }

        /// <summary>存活列表里第 i 个生物所在槽位的下标。仅用于遍历，不要长期缓存。</summary>
        public int GetAliveSlot(int aliveIndex)
        {
            return _alive[aliveIndex];
        }

        /// <summary>存活列表里第 i 个生物的句柄。</summary>
        public CreatureHandle GetAliveHandle(int aliveIndex)
        {
            int slot = _alive[aliveIndex];
            return new CreatureHandle { Index = slot, Generation = _slots[slot].Generation };
        }

        /// <summary>存活列表里第 i 个生物。仅用于遍历，不要长期缓存。</summary>
        public Creature GetAlive(int aliveIndex)
        {
            return _slots[_alive[aliveIndex]];
        }

        /// <summary>句柄是否指向一只仍然活着的生物。槽位被复用后，老句柄会在这里返回 false。</summary>
        public bool IsAlive(CreatureHandle handle)
        {
            if (handle.IsNone || handle.Index >= _slots.Length)
            {
                return false;
            }

            Creature slot = _slots[handle.Index];
            return slot.Alive && slot.Generation == handle.Generation;
        }

        /// <summary>按句柄读取生物。句柄失效时通过 <paramref name="creature"/> 返回默认值并返回 false。</summary>
        public bool TryGet(CreatureHandle handle, out Creature creature)
        {
            if (!IsAlive(handle))
            {
                creature = default(Creature);
                return false;
            }

            creature = _slots[handle.Index];
            return true;
        }

        /// <summary>新增一只生物，返回它的句柄。</summary>
        public CreatureHandle Spawn(SpeciesKind kind, GridPos position, float energy, float reproductionCooldown)
        {
            int slot;

            if (_freeCount > 0)
            {
                _freeCount--;
                slot = _freeSlots[_freeCount];
            }
            else
            {
                slot = AllocateFreshSlot();
            }

            // 代际号来自一个只增不减的计数器，所以同一个槽位复用多少次都不会给出重复的代际号，
            // 老句柄也不可能"复活"。
            _generationCounter++;
            _slots[slot] = new Creature
            {
                Generation = unchecked((uint)_generationCounter),
                Alive = true,
                Kind = kind,
                Position = position,
                Energy = energy,
                ReproductionCooldown = reproductionCooldown,
                Age = 0,
            };

            // 注意：Clear() 会把计数器归零。为此 Clear 之后槽位的 Alive 全为 false，
            // IsAlive 依然能正确拒绝老句柄；只是新生物的代际号会从头再来一遍，
            // 所以"清空后仍持有老句柄"的调用方必须自己保证不再使用它们（换岛重建就是这个场景）。

            _aliveIndexOf[slot] = AliveCount;
            _alive[AliveCount] = slot;
            AliveCount++;
            PoolVersion++;

            return new CreatureHandle { Index = slot, Generation = _slots[slot].Generation };
        }

        /// <summary>
        /// 杀死一只生物并释放槽位。重复调用同一个句柄是安全的（第二次返回 false）。
        /// </summary>
        public bool Kill(CreatureHandle handle)
        {
            if (!IsAlive(handle))
            {
                return false;
            }

            int slot = handle.Index;
            _slots[slot] = default(Creature);   // Alive=false，释放槽位
            RemoveFromAlive(slot);
            ReleaseSlot(slot);
            PoolVersion++;
            return true;
        }

        /// <summary>把生物移动到新格子。本方法<b>不</b>更新网格，网格由模拟层统一维护。</summary>
        public void SetPosition(CreatureHandle handle, GridPos position)
        {
            if (!IsAlive(handle))
            {
                return;
            }

            _slots[handle.Index].Position = position;
        }

        /// <summary>
        /// 写回生物的能量 / 繁殖冷却 / 年龄。
        /// 不要用它改 Alive 或 Generation，那会破坏池的一致性。
        /// </summary>
        public void SetState(CreatureHandle handle, float energy, float reproductionCooldown, int age)
        {
            if (!IsAlive(handle))
            {
                return;
            }

            int slot = handle.Index;
            _slots[slot].Energy = energy;
            _slots[slot].ReproductionCooldown = reproductionCooldown;
            _slots[slot].Age = age;
        }

        /// <summary>把一只生物的能量写回（最常用的单项更新）。</summary>
        public void SetEnergy(CreatureHandle handle, float energy)
        {
            if (!IsAlive(handle))
            {
                return;
            }

            _slots[handle.Index].Energy = energy;
        }

        /// <summary>把一只生物的繁殖冷却写回。</summary>
        public void SetReproductionCooldown(CreatureHandle handle, float cooldown)
        {
            if (!IsAlive(handle))
            {
                return;
            }

            _slots[handle.Index].ReproductionCooldown = cooldown;
        }

        /// <summary>把一只生物的年龄 +1。年龄只用于统计与调试。</summary>
        public void IncrementAge(CreatureHandle handle)
        {
            if (!IsAlive(handle))
            {
                return;
            }

            _slots[handle.Index].Age++;
        }

        /// <summary>清空池，恢复到刚构造的状态。重建岛屿时调用。</summary>
        public void Clear()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i] = default(Creature);
                _alive[i] = 0;
                _aliveIndexOf[i] = 0;
                _freeSlots[i] = 0;
            }

            AliveCount = 0;
            _freeCount = 0;
            _nextFreshSlot = 0;
            _generationCounter = 0;
            PoolVersion++;
        }

        /// <summary>统计某个物种当前有多少只。</summary>
        public int CountSpecies(SpeciesKind kind)
        {
            int total = 0;
            for (int i = 0; i < AliveCount; i++)
            {
                Creature c = _slots[_alive[i]];
                if (c.Alive && c.Kind == kind)
                {
                    total++;
                }
            }

            return total;
        }

        /// <summary>
        /// 一致性自检：存活列表、反查表、槽位标记三者必须互相吻合。
        /// 返回 null 表示一致；否则返回第一条不一致的描述。供单测与调试使用。
        /// </summary>
        public string Validate()
        {
            for (int i = 0; i < AliveCount; i++)
            {
                int slot = _alive[i];
                if (slot < 0 || slot >= _slots.Length)
                {
                    return "存活列表第 " + i + " 项槽位越界: " + slot;
                }

                if (!_slots[slot].Alive)
                {
                    return "存活列表第 " + i + " 项指向的槽位 " + slot + " 并未标记为存活";
                }

                if (_aliveIndexOf[slot] != i)
                {
                    return "槽位 " + slot + " 的反查下标为 " + _aliveIndexOf[slot] + "，应为 " + i;
                }
            }

            for (int slot = 0; slot < _slots.Length; slot++)
            {
                bool alive = _slots[slot].Alive;
                bool inAliveList = false;
                for (int i = 0; i < AliveCount; i++)
                {
                    if (_alive[i] == slot)
                    {
                        inAliveList = true;
                        break;
                    }
                }

                if (alive != inAliveList)
                {
                    return "槽位 " + slot + " 的存活标记(" + alive + ")与存活列表(" + inAliveList + ")不一致";
                }
            }

            return null;
        }

        /// <summary>把活着的生物拼成一段可读文本，用于调试与测试断言。</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("CreaturePool{alive=").Append(AliveCount).Append(", capacity=").Append(Capacity)
                .Append(", free=").Append(_freeCount).Append('}');

            for (int i = 0; i < AliveCount; i++)
            {
                sb.AppendLine().Append("  [").Append(i).Append("] slot=").Append(_alive[i]).Append(' ')
                    .Append(_slots[_alive[i]]);
            }

            return sb.ToString();
        }

        // ---------------------------------------------------------------- 内部

        private int _nextFreshSlot;

        private int AllocateFreshSlot()
        {
            if (_nextFreshSlot >= _slots.Length)
            {
                Grow();
            }

            int slot = _nextFreshSlot;
            _nextFreshSlot++;
            return slot;
        }

        /// <summary>
        /// 从存活列表摘掉一个槽位：把列表末尾搬到它的位置。
        ///
        /// 关键顺序：<b>先读末尾元素，再递减 AliveCount</b>。
        /// 若先递减，末尾元素就变成了另一个（甚至是被摘掉的自己），
        /// 结果是列表里混进死槽位、或活生物被凭空丢掉。
        /// </summary>
        private void RemoveFromAlive(int slot)
        {
            int index = _aliveIndexOf[slot];
            int last = AliveCount - 1;

            if (index != last)
            {
                int movedSlot = _alive[last];
                _alive[index] = movedSlot;
                _aliveIndexOf[movedSlot] = index;
            }

            _alive[last] = 0;
            AliveCount--;
        }

        private void ReleaseSlot(int slot)
        {
            if (_freeCount >= _freeSlots.Length)
            {
                Array.Resize(ref _freeSlots, _freeSlots.Length * 2);
            }

            _freeSlots[_freeCount] = slot;
            _freeCount++;
        }

        private void Grow()
        {
            int newCapacity = _slots.Length * 2;
            Array.Resize(ref _slots, newCapacity);
            Array.Resize(ref _alive, newCapacity);
            Array.Resize(ref _aliveIndexOf, newCapacity);
            Array.Resize(ref _freeSlots, newCapacity);
        }
    }
}
