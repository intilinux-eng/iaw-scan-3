using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace IES_2.Avalonia.Localization
{
    public partial class StringsEnglish : ResourceDictionary
    {
        public StringsEnglish()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }

    public partial class StringsItalian : ResourceDictionary
    {
        public StringsItalian()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
