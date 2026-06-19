using Brewvio.Models;
using Brewvio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Brewvio.Controllers;

// Manager-only access to the audit log of recorded system actions.
[ApiController]
[Route("api/audit")]
[Authorize(Roles = Roles.Manager)]
public class AuditController(AuditService audit) : ControllerBase
{
    // Returns the most recent audit log entries.
    // take: max number of entries to return (clamped to 1-1000)
    // ct: cancellation token
    // returns: 200 OK with the list of audit entries
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int take = 200, CancellationToken ct = default) =>
        // Clamp caller-supplied page size to a sane range so a huge ?take= can't pull the whole table.
        Ok(await audit.ListAsync(Math.Clamp(take, 1, 1000), ct));
}
