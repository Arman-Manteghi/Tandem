using System.Windows;
using System.Windows.Controls;
using Tandem.Core.Profiles;

namespace Tandem.App.Views;

public partial class CopySetupDialog : Window
{
    public CopySetupDialog(Window? owner, Profile target, IReadOnlyList<Profile> sources)
    {
        InitializeComponent();
        App.Current.Host?.Theme.Attach(this);
        if (owner is { IsVisible: true })
        {
            Owner = owner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Heading.Text = $"Copy app setup into “{target.DisplayName}”";
        Source.ItemsSource = sources;
        Source.SelectedIndex = sources.Count > 0 ? 0 : -1;
    }

    public Profile? SelectedSource => Source.SelectedItem as Profile;

    private void OnSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        var items = SelectedSource is { } source ? ProfileSetup.Available(source) : [];
        ItemsList.ItemsSource = items;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.IsEnabled = items.Count > 0;
    }

    private void OnCopy(object sender, RoutedEventArgs e) => DialogResult = true;
}
