namespace Brewvio.Models;

// Role names used for RBAC (stored on User.Role and the JWT "role" claim).
public static class Roles
{
    public const string Manager = "Manager";
    public const string Cashier = "Cashier";
}
