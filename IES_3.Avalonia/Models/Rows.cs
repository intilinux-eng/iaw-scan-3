using CommunityToolkit.Mvvm.ComponentModel;
using IES_2.Avalonia.Services;
using IES_2.ECU;
using IES_2.Session;

namespace IES_2.Avalonia.Models
{
    /// <summary>A fault code currently present in the ECU.</summary>
    public sealed record FaultRow(
        string Description,
        string Reason,
        bool IsActive,
        bool IsVerified,
        bool IsStored,
        bool MilOn,
        string FirstSeen)
    {
        public bool HasReason => Reason.Length > 0;
    }

    /// <summary>One line of the session's fault history: a code that appeared or went away.</summary>
    public sealed record FaultHistoryRow(string Time, string Description, bool Appeared);

    /// <summary>An operation's last result, in words of the current language.</summary>
    public abstract partial class ActiveRow : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ResultText), nameof(HasResult), nameof(Passed), nameof(Failed))]
        private ActiveOutcome? outcome;

        [ObservableProperty]
        private bool isRunning;

        public bool HasResult => Outcome != null;
        public bool Passed => Outcome is ActiveOutcome.Passed or ActiveOutcome.Demo;
        public bool Failed => HasResult && !Passed;

        public string ResultText => Outcome switch
        {
            ActiveOutcome.Demo => Loc.T("Outcome_Demo"),
            ActiveOutcome.Passed => Loc.T("Outcome_Passed"),
            ActiveOutcome.Timeout => Loc.T("Outcome_Timeout"),
            ActiveOutcome.InitFailed => Loc.T("Outcome_InitFailed"),
            ActiveOutcome.Cancelled => Loc.T("Outcome_Cancelled"),
            ActiveOutcome.Failed => Loc.T("Outcome_Failed"),
            _ => "",
        };

        /// <summary>After a language change: the result words are recomputed.</summary>
        public void RefreshText() => OnPropertyChanged(nameof(ResultText));
    }

    public sealed class TestRow : ActiveRow
    {
        public testElement Test { get; }
        public string Description { get; }
        public int MaxSeconds => Test.TimeOut;
        public bool EngineRunning => Test.EngineRunning;

        public TestRow(testElement test)
        {
            Test = test;
            Description = test.Description.Trim();
        }
    }

    public sealed class AdjustRow : ActiveRow
    {
        public adjustElement Adjust { get; }
        public string Description { get; }

        public AdjustRow(adjustElement adjust)
        {
            Adjust = adjust;
            Description = adjust.Description.Trim();
        }
    }

    /// <summary>A vehicle in the connect page's list, and the ECU family it uses.</summary>
    public sealed record VehicleChoice(string Name, string Make, EcuOption Ecu)
    {
        public string EcuName => Ecu.DisplayName;
        public string Badge => Ecu.Badge;
    }

    /// <summary>A filter chip on the connect page: all engine ECUs, or one family.</summary>
    public sealed partial class EcuFilter : ObservableObject
    {
        public EcuOption? Ecu { get; }

        /// <summary>The text resource of a translated label; null when it's just the ECU's name.</summary>
        public string? LabelKey { get; }

        [ObservableProperty]
        private string label;

        [ObservableProperty]
        private bool isSelected;

        public EcuFilter(EcuOption? ecu, string fixedLabel, string? labelKey = null)
        {
            Ecu = ecu;
            LabelKey = labelKey;
            label = labelKey != null ? Loc.T(labelKey) : fixedLabel;
        }

        public void RefreshText()
        {
            if (LabelKey != null) Label = Loc.T(LabelKey);
        }
    }
}
