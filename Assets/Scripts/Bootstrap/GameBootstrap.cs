using System;
using System.Text;
using UnityEngine;
using Omen.Core;
using Omen.Config;
using Omen.View;

namespace Omen.Bootstrap
{
    /// <summary>
    /// 场景唯一入口。职责只有一个：<b>装配</b>。
    /// 它自己不含任何玩法逻辑，只负责按顺序把数据、系统、视图拼起来。
    ///
    /// Phase 1.2 现状：生成岛屿数据 → 建 Tile 视图 → 让相机自动构图。
    /// Phase 1.3 会追加 GameClock / DebugHud 的装配。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("配置")]
        [Tooltip("岛屿生成配置。留空则使用代码内置的默认值（16x16 / 陆地 45% / 随机种子）。")]
        [SerializeField] private IslandConfig islandConfig;

        [Header("视图")]
        [Tooltip("网格视图。留空则自动在场景里创建（不会写回场景，只在运行时存在）。")]
        [SerializeField] private GridView gridView;

        [Tooltip("负责构图的相机。留空则自动使用 Camera.main。")]
        [SerializeField] private Camera targetCamera;

        [Tooltip("是否让相机自动构图。关掉的话相机参数完全由你手动控制。")]
        [SerializeField] private bool autoFrameCamera = true;

        [Header("调试")]
        [Tooltip("启动时把 ASCII 岛屿图打到 Console。")]
        [SerializeField] private bool logAsciiMap = true;

        [Tooltip("每隔多少秒把 ASCII 图重打一次。0 = 只打一次。")]
        [Min(0f)]
        [SerializeField] private float asciiLogInterval = 0f;

        private IslandGrid _grid;
        private IslandGenerator _generator;
        private CameraRig _cameraRig;
        private int _seed;
        private float _asciiTimer;
        private readonly StringBuilder _logBuilder = new StringBuilder(2048);

        /// <summary>当前岛屿数据。其它系统只通过这里访问网格。</summary>
        public IslandGrid Grid
        {
            get { return _grid; }
        }

        /// <summary>当前网格视图。</summary>
        public GridView View
        {
            get { return gridView; }
        }

        /// <summary>本局实际使用的随机种子（即使配置填 0 也会得到一个确定值）。</summary>
        public int Seed
        {
            get { return _seed; }
        }

        private void Awake()
        {
            InitializeGame();
        }

        private void Update()
        {
            if (logAsciiMap && asciiLogInterval > 0f && _grid != null)
            {
                _asciiTimer += Time.deltaTime;
                if (_asciiTimer >= asciiLogInterval)
                {
                    _asciiTimer -= asciiLogInterval;
                    LogAsciiMap();
                }
            }
        }

        // ---------------------------------------------------------------- 装配

        /// <summary>
        /// 生成岛屿数据并装配视图。Phase 1.3 会在这里追加时钟。
        /// </summary>
        public void InitializeGame()
        {
            IslandConfigData data = islandConfig != null ? islandConfig.ToData() : new IslandConfigData();
            data.Sanitize();

            _seed = ResolveSeed();
            _generator = new IslandGenerator(data.Width, data.Height);
            _grid = new IslandGrid(data.Width, data.Height);
            _generator.Generate(_grid, _seed, data);

            BuildGridView();
            SetupCamera(data);

            if (logAsciiMap)
            {
                LogAsciiMap();
            }
            else
            {
                Debug.Log(BuildSummary(data), this);
            }
        }

        /// <summary>
        /// 装配网格视图。优先复用场景里已挂好的组件，找不到才创建 —— 这样
        /// 你既可以直接 Play，也可以在场景里手动放一个 GridView 来微调参数。
        /// </summary>
        private void BuildGridView()
        {
            if (gridView == null)
            {
                gridView = GetComponentInChildren<GridView>(true);
            }

            if (gridView == null)
            {
                var go = new GameObject("GridView");
                go.transform.SetParent(transform, false);
                gridView = go.AddComponent<GridView>();
            }

            gridView.Build(_grid, islandConfig);
        }

