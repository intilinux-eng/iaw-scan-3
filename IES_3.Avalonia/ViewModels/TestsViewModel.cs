using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IES_2.Avalonia.Models;
using IES_2.Avalonia.Services;

namespace IES_2.Avalonia.ViewModels
{
    /// <summary>
    /// [F4] Tests screen: active-diagnostics actuator tests, replaces frmMain's dgvTests +
    /// bgwTest wiring around ExecTest().
    /// </summary>
    public partial class TestsViewModel : ObservableObject
    {
        private readonly DiagnosticsSession _session;
        private CancellationTokenSource? _cts;

        public ObservableCollection<TestRow> Rows { get; } = new();

        [ObservableProperty]
        private TestRow? selectedRow;

        [ObservableProperty]
        private bool isRunning;

        public TestsViewModel(DiagnosticsSession session)
        {
            _session = session;
            foreach (var t in session.Ecu.activeTest)
                Rows.Add(new TestRow(t));
        }

        private bool CanExecute => !IsRunning && SelectedRow != null;

        [RelayCommand(CanExecute = nameof(CanExecute))]
        private async Task Execute()
        {
            if (SelectedRow == null) return;
            var row = SelectedRow;
            IsRunning = true;
            ExecuteCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
            row.Result = "In corso...";
            _cts = new CancellationTokenSource();
            try
            {
                var outcome = await _session.RunTestAsync(row.Test, _cts.Token);
                row.Result = Describe(outcome);
            }
            finally
            {
                IsRunning = false;
                _cts = null;
                ExecuteCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
            }
        }

        private bool CanCancel => IsRunning;

        [RelayCommand(CanExecute = nameof(CanCancel))]
        private void Cancel() => _cts?.Cancel();

        private static string Describe(ActiveOutcome outcome) => outcome switch
        {
            ActiveOutcome.Demo => "DEMO",
            ActiveOutcome.Passed => "Superato",
            ActiveOutcome.Timeout => "Scaduto",
            ActiveOutcome.InitFailed => "Avvio fallito",
            ActiveOutcome.Cancelled => "Annullato",
            _ => "Fallito",
        };

        partial void OnSelectedRowChanged(TestRow? value) => ExecuteCommand.NotifyCanExecuteChanged();
        partial void OnIsRunningChanged(bool value)
        {
            ExecuteCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }
    }
}
