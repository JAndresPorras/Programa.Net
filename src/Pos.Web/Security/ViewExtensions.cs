using System.Security.Claims;

namespace Pos.Web.Security;

public static class UserPermissionExtensions
{
    /// <summary>Permite consultar un permiso desde las vistas y los controladores.</summary>
    public static bool HasPermission(this ClaimsPrincipal? user, string permission)
    {
        if (user?.Identity?.IsAuthenticated != true) return false;
        if (user.IsInRole(Roles.Admin)) return true;
        return user.Claims.Any(c =>
            c.Type == Permissions.ClaimType &&
            string.Equals(c.Value, permission, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Verdadero si el usuario tiene al menos uno de los permisos indicados.</summary>
    public static bool HasAnyPermission(this ClaimsPrincipal? user, params string[] permissions) =>
        permissions.Any(user.HasPermission);

    /// <summary>Rol principal del usuario para mostrarlo en el encabezado.</summary>
    public static string MainRole(this ClaimsPrincipal? user) =>
        user?.FindFirst(ClaimTypes.Role)?.Value ?? "Usuario";
}
