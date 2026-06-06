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
    // keyed by client IP. Defense-in-depth only: it lives per-Lambda-instance, so the authoritative
    // throttle is the API Gateway HTTP API stage limit (DefaultRouteSettings in template.yaml),
    // which applies globally across all instances. This local check still trims obvious abuse
    // bursts within a single warm instance before they reach the service/DB.
    private static readonly ConcurrentDictionary<string, (int Count, DateTime Window)> _hits = new();
    private const int RegisterLimit = 5;                       // max sign-ups per window per IP
    // Login is more lenient than register: a POS often has several terminals behind a single
    // NAT'd public IP, so many staff legitimately sign in from one address. This per-instance
    // check only trims obvious brute-force bursts; the authoritative throttle is the API Gateway
    // stage limit. Tune LoginLimit down if terminals don't share an egress IP.
    private const int LoginLimit = 20;                         // max sign-in attempts per window per IP
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
        // Rate-limit by IP before hitting the DB / password hasher (PBKDF2 is intentionally
        // expensive, so unthrottled login attempts are also a CPU-amplification vector).
        if (RateLimited("login", LoginLimit))
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { message = "Too many sign-in attempts. Please try again later." });

        var outcome = await auth.LoginAsync(req.Username, req.Password, ct);
        return outcome.Response is null
            ? Unauthorized(new { message = outcome.Error ?? "Invalid username or password." })
            : Ok(outcome.Response);
    }

    // Self-service sign-up. Anonymous + rate-limited; creates a Pending account that a
    // Manager must approve before the user can sign in.
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req, CancellationToken ct)
    {
        if (RateLimited("register", RegisterLimit))
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { message = "Too many sign-up attempts. Please try again later." });
        return Ok(await auth.RegisterAsync(req, ct));
    }

    // Polled by the "Authenticating…" screen to learn when a pending account is approved/rejected.
    // Anonymous (the user isn't signed in yet) and rate-limited.
    [AllowAnonymous]
    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] string username, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username)) return BadRequest(new { message = "Username is required." });
        if (RateLimited("status", 60))
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = "Too many requests." });
        return await auth.GetAccountStatusAsync(username, ct) is { } s ? Ok(s) : NotFound();
    }

    // Lets the SPA validate a stored token and recover the current user/role after a refresh.
    [HttpGet("me")]
    public IActionResult Me() => Ok(new MeResponse(
        int.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : 0,
        User.FindFirst("name")?.Value ?? "",
        User.FindFirst("fullname")?.Value ?? "",
        User.FindFirst("role")?.Value ?? ""));
}
