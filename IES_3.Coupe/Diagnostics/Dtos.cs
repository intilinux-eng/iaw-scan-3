using System.Collections.Generic;

namespace IES_2.Coupe.Diagnostics
{
    // JSON shapes pushed to the browser (camelCase via the web serializer defaults).

    public sealed record StateDto(
        string Status,
        string? ErrorMessage,
        string? CommunicationLostMessage,
        ConnectionDto? Connection,
        IReadOnlyList<ParameterDto> Parameters,
        IReadOnlyList<ErrorDto> Errors,
        bool ErrorsRead,
        string? ErrorsStatus,
        bool CanClearCodes,
        bool IsClearing,
        IReadOnlyList<TestDto> Tests,
        int? RunningTest,
        RecordingDto Recording,
        LoggerDto Logger);

    public sealed record LoggerDto(
        bool AutoEnabled,
        bool Paused,
        bool Active,
        bool Searching,
        string Status,
        int BootSeq,
        bool ClockTrusted,
        string? ClockSource,
        string? DataFile);

    public sealed record DatalogDto(
        int CurrentBoot,
        long UsedBytes,
        long MaxBytes,
        double SamplesPerSecond,
        IReadOnlyList<IES_2.Coupe.Logging.DatalogArchive.BootSummary> Boots);

    public sealed record ConnectionDto(
        string EcuId,
        string EcuType,
        string IsoCode,
        string RepCode,
        string CarModel,
        bool IsSimulation,
        bool IsKnownCoupe);

    public sealed record ParameterDto(int Index, string Description, string Unit, bool Checked, string? Value);

    public sealed record ErrorDto(string Description, string Reason, string State, bool MilOn);

    public sealed record TestDto(int Index, string Description, int TimeoutSeconds, string? Result);

    public sealed record RecordingDto(bool IsRecording, IReadOnlyList<int> Indices, string? FileName);

    public sealed record SampleDto(double T, IReadOnlyList<double> V);

    public sealed record RecordingFileDto(string Name, long Size, string Modified);

    public sealed record EcuChoiceDto(string Id, string Name, string Badge, string Role, IReadOnlyList<string> Vehicles);

    public sealed record SetupDto(
        IReadOnlyList<EcuChoiceDto> Ecus,
        IReadOnlyList<string> Ports,
        string? LastEcu,
        string? LastPort,
        AboutDto About);

    public sealed record AboutDto(
        string ProductName,
        string Version,
        string ForkCopyright,
        string ForkRepoUrl,
        string OriginalProjectLine,
        string OriginalProjectUrl,
        string Description,
        string LicenseText);
}
