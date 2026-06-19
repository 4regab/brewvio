using System.Security.Claims;

namespace Brewvio.Helpers;

// Reads the authenticated user from the JWT claims (set in AuthService: sub, name, role).
public class CurrentUser(IHttpContextAccessor accessor)
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public int Id => int.TryParse(Principal?.FindFirst("sub")?.Value, out var id) ? id : 0;
    public string Username => Principal?.FindFirst("name")?.Value ?? "system";
    public string Role => Principal?.FindFirst("role")?.Value ?? "";
}
