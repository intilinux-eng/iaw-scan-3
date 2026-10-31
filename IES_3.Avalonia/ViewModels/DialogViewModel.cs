using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IES_2.Avalonia.ViewModels
{
    /// <summary>
    /// The window's one modal layer: a question to confirm, or the byte-value adjustment. An overlay
    /// rather than a separate window, so it behaves the same fullscreen and on minimal Linux desktops.
    /// </summary>
    public partial class DialogViewModel : ObservableObject
    {
        private TaskCompletionSource<bool>? _answer;

        [ObservableProperty]
        private bool isConfirmOpen;

        [ObservableProperty]
        private string title = "";

        [ObservableProperty]
        private string body = "";

        [ObservableProperty]
        private string confirmText = "";

        [ObservableProperty]
        private bool isDangerous;

        [ObservableProperty]
        private OneByteAdjustViewModel? oneByte;

        public Task<bool> ConfirmAsync(string title, string body, string confirmText, bool dangerous = false)
        {
            _answer?.TrySetResult(false);
            Title = title;
            Body = body;
            ConfirmText = confirmText;
            IsDangerous = dangerous;
            IsConfirmOpen = true;
            _answer = new TaskCompletionSource<bool>();
            return _answer.Task;
        }

        [RelayCommand]
        private void Accept() => Close(true);

        [RelayCommand]
        public void Cancel() => Close(false);

        private void Close(bool result)
        {
            IsConfirmOpen = false;
            _answer?.TrySetResult(result);
            _answer = null;
        }

        /// <summary>Shows the adjustment and completes when the user closes it.</summary>
        public Task ShowOneByteAsync(OneByteAdjustViewModel adjust)
        {
            var closed = new TaskCompletionSource();
            void OnClose()
            {
                adjust.CloseRequested -= OnClose;
                OneByte = null;
                closed.TrySetResult();
            }
            adjust.CloseRequested += OnClose;
            OneByte = adjust;
            return closed.Task;
        }
    }

    /// <summary>
    /// Byte-value adjustment: a 0-255 value sent to the ECU on demand, until the user closes it. The
    /// adjustment flow in DiagnosticsSession waits meanwhile.
    /// </summary>
    public partial class OneByteAdjustViewModel : ObservableObject
    {
        private readonly Func<byte, bool> _applyQuery;

        public string Title { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ValueText))]
        private double value;

        [ObservableProperty]
        private string? statusMessage;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
        private bool isSending;

        public string ValueText => ((int)Value).ToString();

        public event Action? CloseRequested;

        public OneByteAdjustViewModel(string title, byte initialValue, Func<byte, bool> applyQuery)
        {
            Title = title;
            Value = initialValue;
            _applyQuery = applyQuery;
        }

        [RelayCommand]
        private void Step(string amount)
        {
            if (int.TryParse(amount, out var delta))
                Value = Math.Clamp((int)Value + delta, 0, 255);
        }

        private bool CanApply() => !IsSending;

        /// <summary>Off the UI thread: a query waits on the serial line.</summary>
        [RelayCommand(CanExecute = nameof(CanApply))]
        private async Task Apply()
        {
            IsSending = true;
            var sent = (byte)Value;
            var ok = await Task.Run(() => _applyQuery(sent));
            StatusMessage = ok
                ? Services.Loc.F("Adjust_Sent", sent)
                : Services.Loc.T("Adjust_SendFailed");
            IsSending = false;
        }

        [RelayCommand]
        private void Finish() => CloseRequested?.Invoke();
    }
}
