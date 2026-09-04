namespace WmhLms.Api.Services;

/// <summary>
/// The resolved JWT settings, registered once at startup.
///
/// Signing and validation must use the same key. Reading Jwt:Key separately in
/// two places is what broke when the key moved out of tracked configuration:
/// validation used the resolved value while signing still read config and got
/// null. One resolved object, injected everywhere, removes that class of bug.
/// </summary>
public sealed record JwtOptions(string Key, string? Issuer, string? Audience, int ExpiryMinutes);
