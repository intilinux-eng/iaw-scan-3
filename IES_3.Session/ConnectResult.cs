namespace IES_2.Session
{
    /// <summary>Why a connection attempt failed; each UI words it in its own language.</summary>
    public enum ConnectError
    {
        None,
        NoPortSelected,
        PortOpenFailed,
        Cancelled,
        InvalidEcu,
        Communication,

        /// <summary>The port opened but nothing came back, not even the cable's own echo.</summary>
        CableNotAnswering,
    }

    public class ConnectResult
    {
        public bool Success { get; init; }
        public ConnectError Error { get; init; }

        /// <summary>Technical detail from the system (port name, exception text), when there is one.</summary>
        public string? Detail { get; init; }

        public string? EcuTypeDisplayName { get; init; }
        public string? IsoCode { get; init; }
        public string? RepCode { get; init; }
        public string? CarModel { get; init; }

        /// <summary>
        /// False when nothing answered: the connect flow, like the original program's, still succeeds
        /// with the port open, and the loss only shows once something is polled.
        /// </summary>
        public bool EcuIdentified { get; init; }

        /// <summary>
        /// The ECU answers, but it's an IAW-6F/16F that didn't give its ISO code, so the exact car isn't
        /// known. It gives that code only by itself as the ignition comes on, and can't be asked later.
        /// </summary>
        public bool CarNotIdentified { get; init; }

        public DiagnosticsSession? Session { get; init; }

        public static ConnectResult Failure(ConnectError error, string? detail = null) =>
            new() { Success = false, Error = error, Detail = detail };
    }
}
