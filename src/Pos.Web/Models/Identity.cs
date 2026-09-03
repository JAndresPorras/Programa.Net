using Microsoft.AspNetCore.Identity;

namespace Pos.Web.Models;

/// <summary>Usuario del sistema (cajera, supervisor, administrador).</summary>
public class ApplicationUser : IdentityUser<int>
{
    [PersonalData]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Documento de identidad / DNI / cedula. Solo informativo.</summary>
    public string? DocumentId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }

    /// <summary>Iniciales para el avatar del encabezado.</summary>
    public string Initials
    {
        get
        {
            var source = string.IsNullOrWhiteSpace(FullName) ? (UserName ?? "?") : FullName;
            var parts = source.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
            return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
        }
    }
}

/// <summary>Rol del sistema. Los permisos finos se guardan como claims del rol.</summary>
public class ApplicationRole : IdentityRole<int>
{
    public ApplicationRole() { }

    public ApplicationRole(string name) : base(name) { }

    public string? Description { get; set; }

    /// <summary>Los roles del sistema no se pueden eliminar.</summary>
    public bool IsSystemRole { get; set; }
}
