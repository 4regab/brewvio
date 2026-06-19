namespace Brewvio.Models;

// A system account (Manager or Cashier) with credentials, RBAC role, and lifecycle status.
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = Roles.Cashier;   // "Manager" or "Cashier"
    public bool IsActive { get; set; } = true;

    // Self-service registration workflow: a new sign-up lands as Pending and a Manager
    // must Approve (-> Active) or Reject. Only Active users may sign in.
    public string Status { get; set; } = UserStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Tracks when the most recent valid token was issued. Any token whose iat (issued-at)
    // predates this value is considered revoked — used to invalidate sessions immediately when
    // a user is deactivated, rejected, or has their password reset by a manager.
    public DateTime? TokenIssuedAt { get; set; }
}

// Account lifecycle states for the registration/approval workflow.
public static class UserStatus
{
    public const string Pending = "Pending";
    public const string Active = "Active";
    public const string Rejected = "Rejected";
}
