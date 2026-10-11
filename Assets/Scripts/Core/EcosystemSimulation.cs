using System;
using System.Collections.Generic;

namespace Omen.Core
{
    /// <summary>
    /// 生态模拟：草、兔、狼的全部行为逻辑。纯 C#，不引用 UnityEngine，
    /// 因此可以脱离 Unity 跑成千上万局做平衡分析。
    ///
    /// 架构铁律 1：本类通过 <see cref="IslandGrid"/> 的方法改状态，绝不绕过它。
    /// 节奏由 <c>GameClock</c> 驱动，本类只暴露"推进一个 tick"这一个入口。
    ///
    /// 一个 tick 的处理顺序（顺序本身就是规则，会显著影响结果）：
    ///   1. 动物消耗能量、结算饿死
    ///   2. 草扩散（先扫全场收集新增，扫完统一写入，避免新草在同 tick 继续扩散）
    ///   3. 动物行动：进食 → 逃跑/追猎/找草 → 移动 → 繁殖
    ///
    /// 关于遍历：死亡与出生都会改动存活列表，所以<b>先快照句柄，再逐个处理</b>，
    /// 不做"边遍历边改列表"。每个句柄使用前都用 <see cref="CreaturePool.IsAlive"/> 校验，
    /// 因此刚被别的生物吃掉的个体不会让后续逻辑读到脏数据。
    /// </summary>
    public sealed class EcosystemSimulation
    {
        private readonly IslandGrid _grid;
        private readonly CreaturePool _pool;
        private readonly DeterministicRandom _rng;
        private readonly SpeciesConfigData _rabbitConfig;
        private readonly SpeciesConfigData _wolfConfig;
        private readonly SpeciesConfigData _grassConfig;

        // 复用缓冲区：避免每 tick 分配数组造成 GC 抖动。
        private readonly List<CreatureHandle> _animalBuffer = new List<CreatureHandle>(512);
        private readonly List<GridPos> _landBuffer = new List<GridPos>(512);
        private readonly List<GridPos> _grassToAdd = new List<GridPos>(512);
        private readonly List<GridPos> _scratchTargets = new List<GridPos>(64);
        private readonly NeighborBuffer _neighbors = new NeighborBuffer();
        private readonly NeighborBuffer _neighbors2 = new NeighborBuffer();
        private int[] _shuffleScratch = new int[512];

        private int _tick;
        private EcosystemStats _stats;

        // 每 tick 重置的移动诊断计数。纯统计，不参与规则。
        private int _movesSucceeded;
        private int _movesBlocked;
        private int _movesNotAttempted;
        private int _mealsEaten;
        private int _animalsConsidered;

        /// <summary>本 tick 成功移动的次数。用于观察"生物是不是几乎不动"。</summary>
        public int MovesSucceeded
        {
            get { return _movesSucceeded; }
        }

        /// <summary>本 tick 想动但四邻全被挡住的次数。</summary>
        public int MovesBlocked
        {
            get { return _movesBlocked; }
        }

        /// <summary>本 tick 没尝试移动的动物次数（例如吃饱了又没有目标，或退无可退）。</summary>
        public int MovesNotAttempted
        {
            get { return _movesNotAttempted; }
        }

        /// <summary>本 tick 处理过的动物总数。用来算"移动率"。</summary>
        public int AnimalsConsidered
        {
            get { return _animalsConsidered; }
        }

        public EcosystemSimulation(IslandGrid grid, EcosystemConfigData config)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            _grid = grid;
            _pool = new CreaturePool(256);

            EcosystemConfigData source = config ?? new EcosystemConfigData();
            _rabbitConfig = source.Rabbit.Clone();
            _wolfConfig = source.Wolf.Clone();
            _grassConfig = source.Grass.Clone();

            _rabbitConfig.Sanitize();
            _wolfConfig.Sanitize();
            _grassConfig.Sanitize();

            _rng = new DeterministicRandom(source.Seed);
        }

        /// <summary>动物对象池。只读用途（HUD、视图）。</summary>
        public CreaturePool Pool
        {
            get { return _pool; }
        }

        /// <summary>底下的网格。只读用途（视图、调试、统计）。</summary>
        public IslandGrid Grid
        {
            get { return _grid; }
        }

        /// <summary>当前已推进的 tick 数。</summary>
        public int Tick
        {
            get { return _tick; }
        }

        /// <summary>最近一次 tick 之后的统计快照。</summary>
        public EcosystemStats Stats
        {
            get { return _stats; }
        }

        public SpeciesConfigData RabbitConfig
        {
            get { return _rabbitConfig; }
        }

        public SpeciesConfigData WolfConfig
        {
            get { return _wolfConfig; }
        }

        public SpeciesConfigData GrassConfig
        {
            get { return _grassConfig; }
        }

