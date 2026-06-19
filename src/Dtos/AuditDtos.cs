namespace Brewvio.Dtos;

// One audit-trail entry recording who performed which action, when, and any details.
public record AuditLogDto(int Id, DateTime Timestamp, string Username, string Action, string Details);
