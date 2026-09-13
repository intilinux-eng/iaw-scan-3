using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IES_2.Avalonia.Models;
using IES_2.Avalonia.Services;

namespace IES_2.Avalonia.ViewModels
{
    /// <summary>
    /// [F6] Adjustments screen: actuator/parameter adjustments, replaces frmMain's dgvAdjusts +
    /// ExecAdjustment() (including the "onebyte" step that used to modally show frmOneByte).
    /// </summary>
    public partial class AdjustmentsViewModel : ObservableObject
    {
        private readonly DiagnosticsSession _session;
        private CancellationTokenSource? _cts;

        public ObservableCollection<AdjustRow> Rows { get; } = new();

        [ObservableProperty]
        private AdjustRow? selectedRow;

        [ObservableProperty]
        private bool isRunning;

        /// <summary>Wired up by the view: shows the one-byte dialog and completes when the user closes it.</summary>
        public Func<OneByteAdjustViewModel, Task>? ShowOneByteDialog { get; set; }

        public AdjustmentsViewModel(DiagnosticsSession session)
        {
            _session = session;
            foreach (var a in session.Ecu.adjustments)
                Rows.Add(new AdjustRow(a));
        }

        private bool CanExecute => !IsRunning && SelectedRow != null;

        [RelayCommand(CanExecute = nameof(CanExecute))]
        private async Task Execute()
        {
            if (SelectedRow == null) return;
            var row = SelectedRow;
            IsRunning = true;
            ExecuteCommand.NotifyCanExecuteChanged();
            row.Status = "In corso...";
            _cts = new CancellationTokenSource();
            try
            {
                Func<byte, Func<byte, bool>, Task>? oneByteStep = null;
                if (row.Adjust.Type == "onebyte")
                {
                    // DiagnosticsSession resumes this delegate on whatever thread paused the passive
                    // loop's background task, not necessarily the UI thread - hop back explicitly
                    // before touching ShowOneByteDialog, which opens an Avalonia Window.
                    oneByteStep = (initial, applyQuery) =>
                    {
                        if (ShowOneByteDialog == null) return Task.CompletedTask;
                        var dialogVm = new OneByteAdjustViewModel(row.Description, initial, applyQuery);
                        var tcs = new TaskCompletionSource();
                        Dispatcher.UIThread.Post(async () =>
                        {
                            try
                            {
                                await ShowOneByteDialog(dialogVm);
                                tcs.SetResult();
                            }
                            catch (Exception ex)
                            {
                                tcs.SetException(ex);
                            }
                        });
                        return tcs.Task;
                    };
                }
                var outcome = await _session.RunAdjustmentAsync(row.Adjust, oneByteStep, _cts.Token);
                row.Status = Describe(outcome);
            }
            finally
            {
                IsRunning = false;
                _cts = null;
                ExecuteCommand.NotifyCanExecuteChanged();
            }
        }

        private static string Describe(ActiveOutcome outcome) => outcome switch
        {
            ActiveOutcome.Demo => "DEMO",
            ActiveOutcome.Passed => "Eseguita",
            ActiveOutcome.InitFailed => "Avvio fallito",
            ActiveOutcome.Cancelled => "Annullata",
            _ => "Fallita",
        };

        partial void OnSelectedRowChanged(AdjustRow? value) => ExecuteCommand.NotifyCanExecuteChanged();
        partial void OnIsRunningChanged(bool value) => ExecuteCommand.NotifyCanExecuteChanged();
    }
}
