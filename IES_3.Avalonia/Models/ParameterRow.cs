using CommunityToolkit.Mvvm.ComponentModel;
using IES_2.ECU;

namespace IES_2.Avalonia.Models
{
    /// <summary>One row of the Parameters screen: mirrors a dataElement plus its live decoded value.</summary>
    public partial class ParameterRow : ObservableObject
    {
        public dataElement Data { get; }
        public string Description => Data.Description;
        public string Unit => Data.Unit;
        public byte[] RequestSet => Data.RequestSet;

        [ObservableProperty]
        private bool isChecked;

        [ObservableProperty]
        private string value = "";

        public ParameterRow(dataElement data)
        {
            Data = data;
        }
    }
}
