using System;
using UnityEngine;
using Omen.Core;

namespace Omen.Systems
{
    /// <summary>
    /// 半实时时钟：驱动"每 12 秒一天、每 1 秒一次生物行动"的节奏。
    ///
    /// 设计要点：
    ///   - 时间推进与生物行为<b>解耦</b>：本类只负责"什么时候该发生一次 tick"，
    ///     具体谁响应由订阅者决定。Phase 2 的生态模拟、Phase 3 的预兆触发都挂在事件上。
    ///   - 用累加器而不是"每帧判断时间戳"，所以支持任意帧率，也能被 <see cref="TimeScale"/> 缩放
    ///     —— 预兆系统要"加速全岛"时只需要改这一个值。
    ///   - 单帧最多补 <see cref="MaxTicksPerFrame"/> 次，防止编辑器暂停后恢复时
    ///     "追帧"雪崩（一帧内跑几百次 tick 会把帧率彻底打死）。
    ///
    /// 时间约定：Day 从 1 开始；Tick 是<b>当天内</b>的 tick 序号，从 0 到 TicksPerDay-1。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameClock : MonoBehaviour
    {
        /// <summary>单帧最多补多少次 tick，超出部分直接丢弃（宁可慢一点也不要卡死）。</summary>
        public const int MaxTicksPerFrame = 8;

        [Tooltip("时钟配置。留空则使用代码内置默认值（15 天 / 12 秒一天 / 1 秒一 tick）。")]
        [SerializeField] private Config.GameClockConfig clockConfig;

        [Tooltip("是否在 Awake 后自动开始走时间。")]
        [SerializeField] private bool autoStart = true;

        [Tooltip("启动时把所有 tick 推进日志打到 Console。调节奏时很有用，平时请关掉，否则 Console 会被刷爆。")]
        [SerializeField] private bool logProgress = false;

        private GameClockData _data = new GameClockData();
        private double _dayAccumulator;
        private bool _running;
        private bool _finished;
        private long _completedTicks;

        /// <summary>一天开始了。参数是新的天数（从 1 开始）。</summary>
        public event Action<int> DayChanged;

        /// <summary>一次生物行动。参数是第几个 tick（从 0 开始，跨天累加）。</summary>
        public event Action<int> TickAdvanced;

        /// <summary>天数推进到超过 <see cref="TotalDays"/> 时触发一次，游戏结束。</summary>
        public event Action Finished;

        /// <summary>当前天数，从 1 开始。</summary>
        public int Day { get; private set; } = 1;

        /// <summary>当天内的 tick 序号，0 ~ TicksPerDay-1。</summary>
        public int TickInDay { get; private set; }

        /// <summary>本局已经发生的 tick 总数。</summary>
        public int TotalTickCount
        {
            get { return _completedTicks > int.MaxValue ? int.MaxValue : (int)_completedTicks; }
        }

        public int TotalDays
        {
            get { return _data.TotalDays; }
        }

        public int TicksPerDay
        {
            get { return _data.TicksPerDay; }
        }

        public double SecondsPerDay
        {
            get { return _data.SecondsPerDay; }
        }

        public double SecondsPerTick
        {
            get { return _data.SecondsPerTick; }
        }

        /// <summary>当前这一天的进度，0 ~ 1。HUD 用来画倒计时条。</summary>
        public float DayProgress
        {
            get { return Mathf.Clamp01((float)(_dayAccumulator / _data.SecondsPerDay)); }
        }

        /// <summary>距离下一天还有多少真实秒。</summary>
        public float SecondsUntilNextDay
        {
            get { return Mathf.Max(0f, (float)(_data.SecondsPerDay - _dayAccumulator)); }
        }

        public bool IsRunning
        {
            get { return _running && !_finished; }
        }

        public bool IsFinished
        {
            get { return _finished; }
        }

        /// <summary>
        /// 时间流速倍率。1 = 正常。预兆系统要"全岛加速"时改这个值即可，
        /// 时钟会自动按新倍率推进，不会因为变速而丢失或重复 tick。
        /// </summary>
        public float TimeScale { get; set; } = 1f;

        // ---------------------------------------------------------------- 生命周期

        private void Awake()
        {
            Reset();
        }

        private void Update()
        {
            if (!_running || _finished)
            {
                return;
            }

            double scaledDelta = Time.deltaTime * TimeScale;
            if (scaledDelta <= 0.0)
            {
                return;
            }

            _dayAccumulator += scaledDelta;

            // 单帧最多补 MaxTicksPerFrame 次，避免暂停恢复后"追帧"雪崩。
            int ticksThisFrame = 0;
            while (_dayAccumulator >= _data.SecondsPerTick && ticksThisFrame < MaxTicksPerFrame)
            {
                _dayAccumulator -= _data.SecondsPerTick;
                AdvanceTick();
                ticksThisFrame++;

                if (_finished)
                {
                    return;
                }
            }

            // 触顶时把超出的时间丢掉，宁可慢一点也不要让帧率崩掉。
            if (ticksThisFrame >= MaxTicksPerFrame)
            {
                _dayAccumulator = Math.Min(_dayAccumulator, _data.SecondsPerTick);
            }
        }

        // ---------------------------------------------------------------- 控制

        /// <summary>重新读取配置并把时钟复位到第 1 天。换岛重建时调用。</summary>
        public void Reset()
        {
            _data = clockConfig != null ? clockConfig.ToData() : new GameClockData();
            _data.Sanitize();

            Day = 1;
            TickInDay = 0;
            _completedTicks = 0;
            _dayAccumulator = 0.0;
            _finished = false;
            TimeScale = 1f;

            _running = clockConfig != null ? clockConfig.AutoStart : autoStart;

            if (logProgress)
            {
                Debug.Log("[GameClock] 复位：共 " + _data.TotalDays + " 天，每天 " + _data.TicksPerDay
                    + " tick，一局 " + _data.TotalSeconds.ToString("F0") + " 秒。", this);
            }
        }

        public void StartClock()
        {
            _running = true;
        }

        public void Pause()
        {
            _running = false;
        }

        public void Resume()
        {
            if (!_finished)
            {
                _running = true;
            }
        }

        public void TogglePause()
        {
            if (_running)
            {
                Pause();
            }
            else
            {
                Resume();
            }
        }

        // ---------------------------------------------------------------- 时间推进

        private void AdvanceTick()
        {
            TickInDay++;
            _completedTicks++;

            if (TickInDay >= _data.TicksPerDay)
            {
                TickInDay = 0;
                Day++;

                if (Day > _data.TotalDays)
                {
                    // 越过最后一天：先发完本次 tick，再宣告结束，避免订阅者拿到半个状态。
                    RaiseTick();
                    Finish();
                    return;
                }

                if (logProgress)
                {
                    Debug.Log("[GameClock] 第 " + Day + " 天开始。", this);
                }

                DayChanged?.Invoke(Day);
            }

            RaiseTick();
        }

        private void RaiseTick()
        {
            if (logProgress)
            {
                Debug.Log("[GameClock] Day " + Day + " / Tick " + TickInDay + "（累计 " + TotalTickCount + "）", this);
            }

            TickAdvanced?.Invoke(TotalTickCount);
        }

        private void Finish()
        {
            _finished = true;
            _running = false;

            if (logProgress)
            {
                Debug.Log("[GameClock] 一局结束：共 " + TotalTickCount + " 个 tick。", this);
            }

            Finished?.Invoke();
        }
    }
}
