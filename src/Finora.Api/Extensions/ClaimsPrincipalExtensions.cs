using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Finora.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Resolves the authenticated user id from common JWT claim types (NameIdentifier, sub).
    /// </summary>
    public static Guid? GetUserId(this ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return null;

        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? user.FindFirst("sub")?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
