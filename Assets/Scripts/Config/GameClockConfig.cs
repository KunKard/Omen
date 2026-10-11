using System;
using UnityEngine;
using Omen.Core;

namespace Omen.Config
{
    /// <summary>
    /// 时钟配置资产 —— 在 Inspector 里调节奏，运行时转成纯 C# 的 <see cref="GameClockData"/>。
    ///
    /// 创建方式：Project 窗口右键 → Create → Omen → Game Clock Config
    /// 建议路径：Assets/Config/GameClockConfig.asset
    ///
    /// 为什么时间参数要资产化：一局的总时长直接决定评审的试玩体验。
    /// 当前 15 天 × 12 秒 = 180 秒，改动不需要碰任何代码。
    /// </summary>
    [CreateAssetMenu(fileName = "GameClockConfig", menuName = "Omen/Game Clock Config", order = 1)]
    public sealed class GameClockConfig : ScriptableObject
    {
        [Header("节奏")]
        [Tooltip("一局的总天数。第 15 天结算。")]
        [Range(1, 999)]
        [SerializeField] private int totalDays = 15;

        [Tooltip("游戏内 1 天对应多少真实秒。12 秒 × 15 天 = 一局 3 分钟。")]
        [Range(0.5f, 600f)]
        [SerializeField] private float secondsPerDay = 12f;

        [Tooltip("一次生物行动（tick）对应多少真实秒。必须 ≤ 一天的秒数。")]
        [Range(0.05f, 600f)]
        [SerializeField] private float secondsPerTick = 1f;

        [Header("行为")]
        [Tooltip("是否在 Awake 后自动开始走时间。")]
        [SerializeField] private bool autoStart = true;

        public bool AutoStart { get { return autoStart; } }

        /// <summary>一天包含多少 tick。由前两个参数推导，保证不出现互相矛盾的配置。</summary>
        public int TicksPerDay
        {
            get
            {
                if (secondsPerTick <= 0f)
                {
                    return 1;
                }

                return Mathf.Max(1, Mathf.RoundToInt(secondsPerDay / secondsPerTick));
            }
        }

        /// <summary>转成 Core 层可用的纯 C# 数据。</summary>
        public GameClockData ToData()
        {
            var data = new GameClockData
            {
                TotalDays = totalDays,
                SecondsPerDay = secondsPerDay,
                SecondsPerTick = secondsPerTick,
            };

            data.Sanitize();
            return data;
        }

        private void OnValidate()
        {
            // tick 比一天还长的话，一天里连一个 tick 都跑不完，天数永远不会推进。
            if (secondsPerTick > secondsPerDay)
            {
                secondsPerTick = secondsPerDay;
            }
        }
    }
}