        // ---------------------------------------------------------------- 重置与投放

        /// <summary>清空一切并按当前参数重新投放初始种群。换岛 / 重开一局时调用。</summary>
        /// <param name="seed">随机种子；0 表示沿用构造时的种子。</param>
        public void Reset(int seed = 0)
        {
            _tick = 0;
            _pool.Clear();

            // 清掉上一次的草与占据标记，保证不残留状态。
            for (int i = 0; i < _grid.CellCount; i++)
            {
                CellState cell = _grid.GetByIndex(i);
                if (cell.Type != TileType.Land)
                {
                    continue;
                }

                GridPos pos = GridPos.FromIndex(i, _grid.Width);
                if (cell.HasGrass)
                {
                    _grid.SetGrass(pos, false);
                }

                if (cell.OccupantId != CellState.NoCreature)
                {
                    _grid.SetOccupant(pos, CellState.NoCreature);
                }
            }

            if (seed != 0)
            {
                _rng.Reset(seed);
            }

            SeedInitialPopulation();
            RefreshStats(0, 0, 0, 0, 0);
            _stats.Tick = 0;
        }

        /// <summary>投放初始种群。草 → 兔 → 狼，顺序固定以保证可复现。</summary>
        public void SeedInitialPopulation()
        {
            CollectLandCells(_landBuffer);
            if (_landBuffer.Count == 0)
            {
                return;
            }

            Shuffle(_landBuffer);

            PlaceGrass(_grassConfig.InitialCount);
            PlaceAnimals(_rabbitConfig, _rabbitConfig.InitialCount);
            PlaceAnimals(_wolfConfig, _wolfConfig.InitialCount);
        }

        private void PlaceGrass(int count)
        {
            int placed = 0;
            for (int i = 0; i < _landBuffer.Count && placed < count; i++)
            {
                _grid.SetGrass(_landBuffer[i], true);
                placed++;
            }
        }

        private void PlaceAnimals(SpeciesConfigData config, int count)
        {
            int placed = 0;
            for (int i = 0; i < _landBuffer.Count && placed < count; i++)
            {
                GridPos pos = _landBuffer[i];
                if (_grid.Get(pos.X, pos.Y).HasCreature)
                {
                    continue;
                }

                SpawnAnimal(config, pos);
                placed++;
            }
        }

        // ---------------------------------------------------------------- 推进

        /// <summary>
        /// 推进一个 tick。
        /// </summary>
        /// <param name="events">
        /// 可选：把本 tick 值得记录的事件（出生 / 饿死 / 被吃）追加进去。
        /// 传 null 时完全不分配。Phase 5 的转折点回溯会用到。
        /// </param>
        public EcosystemStats TickOnce(List<EcosystemEvent> events = null)
        {
            int rabbitBirths = 0;
            int rabbitDeaths = 0;
            int wolfBirths = 0;
            int wolfDeaths = 0;
            int grassEaten = 0;

            _movesSucceeded = 0;
            _movesBlocked = 0;
            _movesNotAttempted = 0;
            _mealsEaten = 0;
            _animalsConsidered = 0;

            // 1) 能量与饿死
            DrainAndStarve(_rabbitConfig, events, ref rabbitDeaths);
            DrainAndStarve(_wolfConfig, events, ref wolfDeaths);

            // 2) 狼先行动，兔后行动。
            //    顺序是规则的一部分：狼先动，才有机会"走到兔子那一格"把它吃掉；
            //    兔子后动，于是逃跑是对狼本 tick 的位置做出的反应。
            RunAnimalPhase(_wolfConfig, events, ref wolfBirths, ref rabbitDeaths, ref wolfDeaths, ref grassEaten);
            RunAnimalPhase(_rabbitConfig, events, ref rabbitBirths, ref rabbitDeaths, ref wolfDeaths, ref grassEaten);

            // 3) 草扩散放在动物之后：让草"补位"到动物刚腾出来的格子上。
            //    （草只按自己的规则长，与动物无关；放这里纯粹是为了让本 tick 的画面一起更新。）
            SpreadGrass();

            _tick++;
            RefreshStats(rabbitBirths, rabbitDeaths, wolfBirths, wolfDeaths, grassEaten);
            _stats.Tick = _tick;
            return _stats;
        }

        // ---------------------------------------------------------------- 第 1 步：能量与饿死

