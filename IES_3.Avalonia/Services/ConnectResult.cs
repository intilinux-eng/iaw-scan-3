namespace IES_2.Avalonia.Services
{
    public class ConnectResult
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public string? EcuTypeDisplayName { get; init; }
        public string? IsoCode { get; init; }
        public string? RepCode { get; init; }
        public string? CarModel { get; init; }
        public DiagnosticsSession? Session { get; init; }

        public static ConnectResult Failure(string message) => new() { Success = false, ErrorMessage = message };
    }
}
