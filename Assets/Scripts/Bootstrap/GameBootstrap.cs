using System;
using System.Text;
using UnityEngine;
using Omen.Core;
using Omen.Config;
using Omen.Systems;
using Omen.View;

namespace Omen.Bootstrap
{
    /// <summary>
    /// 场景唯一入口。职责只有一个：<b>装配</b>。
    /// 它自己不含任何玩法逻辑，只负责按顺序把数据、系统、视图拼起来。
    ///
    /// Phase 1 现状：生成岛屿数据 → 建 Tile 视图 → 让相机自动构图 → 启动时钟 → 挂调试 HUD。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("配置")]
        [Tooltip("岛屿生成配置。留空则使用代码内置的默认值（16x16 / 陆地 45% / 随机种子）。")]
        [SerializeField] private IslandConfig islandConfig;

        [Tooltip("时钟配置。留空则使用代码内置的默认值（15 天 / 12 秒一天 / 1 秒一 tick）。")]
        [SerializeField] private GameClockConfig clockConfig;

        [Header("视图")]
        [Tooltip("网格视图。留空则自动在场景里创建（运行时物体，不写回场景）。")]
        [SerializeField] private GridView gridView;

        [Tooltip("生物视图（兔与狼的小圆点）。留空则自动创建（运行时物体，不写回场景）。")]
        [SerializeField] private CreatureView creatureView;

        [Tooltip("负责构图的相机。留空则自动使用 Camera.main。")]
        [SerializeField] private Camera targetCamera;

        [Tooltip("是否让相机自动构图。关掉的话相机参数完全由你手动控制。")]
        [SerializeField] private bool autoFrameCamera = true;

        [Header("系统")]
        [Tooltip("半实时时钟。留空则自动创建（运行时物体，不写回场景）。")]
        [SerializeField] private GameClock gameClock;

        [Tooltip("生态系统配置。留空则使用代码内置的默认参数。")]
        [SerializeField] private EcosystemConfig ecosystemConfig;

        [Tooltip("屏上调试面板。留空则自动创建（运行时物体，不写回场景）。")]
        [SerializeField] private Debugging.DebugHud debugHud;

        [Tooltip("启动时把 ASCII 岛屿图打到 Console。")]
        [SerializeField] private bool logAsciiMap = true;

        [Tooltip("每隔多少秒把 ASCII 图重打一次。0 = 只打一次。")]
        [Min(0f)]
        [SerializeField] private float asciiLogInterval = 0f;

        [Header("热键")]
        [Tooltip("启用调试热键。发布时关掉。")]
        [SerializeField] private bool enableHotkeys = true;

        private IslandGrid _grid;
        private IslandGenerator _generator;
        private CameraRig _cameraRig;
        private EcosystemSimulation _simulation;
        private int _seed;
        private int _seedOverride;
        private bool _hasSeedOverride;
        private float _asciiTimer;
        private readonly StringBuilder _logBuilder = new StringBuilder(2048);

        /// <summary>当前岛屿数据。其它系统只通过这里访问网格。</summary>
        public IslandGrid Grid
        {
            get { return _grid; }
        }

        /// <summary>生态系统模拟。视图与 HUD 只读它。</summary>
        public EcosystemSimulation Simulation
        {
            get { return _simulation; }
        }

        /// <summary>当前网格视图。</summary>
        public GridView View
        {
            get { return gridView; }
        }

        /// <summary>半实时时钟。</summary>
        public GameClock Clock
        {
            get { return gameClock; }
        }

        /// <summary>本局实际使用的随机种子（即使配置填 0 也会得到一个确定值）。</summary>
        public int Seed
        {
            get { return _seed; }
        }

        private void Awake()
        {
            InitializeGame();

            if (gameClock != null)
            {
                gameClock.TickAdvanced += HandleTick;
            }
        }

        private void OnDestroy()
        {
            if (gameClock != null)
            {
                gameClock.TickAdvanced -= HandleTick;
            }
        }

        /// <summary>
        /// 时钟推进一次 = 生态前进一步。
        /// 模拟与时钟是解耦的：这里只做"接线"，节奏完全由时钟决定。
        /// 以后 Phase 3 的预兆系统也挂在同一个事件上。
        /// </summary>
        private void HandleTick(int tickIndex)
        {
            if (_simulation != null)
            {
                _simulation.TickOnce();
            }
        }

