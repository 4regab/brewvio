namespace Brewvio.Dtos;

// API request/response contracts for auth — kept separate from EF entities (Models/).
public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, string Username, string FullName, string Role);
public record MeResponse(int Id, string Username, string FullName, string Role);

// ----- Self-service registration & approval workflow -----
// A sign-up creates a Pending account; a Manager approves (-> Active) or rejects it.
public record RegisterRequest(string Username, string FullName, string Password, string Role);
public record RegisterResponse(int Id, string Username, string Status);

// Polled by the "Authenticating…" screen so the SPA can advance to "Account Approved!"
// (or surface a rejection) without exposing anything but the account state.
public record AccountStatusResponse(string Username, string Status);
