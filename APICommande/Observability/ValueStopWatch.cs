using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace APIOrder.Utilitaires
{
    public struct ValueStopwatch
    {
        readonly private long _startTimestamp;

        public static bool IsHighResolution => Stopwatch.IsHighResolution;
        public static long Frequency => Stopwatch.Frequency;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ValueStopwatch StartNew() => new ValueStopwatch(Stopwatch.GetTimestamp());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ValueStopwatch(long startTimestamp)
        {
            _startTimestamp = startTimestamp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TimeSpan GetElapsedTime()
        {
            long endTimestamp = Stopwatch.GetTimestamp();
            long elapsed = endTimestamp - _startTimestamp;
            return TimeSpan.FromSeconds((double)elapsed / Frequency);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetElapsedMilliseconds()
        {
            return GetElapsedTime().TotalMilliseconds;
        }
    }
}