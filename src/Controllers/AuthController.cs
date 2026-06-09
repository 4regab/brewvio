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
    // Lightweight fixed-window rate limiter for the login endpoint, keyed by client IP.
    // Defense-in-depth only: the authoritative throttle is the API Gateway HTTP API stage
    // limit (DefaultRouteSettings in template.yaml), which applies globally across all instances.
    // This local check trims obvious brute-force bursts within a single warm instance before
    // they reach the service/DB (PBKDF2 is intentionally expensive — unthrottled attempts are
    // also a CPU-amplification vector).
    private static readonly ConcurrentDictionary<string, (int Count, DateTime Window)> _hits = new();
    private const int LoginLimit = 20;   // max sign-in attempts per window per IP
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
    public async Task<IActionResult> Login(LoginRequest req, CancellationToken ct)
    {
        if (RateLimited("login", LoginLimit))
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { message = "Too many sign-in attempts. Please try again later." });

        var outcome = await auth.LoginAsync(req.Username, req.Password, ct);
        return outcome.Response is null
            ? Unauthorized(new { message = outcome.Error ?? "Invalid username or password." })
            : Ok(outcome.Response);
    }

    // Lets the SPA validate a stored token and recover the current user/role after a refresh.
    [HttpGet("me")]
    public IActionResult Me() => Ok(new MeResponse(
        int.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : 0,
        User.FindFirst("name")?.Value ?? "",
        User.FindFirst("fullname")?.Value ?? "",
        User.FindFirst("role")?.Value ?? ""));
}
