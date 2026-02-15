using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;

namespace Common;

public sealed partial class Dialog : Page
{
    public Dialog()
    {
        InitializeComponent();
    }

    public static async Task<ContentDialogResult> Show(UIElement content, string message, string title = null)
    {
        title ??= I18n.Information();
        ContentDialog contentDialog = new()
        {
            XamlRoot = content.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            Title = new TextBlock { Text = title },
            PrimaryButtonText = "OK",
            DefaultButton = ContentDialogButton.Primary,
            Content = message
        };
        return await contentDialog.ShowAsync();
    }

    public static async Task<ContentDialogResult> ShowOKCancel(UIElement content, string message, string title = null)
    {
        title ??= I18n.Information();
        ContentDialog contentDialog = new()
        {
            XamlRoot = content.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            Title = new TextBlock { Text = title },
            PrimaryButtonText = "OK",
            SecondaryButtonText = I18n.Cancel(),
            DefaultButton = ContentDialogButton.Primary,
            Content = message
        };
        return await contentDialog.ShowAsync();
    }

    public static async Task<ContentDialogResult> ShowError(UIElement content, string message, string title = null)
    {
        title ??= I18n.Error();
        ContentDialog contentDialog = new()
        {
            XamlRoot = content.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            Title = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new Grid
                    {
                        Margin = new Thickness(0, 2, 0, 0),
                        Children =
                        {
                            new FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "\uEB90", Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE6, 0x0F, 0x22)) },
                            new FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "\uEA39", Foreground = new SolidColorBrush(Colors.White) }
                        }
                    },
                    new TextBlock { Text = title, Margin = new Thickness(8, 0, 0, 0) }
                }
            },
            PrimaryButtonText = "OK",
            DefaultButton = ContentDialogButton.Primary,
            Content = message
        };
        return await contentDialog.ShowAsync();
    }
}