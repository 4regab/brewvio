namespace Brewvio.Dtos;

public record AuditLogDto(int Id, DateTime Timestamp, string Username, string Action, string Details);
