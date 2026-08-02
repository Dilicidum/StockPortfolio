namespace StockPortfolio.Modules.Identity.Application;

/// <summary>How long tokens live and whether a refresh token is rotated when it is used.</summary>
public static class TokenPolicy
{
    // TODO(you): access TTL, refresh TTL, whether refresh rotates on use, and the grace window.

    /// <summary>Gets how long an issued access token is accepted for.</summary>
    public static TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(15);

    /// <summary>Gets how long a session can be refreshed before the user must sign in again.</summary>
    public static TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(14);

    /// <summary>Gets a value indicating whether using a refresh token supersedes it and issues a new one.</summary>
    public static bool RotateOnUse => true;

    /// <summary>Gets how long a just-superseded refresh token keeps working, so that two tabs refreshing at the.</summary>
    public static TimeSpan RotationGracePeriod => TimeSpan.FromSeconds(30);
}
