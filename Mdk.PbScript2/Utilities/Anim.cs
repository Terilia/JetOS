using System;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // Lightweight animation primitives for the MFD UI. All durations are wall-clock
        // seconds (SystemManager.ElapsedSeconds) so animations don't lag with sim hitches.
        // Pure functions — state lives on the caller (AnimatedValue or page-local timestamps).
        static class Anim
        {
            public static double Lerp(double a, double b, double t)
            {
                if (t <= 0) return a;
                if (t >= 1) return b;
                return a + (b - a) * t;
            }

            // Cubic ease-out: 1 - (1-t)^3. Fast start, gentle settle.
            public static double EaseOut(double t)
            {
                if (t <= 0) return 0;
                if (t >= 1) return 1;
                double inv = 1 - t;
                return 1 - inv * inv * inv;
            }

            // Smoothstep — symmetric ease in and out.
            public static double EaseInOut(double t)
            {
                if (t <= 0) return 0;
                if (t >= 1) return 1;
                return t * t * (3 - 2 * t);
            }

            // Sine pulse 0..1..0 over `period` seconds. Useful for warning blinks.
            public static double Pulse(double period)
            {
                if (period <= 0) return 0;
                double phase = (SystemManager.ElapsedSeconds % period) / period;
                return 0.5 - 0.5 * Cs(phase * 2 * PI);
            }

            // Sawtooth 0..1 over `period`. Useful for sweeps and progress dots.
            public static double Saw(double period)
            {
                if (period <= 0) return 0;
                return (SystemManager.ElapsedSeconds % period) / period;
            }

            public static Color LerpColor(Color a, Color b, double t)
            {
                if (t <= 0) return a;
                if (t >= 1) return b;
                float ft = (float)t;
                return Cr(
                    (byte)(a.R + (b.R - a.R) * ft),
                    (byte)(a.G + (b.G - a.G) * ft),
                    (byte)(a.B + (b.B - a.B) * ft),
                    (byte)(a.A + (b.A - a.A) * ft));
            }

            // Color with modulated alpha — used for warning pulses without changing tint.
            public static Color WithAlpha(Color c, float alpha)
            {
                if (alpha < 0) alpha = 0; else if (alpha > 1) alpha = 1;
                return Cr(c.R, c.G, c.B, (byte)(255 * alpha));
            }

            // 0.55..1.0 alpha pulse for warning indicators.
            public static float WarnAlpha(double period = 1.0)
            {
                return 0.55f + 0.45f * (float)Pulse(period);
            }
        }

        // Tracks a smoothly-animated scalar. Set Target each tick; read Value to get the
        // eased current value. Snaps to target when it doesn't change.
        class AnimatedValue
        {
            private double _from;
            private double _to;
            private double _startTime;
            private readonly double _duration;
            private bool _initialized;

            public AnimatedValue(double duration = 0.20) { _duration = duration; }

            public void SetTarget(double v)
            {
                if (!_initialized)
                {
                    _from = v; _to = v;
                    _startTime = SystemManager.ElapsedSeconds - _duration;
                    _initialized = true;
                    return;
                }
                if (v == _to) return;
                _from = Value;            // snapshot current eased value
                _to = v;
                _startTime = SystemManager.ElapsedSeconds;
            }

            public double Value
            {
                get
                {
                    if (!_initialized) return 0;
                    double t = (SystemManager.ElapsedSeconds - _startTime) / _duration;
                    return Anim.Lerp(_from, _to, Anim.EaseOut(t));
                }
            }
        }
    }
}
