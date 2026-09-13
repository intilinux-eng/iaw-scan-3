using CommunityToolkit.Mvvm.ComponentModel;
using IES_2.ECU;

namespace IES_2.Avalonia.Models
{
    /// <summary>One row of the Adjustments screen: mirrors an adjustElement plus its last run result.</summary>
    public partial class AdjustRow : ObservableObject
    {
        public adjustElement Adjust { get; }
        public string Description => Adjust.Description;

        [ObservableProperty]
        private string status = "";

        public AdjustRow(adjustElement adjust)
        {
            Adjust = adjust;
        }
    }
}
