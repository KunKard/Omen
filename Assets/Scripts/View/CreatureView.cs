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
    ///   - 哪个物体代表哪只生物，由"槽位 + 代际"决定：池的槽位被复用时代际号会变，
    ///     于是本类能立刻发现"这个物体该换主人了"，而不是把死亡生物的精灵留在原地
    ///   - 画不画由 <c>SpriteRenderer.enabled</c> 控制，比自己增删 GameObject 便宜得多
    ///
    /// 刷新时机：只在池版本号变化时同步（即发生了一次 tick），所以静止时开销为零。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CreatureView : MonoBehaviour
    {
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

        [Header("状态（运行时只读）")]
        [Tooltip("已创建的精灵物体数量。")]
        [SerializeField] private int spriteCapacity;

        private EcosystemSimulation _simulation;
        private CellPalette _palette;
        private SpriteRenderer[] _renderers;
        private uint[] _viewGenerations;
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

        /// <summary>绑定模拟与配色。由 <c>GameBootstrap</c> 在装配时调用。</summary>
        public void Bind(EcosystemSimulation simulation, Config.IslandConfig config)
        {
            _simulation = simulation;
            _palette = CellPalette.FromConfig(config != null ? config.ToData() : null);
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
                bool alive = slot < pool.Capacity && pool.IsSlotAlive(slot);
                SpriteRenderer renderer = _renderers[slot];

                if (!alive)
                {
                    if (renderer != null && renderer.enabled)
                    {
                        renderer.enabled = false;
                    }

                    _viewGenerations[slot] = 0u;
                    continue;
                }

                Creature creature = pool.GetSlot(slot);
                uint generation = creature.Generation;

                // 槽位还是同一只生物 → 只可能移动了位置，直接搬。
                // 代际号变了 → 说明这只生物是新生的（复用了死亡生物的槽位），需要换颜色与大小。
                bool sameOccupant = _viewGenerations[slot] == generation;
                _viewGenerations[slot] = generation;

                if (renderer == null || !renderer.enabled || !sameOccupant)
                {
                    ApplyAppearance(renderer, creature);
                }

                renderer.transform.localPosition = WorldGrid.CellCenter(creature.Position);

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
            _palette = CellPalette.FromConfig(config != null ? config.ToData() : null);
            _viewGenerations = null;   // 强制全部重刷
            _syncedVersion = -1;
            EnsureCapacity(true);
            Synchronize();
        }

        private void Update()
        {
            Synchronize();
        }

        // ---------------------------------------------------------------- 内部

        private void ApplyAppearance(SpriteRenderer renderer, Creature creature)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.enabled = true;
            renderer.sprite = RuntimeSprite.Disc;
            renderer.color = _palette.ForSpecies(creature.Kind);

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

            if (!force && _renderers != null && _renderers.Length >= needed)
            {
                return;
            }

            if (_renderers == null)
            {
                _renderers = new SpriteRenderer[needed];
                _viewGenerations = new uint[needed];
            }
            else if (_renderers.Length < needed)
            {
                int oldLength = _renderers.Length;
                System.Array.Resize(ref _renderers, needed);
                System.Array.Resize(ref _viewGenerations, needed);
                for (int i = oldLength; i < needed; i++)
                {
                    _renderers[i] = null;
                }
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null)
                {
                    _renderers[i] = CreateSpriteObject(i);
                }
            }

            spriteCapacity = _renderers.Length;
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