        /// <summary>把网格尺寸告诉相机，让它自己算构图与像素对齐。</summary>
        private void SetupCamera(IslandConfigData data)
        {
            if (!autoFrameCamera)
            {
                return;
            }

            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[GameBootstrap] 场景里找不到相机，已跳过自动构图。", this);
                return;
            }

            _cameraRig = cam.GetComponent<CameraRig>();
            bool added = false;
            if (_cameraRig == null)
            {
                _cameraRig = cam.gameObject.AddComponent<CameraRig>();
                added = true;
            }

            // 运行时临时加的组件不会写回场景，这里明确标注，避免你以后在 Inspector 里找不到它而困惑。
            if (added)
            {
                _cameraRig.hideFlags = HideFlags.DontSave;
            }

            // 缩放参数从 IslandConfig 资产传入：CameraRig 是运行时挂上去的组件（见下），
            // 在 Inspector 里看不到它的字段，所以"能调的入口"必须留在配置资产上。
            _cameraRig.Configure(_grid, data.PixelsPerUnit, data.CameraZoom);
        }

        private int ResolveSeed()
        {
            int configured = islandConfig != null ? islandConfig.Seed : 0;
            if (configured != 0)
            {
                return configured;
            }

            // 配置为 0 时取一个随机种子，并把它显式记录下来，保证这一局可复现。
            return Environment.TickCount;
        }

        // ---------------------------------------------------------------- 调试输出

        private void LogAsciiMap()
        {
            if (_grid == null)
            {
                return;
            }

            _logBuilder.Clear();
            _logBuilder.Append("===== 《预兆》岛屿地图 · Seed ").Append(_seed).AppendLine(" =====");
            _logBuilder.Append("图例：~ 海水   . 空陆地   \" 草地   @ 生物   * 生物+草").AppendLine();
            _logBuilder.AppendLine();
            _logBuilder.Append(_grid.ToAsciiMap());
            _logBuilder.AppendLine();
            _logBuilder.Append("陆地 ").Append(_grid.LandCount).Append('/').Append(_grid.CellCount)
                .Append(" 格（实际占比 ").Append((_grid.LandCount * 100f / _grid.CellCount).ToString("F1")).Append("%）")
                .Append("　孤岛裁掉 ").Append(_generator.LastTrimmedCellCount).Append(" 格")
                .Append("　阈值 ").Append(_generator.LastThreshold.ToString("F4"));

            Debug.Log(_logBuilder.ToString(), this);
        }

        private string BuildSummary(IslandConfigData data)
        {
            return "《预兆》岛屿已生成：Seed " + _seed
                + "，尺寸 " + data.Width + "x" + data.Height
                + "，陆地 " + _grid.LandCount + " 格（目标占比 " + (data.LandRatio * 100f).ToString("F0") + "%）"
                + "，孤岛裁掉 " + _generator.LastTrimmedCellCount + " 格。";
        }

        // ---------------------------------------------------------------- 上下文菜单（便于在编辑器里快速自测）

        /// <summary>换一张岛。运行中点击可立刻重新生成（依赖 GridView 检测到版本号变小后全量刷新）。</summary>
        [ContextMenu("Regenerate Island (Replace)")]
        private void RegenerateIsland()
        {
            InitializeGame();
        }

        /// <summary>用同一个种子重建，用于验证生成是确定性的。</summary>
        [ContextMenu("Regenerate Island (Same Seed)")]
        private void RegenerateSameSeed()
        {
            if (_grid == null || _generator == null)
            {
                InitializeGame();
                return;
            }

            IslandConfigData data = islandConfig != null ? islandConfig.ToData() : new IslandConfigData();
            data.Sanitize();

            _generator.Generate(_grid, _seed, data);
            if (gridView != null)
            {
                gridView.Synchronize();
            }

            Debug.Log("已用同一种子 " + _seed + " 重建岛屿，画面应与之前完全一致。", this);
        }
    }
}
