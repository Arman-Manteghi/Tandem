using System.Text.Json.Serialization;

namespace Tandem.Core.Profiles;

/// <summary>
/// The subscription the user says an account has. The app does not store it anywhere Tandem
/// can read, so the user picks it once.
/// </summary>
public enum AccountPlan
{
    Unknown,
    Free,
    Pro,
    Max,
    Team,
    Enterprise,
}

/// <summary>One Claude account, i.e. one separate Claude data folder.</summary>
public sealed record Profile
{
    public const string DefaultId = "default";

    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>"#RRGGBB".</summary>
    public required string Color { get; init; }

    /// <summary>
    /// Data folder passed to Claude as <c>--user-data-dir</c>, always under Tandem's profiles root.
    /// Null for the Default profile, which starts with no arguments and uses Claude's own folder.
    /// </summary>
    public string? DataFolder { get; init; }

    public DateTimeOffset Created { get; init; }

    public AccountPlan Plan { get; init; }

    [JsonIgnore]
    public bool IsDefault => string.Equals(Id, DefaultId, StringComparison.Ordinal);
}
