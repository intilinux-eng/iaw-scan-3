using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IES_2.Avalonia.Models;
using IES_2.Avalonia.Services;
using IES_2.ECU;

namespace IES_2.Avalonia.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        public const int TabParameters = 0;
        public const int TabErrors = 1;
        public const int TabTests = 2;
        public const int TabGraphs = 3;
        public const int TabAdjustments = 4;

        private readonly EcuConnectionService _connectionService = new();
        private CancellationTokenSource? _connectCts;
        private DiagnosticsSession? _session;

        public ObservableCollection<EcuOption> EcuOptions { get; } = new();
        public ObservableCollection<string> ComPorts { get; } = new();

        [ObservableProperty]
        private EcuOption? selectedEcuOption;

        [ObservableProperty]
        private string searchText = "";

        [ObservableProperty]
        private IReadOnlyList<VehicleGroup> filteredVehicleGroups = Array.Empty<VehicleGroup>();

        [ObservableProperty]
        private string? selectedComPort;

        [ObservableProperty]
        private bool isSimulationMode;

        [ObservableProperty]
        private bool isConnecting;

        [ObservableProperty]
        private bool isConnected;

        [ObservableProperty]
        private string? errorMessage;

        [ObservableProperty]
        private string? communicationLostMessage;

        [ObservableProperty]
        private string ecuTypeText = "-";

        [ObservableProperty]
        private string isoCodeText = "-";

        [ObservableProperty]
        private string repCodeText = "-";

        [ObservableProperty]
        private string carModelText = "-";

        [ObservableProperty]
        private int selectedTabIndex = TabParameters;

        [ObservableProperty]
        private ParametersViewModel? parameters;

        [ObservableProperty]
        private ErrorsViewModel? errors;

        [ObservableProperty]
        private TestsViewModel? tests;

        [ObservableProperty]
        private GraphsViewModel? graphs;

        [ObservableProperty]
        private AdjustmentsViewModel? adjustments;

        /// <summary>Wired up by the view: shows the About dialog.</summary>
        public Action? ShowAboutDialogRequested { get; set; }

        public MainWindowViewModel()
        {
            EcuOptions.Add(new EcuOption(iaw16f.name, iaw16f.longName, iaw16f.GetCars()));
            EcuOptions.Add(new EcuOption(iaw18f.name, iaw18f.longName, iaw18f.GetCars()));
            EcuOptions.Add(new EcuOption(iaw18fd.name, iaw18fd.longName, iaw18fd.GetCars()));
            EcuOptions.Add(new EcuOption(iaw8f_68.name, iaw8f_68.longName, iaw8f_68.GetCars()));
            EcuOptions.Add(new EcuOption(iaw04k.name, iaw04k.longName, iaw04k.GetCars()));
            EcuOptions.Add(new EcuOption(code.name, code.longName, code.GetCars()));
            SelectedEcuOption = EcuOptions[0];
            RefreshFilteredVehicleGroups();

            foreach (var port in SerialPort.GetPortNames())
                ComPorts.Add(port);
            if (ComPorts.Count > 0)
                SelectedComPort = ComPorts[0];
        }

        private bool CanConnect() => !IsConnecting && !IsConnected && SelectedEcuOption != null;

        [RelayCommand(CanExecute = nameof(CanConnect))]
        private Task ConnectAsync() => ConnectCoreAsync(forceSimulation: false);

        /// <summary>Ctrl+F10 in the original: connects in simulation mode regardless of the checkbox.</summary>
        [RelayCommand(CanExecute = nameof(CanConnect))]
        private Task ConnectSimulatedAsync() => ConnectCoreAsync(forceSimulation: true);

        private async Task ConnectCoreAsync(bool forceSimulation)
        {
            if (SelectedEcuOption == null) return;

            ErrorMessage = null;
            IsConnecting = true;
            ConnectCommand.NotifyCanExecuteChanged();
            ConnectSimulatedCommand.NotifyCanExecuteChanged();
            CancelConnectCommand.NotifyCanExecuteChanged();
            _connectCts = new CancellationTokenSource();

            try
            {
                var simulation = forceSimulation || IsSimulationMode;
                var result = await _connectionService.ConnectAsync(SelectedEcuOption, SelectedComPort, simulation, _connectCts.Token);
                if (result.Success && result.Session != null)
                {
                    AttachSession(result.Session);
                    IsConnected = true;
                    EcuTypeText = result.EcuTypeDisplayName ?? "-";
                    IsoCodeText = result.IsoCode ?? "-";
                    RepCodeText = result.RepCode ?? "-";
                    CarModelText = result.CarModel ?? "-";
                }
                else
                {
                    ErrorMessage = result.ErrorMessage;
                }
            }
            finally
            {
                IsConnecting = false;
                _connectCts = null;
                ConnectCommand.NotifyCanExecuteChanged();
                ConnectSimulatedCommand.NotifyCanExecuteChanged();
                CancelConnectCommand.NotifyCanExecuteChanged();
                DisconnectCommand.NotifyCanExecuteChanged();
            }
        }

        private void AttachSession(DiagnosticsSession session)
        {
            _session = session;
            session.CommunicationLost += OnCommunicationLost;

            var paramsVm = new ParametersViewModel(session);
            Parameters = paramsVm;
            Errors = new ErrorsViewModel(session);
            Tests = new TestsViewModel(session);
            Graphs = new GraphsViewModel(session, paramsVm);
            Adjustments = new AdjustmentsViewModel(session);

            SelectedTabIndex = TabParameters;
            session.SetActiveScreen(DiagnosticsScreen.Parameters);
            session.StartLoop();
        }

        private void OnCommunicationLost(Exception ex) =>
            Dispatcher.UIThread.Post(() => CommunicationLostMessage = "Comunicazione con la centralina persa. Spegnere il quadro e riprovare.");

        [RelayCommand]
        private void ReconnectAfterCommLoss()
        {
            if (_session == null) return;
            CommunicationLostMessage = null;
            _session.Ecu.InitPasvDiag();
            _session.StartLoop();
        }

        private bool CanCancelConnect() => IsConnecting;

        [RelayCommand(CanExecute = nameof(CanCancelConnect))]
        private void CancelConnect() => _connectCts?.Cancel();

        private bool CanDisconnect() => IsConnected;

        [RelayCommand(CanExecute = nameof(CanDisconnect))]
        private void Disconnect()
        {
            if (_session != null)
            {
                _session.CommunicationLost -= OnCommunicationLost;
                _session.Dispose();
                _session = null;
            }
            Parameters?.Detach();
            Errors?.Detach();
            Graphs?.Detach();
            Parameters = null;
            Errors = null;
            Tests = null;
            Graphs = null;
            Adjustments = null;

            IsConnected = false;
            CommunicationLostMessage = null;
            EcuTypeText = "-";
            IsoCodeText = "-";
            RepCodeText = "-";
            CarModelText = "-";
            ConnectCommand.NotifyCanExecuteChanged();
            ConnectSimulatedCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
        }

        partial void OnIsConnectingChanged(bool value)
        {
            ConnectCommand.NotifyCanExecuteChanged();
            ConnectSimulatedCommand.NotifyCanExecuteChanged();
            CancelConnectCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedEcuOptionChanged(EcuOption? value)
        {
            ConnectCommand.NotifyCanExecuteChanged();
            ConnectSimulatedCommand.NotifyCanExecuteChanged();
            RefreshFilteredVehicleGroups();
        }

        partial void OnSearchTextChanged(string value) => RefreshFilteredVehicleGroups();

        partial void OnSelectedTabIndexChanged(int value)
        {
            _session?.SetActiveScreen(value switch
            {
                TabParameters => DiagnosticsScreen.Parameters,
                TabErrors => DiagnosticsScreen.Errors,
                TabGraphs => DiagnosticsScreen.Graphs,
                _ => DiagnosticsScreen.None,
            });
        }

        private void RefreshFilteredVehicleGroups()
        {
            var groups = SelectedEcuOption?.VehicleGroups ?? Array.Empty<VehicleGroup>();
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                FilteredVehicleGroups = groups;
                return;
            }

            var needle = SearchText.Trim();
            FilteredVehicleGroups = groups
                .Select(g => new VehicleGroup(g.Brand, g.Vehicles.Where(v => v.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList()))
                .Where(g => g.Vehicles.Count > 0)
                .ToList();
        }

        [RelayCommand]
        private void ShowAbout() => ShowAboutDialogRequested?.Invoke();

        // Keyboard accelerators (F2-F6, A, N, C, S, E) mirroring frmMain's SetAccelerator wiring.
        [RelayCommand] private void GoToParameters() => SelectedTabIndex = TabParameters;
        [RelayCommand] private void GoToErrors() => SelectedTabIndex = TabErrors;
        [RelayCommand] private void GoToTests() => SelectedTabIndex = TabTests;
        [RelayCommand] private void GoToGraphs() => SelectedTabIndex = TabGraphs;
        [RelayCommand] private void GoToAdjustments() => SelectedTabIndex = TabAdjustments;

        [RelayCommand]
        private void CheckAllParameters()
        {
            if (IsConnected && SelectedTabIndex == TabParameters)
                Parameters?.CheckAllCommand.Execute(null);
        }

        [RelayCommand]
        private void UncheckAllParameters()
        {
            if (IsConnected && SelectedTabIndex == TabParameters)
                Parameters?.UncheckAllCommand.Execute(null);
        }

        [RelayCommand]
        private void ClearCodesShortcut()
        {
            if (IsConnected && Errors?.ClearCodesCommand.CanExecute(null) == true)
                Errors.ClearCodesCommand.Execute(null);
        }

        [RelayCommand]
        private void ToggleRecordingShortcut()
        {
            if (IsConnected && SelectedTabIndex == TabGraphs && Graphs?.ToggleRecordingCommand.CanExecute(null) == true)
                Graphs.ToggleRecordingCommand.Execute(null);
        }

        [RelayCommand]
        private void ExecuteTestShortcut()
        {
            if (IsConnected && SelectedTabIndex == TabTests && Tests?.ExecuteCommand.CanExecute(null) == true)
                Tests.ExecuteCommand.Execute(null);
        }

        [RelayCommand]
        private void ConnectToggleShortcut()
        {
            if (IsConnected) DisconnectCommand.Execute(null);
            else if (CanConnect()) ConnectCommand.Execute(null);
        }
    }
}
