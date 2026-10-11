using System.Text;
using UnityEngine;
using Omen.Core;
using Omen.Systems;

namespace Omen.Debugging
{
    /// <summary>
    /// 屏上调试面板。用 OnGUI 实现 —— Phase 1 阶段这是最省事的选择：
    /// 零依赖、零预制体、不需要 Canvas，改一行就能加一项。
    /// Phase 5 做正式结算界面时会换成 uGUI，但调试面板可以一直留着（发布时关掉即可）。
    ///
    /// 架构上它属于只读消费者：只读数据、不写任何状态，因此不会污染玩法逻辑。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugHud : MonoBehaviour
    {
        [Tooltip("是否显示 HUD。发布时关掉。")]
        [SerializeField] private bool visible = true;

        [Tooltip("文字缩放。像素风建议保持整数，避免字体发虚。")]
        [Range(1, 4)]
        [SerializeField] private int fontScale = 2;

        [Tooltip("距离屏幕左上角的边距（像素）。")]
        [SerializeField] private int margin = 12;

        [Header("数据源（由 GameBootstrap 注入）")]
        [SerializeField] private Bootstrap.GameBootstrap gameBootstrap;
        [SerializeField] private GameClock gameClock;

        private GUIStyle _textStyle;
        private Texture2D _panelTexture;
        private float _fpsSmoothed;
        private float _fpsTimer;
        private int _fpsFrames;
        private readonly StringBuilder _builder = new StringBuilder(512);

        /// <summary>显示/隐藏 HUD。</summary>
        public bool Visible
        {
            get { return visible; }
            set { visible = value; }
        }

        /// <summary>文字缩放。</summary>
        public int FontScale
        {
            get { return fontScale; }
            set { fontScale = Mathf.Clamp(value, 1, 4); }
        }

        /// <summary>由 <c>GameBootstrap</c> 在装配时调用。</summary>
        public void Bind(Bootstrap.GameBootstrap bootstrap, GameClock clock)
        {
            gameBootstrap = bootstrap;
            gameClock = clock;
        }

        public void Toggle()
        {
            visible = !visible;
        }

        private void OnDestroy()
        {
            // 运行时创建的贴图必须自己回收，否则每次重载场景都会泄漏一张。
            if (_panelTexture != null)
            {
                Destroy(_panelTexture);
                _panelTexture = null;
            }
        }

        // ---------------------------------------------------------------- 采样

        private void Update()
        {
            // FPS 每 0.5 秒统计一次。逐帧算 FPS 会让数字乱跳，读者根本看不清。
            _fpsTimer += Time.unscaledDeltaTime;
            _fpsFrames++;

            if (_fpsTimer >= 0.5f)
            {
                _fpsSmoothed = _fpsFrames / _fpsTimer;
                _fpsTimer = 0f;
                _fpsFrames = 0;
            }
        }

        // ---------------------------------------------------------------- 绘制

        private void OnGUI()
        {
            if (!visible)
            {
                return;
            }

            EnsureStyles();
            string text = BuildText();

            Vector2 size = _textStyle.CalcSize(new GUIContent(text));
            var rect = new Rect(margin, margin, size.x + 16f, size.y + 16f);

            GUI.DrawTexture(rect, _panelTexture);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 8f, size.x, size.y), text, _textStyle);
        }

        private string BuildText()
        {
            _builder.Clear();

            _builder.Append("FPS ").Append(_fpsSmoothed.ToString("F0"))
                .Append("   屏幕 ").Append(Screen.width).Append('x').Append(Screen.height)
                .AppendLine();

            AppendClock();
            AppendGrid();
            AppendCamera();
            AppendSpecies();

            return _builder.ToString();
        }

        private void AppendClock()
        {
            if (gameClock == null)
            {
                _builder.Append("时钟 未接入").AppendLine();
                return;
            }

            string state = gameClock.IsFinished ? "已结束" : (gameClock.IsRunning ? "运行中" : "已暂停");

            _builder.Append("Day ").Append(gameClock.Day).Append(" / ").Append(gameClock.TotalDays)
                .Append("   Tick ").Append(gameClock.TickInDay).Append(" / ").Append(gameClock.TicksPerDay)
                .Append("   [").Append(state).Append(']');

            if (!Mathf.Approximately(gameClock.TimeScale, 1f))
            {
                _builder.Append("   时间x").Append(gameClock.TimeScale.ToString("F2"));
            }

            _builder.AppendLine();

            _builder.Append("距离下一天 ").Append(gameClock.SecondsUntilNextDay.ToString("F1")).Append(" 秒")
                .Append("   累计 tick ").Append(gameClock.TotalTickCount)
                .AppendLine();

            _builder.Append("Day 进度 ").Append(gameClock.DayProgress.ToString("P0")).AppendLine();
        }

        private void AppendGrid()
        {
            IslandGrid grid = gameBootstrap != null ? gameBootstrap.Grid : null;
            if (grid == null)
            {
                _builder.Append("网格 未生成").AppendLine();
                return;
            }

            _builder.Append("网格 ").Append(grid.Width).Append('x').Append(grid.Height)
                .Append("   陆地 ").Append(grid.LandCount).Append('/').Append(grid.CellCount)
                .Append(" (").Append((grid.LandCount * 100f / grid.CellCount).ToString("F1")).Append("%)")
                .AppendLine();

            _builder.Append("草地 ").Append(grid.GrassCount)
                .Append("   地面生物 ").Append(grid.CountOccupiedCells())
                .Append("   Seed ").Append(gameBootstrap.Seed)
                .AppendLine();        }

        private void AppendCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                _builder.Append("相机 未找到").AppendLine();
                return;
            }

            _builder.Append("相机 正交尺寸 ").Append(cam.orthographicSize.ToString("F2"));

            var rig = cam.GetComponent<View.CameraRig>();
            if (rig != null)
            {
                // 实际 PPU 是判断"缩放有没有被屏幕限制降档"的关键指标。
                _builder.Append("   PPU ").Append(rig.EffectivePixelsPerUnit.ToString("F0"))
                    .Append("   Zoom ").Append(rig.Zoom);
            }

            _builder.AppendLine();
        }

        private void AppendSpecies()
        {
            EcosystemSimulation sim = gameBootstrap != null ? gameBootstrap.Simulation : null;
            if (sim == null)
            {
                _builder.Append("生态 未接入").AppendLine();
                return;
            }

            EcosystemStats stats = sim.Stats;

            _builder.Append("兔 ").Append(stats.RabbitCount)
                .Append("   狼 ").Append(stats.WolfCount)
                .Append("   草 ").Append(stats.GrassCells).Append('/').Append(stats.LandCells)
                .Append(" (").Append((stats.GrassCoverage * 100f).ToString("F1")).Append("%)")
                .AppendLine();

            // 存活判定预览：Phase 5 的结算就是照这三条判的，提前显示便于观察。
            _builder.Append("存活判定  兔 ").Append(Mark(stats.RabbitCount > 0))
                .Append("   狼 ").Append(Mark(stats.WolfCount > 0))
                .Append("   草 ").Append(Mark(stats.GrassCoverage >= 0.5f))
                .AppendLine();

            _builder.Append("本 tick   兔 +").Append(stats.RabbitBirths).Append(" -").Append(stats.RabbitDeaths)
                .Append("   狼 +").Append(stats.WolfBirths).Append(" -").Append(stats.WolfDeaths)
                .Append("   草被吃 ").Append(stats.GrassEaten)
                .AppendLine();
        }

        private static string Mark(bool alive)
        {
            return alive ? "存活" : "灭绝";
        }

        // ---------------------------------------------------------------- 样式

        private void EnsureStyles()
        {
            if (_panelTexture == null)
            {
                _panelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    name = "OmenHudPanel",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };

                _panelTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.62f));
                _panelTexture.Apply(false, false);
            }

            int fontSize = 8 * fontScale;
            if (_textStyle == null || _textStyle.fontSize != fontSize)
            {
                _textStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = fontSize,
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = false,
                    richText = false,
                    padding = new RectOffset(0, 0, 0, 0),
                };

                _textStyle.normal.textColor = Color.white;
            }
        }
    }
}
