using Microsoft.UI.Xaml.Controls;

namespace Common;

public sealed partial class I18n : Page
{
    public I18n()
    {
        InitializeComponent();
    }

    public static string Information()
    {
        return new I18n().InformationText.Text;
    }

    public static string Error()
    {
        return new I18n().ErrorText.Text;
    }

    public static string Cancel()
    {
        return new I18n().CancelText.Text;
    }
}