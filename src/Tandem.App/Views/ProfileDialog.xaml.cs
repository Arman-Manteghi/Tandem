using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tandem.Core.Profiles;

namespace Tandem.App.Views;

/// <summary>Add an account (name, color, subscription, what to bring over) or edit one.</summary>
public partial class ProfileDialog : Window
{
    private readonly Func<string, string, string?> _validate;
    private string _color;
    private AccountPlan _plan;

    private ProfileDialog(string color, AccountPlan plan, Func<string, string, string?> validate)
    {
        InitializeComponent();
        _color = color;
        _plan = plan;
        _validate = validate;
        foreach (var option in Enum.GetValues<AccountPlan>())
        {
            var radio = new RadioButton
            {
                Style = (Style)FindResource("Segment"),
                GroupName = "plan",
                Content = option == AccountPlan.Unknown ? "Not set" : ProfileRow.PlanName(option),
                IsChecked = option == plan,
                Padding = new Thickness(10, 6, 10, 6),
            };
            radio.Checked += (_, _) => _plan = option;
            PlanBar.Children.Add(radio);
        }

        foreach (var swatch in ProfilePalette.Colors)
        {
            var radio = new RadioButton
            {
                Style = (Style)FindResource("Swatch"),
                Background = (Brush)new BrushConverter().ConvertFromString(swatch.Hex)!,
                GroupName = "color",
                ToolTip = swatch.Name,
                IsChecked = string.Equals(swatch.Hex, color, StringComparison.OrdinalIgnoreCase),
            };
            System.Windows.Automation.AutomationProperties.SetName(radio, swatch.Name);
            radio.Checked += (_, _) =>
            {
                _color = swatch.Hex;
                UpdatePreview();
            };
            Swatches.Children.Add(radio);
        }

        UpdatePreview();
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string ResultName => NameBox.Text;

    public string ResultColor => _color;

    public AccountPlan ResultPlan => _plan;

    /// <summary>The account to copy the app setup from once (only when automatic sync is off), or null.</summary>
    public Profile? CopyFrom => CopyPanel.Visibility == Visibility.Visible && CopyRow.Visibility == Visibility.Visible && CopyCheck.IsChecked == true
        ? CopySource.SelectedItem as Profile
        : null;

    /// <summary>Bring the account's Code and Cowork history from the original folder.</summary>
    public bool ImportHistory => CopyPanel.Visibility == Visibility.Visible && HistoryCheck.IsChecked == true;

    /// <param name="syncSetup">Automatic setup sync is on: no one-time copy choice is needed.</param>
    /// <param name="validate">Returns an error message, or null when name and color are fine.</param>
    public static ProfileDialog ForAdd(
        Window? owner, string suggestedColor, IReadOnlyList<Profile> copySources, bool syncSetup, bool originalHasHistory,
        Func<string, string, string?> validate)
    {
        var dialog = new ProfileDialog(suggestedColor, AccountPlan.Unknown, validate)
        {
            Title = "Add account",
        };
        dialog.Heading.Text = "Add an account";
        dialog.Intro.Text = "Tandem opens a new, separate Claude window for it. Sign in there once; it stays signed in.";
        dialog.OkButton.Content = "Add and sign in";
        dialog.NameBox.Text = string.Empty;
        dialog.HistoryCheck.IsChecked = originalHasHistory;
        dialog.HistoryCheck.IsEnabled = originalHasHistory;
        dialog.SyncInfo.Visibility = syncSetup ? Visibility.Visible : Visibility.Collapsed;
        dialog.CopyRow.Visibility = syncSetup ? Visibility.Collapsed : Visibility.Visible;
        dialog.CopySource.ItemsSource = copySources;
        dialog.CopySource.SelectedIndex = copySources.Count > 0 ? 0 : -1;
        dialog.CopyCheck.IsChecked = copySources.Any(p => ProfileSetup.Available(p).Count > 0);
        dialog.UpdateCopyDetails();
        dialog.Prepare(owner);
        return dialog;
    }

    public static ProfileDialog ForEdit(Window? owner, Profile profile, Func<string, string, string?> validate)
    {
        var dialog = new ProfileDialog(profile.Color, profile.Plan, validate)
        {
            Title = "Edit account",
        };
        dialog.Heading.Text = "Edit account";
        dialog.Intro.Text = profile.IsDefault
            ? "This is the Claude you already had: it keeps its own data folder and sign-in."
            : "Renaming does not change the account's data folder or sign-in.";
        dialog.OkButton.Content = "Save";
        dialog.NameBox.Text = profile.DisplayName;
        dialog.CopyPanel.Visibility = Visibility.Collapsed;
        dialog.Prepare(owner);
        return dialog;
    }

    private void Prepare(Window? owner)
    {
        App.Current.Host?.Theme.Attach(this);
        if (owner is { IsVisible: true })
        {
            Owner = owner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
            Topmost = true;
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var error = _validate(NameBox.Text, _color);
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            NameBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (PreviewAvatar is null || NameBox is null)
        {
            return;
        }

        PreviewAvatar.Background = (Brush)new BrushConverter().ConvertFromString(_color)!;
        PreviewInitial.Text = NameBox.Text.Trim().Length == 0 ? "+" : ProfileRow.InitialOf(NameBox.Text);
    }

    private void OnCopyChanged(object sender, RoutedEventArgs e) => UpdateCopyDetails();

    private void OnCopySourceChanged(object sender, SelectionChangedEventArgs e) => UpdateCopyDetails();

    private static string JoinNatural(IReadOnlyList<string> parts) =>
        parts.Count <= 1 ? string.Join(string.Empty, parts) : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];

    private void UpdateCopyDetails()
    {
        if (CopyDetails is null)
        {
            return;
        }

        CopySource.IsEnabled = CopyCheck.IsChecked == true;
        if (CopySource.SelectedItem is not Profile source)
        {
            CopyDetails.Text = string.Empty;
            return;
        }

        var available = ProfileSetup.Available(source);
        CopyDetails.Text = available.Count == 0
            ? "That account has no app setup to copy yet."
            : $"Copies {JoinNatural(available.Select(i => i.ShortName).ToList())}. Never copies sign-in, history or device identity.";
    }
}