        private void DrainAndStarve(SpeciesConfigData config, List<EcosystemEvent> events, ref int deaths)
        {
            SnapshotSpecies(config.Kind);

            for (int i = 0; i < _animalBuffer.Count; i++)
            {
                CreatureHandle handle = _animalBuffer[i];
                if (!_pool.TryGet(handle, out Creature creature))
                {
                    continue;
                }

                float energy = creature.Energy - config.EnergyDrainPerTick;
                _pool.SetEnergy(handle, energy);
                _pool.IncrementAge(handle);

                if (energy <= 0f)
                {
                    _grid.SetOccupant(creature.Position, CellState.NoCreature);
                    _pool.Kill(handle);
                    deaths++;
                    Record(events, config.Kind, EcosystemEventType.Starved, creature.Position);
                }
            }
        }

        // ---------------------------------------------------------------- 第 2 步：草扩散

        /// <summary>
        /// 草扩散。扫描全场收集"本 tick 新长出的草"，扫完统一写入 ——
        /// 这样本 tick 新长的草不会在同一 tick 内继续扩散（否则一天就能铺满全岛）。
        ///
        /// 自限规则的检查对象是<b>目标格</b>而不是源格，这一点极其关键：
        ///   检查源格会导致"源格自己草邻居多就不能扩散" —— 而 60 格随机初始草彼此相邻，
        ///   几乎所有源格都超标，草从第 1 tick 起就彻底冻结（实测 40 个 tick 只长了 2 格）。
        ///   改为检查目标格后，行为才符合直觉：
        ///     被兔子啃掉的格子（草邻居少）→ 允许长回来 → 草能再生
        ///     已经被草包围的空隙（草邻居多）→ 不再填 → 保留出永久空地，覆盖率达到平衡
        ///   于是"草覆盖率"有了一个由参数决定的上限，而不是一路涨到 100%。
        /// </summary>
        private void SpreadGrass()
        {
            int width = _grid.Width;
            int height = _grid.Height;

            _grassToAdd.Clear();

            int attempts = RollSpreadAttempts();
            if (attempts <= 0)
            {
                return;
            }

            for (int i = 0; i < _grid.CellCount; i++)
            {
                CellState cell = _grid.GetByIndex(i);
                if (cell.Type != TileType.Land || !cell.HasGrass)
                {
                    continue;
                }

                int x = i % width;
                int y = i / width;

                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    if (!_rng.Chance(_grassConfig.SpreadChance))
                    {
                        continue;
                    }

                    int neighborCount = GridUtility.FillNeighbors(x, y, width, height, _neighbors);
                    if (neighborCount == 0)
                    {
                        continue;
                    }

                    int offset = _rng.NextInt(neighborCount);
                    int nx = _neighbors.X[offset];
                    int ny = _neighbors.Y[offset];

                    CellState target = _grid.Get(nx, ny);
                    if (target.Type != TileType.Land || target.HasGrass)
                    {
                        continue;
                    }

                    var pos = new GridPos(nx, ny);
                    if (ContainsPos(_grassToAdd, pos))
                    {
                        continue;
                    }

                    // 自限：目标格已经很"被草包围"时不再填它，保留出空地。
                    if (_grassConfig.MaxGrassNeighbors > 0
                        && CountGrassNeighbors(nx, ny) >= _grassConfig.MaxGrassNeighbors)
                    {
                        continue;
                    }

                    // 可选：要求目标格至少挨着一格草，避免出现"隔着空地从远处长出来"的观感。
                    if (_grassConfig.RequireGrassNeighbor && CountGrassNeighbors(nx, ny) < 1)
                    {
                        continue;
                    }

                    _grassToAdd.Add(pos);
                }
            }

