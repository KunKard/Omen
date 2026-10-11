using UnityEngine;
using Omen.Core;

namespace Omen.View
{
    /// <summary>
    /// 相机构图：自动把整张网格框进画面，并做像素完美对齐。
    ///
    /// 为什么要自己算像素对齐：本项目<b>没有安装</b> com.unity.2d.pixel-perfect 包
    /// （见 Decision Log D3），而像素风一旦出现非整数倍缩放，方块边缘就会糊掉。
    /// 对齐只需两件事：
    ///   1) <c>orthographicSize = 屏幕像素高 / (2 × PPU)</c>，让 1 个世界单位正好等于 PPU 个屏幕像素
    ///   2) 相机位置取整到"一个屏幕像素对应的世界尺寸"的整数倍，避免半像素抖动
    /// 如果屏幕尺寸导致 1 单位对不齐整数像素（例如 1080 / 9 = 120 可以，但某些分辨率不行），
    /// 就把 PPU 向下取到能整除的最大整数，宁可画面略小也不要糊。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        [Header("目标")]
        [Tooltip("要框住的世界宽度（单位）。16x16 的地图填 16。")]
        [SerializeField] private float worldWidth = 16f;

        [Tooltip("要框住的世界高度（单位）。")]
        [SerializeField] private float worldHeight = 16f;

        [Tooltip("构图中心的世界坐标。")]
        [SerializeField] private Vector2 worldCenter = new Vector2(8f, 8f);

        [Header("构图")]
        [Tooltip("四周留白比例。0.06 = 上下各留 6% 的余量。")]
        [Range(0f, 0.5f)]
        [SerializeField] private float padding = 0.06f;

        [Header("像素对齐")]
        [Tooltip("期望的每单位像素数（像素风基准）。")]
        [Min(1)]
        [SerializeField] private int targetPixelsPerUnit = 16;

        [Tooltip("是否强制 1 个世界单位 = 整数个屏幕像素。像素风建议保持开启。")]
        [SerializeField] private bool snapToIntegerPixels = true;

        [Tooltip("是否把相机位置对齐到整像素。")]
        [SerializeField] private bool snapCameraPosition = true;

        [Tooltip("是否把放大倍数限制在 PixelsPerUnit 以内。\n" +
                 "开启：1 格 = 恰好 PixelsPerUnit 个屏幕像素（像素绝对精确，但在 1080p 下岛只占屏幕高度约一半）。\n" +
                 "关闭：岛尽量铺满屏幕（更醒目），缩放倍数可能不是整数，边缘会有极轻微插值。")]
        [SerializeField] private bool limitZoomToPixelsPerUnit = true;

        private Camera _camera;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private bool _configured;

        /// <summary>当前实际生效的每单位像素数（可能因整数对齐而小于配置值）。</summary>
        public float EffectivePixelsPerUnit { get; private set; }

        /// <summary>由 <c>GameBootstrap</c> 在装配时调用，把网格尺寸告诉相机。</summary>
        public void Configure(IslandGrid grid, int pixelsPerUnit)
        {
            if (grid != null)
            {
                Vector2 size = WorldGrid.MapSize(grid.Width, grid.Height);
                worldWidth = size.x;
                worldHeight = size.y;
                worldCenter = WorldGrid.MapCenter(grid.Width, grid.Height);
            }

            if (pixelsPerUnit > 0)
            {
                targetPixelsPerUnit = pixelsPerUnit;
            }

            _configured = true;
            Apply();
        }

        private void Awake()
        {
            // 这里刻意不调用 Apply()：GameBootstrap 可能在同一个 Awake 里紧接着调用 Configure()，
            // 而 Configure() 会补上真实的网格尺寸。等第一次 LateUpdate 再算，避免用默认尺寸白算一遍。
            _camera = GetComponent<Camera>();
        }

        private void LateUpdate()
        {
            if (!_configured)
            {
                return;
            }

            // 只在分辨率变化时重算，避免每帧无谓的浮点运算。
            if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight)
            {
                Apply();
            }
        }

        /// <summary>按当前参数重算正交尺寸与相机位置。</summary>
        public void Apply()
        {
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
                if (_camera == null)
                {
                    return;
                }
            }

            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;

            // 像素风的相机必须是正交投影，否则透视会让远处的格子变小。
            _camera.orthographic = true;

            float screenHeight = Mathf.Max(1f, Screen.height);
            float screenWidth = Mathf.Max(1f, Screen.width);
            float configuredPpu = Mathf.Max(1f, targetPixelsPerUnit);

            // 相机最多能放大到多少 PPU 还能同时装下宽和高：
            //   高度限制 = 屏幕像素高 / 需要容纳的世界高度
            //   宽度限制 = 屏幕像素宽 / 需要容纳的世界宽度
            // 两者取小。再向下取整，保证 1 个世界单位恰好等于整数个屏幕像素 —— 这是像素不糊的关键。
            float needHalfHeight = worldHeight * 0.5f * (1f + padding);
            float needHalfWidth = worldWidth * 0.5f * (1f + padding);

            float maxPpu = Mathf.Min(screenHeight / (needHalfHeight * 2f), screenWidth / (needHalfWidth * 2f));
            if (snapToIntegerPixels)
            {
                maxPpu = Mathf.Floor(maxPpu);
            }

            // 实际 PPU 上限：开启 limitZoomToPixelsPerUnit 时不允许超过配置值，
            // 这样 1 格永远等于整数个屏幕像素；关掉则放手让岛铺满屏幕。
            float upperBound = limitZoomToPixelsPerUnit ? configuredPpu : maxPpu;
            float effectivePpu = Mathf.Max(1f, Mathf.Min(maxPpu, upperBound));

            // 正交尺寸 = 画面半高（世界单位） = (屏幕像素高 / 2) / 每单位像素数
            _camera.orthographicSize = screenHeight / (2f * effectivePpu);
            EffectivePixelsPerUnit = effectivePpu;

            Vector3 position = new Vector3(worldCenter.x, worldCenter.y, -10f);
            if (snapCameraPosition)
            {
                float worldPerPixel = 1f / Mathf.Max(0.001f, EffectivePixelsPerUnit);
                position.x = Mathf.Round(position.x / worldPerPixel) * worldPerPixel;
                position.y = Mathf.Round(position.y / worldPerPixel) * worldPerPixel;
            }

            position.z = -10f;
            transform.position = position;
        }
    }
}
