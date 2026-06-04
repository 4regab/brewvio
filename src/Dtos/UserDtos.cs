using System.Text.Json.Serialization;

namespace Brewvio.Dtos;

public record UserDto(int Id, string Username, string FullName, string Role, bool IsActive,
    string Status, DateTime CreatedAt);
public record CreateUserRequest(string Username, string FullName, string Password, string Role);
public record UpdateUserRequest(string FullName, string Role, [property: JsonRequired] bool IsActive);
public record ResetPasswordRequest(string NewPassword);

// Approval workflow: a pending sign-up awaiting a Manager's decision.
public record PendingUserDto(int Id, string Username, string FullName, string Role, DateTime CreatedAt);
