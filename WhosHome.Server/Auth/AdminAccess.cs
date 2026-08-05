using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Primitives;
using WhosHome.Server.Configuration;

namespace WhosHome.Server.Auth;

public static class AdminAccess
{
    /// <summary>
    /// A request is admin if the browser holds admin mode, or if it carries the token directly.
    /// The header path is kept because it is the break-glass route: when no browser has admin
    /// mode, curl with the token is the only way back in.
    /// </summary>
    public static async Task<bool> IsAdminAsync(HttpContext context, WhosHomeOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.AdminToken))
        {
            // No token configured means admin is disabled outright, so a misconfigured
            // deployment fails closed rather than open.
            return false;
        }

        if (TokenMatches(context.Request, options))
        {
            return true;
        }

        AuthenticateResult result = await context.AuthenticateAsync(AuthSchemes.Admin);
        return result.Succeeded;
    }

    public static bool TokenMatches(HttpRequest request, WhosHomeOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.AdminToken))
        {
            return false;
        }

        if (!request.Headers.TryGetValue(AuthSchemes.AdminTokenHeader, out StringValues provided))
        {
            return false;
        }

        return ConstantTimeEquals(provided.ToString(), options.AdminToken);
    }

    public static bool ConstantTimeEquals(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));
    }
}
