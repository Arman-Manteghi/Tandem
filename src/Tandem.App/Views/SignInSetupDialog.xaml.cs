using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Tandem.Core.Integration;
using Tandem.Core.Logging;

namespace Tandem.App.Views;

/// <summary>
/// One-time setup of the sign-in helper, as one guided screen: Tandem registers its own link
/// handler, opens Windows Settings on its page, and notices by itself when the user has chosen
/// Tandem for CLAUDE links. Windows reserves that choice for the user; Tandem never makes it.
/// </summary>
public partial class SignInSetupDialog : Window
{
    private readonly string _exePath;
    private readonly DispatcherTimer _poll;
    private bool _waiting;
    private bool _done;

    private SignInSetupDialog(string exePath)
    {
        _exePath = exePath;
        InitializeComponent();
        Details.ItemsSource = LinkHandlerRegistration.KeysWritten;
        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(800) };
        _poll.Tick += (_, _) => Refresh();
        Closed += (_, _) => _poll.Stop();
    }

    /// <summary>For --render-previews: the dialog in its "waiting for your choice" state.</summary>
    internal static SignInSetupDialog ForPreview(string exePath)
    {
        var dialog = new SignInSetupDialog(exePath);
        dialog.Intro.Text = "Before “Work” opens: " + dialog.Intro.Text;
        dialog.SetStep(dialog.Step1Circle, dialog.Step1Mark, true, "1");
        dialog.WaitingRow.Visibility = Visibility.Visible;
        dialog.PrimaryButton.Content = "Open Windows Settings";
        return dialog;
    }

    /// <summary>Shows the setup unless the helper already works. Returns whether it is active afterwards.</summary>
    internal static bool Run(Window? owner, string exePath, string? reason = null)
    {
        if (LinkHandlerRegistration.GetStatus(exePath) == LinkHandlerStatus.Active)
        {
            return true;
        }

        var dialog = new SignInSetupDialog(exePath);
        App.Current.Host?.Theme.Attach(dialog);
        if (reason is not null)
        {
            dialog.Intro.Text = reason + " " + dialog.Intro.Text;
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

        dialog.Refresh();
        dialog.ShowDialog();
        return LinkHandlerRegistration.GetStatus(exePath) == LinkHandlerStatus.Active;
    }

    private void OnPrimary(object sender, RoutedEventArgs e)
    {
        if (_done)
        {
            DialogResult = true;
            return;
        }

        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            if (LinkHandlerRegistration.GetStatus(_exePath) is LinkHandlerStatus.Off or LinkHandlerStatus.NeedsRepair)
            {
                LinkHandlerRegistration.Register(_exePath);
            }

            OpenSettings();
            _waiting = true;
            _poll.Start();
            Refresh();
        }
        catch (Exception ex)
        {
            Log.Error("Setting up the sign-in helper failed.", ex);
            ErrorText.Text = "Windows did not accept Tandem's entries: " + ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e) => OpenSettings();

    private void OnLater(object sender, RoutedEventArgs e) => DialogResult = _done;

    private void OnToggleDetails(object sender, RoutedEventArgs e)
    {
        var show = DetailsBox.Visibility != Visibility.Visible;
        DetailsBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        DetailsToggle.Content = show ? "Hide details" : "Show exactly what is added";
    }

    private static void OpenSettings()
    {
        try
        {
            AppHost.OpenUri(LinkHandlerRegistration.SettingsUriForTandem);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open Windows Settings.", ex);
        }
    }

    private void Refresh()
    {
        var status = LinkHandlerRegistration.GetStatus(_exePath);
        var registered = status is LinkHandlerStatus.WaitingForUserChoice or LinkHandlerStatus.Active;
        var chosen = status == LinkHandlerStatus.Active;
        if (status == LinkHandlerStatus.WaitingForUserChoice)
        {
            _waiting = true;
            _poll.Start();
        }

        SetStep(Step1Circle, Step1Mark, registered, "1");
        SetStep(Step2Circle, Step2Mark, chosen, "2");
        WaitingRow.Visibility = _waiting && !chosen ? Visibility.Visible : Visibility.Collapsed;
        if (WaitingRow.Visibility == Visibility.Visible)
        {
            StartSpinner();
        }

        if (chosen && !_done)
        {
            _done = true;
            _poll.Stop();
            Log.Info("Sign-in helper is active.");
            Heading.Text = "All set";
            Intro.Text = "From now on, the browser's sign-in reaches the account you are signing in to, automatically. You never have to do this again.";
            IconGlyph.Text = "";
            IconBox.SetResourceReference(Border.BackgroundProperty, "Brush.SuccessSoft");
            IconGlyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Success");
            PrimaryButton.Content = "Continue";
            LaterButton.Visibility = Visibility.Collapsed;
            Activate();
            return;
        }

        PrimaryButton.Content = _waiting ? "Open Windows Settings" : "Set up";
    }

    private void StartSpinner()
    {
        if (Spinner.RenderTransform is RotateTransform rotate && !rotate.HasAnimatedProperties)
        {
            rotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
        }
    }

    private void SetStep(Border circle, TextBlock mark, bool done, string number)
    {
        mark.Text = done ? "" : number;
        mark.FontFamily = (FontFamily)FindResource(done ? "Font.Icons" : "Font.UI");
        circle.SetResourceReference(Border.BackgroundProperty, done ? "Brush.Accent" : "Brush.Badge");
        mark.SetResourceReference(TextBlock.ForegroundProperty, done ? "Brush.OnAccent" : "Brush.TextMuted");
    }
}
