namespace Tandem.Core.Profiles;

/// <summary>A user-facing validation error; the message is meant to be shown as is.</summary>
public sealed class ProfileValidationException(string message) : Exception(message);
