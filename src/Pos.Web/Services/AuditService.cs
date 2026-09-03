using Pos.Web.Data;
using Pos.Web.Models;

namespace Pos.Web.Services;

/// <summary>Registra en la bitacora las acciones sensibles del sistema.</summary>
public interface IAuditService
{
    Task LogAsync(string action, string entity, string? entityId = null, string? details = null);
}

public class AuditService(
    ApplicationDbContext db,
    IHttpContextAccessor accessor,
    ILogger<AuditService> logger) : IAuditService
{
    public async Task LogAsync(string action, string entity, string? entityId = null, string? details = null)
    {
        try
        {
            var http = accessor.HttpContext;
            db.AuditLogs.Add(new AuditLog
            {
                Action = action,
                Entity = entity,
                EntityId = entityId,
                Details = details,
                UserId = http?.User.GetUserId(),
                UserName = http?.User.Identity?.Name ?? "sistema",
                IpAddress = http?.Connection.RemoteIpAddress?.ToString(),
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // La bitacora nunca debe tumbar una venta.
            logger.LogWarning(ex, "No se pudo escribir en la bitacora: {Action} {Entity}", action, entity);
        }
    }
}

public static class ClaimsPrincipalExtensions
{
    public static int? GetUserId(this System.Security.Claims.ClaimsPrincipal? user)
    {
        var raw = user?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(raw, out var id) ? id : null;
    }

    public static string GetDisplayName(this System.Security.Claims.ClaimsPrincipal? user) =>
        user?.FindFirst("full_name")?.Value ?? user?.Identity?.Name ?? "sistema";
}
