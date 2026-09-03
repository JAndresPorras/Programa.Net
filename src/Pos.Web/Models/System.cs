using System.ComponentModel.DataAnnotations;

namespace Pos.Web.Models;

/// <summary>Configuracion del sistema editable desde el menu Configuracion (solo admin).</summary>
public class AppSetting
{
    public int Id { get; set; }

    [Required]
    [StringLength(80)]
    public string Key { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Value { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int? UpdatedByUserId { get; set; }

    [StringLength(150)]
    public string? UpdatedByUserName { get; set; }
}

/// <summary>Bitacora de acciones sensibles: quien hizo que y cuando.</summary>
public class AuditLog
{
    public int Id { get; set; }

    public int? UserId { get; set; }

    [StringLength(150)]
    public string UserName { get; set; } = string.Empty;

    [StringLength(60)]
    public string Action { get; set; } = string.Empty;

    [StringLength(60)]
    public string Entity { get; set; } = string.Empty;

    [StringLength(40)]
    public string? EntityId { get; set; }

    [StringLength(2000)]
    public string? Details { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [StringLength(60)]
    public string? IpAddress { get; set; }
}
