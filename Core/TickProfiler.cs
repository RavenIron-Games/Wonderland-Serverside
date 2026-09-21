using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace Wonderland.Core
{
    /// <summary>
    /// Where does a server tick go? Two things a dedicated server cannot otherwise tell you: how long the
    /// whole frame took (Time.unscaledDeltaTime - vanilla, every mod, Wonderland together; a long frame
    /// here is a stall every client feels as the other players and every creature they see skipping,
    /// since all of it reaches them through this server's relay) and how much of the frame was this mod
    /// (a Stopwatch around each subsystem's OnUpdate). Added in 0.10.8 for the "game stutters while
    /// moving items, only with two players on" report, which no server-side read could explain.
    ///
    /// Output, both on the console/BepInEx log: a line the moment a frame is slower than the configured
    /// threshold (`[TickProfiler] slow frame 143 ms - Wonderland 9 ms: Security 7, ItemFlow 2 | players 2`),
    /// rate-limited to one per second, and a summary every reporting interval (`[TickProfiler] 5 min:
    /// 14,912 frames, avg 20.1 ms, max 143 ms, 7 over 100 ms | Wonderland avg 0.4 ms, max 9 ms (Security)
    /// | players 2`). The per-subsystem Stopwatch is a few hundred nanoseconds per subsystem per frame;
    /// TickProfilerEnabled turns the whole thing off and is read at use time.
    /// </summary>
    public static class TickProfiler
    {
        private const float RateLimitSeconds = 1f;
        private const float ReportSeconds = 300f;

        private static readonly Stopwatch _stopwatch = new Stopwatch();
        private static Dictionary<string, double> _frameMs = new Dictionary<string, double>();
        private static Dictionary<string, double> _prevFrameMs = new Dictionary<string, double>();
        private static readonly Dictionary<string, double> _windowMaxMs = new Dictionary<string, double>();
        private static readonly StringBuilder _sb = new StringBuilder(256);

        private static int _frames;
        private static double _frameSumMs;
        private static double _frameMaxMs;
        private static int _slowFrames;
        private static double _modSumMs;
        private static double _modMaxMs;
        private static string _modMaxWho = "";
        private static float _lastSlowLog = -10f;
        private static float _reportTimer;
        private static double _thisFrameModMs;
        private static double _prevFrameModMs;
        private const double IgnoreFrameAboveMs = 10000.0;

        public static bool Enabled => WonderlandConfig.TickProfilerEnabled?.Value ?? true;

        private static float SlowFrameMs => WonderlandConfig.TickProfilerSlowFrameMs?.Value ?? 100f;

        /// <summary>Wraps one subsystem's OnUpdate. The subsystem runs whether or not profiling is on.</summary>
        public static void Measure(IWonderlandSubsystem subsystem)
        {
            if (!Enabled)
            {
                subsystem.OnUpdate();
                return;
            }
            _stopwatch.Restart();
            try
            {
                subsystem.OnUpdate();
            }
            finally
            {
                _stopwatch.Stop();
                double ms = _stopwatch.Elapsed.TotalMilliseconds;
                _frameMs[subsystem.Name] = ms;
                _thisFrameModMs += ms;
                if (!_windowMaxMs.TryGetValue(subsystem.Name, out double max) || ms > max)
                {
                    _windowMaxMs[subsystem.Name] = ms;
                }
            }
        }

        /// <summary>Call once per frame after every subsystem ran. unscaledDeltaTime is the length of the
        /// frame that just ENDED before this one started, so it is paired with the breakdown buffered from
        /// that frame; this frame's breakdown is then kept for the next call.</summary>
        public static void EndFrame(float unscaledDeltaTime, int players)
        {
            if (!Enabled)
            {
                _frameMs.Clear();
                _prevFrameMs.Clear();
                _thisFrameModMs = 0;
                _prevFrameModMs = 0;
                return;
            }

            double frameMs = unscaledDeltaTime * 1000.0;
            if (frameMs < IgnoreFrameAboveMs)
            {
                _frames++;
                _frameSumMs += frameMs;
                if (frameMs > _frameMaxMs) _frameMaxMs = frameMs;
                _modSumMs += _prevFrameModMs;
                if (_prevFrameModMs > _modMaxMs)
                {
                    _modMaxMs = _prevFrameModMs;
                    _modMaxWho = Heaviest(_prevFrameMs);
                }

                if (frameMs > SlowFrameMs)
                {
                    _slowFrames++;
                    if (Time.unscaledTime - _lastSlowLog >= RateLimitSeconds)
                    {
                        _lastSlowLog = Time.unscaledTime;
                        WonderlandDebug.LogAlways($"[TickProfiler] slow frame {frameMs:0} ms - Wonderland {_prevFrameModMs:0.0} ms: {Breakdown(_prevFrameMs)} | players {players}");
                    }
                }
                _reportTimer += unscaledDeltaTime;
            }

            // Rotate: this frame's numbers become "previous" for the next dt.
            Dictionary<string, double> swap = _prevFrameMs;
            _prevFrameMs = _frameMs;
            _frameMs = swap;
            _frameMs.Clear();
            _prevFrameModMs = _thisFrameModMs;
            _thisFrameModMs = 0;

            if (_reportTimer >= ReportSeconds)
            {
                Report(players);
            }
        }

        private static void Report(int players)
        {
            if (_frames > 0)
            {
                WonderlandDebug.LogAlways(
                    $"[TickProfiler] {ReportSeconds / 60f:0} min: {_frames:N0} frames, avg {_frameSumMs / _frames:0.0} ms, max {_frameMaxMs:0} ms, {_slowFrames} over {SlowFrameMs:0} ms" +
                    $" | Wonderland avg {_modSumMs / _frames:0.00} ms, max {_modMaxMs:0.0} ms ({_modMaxWho}) | players {players}");
            }
            _frames = 0;
            _frameSumMs = 0;
            _frameMaxMs = 0;
            _slowFrames = 0;
            _modSumMs = 0;
            _modMaxMs = 0;
            _modMaxWho = "";
            _windowMaxMs.Clear();
            _reportTimer = 0f;
        }

        private static string Breakdown(Dictionary<string, double> frame)
        {
            _sb.Clear();
            foreach (KeyValuePair<string, double> kvp in frame)
            {
                if (kvp.Value < 0.5) continue;
                if (_sb.Length > 0) _sb.Append(", ");
                _sb.Append(kvp.Key).Append(' ').Append(kvp.Value.ToString("0.0"));
            }
            return _sb.Length == 0 ? "all under 0.5 ms" : _sb.ToString();
        }

        private static string Heaviest(Dictionary<string, double> frame)
        {
            string who = "";
            double best = -1;
            foreach (KeyValuePair<string, double> kvp in frame)
            {
                if (kvp.Value > best)
                {
                    best = kvp.Value;
                    who = kvp.Key;
                }
            }
            return who;
        }
    }
}
