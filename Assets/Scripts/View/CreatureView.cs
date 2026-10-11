using UnityEngine;
using Omen.Core;

namespace Omen.View
{
    /// <summary>
    /// 生物视图：把 <see cref="CreaturePool"/> 里的兔与狼画成小圆点。
    ///
    /// 架构铁律 2：本类<b>只读</b>数据，绝不修改模拟状态。
    ///
    /// 与 <see cref="GridView"/> 的"一格一个物体"不同，这里是"<b>一个槽位一个物体</b>"：
    ///   - 物体数量 = 池容量，创建后<b>永不增删</b>（真正的对象池）
    ///   - 哪个物体代表哪只生物，由"槽位 + 代际"决定：池槽位被复用时代际号会变，
    ///     本类据此立刻发现"该换主人了"，而不是把死亡生物的精灵留在原地
    ///   - 画不画由 <c>SpriteRenderer.enabled</c> 控制，比自己增删 GameObject 便宜得多
    ///
    /// 两个纯表现层的效果（都不影响模拟结果）：
    ///   - <b>平滑移动</b>：模拟是"每 tick 瞬移一格"，视图把这段位移按真实时间插值补上。
    ///     生物一 tick 走一格、1 tick = 1 秒，所以插值时长就取"一个 tick 的秒数"。
    ///   - <b>进食闪烁</b>：视图自己发现某只的能量突然上涨（说明它刚吃到东西），
    ///     于是把它高亮闪一下。不需要模拟层发事件 —— 少一条跨层耦合。
    ///
    /// 刷新时机：只在池版本号变化时同步（即发生了一次 tick），所以静止时同步开销为零。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CreatureView : MonoBehaviour
    {
        /// <summary>能量上涨超过这个值就认为是"吃到东西了"，触发闪烁。狼吃一只兔涨 25，兔吃一口草涨 5，都远大于它。</summary>
        private const float EatEnergyThreshold = 1.5f;

        [Header("尺寸（世界单位，1 格 = 1 单位）")]
        [Tooltip("兔的圆点直径。")]
        [Range(0.1f, 1.0f)]
        [SerializeField] private float rabbitSize = 0.44f;

        [Tooltip("狼的圆点直径。狼比兔大一圈，便于一眼区分。")]
        [Range(0.1f, 1.0f)]
        [SerializeField] private float wolfSize = 0.62f;

        [Header("渲染")]
        [Tooltip("生物的 Sorting Order。必须高于地形（地形为 0）才能盖在草地上。")]
        [SerializeField] private int sortingOrder = 10;

        [Tooltip("Sorting Layer。留空表示用 Default。")]
        [SerializeField] private string sortingLayerName = "";

        [Header("动画（纯表现）")]
        [Tooltip("是否启用平滑移动。关掉就退回『每 tick 瞬移一格』。")]
        [SerializeField] private bool smoothMovement = true;

        [Tooltip("是否启用进食闪烁。")]
        [SerializeField] private bool eatFlash = true;

        private EcosystemSimulation _simulation;
        private CellPalette _palette;

        private SpriteRenderer[] _renderers;
        private uint[] _viewGenerations;

        // 平滑移动状态：起点位置 + 插值进度。
        private Vector3[] _moveFrom;
        private float[] _moveProgress;

        // 进食闪烁状态：剩余时长 + 该生物的基准色。
        private float[] _flashRemaining;
        private Color[] _baseColors;

        // 上一 tick 的能量，用于发现"刚吃到东西"。
        private float[] _lastEnergy;
        private bool[] _hasLastEnergy;

        private float _tickSeconds = 1f;
        private float _moveSeconds = 1.6f;
        private float _flashSeconds = 0.35f;
        private float _flashBrightness = 1.7f;
        private int _syncedVersion = -1;

        /// <summary>当前显示的兔数量。</summary>
        public int VisibleRabbits { get; private set; }

        /// <summary>当前显示的狼数量。</summary>
        public int VisibleWolves { get; private set; }

        /// <summary>已创建的精灵物体数量。</summary>
        public int SpriteCapacity
        {
            get { return _renderers != null ? _renderers.Length : 0; }
        }

        /// <summary>
        /// 绑定模拟、配色与时间尺度。由 <c>GameBootstrap</c> 在装配时调用。
        /// </summary>
        /// <param name="simulation">生态系统模拟。</param>
        /// <param name="config">配色与动画参数来源。</param>
        /// <param name="secondsPerTick">一个 tick 对应多少真实秒。移动动画时长由它乘以 MoveAnimationScale 得出。</param>
        public void Bind(EcosystemSimulation simulation, Config.IslandConfig config, float secondsPerTick)
        {
            _simulation = simulation;
            _tickSeconds = Mathf.Max(0.01f, secondsPerTick);

            ApplyConfig(config);

            _syncedVersion = -1;   // 强制下一次同步做全量刷新
            EnsureCapacity(true);
            Synchronize();
        }

        /// <summary>每帧同步。池版本号没变时直接返回，开销为零。</summary>
        public void Synchronize()
        {
            if (_simulation == null)
            {
                return;
            }

            CreaturePool pool = _simulation.Pool;
            if (pool.PoolVersion == _syncedVersion)
            {
                return;
            }

            _syncedVersion = pool.PoolVersion;
            EnsureCapacity(false);

            int rabbits = 0;
            int wolves = 0;

            for (int slot = 0; slot < _renderers.Length; slot++)
            {
                SpriteRenderer renderer = _renderers[slot];
                bool alive = slot < pool.Capacity && pool.IsSlotAlive(slot);

                if (!alive)
                {
                    if (renderer != null && renderer.enabled)
                    {
                        renderer.enabled = false;
                    }

                    _viewGenerations[slot] = 0u;
                    _hasLastEnergy[slot] = false;
                    _flashRemaining[slot] = 0f;
                    continue;
                }

                Creature creature = pool.GetSlot(slot);
                uint generation = creature.Generation;

                // 槽位还是同一只生物 → 只可能移动了位置，直接搬。
                // 代际号变了 → 这只生物是新生的（复用了死亡生物的槽位），需要换颜色与大小。
                bool sameOccupant = _viewGenerations[slot] == generation;
                Vector3 target = WorldGrid.CellCenter(creature.Position);

                if (renderer == null)
                {
                    _viewGenerations[slot] = generation;
                    _lastEnergy[slot] = creature.Energy;
                    _hasLastEnergy[slot] = true;
                    continue;
                }

                if (!renderer.enabled || !sameOccupant)
                {
                    _baseColors[slot] = _palette.ForSpecies(creature.Kind);
                    ApplyAppearance(renderer, creature, _baseColors[slot]);

                    // 新出现的生物直接落到目标格，不要从别处滑过来。
                    renderer.transform.localPosition = target;
                    _moveFrom[slot] = target;
                    _moveProgress[slot] = 1f;
                    _flashRemaining[slot] = 0f;
                }
                else if (renderer.transform.localPosition != target)
                {
                    // 位置变了：从当前位置开始，在一个 tick 的时长内滑到新格子。
                    _moveFrom[slot] = renderer.transform.localPosition;
                    _moveProgress[slot] = smoothMovement ? 0f : 1f;

                    if (!smoothMovement)
                    {
                        renderer.transform.localPosition = target;
                    }
                }

                // 进食检测：能量突然上涨说明刚吃到东西。
                if (eatFlash && _hasLastEnergy[slot] && creature.Energy > _lastEnergy[slot] + EatEnergyThreshold)
                {
                    _flashRemaining[slot] = _flashSeconds;
                }

                _lastEnergy[slot] = creature.Energy;
                _hasLastEnergy[slot] = true;
                _viewGenerations[slot] = generation;

                if (creature.Kind == SpeciesKind.Rabbit)
                {
                    rabbits++;
                }
                else if (creature.Kind == SpeciesKind.Wolf)
                {
                    wolves++;
                }
            }

            VisibleRabbits = rabbits;
            VisibleWolves = wolves;
        }

        /// <summary>换配色后立即刷新全部外观。</summary>
        public void ApplyPalette(Config.IslandConfig config)
        {
            ApplyConfig(config);

            _viewGenerations = null;
            _syncedVersion = -1;
            EnsureCapacity(true);
            Synchronize();
        }

        /// <summary>
        /// 从配置资产刷新全部表现参数。
        /// 抽成一处是因为 Bind 与 ApplyPalette 都要用；曾经只在 Bind 里算过一次移动时长，
        /// 结果是"改了动画系数却不生效"这种难查的不同步问题。
        /// </summary>
        private void ApplyConfig(Config.IslandConfig config)
        {
            IslandConfigData data = config != null ? config.ToData() : new IslandConfigData();
            _palette = CellPalette.FromConfig(data);
            _flashSeconds = Mathf.Max(0f, data.EatFlashSeconds);
            _flashBrightness = Mathf.Max(1f, data.EatFlashBrightness);

            // 移动动画时长 = 一个 tick × 系数。系数 > 1 时相邻两次移动的动画会重叠，
            // 于是"停一下走一格"变成连续滑行（详见 IslandConfigData.MoveAnimationScale）。
            _moveSeconds = _tickSeconds * Mathf.Max(1f, data.MoveAnimationScale);
        }

        private void Update()
        {
            Synchronize();
            AdvanceAnimations(Time.deltaTime);
        }

        // ---------------------------------------------------------------- 动画推进

        /// <summary>
        /// 按真实时间推进平滑移动与闪烁。两个效果都用真实时间，与模拟的 tick 节奏解耦 ——
        /// 所以调快调慢一局的时长，动画的快慢不变。
        /// </summary>
        private void AdvanceAnimations(float deltaSeconds)
        {
            if (_renderers == null || _simulation == null)
            {
                return;
            }

            CreaturePool pool = _simulation.Pool;

            for (int slot = 0; slot < _renderers.Length; slot++)
            {
                SpriteRenderer renderer = _renderers[slot];
                if (renderer == null || !renderer.enabled || !pool.IsSlotAlive(slot))
                {
                    continue;
                }

                // 平滑移动
                if (_moveProgress[slot] < 1f)
                {
                    _moveProgress[slot] = Mathf.Clamp01(_moveProgress[slot] + deltaSeconds / _moveSeconds);

                    Creature creature = pool.GetSlot(slot);
                    Vector3 target = WorldGrid.CellCenter(creature.Position);

                    // SmoothStep 让"起步—到位"有加减速感，而不是匀速平移。
                    float eased = Mathf.SmoothStep(0f, 1f, _moveProgress[slot]);
                    renderer.transform.localPosition = Vector3.Lerp(_moveFrom[slot], target, eased);
                }

                // 进食闪烁
                if (_flashRemaining[slot] > 0f)
                {
                    _flashRemaining[slot] = Mathf.Max(0f, _flashRemaining[slot] - deltaSeconds);

                    if (_flashRemaining[slot] > 0f && _flashSeconds > 0f)
                    {
                        // 剩余时间越少越暗，形成一次衰减的闪光。
                        float strength = _flashRemaining[slot] / _flashSeconds;
                        float factor = 1f + (_flashBrightness - 1f) * strength;
                        renderer.color = CellPalette.Brighten(_baseColors[slot], factor);
                    }
                    else
                    {
                        renderer.color = _baseColors[slot];
                    }
                }
            }
        }

        // ---------------------------------------------------------------- 内部

        private void ApplyAppearance(SpriteRenderer renderer, Creature creature, Color color)
        {
            renderer.enabled = true;
            renderer.sprite = RuntimeSprite.Disc;
            renderer.color = color;

            float size = creature.Kind == SpeciesKind.Wolf ? wolfSize : rabbitSize;

            // 精灵的 PPU 等于纹理直径，所以 1 世界单位 = 直径像素。
            // 想要直径 size 个世界单位，缩放就是 size。
            renderer.transform.localScale = new Vector3(size, size, 1f);
        }

        /// <summary>
        /// 保证精灵物体数量与池容量一致。只在池扩容时才会增加，不会随生物死亡而减少。
        /// </summary>
        private void EnsureCapacity(bool force)
        {
            int needed = _simulation != null ? _simulation.Pool.Capacity : 0;
            if (needed <= 0)
            {
                return;
            }

            bool needResize = _renderers == null || (force && _renderers.Length != needed) || _renderers.Length < needed;
            if (!needResize)
            {
                return;
            }

            int oldLength = _renderers != null ? _renderers.Length : 0;

            System.Array.Resize(ref _renderers, needed);
            System.Array.Resize(ref _viewGenerations, needed);
            System.Array.Resize(ref _moveFrom, needed);
            System.Array.Resize(ref _moveProgress, needed);
            System.Array.Resize(ref _flashRemaining, needed);
            System.Array.Resize(ref _baseColors, needed);
            System.Array.Resize(ref _lastEnergy, needed);
            System.Array.Resize(ref _hasLastEnergy, needed);

            for (int i = oldLength; i < needed; i++)
            {
                _renderers[i] = null;
                _moveProgress[i] = 1f;
                _baseColors[i] = Color.white;
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null)
                {
                    _renderers[i] = CreateSpriteObject(i);
                }
            }
        }

        private SpriteRenderer CreateSpriteObject(int index)
        {
            var go = new GameObject("Creature_" + index);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = RuntimeSprite.Disc;
            renderer.drawMode = SpriteDrawMode.Simple;
            renderer.enabled = false;

            if (!string.IsNullOrEmpty(sortingLayerName))
            {
                renderer.sortingLayerName = sortingLayerName;
            }

            renderer.sortingOrder = sortingOrder;
            return renderer;
        }
    }
}