            for (int i = 0; i < _grassToAdd.Count; i++)
            {
                _grid.SetGrass(_grassToAdd[i], true);
            }
        }

        /// <summary>
        /// 掷出本 tick 每格尝试扩散的次数。
        /// 支持小数速率（例如 0.7 表示"70% 的 tick 尝试 1 次，其余不尝试"），
        /// 比"每格随机挑邻居"更容易调参，也完全确定。
        /// </summary>
        private int RollSpreadAttempts()
        {
            float attempts = _grassConfig.SpreadAttemptsPerTick;
            int whole = (int)attempts;
            float fractional = attempts - whole;

            if (fractional > 0f && _rng.Chance(fractional))
            {
                whole++;
            }

            return whole;
        }

        private int CountGrassNeighbors(int x, int y)
        {
            int count = 0;
            int n = GridUtility.FillNeighbors(x, y, _grid.Width, _grid.Height, _neighbors);
            for (int i = 0; i < n; i++)
            {
                if (_grid.Get(_neighbors.X[i], _neighbors.Y[i]).HasGrass)
                {
                    count++;
                }
            }

            return count;
        }

        // ---------------------------------------------------------------- 第 3 步：动物行动

        private void RunAnimalPhase(SpeciesConfigData config, List<EcosystemEvent> events,
            ref int births, ref int rabbitDeaths, ref int wolfDeaths, ref int grassEaten)
        {
            SnapshotSpecies(config.Kind);

            for (int i = 0; i < _animalBuffer.Count; i++)
            {
                CreatureHandle handle = _animalBuffer[i];

                // 可能已被天敌吃掉或刚饿死 —— 使用前必须校验。
                if (!_pool.TryGet(handle, out Creature creature))
                {
                    continue;
                }

                _animalsConsidered++;

                // --- 繁殖中：这一 tick 只用来"生"，不移动也不进食 ---
                if (creature.BreedingTicks > 0)
                {
                    if (AdvanceBreeding(handle, config, events))
                    {
                        births++;
                    }

                    continue;
                }

                float energy = creature.Energy;
                GridPos positionBefore = creature.Position;
                int movesBefore = _movesSucceeded;
                int blockedBefore = _movesBlocked;
                bool moved = false;

                // --- 先决定行进，再决定进食 ---
                bool hungry = energy < config.MaxEnergy * 0.75f;
                bool hunting = config.PreyKind != SpeciesKind.None && hungry;

                for (int step = 0; step < config.MoveStepsPerTick; step++)
                {
                    if (!_pool.IsAlive(handle))
                    {
                        break;
                    }

                    if (!TryStep(handle, config, hunting, events, ref rabbitDeaths, ref wolfDeaths, ref grassEaten))
                    {
                        break;
                    }

                    if (!_pool.IsAlive(handle))
                    {
                        break;
                    }
                }

                if (!_pool.IsAlive(handle))
                {
                    continue;
                }

                moved = _movesSucceeded > movesBefore;

                // --- 移动之后的进食 ---
                //
                // 顺序至关重要：进食必须在移动<b>之后</b>。
                // 若放在移动之前，紧接着的移动会调 CancelEating 把刚建立的啃食进度清掉，
                // 草就永远吃不到（第一版正是这个 bug，兔群在第 18 tick 集体饿死）。
                //
                // 规则很直白：<b>移动到哪一格，就啃哪一格</b>。
                // 所以移动进来的那一 tick 立刻开始啃（进度 1/N），停下来的那一格继续啃。
                if (config.PreyKind == SpeciesKind.None)
                {
                    GridPos grazeAt = creature.Position;
                    if (_pool.TryGet(handle, out Creature settled))
                    {
                        grazeAt = settled.Position;
                    }

                    if (_grid.Get(grazeAt.X, grazeAt.Y).HasGrass)
                    {
                        energy = Graze(handle, config, grazeAt, energy, ref grassEaten);
                    }
                }

                // 本 tick 这一只既没成功移动、也没被阻挡 —— 说明它压根没想动。
                if (!moved && _movesBlocked == blockedBefore)
                {
                    _movesNotAttempted++;
                }

                // 移动与进食都可能改动能量，重新取一次
                if (_pool.TryGet(handle, out creature))
                {
                    energy = creature.Energy;
                }

                _pool.SetEnergy(handle, Clamp(energy, 0f, config.MaxEnergy));

                // --- 繁殖 ---
                TryStartBreeding(handle, config, creature);
            }
        }

        /// <summary>
        /// 就地啃食。规则：<b>站上草格后要花一个 tick 才能把草完全吃掉</b>。
        ///
        /// 实现：给这一格记"已经被啃了几 tick"（<see cref="IslandGrid.TickEating"/>），
        /// 啃满 <see cref="SpeciesConfigData.EatTicks"/> 次后草才消失。
        /// 能量在<b>第一口</b>就给 —— 否则边吃边走的兔子永远等不到进度达标那一刻，一口都吃不上
        /// （第一版就让能量等到"进度达标"才给，结果兔群在第 18 tick 集体饿死）。
        /// 所以这条规则的实际代价是：<b>草要过一回合才消失，而不是兔子要饿一回合</b>。
        /// 中途走开则进度作废（<see cref="IslandGrid.CancelEating"/>），能量已经到手。
        ///
        /// <b>必须在移动之后调用</b>：若在移动之前调用，紧接着的移动会把刚建立的啃食进度取消掉。
        /// </summary>
        private float Graze(CreatureHandle handle, SpeciesConfigData config, GridPos position, float energy,
            ref int grassEaten)
        {
            if (!_grid.Get(position.X, position.Y).HasGrass)
            {
                return energy;
            }

            bool firstBite = _grid.GetEatingTicks(position) <= 0;

            if (_grid.TickEating(position, Math.Max(1, config.EatTicks)))
            {
                // 啃够了：草从这一格消失
                grassEaten++;
                _mealsEaten++;
            }

            // 第一口就给能量。这是"吃到了"的判定点，与草何时消失无关。
            if (firstBite)
            {
                energy = Clamp(energy + config.EnergyPerFood, 0f, config.MaxEnergy);
                _pool.SetEnergy(handle, energy);
            }

            return energy;
        }

        /// <summary>
        /// 尝试开始繁殖：需要能量达标、冷却结束、且<b>相邻格上有同物种</b>。
        /// 满足则先扣能量并进入倒计时（兔 1 tick、狼 2 tick），期间不移动。
        /// </summary>
        private void TryStartBreeding(CreatureHandle handle, SpeciesConfigData config, Creature creature)
        {
            if (creature.Energy < config.ReproductionEnergyThreshold)
            {
                // 没到门槛时照常递减冷却，保证"等冷却"和"攒能量"能并行。
                if (creature.ReproductionCooldown > 0f)
                {
                    _pool.SetReproductionCooldown(handle, creature.ReproductionCooldown - 1f);
                }

                return;
            }

            if (creature.ReproductionCooldown > 0f)
            {
                _pool.SetReproductionCooldown(handle, creature.ReproductionCooldown - 1f);
                return;
            }

            // 人数上限
            if (config.PopulationCap > 0 && _pool.CountSpecies(config.Kind) >= config.PopulationCap)
            {
                return;
            }

            // 需要相邻的同伴
            if (!_pool.TryFindAdjacentMate(handle.Index, config.Kind, out int mateSlot))
            {
                return;
            }

            // 先付代价再进入倒计时：中途被吃掉的话，能量就白花了。
            _pool.SetEnergy(handle, Clamp(creature.Energy - config.ReproductionEnergyCost, 0f, config.MaxEnergy));
            _pool.SetReproductionCooldown(handle, config.ReproductionCooldownTicks);
            _pool.SetBreeding(handle, Math.Max(1, config.BreedingTicks), mateSlot);
        }

        /// <summary>
        /// 推进繁殖倒计时。归零时再确认一次搭档仍在旁边，然后生出后代。
        /// 返回 true 表示这一 tick 成功生出了一只。
        /// </summary>
        private bool AdvanceBreeding(CreatureHandle handle, SpeciesConfigData config, List<EcosystemEvent> events)
        {
            if (!_pool.TryGet(handle, out Creature creature))
            {
                return false;
            }

            int remaining = creature.BreedingTicks - 1;
            if (remaining > 0)
            {
                _pool.SetBreeding(handle, remaining, creature.BreedingPartner);
                return false;
            }

            // 倒计时结束：搭档可能已经死了或走开了，那这次繁殖作废。
            bool partnerStillThere = creature.BreedingPartner >= 0
                && _pool.IsSlotAlive(creature.BreedingPartner)
                && IsAdjacent(creature.Position, _pool.GetSlot(creature.BreedingPartner).Position);

            _pool.SetBreeding(handle, 0, -1);

            if (!partnerStillThere)
            {
                return false;
            }

            if (config.PopulationCap > 0 && _pool.CountSpecies(config.Kind) >= config.PopulationCap)
            {
                return false;
            }

            if (!TryFindEmptyLand(creature.Position, out GridPos spot))
            {
                return false;
            }

            SpawnAnimal(config, spot);
            Record(events, config.Kind, EcosystemEventType.Born, spot);
            return true;
        }

        /// <summary>
        /// 尝试走一步。决策优先级（这个顺序就是"性格"）：
        ///   1. 天敌在视野内 → 逃跑（生存优先，压倒一切）
        ///   2. 肉食且饥饿 → 直奔视野内最近的猎物
        ///   3. 草食且饥饿 → 走向视野内最近的草地
        ///   4. 否则 → 随机游走
        ///
        /// 返回 true 表示成功移动（或成功吃掉了相邻猎物）。
        ///
        /// <b>为什么生物会长时间不动</b>，本方法里有三条明确的原因，都用诊断计数器记着：
        ///   a. 退无可退：想逃跑但四个方向都不是可走空地 → 原地不动（<c>MovesBlocked</c>）
        ///   b. 撞墙：想追猎/吃草但那个方向的格子被占或不是陆地，且没有备选方向
        ///      → 落到随机游走；随机游走也失败就打住（<c>MovesBlocked</c>）
        ///   c. 吃饱了又没有目标：兔子只在能量低于 90% 上限时才主动找草，
        ///      能量高时纯随机游走，看上去就是"在附近晃悠"（<c>MovesNotAttempted</c>）
        /// 另外注意：生物每 tick 只走 1 格（<c>MoveStepsPerTick</c>），
        /// 而 1 tick = 1 真实秒，所以"走一格"在画面上本来就有 1 秒的间隔感 ——
        /// 视图层用平滑插值补上这段位移，观感才会连贯。
        /// </summary>
        private bool TryStep(CreatureHandle handle, SpeciesConfigData config, bool hunting,
            List<EcosystemEvent> events, ref int rabbitDeaths, ref int wolfDeaths, ref int grassEaten)
        {
            if (!_pool.TryGet(handle, out Creature creature))
            {
                return false;
            }

            GridPos from = creature.Position;

            // 1) 逃跑
            if (config.PredatorKind != SpeciesKind.None
                && TryFindNearestCreature(from, config.PredatorKind, config.VisionRadius, out GridPos threat))
            {
                if (TryStepAway(from, threat, out GridPos escape))
                {
                    return ApplyMove(handle, config, from, escape);
                }

                // 后退方向被挡住时不要原地等死 —— 往任意方向挪一格，至少拉开一点距离。
                // 修复前这里是"直接返回 false"，于是被围住的兔子会一直站着不动：
                // 实测默认参数下 120 tick 内有 31.9% 的移动尝试是被挡住的，
                // 观感上就是"生物卡住了"。
                if (TryStepRandom(from, out GridPos panic))
                {
                    return ApplyMove(handle, config, from, panic);
                }

                _movesBlocked++;
                return false;
            }

            // 2) 追猎：狼必须<b>走到兔子那一格</b>才能吃掉它。
            //    走到同一格 = 吃掉并占据该格；所以吃与移动是同一个动作，不额外消耗回合。
            //    （旧规则是"相邻即可吃"，那让狼从斜对角凭空咬到兔子，观感上像隔空取物。）
            if (hunting && TryFindNearestCreature(from, config.PreyKind, config.VisionRadius, out GridPos prey))
            {
                if (TryStepToward(from, prey, true, out GridPos chase))
                {
                    // 目标格正是猎物所在 → 走进去吃掉
                    if (chase == prey && TryEatPreyAt(handle, config, prey, events, ref rabbitDeaths, ref wolfDeaths))
                    {
                        _grid.CancelEating(from);
                        _grid.MoveOccupant(from, prey, handle.Index);
                        _pool.SetPosition(handle, prey);
                        _movesSucceeded++;
                        return true;
                    }

                    return ApplyMove(handle, config, from, chase);
                }
            }

            // 3) 找草
            if (config.PreyKind == SpeciesKind.None
                && creature.Energy < config.MaxEnergy * 0.9f
                && TryFindNearestGrass(from, config.VisionRadius, out GridPos grass))
            {
                if (TryStepToward(from, grass, false, out GridPos graze))
                {
                    return ApplyMove(handle, config, from, graze);
                }
            }

            // 4) 随机游走（最后兜底）
            if (TryStepRandom(from, out GridPos wander))
            {
                return ApplyMove(handle, config, from, wander);
            }

            // 四个方向全部走不通：这就是"撞墙不动"
            _movesBlocked++;
            return false;
        }

        /// <summary>把生物从 from 挪到 to，并同步网格、清理啃食进度、累计移动统计。</summary>
        private bool ApplyMove(CreatureHandle handle, SpeciesConfigData config, GridPos from, GridPos to)
        {
            if (from == to)
            {
                return false;
            }

            // 走开了就不再啃这一格：草只受一次"开始啃"的影响，进度作废。
            _grid.CancelEating(from);

            _grid.MoveOccupant(from, to, handle.Index);
            _pool.SetPosition(handle, to);
            _movesSucceeded++;
            return true;
        }

        /// <summary>吃相邻的猎物。成功返回 true（猎手留在原地，不额外消耗一次移动）。</summary>
        private bool TryEatPrey(CreatureHandle hunter, SpeciesConfigData config, GridPos preyPos,
            List<EcosystemEvent> events, ref int rabbitDeaths, ref int wolfDeaths)
        {
            return TryEatPreyAt(hunter, config, preyPos, events, ref rabbitDeaths, ref wolfDeaths);
        }

        private bool TryEatPreyAt(CreatureHandle hunter, SpeciesConfigData config, GridPos preyPos,
            List<EcosystemEvent> events, ref int rabbitDeaths, ref int wolfDeaths)
        {
            CellState cell = _grid.Get(preyPos.X, preyPos.Y);
            if (cell.OccupantId == CellState.NoCreature)
            {
                return false;
            }

            var preyHandle = new CreatureHandle();
            if (!TryFindCreatureAt(preyPos, out preyHandle))
            {
                return false;
            }

            if (!_pool.TryGet(preyHandle, out Creature prey) || prey.Kind != config.PreyKind)
            {
                return false;
            }

            // 移除猎物
            _grid.SetOccupant(preyPos, CellState.NoCreature);
            _pool.Kill(preyHandle);

            if (prey.Kind == SpeciesKind.Rabbit)
            {
                rabbitDeaths++;
            }
            else if (prey.Kind == SpeciesKind.Wolf)
            {
                wolfDeaths++;
            }

            Record(events, prey.Kind, EcosystemEventType.Eaten, preyPos);

            // 猎手回复能量
            if (_pool.TryGet(hunter, out Creature hunterState))
            {
                _pool.SetEnergy(hunter, Clamp(hunterState.Energy + config.EnergyPerFood, 0f, config.MaxEnergy));
            }

            return true;
        }

        /// <summary>在指定格子上找生物句柄。逐格扫描存活列表，规模很小可以接受。</summary>
        private bool TryFindCreatureAt(GridPos pos, out CreatureHandle handle)
        {
            for (int i = 0; i < _pool.AliveCount; i++)
            {
                Creature c = _pool.GetAlive(i);
                if (c.Position == pos)
                {
                    handle = _pool.GetAliveHandle(i);
                    return true;
                }
            }

            handle = CreatureHandle.None;
            return false;
        }

        /// <summary>在相邻格里找一个可走的空地。找不到返回 false。</summary>
        private bool TryFindEmptyLand(GridPos origin, out GridPos result)
        {
            result = origin;

            int n = GridUtility.FillNeighbors(origin.X, origin.Y, _grid.Width, _grid.Height, _neighbors);
            if (n == 0)
            {
                return false;
            }

            // 从随机起点顺时针找第一个可走格：既随机又不依赖"尝试-失败-重试"。
            int start = _rng.NextInt(n);
            for (int i = 0; i < n; i++)
            {
                int idx = (start + i) % n;
                int x = _neighbors.X[idx];
                int y = _neighbors.Y[idx];

                CellState cell = _grid.Get(x, y);
                if (cell.Type == TileType.Land && !cell.HasCreature)
                {
                    result = new GridPos(x, y);
                    return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------- 寻路与搜索

        /// <summary>在视野内找最近的某物种。视野为 0 时退化成全场搜索。</summary>
        private bool TryFindNearestCreature(GridPos from, SpeciesKind kind, int visionRadius, out GridPos result)
        {
            result = from;
            if (kind == SpeciesKind.None)
            {
                return false;
            }

            int bestDistance = int.MaxValue;
            bool found = false;

            for (int i = 0; i < _pool.AliveCount; i++)
            {
                Creature c = _pool.GetAlive(i);
                if (c.Kind != kind)
                {
                    continue;
                }

                int distance = ChebyshevDistance(from, c.Position);
                if (visionRadius > 0 && distance > visionRadius)
                {
                    continue;
                }

                // 同距离时取遍历顺序更靠前的，保证确定性。
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    result = c.Position;
                    found = true;
                }
            }

            return found;
        }

        private bool TryFindNearestGrass(GridPos from, int visionRadius, out GridPos result)
        {
            result = from;
            int radius = visionRadius > 0 ? visionRadius : Math.Max(_grid.Width, _grid.Height);
            int bestDistance = int.MaxValue;
            bool found = false;

            int minY = Math.Max(0, from.Y - radius);
            int maxY = Math.Min(_grid.Height - 1, from.Y + radius);
            int minX = Math.Max(0, from.X - radius);
            int maxX = Math.Min(_grid.Width - 1, from.X + radius);

            // 按固定顺序扫描（y 外层、x 内层），保证同距离时选中的格子是确定的。
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (!_grid.Get(x, y).HasGrass)
                    {
                        continue;
                    }

                    int distance = ChebyshevDistance(from, new GridPos(x, y));
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        result = new GridPos(x, y);
                        found = true;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// 朝目标走一步。
        /// </summary>
        /// <param name="targetIsPrey">
        /// 目标格上站着猎物。为 true 时允许把"猎物所在格"当成可走目标 ——
        /// 这是狼能追进兔子那一格的前提；为 false 时（找草、逃跑）遇到被占的格子就绕开。
        /// </param>
        private bool TryStepToward(GridPos from, GridPos target, bool targetIsPrey, out GridPos result)
        {
            result = from;

            int dx = Math.Sign(target.X - from.X);
            int dy = Math.Sign(target.Y - from.Y);

            // 先试对角线（更贴近直线追击），再试单轴
            if (dx != 0 && dy != 0 && TryCandidate(from.X + dx, from.Y + dy, targetIsPrey, ref result))
            {
                return true;
            }

            if (dx != 0 && TryCandidate(from.X + dx, from.Y, targetIsPrey, ref result))
            {
                return true;
            }

            if (dy != 0 && TryCandidate(from.X, from.Y + dy, targetIsPrey, ref result))
            {
                return true;
            }

            return false;
        }

        /// <summary>朝远离威胁的方向走一步。与威胁同格时退化为随机游走。</summary>
        private bool TryStepAway(GridPos from, GridPos threat, out GridPos result)
        {
            result = from;

            int dx = Math.Sign(from.X - threat.X);
            int dy = Math.Sign(from.Y - threat.Y);
            if (dx == 0 && dy == 0)
            {
                return TryStepRandom(from, out result);
            }

            if (dx != 0 && dy != 0 && TryCandidate(from.X + dx, from.Y + dy, false, ref result))
            {
                return true;
            }

            if (dx != 0 && TryCandidate(from.X + dx, from.Y, false, ref result))
            {
                return true;
            }

            if (dy != 0 && TryCandidate(from.X, from.Y + dy, false, ref result))
            {
                return true;
            }

            return false;
        }

        private bool TryStepRandom(GridPos from, out GridPos result)
        {
            result = from;

            int n = GridUtility.FillNeighbors(from.X, from.Y, _grid.Width, _grid.Height, _neighbors2);
            if (n == 0)
            {
                return false;
            }

            int start = _rng.NextInt(n);
            for (int i = 0; i < n; i++)
            {
                int idx = (start + i) % n;
                int x = _neighbors2.X[idx];
                int y = _neighbors2.Y[idx];

                if (_grid.Get(x, y).Type != TileType.Land || _grid.Get(x, y).HasCreature)
                {
                    continue;
                }

                result = new GridPos(x, y);
                return true;
            }

            return false;
        }

        private bool TryCandidate(int x, int y, bool allowOccupied, ref GridPos result)
        {
            if (!_grid.InBounds(x, y))
            {
                return false;
            }

            CellState cell = _grid.Get(x, y);
            if (cell.Type != TileType.Land)
            {
                return false;
            }

            if (cell.HasCreature && !allowOccupied)
            {
                return false;
            }

            result = new GridPos(x, y);
            return true;
        }

        // ---------------------------------------------------------------- 工具

        private void SnapshotSpecies(SpeciesKind kind)
        {
            _animalBuffer.Clear();
            for (int i = 0; i < _pool.AliveCount; i++)
            {
                if (_pool.GetAlive(i).Kind == kind)
                {
                    _animalBuffer.Add(_pool.GetAliveHandle(i));
                }
            }
        }

        private void CollectLandCells(List<GridPos> result)
        {
            result.Clear();
            int width = _grid.Width;
            for (int i = 0; i < _grid.CellCount; i++)
            {
                if (_grid.GetByIndex(i).Type == TileType.Land)
                {
                    result.Add(GridPos.FromIndex(i, width));
                }
            }
        }

        private void Shuffle(List<GridPos> list)
        {
            if (list.Count > _shuffleScratch.Length)
            {
                _shuffleScratch = new int[list.Count * 2];
            }

            for (int i = 0; i < list.Count; i++)
            {
                _shuffleScratch[i] = i;
            }

            _rng.Shuffle(_shuffleScratch, list.Count);

            // 用一个临时列表按洗好的下标重排。只在 Reset 时发生，不影响运行时性能。
            var copy = new List<GridPos>(list);
            for (int i = 0; i < list.Count; i++)
            {
                list[i] = copy[_shuffleScratch[i]];
            }
        }

        private CreatureHandle SpawnAnimal(SpeciesConfigData config, GridPos position)
        {
            CreatureHandle handle = _pool.Spawn(config.Kind, position, config.InitialEnergy, config.ReproductionCooldownTicks);
            _grid.SetOccupant(position, handle.Index);
            return handle;
        }

        private static bool ContainsPos(List<GridPos> list, GridPos pos)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == pos)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAdjacent(GridPos a, GridPos b)
        {
            return ChebyshevDistance(a, b) <= 1;
        }

        private static int ChebyshevDistance(GridPos a, GridPos b)
        {
            return Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        private void RefreshStats(int rabbitBirths, int rabbitDeaths, int wolfBirths, int wolfDeaths, int grassEaten)
        {
            _stats = new EcosystemStats
            {
                Tick = _tick,
                RabbitCount = _pool.CountSpecies(SpeciesKind.Rabbit),
                WolfCount = _pool.CountSpecies(SpeciesKind.Wolf),
                GrassCells = _grid.GrassCount,
                LandCells = _grid.LandCount,
                RabbitBirths = rabbitBirths,
                RabbitDeaths = rabbitDeaths,
                WolfBirths = wolfBirths,
                WolfDeaths = wolfDeaths,
                GrassEaten = grassEaten,
                MovesSucceeded = _movesSucceeded,
                MovesBlocked = _movesBlocked,
                MovesNotAttempted = _movesNotAttempted,
                MealsEaten = _mealsEaten,
            };
        }

        private void Record(List<EcosystemEvent> events, SpeciesKind kind, EcosystemEventType type, GridPos position)
        {
            if (events == null)
            {
                return;
            }

            events.Add(new EcosystemEvent
            {
                Tick = _tick,
                Kind = kind,
                Type = type,
                Position = position,
            });
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