        private void Update()
        {
            if (enableHotkeys)
            {
                HandleHotkeys();
            }

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

        /// <summary>
        /// 调试热键。放在这里而不是各个系统里，是为了让"有哪些调试开关"一眼可见。
        /// 输入读取只在这一处发生。
        /// </summary>
        private void HandleHotkeys()
        {
            // 换一张岛：换新种子并从头开始
            if (Input.GetKeyDown(KeyCode.N))
            {
                Debug.Log("[热键] N：换新种子重建岛屿。", this);
                RebuildIsland(Environment.TickCount);
            }

            // 同一个种子重建：用于验证生成是确定性的
            if (Input.GetKeyDown(KeyCode.R))
            {
                Debug.Log("[热键] R：用同一种子重建岛屿（画面应与之前完全一致）。", this);
                RebuildIsland(_seed);
            }

            // 暂停 / 继续
            if (Input.GetKeyDown(KeyCode.Space) && gameClock != null && !gameClock.IsFinished)
            {
                gameClock.TogglePause();
            }

            // 显示 / 隐藏 HUD
            if (Input.GetKeyDown(KeyCode.F1) && debugHud != null)
            {
                debugHud.Toggle();
            }
        }

        // ---------------------------------------------------------------- 装配

        /// <summary>
        /// 生成岛屿数据并装配全部系统与视图。
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
            SetupEcosystem();
            SetupCreatureView();
            SetupClock();
            SetupHud();

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
        /// 装配时钟。换岛重建时调 Reset() 把天数拨回第 1 天 —— 否则重建地形后
        /// 时钟还停在第 7 天，观察到的状态和历史对不上，调试时会非常困惑。
        /// </summary>
        /// <summary>
        /// 建立生态系统：清空上一次的一切，按配置重新投放初始种群。
        /// 生态的随机种子由岛屿种子派生，于是"同一张岛 + 同一个种子 ⇒ 完全相同的演化过程"，
        /// 换岛则会得到不同的开局分布。
        /// </summary>
        private void SetupEcosystem()
        {
            EcosystemConfigData data = ecosystemConfig != null
                ? ecosystemConfig.ToData()
                : new EcosystemConfigData();

            // 派生种子：让生态演化与岛屿布局一一对应，同时保留可复现性。
            // 乘一个奇数再异或，避免"岛屿种子相近 → 生态开局也相近"。
            // 派生种子：让生态演化与岛屿布局一一对应，同时保留可复现性。
            // 乘一个奇数再加偏移，避免"岛屿种子相近 → 生态开局也相近"。
            // 注意：2654435761 超过 int.MaxValue，必须显式转成 int；乘法溢出是刻意的混淆效果。
            data.Seed = unchecked(_seed * (int)2654435761u + 101);
            data.Sanitize();

            _simulation = new EcosystemSimulation(_grid, data);
            _simulation.Reset(data.Seed);
        }

        private void SetupCreatureView()
        {
            if (creatureView == null)
            {
                creatureView = GetComponentInChildren<CreatureView>(true);
            }

            if (creatureView == null)
            {
                var go = new GameObject("CreatureView");
                go.transform.SetParent(transform, false);
                creatureView = go.AddComponent<CreatureView>();
            }

            creatureView.Bind(_simulation, islandConfig);
        }

        private void SetupClock()
        {
            if (gameClock == null)
            {
                gameClock = GetComponentInChildren<GameClock>(true);
            }

            if (gameClock == null)
            {
                var go = new GameObject("GameClock");
                go.transform.SetParent(transform, false);
                gameClock = go.AddComponent<GameClock>();
                gameClock.hideFlags = HideFlags.DontSave;
            }

            gameClock.Reset();
        }

        private void SetupHud()
        {
            if (debugHud == null)
            {
                debugHud = GetComponentInChildren<Debugging.DebugHud>(true);
            }

            if (debugHud == null)
            {
                var go = new GameObject("DebugHud");
                go.transform.SetParent(transform, false);
                debugHud = go.AddComponent<Debugging.DebugHud>();
                debugHud.hideFlags = HideFlags.DontSave;
            }

            debugHud.Bind(this, gameClock);
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
            // 热键要求"换种子"或"沿用一个种子"时，优先听热键的。
            if (_hasSeedOverride)
            {
                return _seedOverride;
            }

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

        /// <summary>换一张岛：取新种子并从头开始。</summary>
        [ContextMenu("Regenerate Island (New Seed)")]
        private void RegenerateWithNewSeed()
        {
            RebuildIsland(Environment.TickCount);
        }

        /// <summary>用同一个种子重建，用于验证生成是确定性的。</summary>
        [ContextMenu("Regenerate Island (Same Seed)")]
        private void RegenerateSameSeed()
        {
            RebuildIsland(_seed);
        }

        /// <summary>
        /// 用指定种子重建整局（岛屿 + 时钟）。
        /// 热键与右键菜单共用这一条路径，避免两处行为不一致。
        /// </summary>
        private void RebuildIsland(int seed)
        {
            _seedOverride = seed;
            _hasSeedOverride = true;
            InitializeGame();
        }
    }
}
