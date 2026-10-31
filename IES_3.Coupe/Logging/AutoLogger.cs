using System;
using System.Threading;
using System.Threading.Tasks;
using IES_2.Coupe.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace IES_2.Coupe.Logging
{
    /// <summary>
    /// Keeps automatic logging going with no phone around: from power-on, and again whenever the ECU
    /// is lost, it keeps trying to connect to the last-used ECU. Each attempt already waits up to 10 s
    /// for the ECU (the connect flow's ISO sync wait), so a short pause between attempts is enough.
    /// </summary>
    public sealed class AutoLogger : BackgroundService
    {
        private static readonly TimeSpan Pause = TimeSpan.FromSeconds(3);

        private readonly CoupeDiagnostics _diagnostics;

        public AutoLogger(CoupeDiagnostics diagnostics)
        {
            _diagnostics = diagnostics;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _diagnostics.AutoConnectStepAsync();
                }
                catch (Exception)
                {
                    // An unexpected failure in one attempt must not end automatic logging for the drive.
                }
                await Task.Delay(Pause, stoppingToken);
            }
        }
    }
}
