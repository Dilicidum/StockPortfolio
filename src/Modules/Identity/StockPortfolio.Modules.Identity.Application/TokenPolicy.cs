namespace StockPortfolio.Modules.Identity.Application;

/// <summary>
/// How long tokens live and whether a refresh token is rotated when it is used.
/// </summary>
/// <remarks>
/// <para>
/// <b>The four values below are provisional and are the repo owner's decision to make.</b> They are
/// filled in only so the module compiles and the handlers behave; nothing here has been weighed
/// against the deployment. See the <c>TODO(you)</c> block in the source.
/// </para>
/// <para>
/// A static class rather than an options record on purpose: these are product decisions, not
/// per-environment configuration. Making them configurable would mean the security posture differs
/// between local and production, which is exactly the property you do not want. If they ever need
/// to vary by environment, promote this to an <c>IOptions&lt;TokenOptions&gt;</c> — but decide the
/// values first.
/// </para>
/// </remarks>
public static class TokenPolicy
{
    // TODO(you): access TTL, refresh TTL, whether refresh rotates on use, and the grace window.
    //
    //   Short access TTL   -> smaller window for a stolen access token, but more refresh
    //                         round-trips: every tab, every cold start, every wake-from-sleep.
    //                         The access token is not revocable, so this number *is* the
    //                         revocation latency for a compromised session.
    //
    //   Rotate-on-use      -> a replayed refresh token is detectable, because presenting an
    //                         already-superseded token is unambiguous evidence. But it breaks
    //                         concurrent tabs: two tabs refreshing at once means the loser holds a
    //                         token that was valid when it was sent. That is what the grace period
    //                         is for. Too short and the second tab logs out; too long and replay
    //                         detection is blunted by exactly that window.
    //
    //   Long refresh TTL   -> fewer forced logins, but a larger blast radius if the token store
    //                         leaks, and a longer tail of sessions that survive a password change.
    //
    // This interacts with the GitHub Pages deployment. Cross-origin is permanent there, so the
    // refresh token cannot live in an httpOnly cookie scoped to the API - it sits in
    // sessionStorage, reachable by any script that manages to run on the page. That argues for a
    // shorter refresh TTL than a same-origin, cookie-based deployment would justify, and it argues
    // for rotation being on.
    //
    // Decide before writing Refresh_RotatesToken_OldOneRejected - the assertions encode these
    // values, and so does the SPA's silent-refresh timer.

    /// <summary>
    /// Gets how long an issued access token is accepted for.
    /// </summary>
    /// <value>PROVISIONAL — 15 minutes, pending the owner's call.</value>
    public static TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets how long a session can be refreshed before the user must sign in again.
    /// </summary>
    /// <value>PROVISIONAL — 14 days, pending the owner's call.</value>
    public static TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(14);

    /// <summary>
    /// Gets a value indicating whether using a refresh token supersedes it and issues a new one.
    /// </summary>
    /// <value>PROVISIONAL — <see langword="true"/>, pending the owner's call.</value>
    public static bool RotateOnUse => true;

    /// <summary>
    /// Gets how long a just-superseded refresh token keeps working, so that two tabs refreshing at
    /// the same moment do not log each other out. Ignored when <see cref="RotateOnUse"/> is
    /// <see langword="false"/>.
    /// </summary>
    /// <value>PROVISIONAL — 30 seconds, pending the owner's call.</value>
    public static TimeSpan RotationGracePeriod => TimeSpan.FromSeconds(30);
}
