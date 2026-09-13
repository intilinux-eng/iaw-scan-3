using System.Collections.ObjectModel;
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
    /// <summary>
    /// [F3] Errors screen: engine + immobilizer fault codes, replaces frmMain's dgvErrors +
    /// RefreshEngineErrors()/RefreshImmoErrors() plus the Clear Codes button.
    /// </summary>
    public partial class ErrorsViewModel : ObservableObject
    {
        private readonly DiagnosticsSession _session;
        private readonly int _engineCount;
        private CancellationTokenSource? _clearCts;

        public ObservableCollection<ErrorRow> Rows { get; } = new();

        [ObservableProperty]
        private bool isClearing;

        [ObservableProperty]
        private string? statusMessage;

        [ObservableProperty]
        private bool hasVisibleErrors;

        public ErrorsViewModel(DiagnosticsSession session)
        {
            _session = session;
            _engineCount = session.Ecu.engineErrors.Length;

            foreach (var e in session.Ecu.engineErrors)
                Rows.Add(new ErrorRow(e.Description));
            if (session.Ecu.hasIMMO)
                foreach (var e in session.Ecu.immoErrors)
                    Rows.Add(new ErrorRow(e.Description));

            session.ErrorsUpdated += OnEngineErrorsUpdated;
            session.ImmoErrorsUpdated += OnImmoErrorsUpdated;
        }

        private void OnEngineErrorsUpdated() => Dispatcher.UIThread.Post(RefreshEngine);
        private void OnImmoErrorsUpdated() => Dispatcher.UIThread.Post(RefreshImmo);

        private void RefreshEngine()
        {
            var errors = _session.Ecu.engineErrors;
            for (int i = 0; i < errors.Length; i++)
            {
                errors[i].Decode();
                ApplyRow(Rows[i], errors[i]);
            }
            RecomputeHasVisibleErrors();
        }

        private void RefreshImmo()
        {
            if (!_session.Ecu.hasIMMO) return;
            var errors = _session.Ecu.immoErrors;
            for (int i = 0; i < errors.Length; i++)
            {
                errors[i].Decode();
                ApplyRow(Rows[_engineCount + i], errors[i]);
            }
            RecomputeHasVisibleErrors();
        }

        private void RecomputeHasVisibleErrors()
        {
            HasVisibleErrors = false;
            foreach (var row in Rows)
            {
                if (!row.IsVisible) continue;
                HasVisibleErrors = true;
                break;
            }
        }

        private static void ApplyRow(ErrorRow row, errorElement e)
        {
            bool active = e.isActive || e.isVerified || e.isStored;
            row.IsVisible = active;
            if (!active) return;
            row.Reason = e.Reason;
            row.StateText = (e.isActive ? "Att. " : "") + (e.isVerified ? "Ver. " : "") + (e.isStored ? "Mem. " : "");
            row.IsMilOn = e.isActive && (e.isVerified || e.isStored);
        }

        private bool CanClearCodes => !IsClearing && !_session.IsSimulation;

        [RelayCommand(CanExecute = nameof(CanClearCodes))]
        private async Task ClearCodes()
        {
            IsClearing = true;
            ClearCodesCommand.NotifyCanExecuteChanged();
            _clearCts = new CancellationTokenSource();
            try
            {
                var outcome = await _session.ClearCodesAsync(_clearCts.Token);
                StatusMessage = outcome switch
                {
                    ActiveOutcome.Passed => "Codici di errore azzerati.",
                    ActiveOutcome.Cancelled => "Operazione annullata.",
                    ActiveOutcome.InitFailed => "Impossibile avviare la diagnosi attiva.",
                    _ => "Azzeramento non riuscito.",
                };
            }
            finally
            {
                IsClearing = false;
                _clearCts = null;
                ClearCodesCommand.NotifyCanExecuteChanged();
            }
        }

        public void Detach()
        {
            _session.ErrorsUpdated -= OnEngineErrorsUpdated;
            _session.ImmoErrorsUpdated -= OnImmoErrorsUpdated;
        }
    }
}
