using System.Windows;

namespace Tandem.App.Views;

public enum DialogChoice
{
    None,
    Primary,
    Secondary,
    Tertiary,
}

/// <summary>Themed replacement for MessageBox with up to three buttons and an optional list.</summary>
public partial class MessageDialog : Window
{
    private DialogChoice _choice = DialogChoice.None;

    private MessageDialog()
    {
        InitializeComponent();
    }

    public static DialogChoice Show(
        Window? owner,
        string title,
        string message,
        string primary = "OK",
        string? secondary = null,
        string? tertiary = null,
        IEnumerable<string>? details = null,
        bool danger = false)
    {
        var dialog = new MessageDialog { Title = title };
        App.Current.Host?.Theme.Attach(dialog);
        dialog.Heading.Text = title;
        dialog.Message.Text = message;
        dialog.PrimaryButton.Content = primary;
        if (danger)
        {
            dialog.IconGlyph.Text = "";
            dialog.IconBox.SetResourceReference(BackgroundProperty, "Brush.DangerSurface");
            dialog.IconGlyph.SetResourceReference(ForegroundProperty, "Brush.Danger");
            dialog.PrimaryButton.SetResourceReference(StyleProperty, "Button.Danger");
        }

        if (secondary is not null)
        {
            dialog.SecondaryButton.Content = secondary;
            dialog.SecondaryButton.Visibility = Visibility.Visible;
        }

        if (tertiary is not null)
        {
            dialog.TertiaryButton.Content = tertiary;
            dialog.TertiaryButton.Visibility = Visibility.Visible;
        }

        var list = details?.ToList();
        if (list is { Count: > 0 })
        {
            dialog.Details.ItemsSource = list;
            dialog.DetailsBox.Visibility = Visibility.Visible;
        }

        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowInTaskbar = true;
            dialog.Topmost = true;
        }

        dialog.ShowDialog();
        return dialog._choice;
    }

    private void OnPrimary(object sender, RoutedEventArgs e) => Finish(DialogChoice.Primary);

    private void OnSecondary(object sender, RoutedEventArgs e) => Finish(DialogChoice.Secondary);

    private void OnTertiary(object sender, RoutedEventArgs e) => Finish(DialogChoice.Tertiary);

    private void Finish(DialogChoice choice)
    {
        _choice = choice;
        Close();
    }
}
