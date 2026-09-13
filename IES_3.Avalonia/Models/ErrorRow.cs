using CommunityToolkit.Mvvm.ComponentModel;

namespace IES_2.Avalonia.Models
{
    /// <summary>One row of the Errors screen: mirrors an errorElement's current decoded state.</summary>
    public partial class ErrorRow : ObservableObject
    {
        public string Description { get; }

        [ObservableProperty]
        private bool isVisible;

        [ObservableProperty]
        private string reason = "";

        [ObservableProperty]
        private string stateText = "";

        [ObservableProperty]
        private bool isMilOn;

        public ErrorRow(string description)
        {
            Description = description;
        }
    }
}
