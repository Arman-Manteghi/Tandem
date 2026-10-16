using System.Windows.Threading;
using Tandem.Core.Logging;
using Tandem.Core.Profiles;
using Tandem.Core.Usage;

namespace Tandem.App.Services;

/// <summary>
/// Keeps the latest usage of every account. Claude writes a new reading about every 15 minutes,
/// so the files are read again once a minute, and sooner while the usage is on screen.
/// </summary>
internal sealed class UsageService : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private readonly ProfileStore _profiles;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private DateTimeOffset _readAt = DateTimeOffset.MinValue;
    private bool _reading;
    private bool _frozen;

    public UsageService(ProfileStore profiles, Dispatcher dispatcher)
    {
        _profiles = profiles;
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = Interval };
        _timer.Tick += (_, _) => Refresh(TimeSpan.Zero);
        profiles.Changed += (_, _) => dispatcher.BeginInvoke(() => Refresh(TimeSpan.Zero));
    }

    /// <summary>Raised on the UI thread with each new reading. The argument is the previous one.</summary>
    public event Action<UsageOverview>? Updated;

    public UsageOverview Current { get; private set; } = UsageOverview.Empty;

    public void Start()
    {
        _timer.Start();
        Refresh(TimeSpan.Zero);
    }

    /// <summary>Reads again in the background when the last reading is older than <paramref name="maxAge"/>.</summary>
    public void Refresh(TimeSpan maxAge)
    {
        if (_frozen || _reading || DateTimeOffset.UtcNow - _readAt < maxAge)
        {
            return;
        }

        _reading = true;
        var profiles = _profiles.Profiles.ToList();
        Task.Run(() => UsageReader.ReadAll(profiles, DateTimeOffset.UtcNow)).ContinueWith(
            task => _dispatcher.BeginInvoke(() =>
            {
                _reading = false;
                if (task.IsCompletedSuccessfully)
                {
                    Publish(task.Result);
                }
                else
                {
                    Log.Warn("Reading usage failed.", task.Exception?.GetBaseException());
                }
            }),
            TaskScheduler.Default);
    }

    /// <summary>Reads right away when the last reading is older than <paramref name="maxAge"/> (the files are small).</summary>
    public UsageOverview ReadNow(TimeSpan maxAge)
    {
        if (!_frozen && DateTimeOffset.UtcNow - _readAt >= maxAge)
        {
            try
            {
                Publish(UsageReader.ReadAll(_profiles.Profiles, DateTimeOffset.UtcNow));
            }
            catch (Exception ex)
            {
                Log.Warn("Reading usage failed.", ex);
            }
        }

        return Current;
    }

    /// <summary>Shows fixed sample numbers instead of reading files (screenshots).</summary>
    public void Freeze(UsageOverview sample)
    {
        _frozen = true;
        _timer.Stop();
        Current = sample;
    }

    public void Dispose() => _timer.Stop();

    private void Publish(UsageOverview overview)
    {
        var previous = Current;
        Current = overview;
        _readAt = DateTimeOffset.UtcNow;
        Updated?.Invoke(previous);
    }
}
