using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IES_2.Avalonia.Models;
using IES_2.Avalonia.Services;
using IES_2.ECU;

namespace IES_2.Avalonia.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly EcuConnectionService _connectionService = new();
        private CancellationTokenSource? _connectCts;

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
        private string ecuTypeText = "-";

        [ObservableProperty]
        private string isoCodeText = "-";

        [ObservableProperty]
        private string repCodeText = "-";

        [ObservableProperty]
        private string carModelText = "-";

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
        private async Task ConnectAsync()
        {
            if (SelectedEcuOption == null) return;

            ErrorMessage = null;
            IsConnecting = true;
            ConnectCommand.NotifyCanExecuteChanged();
            CancelConnectCommand.NotifyCanExecuteChanged();
            _connectCts = new CancellationTokenSource();

            try
            {
                var result = await _connectionService.ConnectAsync(SelectedEcuOption, SelectedComPort, IsSimulationMode, _connectCts.Token);
                if (result.Success)
                {
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
                CancelConnectCommand.NotifyCanExecuteChanged();
                DisconnectCommand.NotifyCanExecuteChanged();
            }
        }

        private bool CanCancelConnect() => IsConnecting;

        [RelayCommand(CanExecute = nameof(CanCancelConnect))]
        private void CancelConnect()
        {
            _connectCts?.Cancel();
        }

        private bool CanDisconnect() => IsConnected;

        [RelayCommand(CanExecute = nameof(CanDisconnect))]
        private void Disconnect()
        {
            _connectionService.Disconnect();
            IsConnected = false;
            EcuTypeText = "-";
            IsoCodeText = "-";
            RepCodeText = "-";
            CarModelText = "-";
            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
        }

        partial void OnIsConnectingChanged(bool value)
        {
            ConnectCommand.NotifyCanExecuteChanged();
            CancelConnectCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedEcuOptionChanged(EcuOption? value)
        {
            ConnectCommand.NotifyCanExecuteChanged();
            RefreshFilteredVehicleGroups();
        }

        partial void OnSearchTextChanged(string value)
        {
            RefreshFilteredVehicleGroups();
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
    }
}
