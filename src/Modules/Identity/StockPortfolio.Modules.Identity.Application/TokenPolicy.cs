namespace StockPortfolio.Modules.Identity.Application;

/// <summary>How long tokens live, and how long a just-rotated refresh token keeps working.</summary>
public static class TokenPolicy
{
    // TODO(you): access TTL, refresh TTL and the grace window.

    /// <summary>Gets how long an issued access token is accepted for.</summary>
    public static TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(15);

    /// <summary>Gets how long a session can be refreshed before the user must sign in again.</summary>
    public static TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(14);

    /// <summary>Gets how long a just-superseded refresh token keeps working, so two tabs refreshing at once agree.</summary>
    public static TimeSpan RotationGracePeriod => TimeSpan.FromSeconds(30);
}
