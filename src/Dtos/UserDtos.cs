using System.Text.Json.Serialization;

namespace Brewvio.Dtos;

// A user account as exposed to the UI (identity, role, and active/status flags).
public record UserDto(int Id, string Username, string FullName, string Role, bool IsActive,
    string Status, DateTime CreatedAt);
// Payload for creating a new user account.
public record CreateUserRequest(string Username, string FullName, string Password, string Role);
// Payload for updating a user's full name, role, and active state.
public record UpdateUserRequest(string FullName, string Role, [property: JsonRequired] bool IsActive);
// Payload carrying a new password for an admin-initiated reset.
public record ResetPasswordRequest(string NewPassword);

// Approval workflow: a pending sign-up awaiting a Manager's decision.
public record PendingUserDto(int Id, string Username, string FullName, string Role, DateTime CreatedAt);
