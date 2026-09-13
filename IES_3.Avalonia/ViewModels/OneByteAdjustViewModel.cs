using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IES_2.Avalonia.ViewModels
{
    /// <summary>
    /// Byte-value adjustment dialog, replaces frmOneByte: a 0-255 slider whose value is sent
    /// to the ECU on demand (Apply), closed explicitly by the user (Finish) - the adjustment
    /// flow on the DiagnosticsSession side is paused/waiting for this dialog to close.
    /// </summary>
    public partial class OneByteAdjustViewModel : ObservableObject
    {
        private readonly Func<byte, bool> _applyQuery;

        public string Title { get; }

        [ObservableProperty]
        private int value;

        [ObservableProperty]
        private string? statusMessage;

        public event Action? CloseRequested;

        public OneByteAdjustViewModel(string title, byte initialValue, Func<byte, bool> applyQuery)
        {
            Title = title;
            Value = initialValue;
            _applyQuery = applyQuery;
        }

        [RelayCommand]
        private void Apply()
        {
            var ok = _applyQuery((byte)Value);
            StatusMessage = ok ? $"Valore inviato: {Value}" : "Errore di comunicazione con la centralina.";
        }

        [RelayCommand]
        private void Finish() => CloseRequested?.Invoke();
    }
}
