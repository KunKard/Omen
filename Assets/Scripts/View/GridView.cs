using UnityEngine;
using Omen.Core;

namespace Omen.View
{
    /// <summary>
    /// 网格视图：把 <see cref="IslandGrid"/> 的状态投影成 256 个彩色方块。
    ///
    /// 架构铁律 1 &amp; 2：本类<b>只读</b>网格，绝不写它；反过来玩法逻辑也绝不查询本类。
    ///
    /// 性能策略（对应"避免每帧 GC"的要求）：
    ///   - Tile 只在 <see cref="Build"/> 时创建一次，之后长期复用，不做 Instantiate/Destroy
    ///   - 同步时只处理网格标记出来的"脏格子"，空转时每帧开销约为零
    ///   - 真正的刷新在 <see cref="TileView.Apply"/> 里还有一层颜色比对，重复脏标记不会造成无谓的赋值
    ///
    /// 装配由 <c>GameBootstrap</c> 负责，它会把长期存在的物体缓存下来；
    /// 本类带 <see cref="Build"/> 兜底，所以单独挂在场景里直接 Play 也能工作。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GridView : MonoBehaviour
    {
        [Header("数据")]
        [Tooltip("岛屿数据源。由 GameBootstrap 在运行时注入。")]
        [SerializeField] private IslandGrid grid;

        [Tooltip("配色配置。留空则使用内置默认配色。")]
        [SerializeField] private Config.IslandConfig islandConfig;

        [Header("渲染")]
        [Tooltip("Tile 使用的 Sorting Layer。留空表示用 Default。")]
        [SerializeField] private string sortingLayerName = "";

        [Tooltip("Tile 的 Sorting Order。")]
        [SerializeField] private int sortingOrder = 0;

        private TileView[] _tiles;
        private CellPalette _palette;
        private int _lastVersion = -1;
        private IslandGrid _builtFor;

        /// <summary>当前绑定的网格数据（只读用途）。</summary>
        public IslandGrid Grid
        {
            get { return grid; }
        }

        /// <summary>当前使用的配色。</summary>
        public CellPalette Palette
        {
            get { return _palette; }
        }

        /// <summary>已创建的 Tile 数量。</summary>
        public int TileCount
        {
            get { return _tiles != null ? _tiles.Length : 0; }
        }

        /// <summary>
        /// 绑定数据源并创建全部 Tile。
        /// 重复用同一张网格调用是安全的，不会重复创建物体。
        /// </summary>
        public void Build(IslandGrid targetGrid, Config.IslandConfig config)
        {
            if (targetGrid == null)
            {
                Debug.LogError("[GridView] 传入的网格为 null，无法构建视图。", this);
                return;
            }

            grid = targetGrid;
            islandConfig = config;
            _palette = CellPalette.FromConfig(config != null ? config.ToData() : null);

            if (_tiles == null || _tiles.Length != grid.CellCount || _builtFor != grid)
            {
                if (_tiles != null && _builtFor != grid)
                {
                    // 换了一张网格对象（例如换种子重建）时，上一次的 Tile 还挂在场景里。
                    // 不清理的话两套方块会重叠在一起，画面看起来像"没刷新"。
                    DestroyExistingTileObjects();
                }

                CreateTiles();
            }

            _lastVersion = -1;
            _builtFor = grid;
            Synchronize();
        }

        /// <summary>每帧同步。没有脏格子时几乎零开销。</summary>
        public void Synchronize()
        {
            if (grid == null || _tiles == null)
            {
                return;
            }

            // 版本号变小 ⇒ 网格被整体重建过 ⇒ 全量刷新。
            if (grid.Version < _lastVersion)
            {
                for (int i = 0; i < _tiles.Length; i++)
                {
                    RefreshTile(i);
                }
            }
            else
            {
                int dirtyCount = grid.DirtyCount;
                for (int i = 0; i < dirtyCount; i++)
                {
                    RefreshTile(grid.GetDirtyIndex(i));
                }
            }

            _lastVersion = grid.Version;
            grid.ClearDirty();
        }

        /// <summary>换配色后立即刷新整张图。</summary>
        public void ApplyPalette(Config.IslandConfig config)
        {
            islandConfig = config;
            _palette = CellPalette.FromConfig(config != null ? config.ToData() : null);

            if (_tiles == null)
            {
                return;
            }

            for (int i = 0; i < _tiles.Length; i++)
            {
                RefreshTile(i);
            }
        }

        private void Update()
        {
            Synchronize();
        }

        private void OnValidate()
        {
            if (_tiles == null)
            {
                return;
            }

            for (int i = 0; i < _tiles.Length; i++)
            {
                if (_tiles[i] == null)
                {
                    continue;
                }

                var renderer = _tiles[i].GetComponent<SpriteRenderer>();
                if (renderer == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(sortingLayerName))
                {
                    renderer.sortingLayerName = sortingLayerName;
                }

                renderer.sortingOrder = sortingOrder;
            }
        }

        private void OnDestroy()
        {
            _tiles = null;
            _builtFor = null;
        }

        // ---------------------------------------------------------------- 内部

        /// <summary>销毁上一次构建出来的所有 Tile 物体。只在换网格对象时调用。</summary>
        private void DestroyExistingTileObjects()
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                if (_tiles[i] == null)
                {
                    continue;
                }

                GameObject go = _tiles[i].gameObject;
                if (Application.isPlaying)
                {
                    Destroy(go);
                }
                else
                {
                    DestroyImmediate(go);
                }
            }

            _tiles = null;
        }

        private void CreateTiles()
        {
            // 已经把 Tile 挂成场景子物体（方便手动微调）时，直接复用它们。
            if (_tiles == null || _tiles.Length == 0)
            {
                var existing = GetComponentsInChildren<TileView>(true);
                if (existing != null && existing.Length == grid.CellCount)
                {
                    _tiles = existing;
                    Debug.Log("[GridView] 复用了场景里已有的 " + existing.Length + " 个 TileView。", this);
                    return;
                }
            }

            _tiles = new TileView[grid.CellCount];

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    int index = GridUtility.ToIndex(x, y, grid.Width);
                    var pos = new GridPos(x, y);

                    var go = new GameObject("Tile_" + x + "_" + y);
                    go.transform.SetParent(transform, false);
                    go.transform.position = WorldGrid.CellCenter(pos);

                    var renderer = go.AddComponent<SpriteRenderer>();
                    renderer.sprite = RuntimeSprite.Shared;
                    renderer.drawMode = SpriteDrawMode.Simple;

                    if (!string.IsNullOrEmpty(sortingLayerName))
                    {
                        renderer.sortingLayerName = sortingLayerName;
                    }

                    renderer.sortingOrder = sortingOrder;

                    var tile = go.AddComponent<TileView>();
                    tile.Initialize(pos, renderer, TileView.ResolveColor(grid.Get(x, y), _palette));

                    _tiles[index] = tile;
                }
            }
        }

        private void RefreshTile(int index)
        {
            if (index < 0 || index >= _tiles.Length)
            {
                return;
            }

            TileView tile = _tiles[index];
            if (tile == null)
            {
                return;
            }

            int x = index % grid.Width;
            int y = index / grid.Width;
            tile.Apply(grid.Get(x, y), _palette);
        }
    }
}
