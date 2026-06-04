using System.Collections.Concurrent;
using Brewvio.Dtos;
using Brewvio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Brewvio.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(AuthService auth) : ControllerBase
{
    // Lightweight fixed-window rate limiter for the anonymous endpoints (register + status poll),
    // keyed by client IP. Prevents sign-up spam / account-enumeration hammering. For a single
    // Lambda this lives per-instance; a production deployment would use API Gateway throttling
    // or a shared store (see api-contract.md → "Security notes").
    private static readonly ConcurrentDictionary<string, (int Count, DateTime Window)> _hits = new();
    private const int RegisterLimit = 5;                       // max sign-ups per window per IP
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private bool RateLimited(string bucket, int limit)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = $"{bucket}:{ip}";
        var now = DateTime.UtcNow;
        var entry = _hits.AddOrUpdate(key,
            _ => (1, now),
            (_, cur) => now - cur.Window > Window ? (1, now) : (cur.Count + 1, cur.Window));
        return entry.Count > limit;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var outcome = await auth.LoginAsync(req.Username, req.Password);
        return outcome.Response is null
            ? Unauthorized(new { message = outcome.Error ?? "Invalid username or password." })
            : Ok(outcome.Response);
    }

    // Self-service sign-up. Anonymous + rate-limited; creates a Pending account that a
    // Manager must approve before the user can sign in.
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        if (RateLimited("register", RegisterLimit))
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { message = "Too many sign-up attempts. Please try again later." });
        return Ok(await auth.RegisterAsync(req));
    }

    // Polled by the "Authenticating…" screen to learn when a pending account is approved/rejected.
    // Anonymous (the user isn't signed in yet) and rate-limited.
    [AllowAnonymous]
    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return BadRequest(new { message = "Username is required." });
        if (RateLimited("status", 60))
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = "Too many requests." });
        return await auth.GetAccountStatusAsync(username) is { } s ? Ok(s) : NotFound();
    }

    // Lets the SPA validate a stored token and recover the current user/role after a refresh.
    [HttpGet("me")]
    public IActionResult Me() => Ok(new MeResponse(
        int.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : 0,
        User.FindFirst("name")?.Value ?? "",
        User.FindFirst("fullname")?.Value ?? "",
        User.FindFirst("role")?.Value ?? ""));
}
