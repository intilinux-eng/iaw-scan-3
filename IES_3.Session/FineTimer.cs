using System;
using System.Runtime.InteropServices;

namespace IES_2.Session
{
    /// <summary>
    /// Asks Windows for a 2 ms timer while a real ECU is connected, as the original program did. The pauses
    /// around each request are only a few milliseconds; at Windows' usual 15.6 ms they'd take far longer,
    /// and every value would update less often. Nothing to do on other systems.
    /// </summary>
    public sealed class FineTimer : IDisposable
    {
        private const uint Milliseconds = 2;
        private bool _held;

        public FineTimer()
        {
            if (OperatingSystem.IsWindows()) _held = timeBeginPeriod(Milliseconds) == 0;
        }

        public void Dispose()
        {
            if (!_held) return;
            _held = false;
            timeEndPeriod(Milliseconds);
        }

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint milliseconds);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint milliseconds);
    }
}
