using Brewvio.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Brewvio.Controllers;

// Unauthenticated health-check endpoint reporting API and database status.
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class HealthController(BrewvioDbContext db) : ControllerBase
{
    // Reports service health, including whether the database is reachable.
    // ct: cancellation token
    // returns: 200 OK with status, service name, and database connectivity ("connected"/"unreachable")
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var dbOk = await db.Database.CanConnectAsync(ct);
        return Ok(new { status = "ok", service = "brewvio-api", database = dbOk ? "connected" : "unreachable" });
    }
}
