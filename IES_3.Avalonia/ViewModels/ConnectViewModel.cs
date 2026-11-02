using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IES_2.Avalonia.Models;
using IES_2.Avalonia.Services;
using IES_2.ECU;
using IES_2.Session;

namespace IES_2.Avalonia.ViewModels
{
    /// <summary>
    /// Getting connected: find your car (or pick the ECU family directly), check the cable, connect.
    /// The serial ports are watched while this page is open, so plugging the cable in just works.
    /// </summary>
    public partial class ConnectViewModel : ObservableObject
    {
        private readonly ShellViewModel _shell;
        private readonly EcuConnectionService _connectionService = new();
        private readonly List<VehicleChoice> _engineVehicles = new();
        private readonly List<VehicleChoice> _immobiliserVehicles = new();
        private readonly EcuFilter _allFilter;
        private readonly DispatcherTimer _portWatcher;
        private CancellationTokenSource? _connectCts;
        private ConnectResult? _lastFailure;
        private CableCheckResult? _lastCableTest;

        public ObservableCollection<EcuFilter> Filters { get; } = new();
        public ObservableCollection<VehicleChoice> Vehicles { get; } = new();
        public ObservableCollection<SerialPortInfo> Ports { get; } = new();

        [ObservableProperty]
        private string searchText = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChosenEcu), nameof(ChosenTitle), nameof(ChosenSubtitle), nameof(HasChoice), nameof(IsoOnlyAtIgnitionOn))]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        private VehicleChoice? selectedVehicle;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChosenEcu), nameof(ChosenTitle), nameof(ChosenSubtitle), nameof(HasChoice), nameof(IsoOnlyAtIgnitionOn))]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        private EcuFilter? selectedFilter;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPort), nameof(SelectedPort))]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(TestCableCommand))]
        private SerialPortInfo? selectedPortInfo;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(TestCableCommand))]
        private bool isSimulation;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(CancelCommand), nameof(TestCableCommand))]
        private bool isConnecting;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(TestCableCommand))]
        private bool isTestingCable;

        [ObservableProperty]
        private string? cableTestMessage;

        [ObservableProperty]
        private bool cableTestPassed;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DriverMessage), nameof(HasDriverPage))]
        private UsbSerialChip? cableWithoutDriver;

        [ObservableProperty]
        private string? errorMessage;

        public string? SelectedPort => SelectedPortInfo?.Name;
        public bool HasPort => SelectedPortInfo != null;
        public string? DriverMessage => CableWithoutDriver == null ? null : Loc.F("Connect_DriverMissing", CableWithoutDriver.Name);
        public bool HasDriverPage => CableWithoutDriver?.DriverPage != null;
        public bool HasChoice => ChosenEcu != null;

        /// <summary>The ECU the connection will talk to: the chosen car's, or the chosen family.</summary>
        public EcuOption? ChosenEcu => SelectedVehicle?.Ecu ?? SelectedFilter?.Ecu;

        /// <summary>An IAW-6F/16F gives its ISO code only as the ignition comes on: the order matters.</summary>
        public bool IsoOnlyAtIgnitionOn => ChosenEcu?.Id == iaw16f.name;

        public string ChosenTitle => SelectedVehicle?.Name ?? ChosenEcu?.DisplayName ?? Loc.T("Connect_NoChoice");
        public string ChosenSubtitle => SelectedVehicle != null ? SelectedVehicle.EcuName : ChosenEcu != null ? Loc.T("Connect_FamilyChosen") : Loc.T("Connect_NoChoiceHint");

        public ConnectViewModel(ShellViewModel shell)
        {
            _shell = shell;

            var engines = new[]
            {
                new EcuOption(iaw16f.name, iaw16f.longName, iaw16f.GetCars()),
                new EcuOption(iaw18f.name, iaw18f.longName, iaw18f.GetCars()),
                new EcuOption(iaw18fd.name, iaw18fd.longName, iaw18fd.GetCars()),
                new EcuOption(iaw8f_68.name, iaw8f_68.longName, iaw8f_68.GetCars()),
                new EcuOption(iaw04k.name, iaw04k.longName, iaw04k.GetCars()),
            };
            var immobiliser = new EcuOption(code.name, code.longName, code.GetCars());

            foreach (var ecu in engines) _engineVehicles.AddRange(Flatten(ecu));
            _immobiliserVehicles.AddRange(Flatten(immobiliser));

            _allFilter = new EcuFilter(null, "", "Connect_AllEngines") { IsSelected = true };
            Filters.Add(_allFilter);
            foreach (var ecu in engines) Filters.Add(new EcuFilter(ecu, ecu.Id.Replace("IAW-", "")));
            Filters.Add(new EcuFilter(immobiliser, "", "Connect_Immobiliser"));
            SelectedFilter = _allFilter;

            RefreshVehicles();
            RestoreLastChoice();
            RefreshPorts();

            _portWatcher = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => RefreshPorts());
        }

        private static IEnumerable<VehicleChoice> Flatten(EcuOption ecu) =>
            ecu.VehicleGroups.SelectMany(g => g.Vehicles.Select(name => new VehicleChoice(name, g.Brand, ecu)));

        private void RestoreLastChoice()
        {
            var last = _shell.Settings.LastVehicle;
            if (last == null) return;
            var ecuId = _shell.Settings.LastEcuId;
            var all = _engineVehicles.Concat(_immobiliserVehicles);
            SelectedVehicle = all.FirstOrDefault(v => v.Name == last && v.Ecu.Id == ecuId);
            if (SelectedVehicle == null || Vehicles.Contains(SelectedVehicle)) return;
            ChooseFilter(Filters.FirstOrDefault(f => f.Ecu?.Id == ecuId) ?? _allFilter);
            SelectedVehicle = all.FirstOrDefault(v => v.Name == last && v.Ecu.Id == ecuId);
        }

        public void Activate()
        {
            RefreshPorts();
            _portWatcher.Start();
        }

        public void Deactivate() => _portWatcher.Stop();

        public void RefreshTexts()
        {
            foreach (var filter in Filters) filter.RefreshText();
            OnPropertyChanged(nameof(ChosenTitle));
            OnPropertyChanged(nameof(ChosenSubtitle));
            if (_lastFailure != null) ErrorMessage = Describe(_lastFailure);
            ShowCableTest();
            OnPropertyChanged(nameof(DriverMessage));
        }

        [RelayCommand]
        private void RefreshPorts()
        {
            CableWithoutDriver = UsbSerialDevices.FindCableWithoutDriver();

            var names = SerialPorts.List();
            if (names.OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(Ports.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal))) return;

            var ports = SerialPorts.Describe(names);
            var keep = SelectedPort;
            Ports.Clear();
            foreach (var port in ports) Ports.Add(port);

            SelectedPortInfo = ports.FirstOrDefault(p => p.Name == keep)
                ?? ports.FirstOrDefault(p => p.Name == _shell.Settings.LastPort)
                ?? ports.FirstOrDefault();
        }

        partial void OnSelectedPortInfoChanged(SerialPortInfo? value)
        {
            _lastCableTest = null;
            CableTestMessage = null;
        }

        private bool CanTestCable() => !IsConnecting && !IsTestingCable && !IsSimulation && HasPort;

        /// <summary>Works with no car: see CableCheck. The port is only held for the moment this takes.</summary>
        [RelayCommand(CanExecute = nameof(CanTestCable))]
        private async Task TestCable()
        {
            var port = SelectedPort;
            if (port == null) return;
            IsTestingCable = true;
            CableTestMessage = null;
            try
            {
                _lastCableTest = await Task.Run(() => CableCheck.Run(port));
                ShowCableTest();
            }
            finally
            {
                IsTestingCable = false;
            }
        }

        private void ShowCableTest()
        {
            if (_lastCableTest == null) return;
            CableTestPassed = _lastCableTest.Condition == CableCondition.Working;
            CableTestMessage = _lastCableTest.Condition switch
            {
                CableCondition.Working => Loc.T("Connect_TestOk"),
                CableCondition.NoEcho => Loc.T("Connect_TestNoEcho"),
                CableCondition.Garbled => Loc.T("Connect_TestGarbled"),
                _ => Loc.F("Connect_ErrorPortOpen", _lastCableTest.Detail),
            };
        }

        [RelayCommand]
        private void GetDriver()
        {
            if (CableWithoutDriver?.DriverPage is { } page) _shell.Files?.OpenUrl(page);
        }

        partial void OnSearchTextChanged(string value) => RefreshVehicles();

        [RelayCommand]
        private void ChooseFilter(EcuFilter filter)
        {
            foreach (var f in Filters) f.IsSelected = ReferenceEquals(f, filter);
            SelectedFilter = filter;
            if (SelectedVehicle != null && filter.Ecu != null && SelectedVehicle.Ecu.Id != filter.Ecu.Id) SelectedVehicle = null;
            RefreshVehicles();
        }

        private void RefreshVehicles()
        {
            var filter = SelectedFilter;
            IEnumerable<VehicleChoice> list = filter?.Ecu == null
                ? _engineVehicles
                : filter.Ecu.Id == code.name ? _immobiliserVehicles : _engineVehicles.Where(v => v.Ecu.Id == filter.Ecu.Id);

            var needle = SearchText.Trim();
            if (needle.Length > 0)
            {
                // Words in any order: "coupe turbo" finds "Coupe S 2.0 T/C" only if every word is there.
                var words = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                list = list.Where(v => words.All(w =>
                    v.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || v.Make.Contains(w, StringComparison.OrdinalIgnoreCase)));
            }

            var selected = SelectedVehicle;
            Vehicles.Clear();
            foreach (var vehicle in list.OrderBy(v => v.Make, StringComparer.CurrentCulture).ThenBy(v => v.Name, StringComparer.CurrentCulture))
                Vehicles.Add(vehicle);
            SelectedVehicle = selected;
        }

        private bool CanConnect() => !IsConnecting && !IsTestingCable && ChosenEcu != null && (IsSimulation || HasPort);

        [RelayCommand(CanExecute = nameof(CanConnect))]
        private async Task Connect()
        {
            var option = ChosenEcu;
            if (option == null) return;

            ErrorMessage = null;
            _lastFailure = null;
            _lastCableTest = null;
            CableTestMessage = null;
            IsConnecting = true;
            _connectCts = new CancellationTokenSource();
            var simulation = IsSimulation;
            var port = SelectedPort;
            try
            {
                ConnectResult result;
                try
                {
                    result = await _connectionService.ConnectAsync(option, port, simulation, _connectCts.Token, SimulatedIso(option, SelectedVehicle?.Name));
                }
                catch (OperationCanceledException)
                {
                    result = ConnectResult.Failure(ConnectError.Cancelled);
                }

                if (result.Success && result.Session != null)
                {
                    var settings = _shell.Settings;
                    settings.LastEcuId = option.Id;
                    settings.LastVehicle = SelectedVehicle?.Name;
                    if (!simulation) settings.LastPort = port;
                    settings.Save();
                    _shell.Attach(result, option, simulation ? null : port);
                }
                else if (result.Error != ConnectError.Cancelled)
                {
                    _lastFailure = result;
                    ErrorMessage = Describe(result);
                }
            }
            finally
            {
                IsConnecting = false;
                _connectCts = null;
            }
        }

        /// <summary>The ISO code of the car being simulated, so that the session shows that car.</summary>
        private static string? SimulatedIso(EcuOption option, string? vehicle)
        {
            string[,]? cars = option.Id switch
            {
                iaw16f.name => iaw16f.cars,
                iaw18f.name => iaw18f.cars,
                iaw18fd.name => iaw18fd.cars,
                iaw8f_68.name => iaw8f_68.cars,
                iaw04k.name => iaw04k.cars,
                code.name => code.cars,
                _ => null,
            };
            if (cars == null || cars.GetLength(0) == 0) return null;
            for (int i = 0; i < cars.GetLength(0); i++)
                if (cars[i, 1] == vehicle) return cars[i, 0];
            return cars[0, 0];
        }

        private bool CanCancel() => IsConnecting;

        [RelayCommand(CanExecute = nameof(CanCancel))]
        private void Cancel() => _connectCts?.Cancel();

        private static string Describe(ConnectResult result) => result.Error switch
        {
            ConnectError.NoPortSelected => Loc.T("Connect_ErrorNoPort"),
            ConnectError.PortOpenFailed => Loc.F("Connect_ErrorPortOpen", result.Detail),
            ConnectError.InvalidEcu => Loc.T("Connect_ErrorInvalidEcu"),
            ConnectError.CableNotAnswering => Loc.T("Connect_TestNoEcho"),
            _ => Loc.F("Connect_ErrorCommunication", result.Detail),
        };
    }
}
