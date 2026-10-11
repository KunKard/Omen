using System;
using System.Text;
using UnityEngine;
using Omen.Core;
using Omen.Config;

namespace Omen.Bootstrap
{
    /// <summary>
    /// 场景唯一入口。职责只有一个：<b>装配</b>。
    /// 它自己不含任何玩法逻辑，只负责按顺序把数据、系统、视图拼起来。
    ///
    /// Phase 1.1 现状：只生成岛屿数据并把 ASCII 图打到 Console，还没有任何画面。
    /// Phase 1.2 会在这里追加 GridView / CameraRig 的装配；Phase 1.3 追加 GameClock / DebugHud。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("配置")]
        [Tooltip("岛屿生成配置。留空则使用代码内置的默认值（16x16 / 陆地 45% / 随机种子）。")]
        [SerializeField] private IslandConfig islandConfig;

        [Header("调试")]
        [Tooltip("启动时把 ASCII 岛屿图打到 Console。Phase 1.1 的主要验收手段。")]
        [SerializeField] private bool logAsciiMap = true;

        [Tooltip("每隔多少秒把 ASCII 图重打一次。0 = 只打一次。")]
        [Min(0f)]
        [SerializeField] private float asciiLogInterval = 0f;

        private IslandGrid _grid;
        private IslandGenerator _generator;
        private int _seed;
        private float _asciiTimer;
        private readonly StringBuilder _logBuilder = new StringBuilder(2048);

        /// <summary>当前岛屿数据。其它系统只通过这里访问网格。</summary>
        public IslandGrid Grid
        {
            get { return _grid; }
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
        /// 生成岛屿数据。Phase 1.3 会在这里追加视图与时钟的初始化。
        /// </summary>
        public void InitializeGame()
        {
            IslandConfigData data = islandConfig != null ? islandConfig.ToData() : new IslandConfigData();
            data.Sanitize();

            _seed = ResolveSeed();
            _generator = new IslandGenerator(data.Width, data.Height);
            _grid = new IslandGrid(data.Width, data.Height);
            _generator.Generate(_grid, _seed, data);

            if (logAsciiMap)
            {
                LogAsciiMap();
            }
            else
            {
                Debug.Log(BuildSummary(data), this);
            }
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

        /// <summary>在 Inspector 的组件右上角菜单里点 "Regenerate Island" 即可换一张图。</summary>
        [ContextMenu("Regenerate Island")]
        private void RegenerateIsland()
        {
            InitializeGame();
        }
    }
}
