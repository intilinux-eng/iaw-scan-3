using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IES_2.Avalonia.Services;
using IES_2.Session;

namespace IES_2.Avalonia.ViewModels
{
    /// <summary>What the views must do for the view models: anything needing a window.</summary>
    public interface IUserFiles
    {
        Task<System.IO.Stream?> PickSaveAsync(string suggestedName);
        Task<string?> PickCsvAsync();
        void OpenFolder(string path);
        void OpenUrl(string url);
    }

    /// <summary>
    /// The application window's content: the navigation, the status bar, and the session connected right
    /// now. Pages exist only while they're reachable, and only the open one is on screen.
    /// </summary>
    public partial class ShellViewModel : ObservableObject
    {
        public const string PageConnect = "connect";
        public const string PageDashboard = "dashboard";
        public const string PageParameters = "parameters";
        public const string PageFaults = "faults";
        public const string PageTests = "tests";
        public const string PageGraph = "graph";
        public const string PageAdjustments = "adjustments";
        public const string PageSessions = "sessions";
        public const string PageSettings = "settings";

        public AppSettings Settings { get; }
        public SessionStore Store { get; }
        public DialogViewModel Dialog { get; } = new();
        public ConnectViewModel Connect { get; }
        public SessionsViewModel Sessions { get; }
        public SettingsViewModel SettingsPage { get; }

        /// <summary>Set by the window once it exists.</summary>
        public IUserFiles? Files { get; set; }

        /// <summary>Wired up by the window: makes it fullscreen, or back.</summary>
        public Action<bool>? SetFullscreen { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsConnected), nameof(IsSimulation), nameof(HasAdjustments), nameof(HasTests))]
        [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
        private LiveSession? live;

        [ObservableProperty]
        private object? currentPage;

        [ObservableProperty]
        private string currentKey = PageConnect;

        [ObservableProperty]
        private DashboardViewModel? dashboard;

        [ObservableProperty]
        private ParametersViewModel? parameters;

        [ObservableProperty]
        private FaultsViewModel? faults;

        [ObservableProperty]
        private TestsViewModel? tests;

        [ObservableProperty]
        private GraphViewModel? graph;

        [ObservableProperty]
        private AdjustmentsViewModel? adjustments;

        [ObservableProperty]
        private bool isImmersive;

        /// <summary>Shown under the status bar when an ECU connected but gave no sign of life.</summary>
        [ObservableProperty]
        private bool ecuNotAnswering;

        public bool IsConnected => Live != null;
        public bool IsSimulation => Live?.IsSimulation == true;
        public bool HasTests => Live?.Session.Ecu.activeTest.Length > 0;
        public bool HasAdjustments => Live?.Session.Ecu.adjustments.Length > 0;

        public ShellViewModel(AppSettings settings, string sessionsDirectory)
        {
            Settings = settings;
            Store = new SessionStore(sessionsDirectory);
            Connect = new ConnectViewModel(this);
            Sessions = new SessionsViewModel(this);
            SettingsPage = new SettingsViewModel(this);
            Loc.LanguageChanged += OnLanguageChanged;
            Navigate(PageConnect);
        }

        private void OnLanguageChanged()
        {
            Connect.RefreshTexts();
            Sessions.RefreshTexts();
            Tests?.RefreshTexts();
            Adjustments?.RefreshTexts();
            Faults?.RefreshTexts();
            SettingsPage.RefreshTexts();
        }

        [RelayCommand]
        public void Navigate(string key)
        {
            object? page = key switch
            {
                PageDashboard => Dashboard,
                PageParameters => Parameters,
                PageFaults => Faults,
                PageTests => Tests,
                PageGraph => Graph,
                PageAdjustments => Adjustments,
                PageSessions => Sessions,
                PageSettings => SettingsPage,
                _ => IsConnected ? Dashboard : Connect,
            };
            if (page == null) return;

            CurrentKey = ReferenceEquals(page, Dashboard) ? PageDashboard : page is ConnectViewModel ? PageConnect : key;
            CurrentPage = page;

            if (ReferenceEquals(page, Connect)) Connect.Activate();
            else Connect.Deactivate();
            if (ReferenceEquals(page, Sessions)) Sessions.Activate();
            if (!ReferenceEquals(page, Dashboard) && IsImmersive) ToggleImmersive();
        }

        /// <summary>Takes over a successful connection: from here on, every page works on it.</summary>
        public void Attach(ConnectResult result, EcuOption option, string? port)
        {
            var session = new LiveSession(result, option, port, Settings, Store);
            Live = session;
            Dashboard = new DashboardViewModel(session, Settings, this);
            Parameters = new ParametersViewModel(session);
            Faults = new FaultsViewModel(session, Dialog);
            Tests = new TestsViewModel(session, Dialog);
            Graph = new GraphViewModel(session);
            Adjustments = new AdjustmentsViewModel(session, Dialog);
            EcuNotAnswering = !result.EcuIdentified && !session.IsSimulation;
            Navigate(PageDashboard);
        }

        private bool CanDisconnect() => IsConnected;

        [RelayCommand(CanExecute = nameof(CanDisconnect))]
        private async Task Disconnect()
        {
            var session = Live;
            if (session == null) return;

            Dashboard?.Detach();
            Graph?.Detach();
            Live = null;
            Dashboard = null;
            Parameters = null;
            Faults = null;
            Tests = null;
            Graph = null;
            Adjustments = null;
            EcuNotAnswering = false;
            Navigate(PageConnect);

            await Task.Run(session.Dispose);
            Sessions.MarkStale();
        }

        /// <summary>Closing the window: the session's files must be closed before the process ends.</summary>
        public void Shutdown()
        {
            Loc.LanguageChanged -= OnLanguageChanged;
            Connect.Deactivate();
            Live?.Dispose();
        }

        [RelayCommand]
        private Task Reconnect() => Live?.ReconnectAsync() ?? Task.CompletedTask;

        [RelayCommand]
        private void ToggleRecording()
        {
            if (Live == null) return;
            if (Live.IsRecording) Live.StopRecording();
            else Live.StartRecording();
        }

        /// <summary>The dashboard alone, fullscreen: for a screen mounted in the car.</summary>
        [RelayCommand]
        public void ToggleImmersive()
        {
            if (!IsImmersive && !ReferenceEquals(CurrentPage, Dashboard)) return;
            IsImmersive = !IsImmersive;
            SetFullscreen?.Invoke(IsImmersive);
        }

        [RelayCommand]
        private async Task ConnectToggle()
        {
            if (IsConnected) await Disconnect();
            else if (Connect.ConnectCommand.CanExecute(null)) await Connect.ConnectCommand.ExecuteAsync(null);
        }

        /// <summary>Esc closes what's on top: a question first, then full screen.</summary>
        [RelayCommand]
        private void Escape()
        {
            if (Dialog.IsConfirmOpen) Dialog.Cancel();
            else if (IsImmersive) ToggleImmersive();
        }
    }
}
