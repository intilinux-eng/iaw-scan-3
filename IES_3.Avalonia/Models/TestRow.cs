using CommunityToolkit.Mvvm.ComponentModel;
using IES_2.ECU;

namespace IES_2.Avalonia.Models
{
    /// <summary>One row of the Tests screen: mirrors a testElement plus its last run result.</summary>
    public partial class TestRow : ObservableObject
    {
        public testElement Test { get; }
        public string Description => Test.Description;

        [ObservableProperty]
        private string result = "";

        public TestRow(testElement test)
        {
            Test = test;
        }
    }
}
