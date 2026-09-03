using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Web.Data;
using Pos.Web.Models;
using Pos.Web.Security;
using Pos.Web.Services;
using Pos.Web.ViewModels;

namespace Pos.Web.Controllers;

/// <summary>
/// Usuarios, roles y permisos: define qué puede hacer cada persona en el sistema.
/// El rol da los permisos base y a cada usuario se le pueden sumar permisos extra.
/// </summary>
[HasPermission(Permissions.UsersManage)]
public class UsersController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IAuditService audit) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Usuarios y permisos";

        var users = await userManager.Users.OrderBy(u => u.FullName).ToListAsync();
        var rows = new List<UserRowViewModel>();

        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            var claims = await userManager.GetClaimsAsync(user);
            rows.Add(new UserRowViewModel
            {
                Id = user.Id,
                UserName = user.UserName ?? string.Empty,
                FullName = user.FullName,
                Email = user.Email,
                Role = roles.FirstOrDefault() ?? "Sin rol",
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                ExtraPermissions = claims.Count(c => c.Type == Permissions.ClaimType)
            });
        }

        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        ViewData["Title"] = "Nuevo usuario";
        return View("Form", new UserFormViewModel
        {
            AvailableRoles = await RoleNamesAsync(),
            Role = Roles.Cashier,
            IsActive = true
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserFormViewModel model)
    {
        ViewData["Title"] = "Nuevo usuario";
        model.AvailableRoles = await RoleNamesAsync();
        model.RolePermissions = await RolePermissionsAsync(model.Role);

        if (string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError(nameof(model.Password), "Asigne una contraseña inicial.");
        }
        if (await userManager.FindByNameAsync(model.UserName.Trim()) is not null)
        {
            ModelState.AddModelError(nameof(model.UserName), "Ya existe un usuario con ese nombre.");
        }
        if (!ModelState.IsValid) return View("Form", model);

        var user = new ApplicationUser
        {
            UserName = model.UserName.Trim(),
            FullName = model.FullName.Trim(),
            Email = model.Email,
            EmailConfirmed = true,
            DocumentId = model.DocumentId,
            IsActive = model.IsActive
        };

        var result = await userManager.CreateAsync(user, model.Password!);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, AccountController.Translate(error));
            return View("Form", model);
        }

        await userManager.AddToRoleAsync(user, model.Role);
        await SyncExtraPermissionsAsync(user, model.Role, model.ExtraPermissions);

        await audit.LogAsync("UsuarioCreado", "Usuario", user.Id.ToString(),
            $"{user.UserName} con rol {model.Role}");
        TempData["Success"] = $"El usuario {user.UserName} fue creado.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        var roles = await userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? Roles.Cashier;
        var claims = await userManager.GetClaimsAsync(user);

        ViewData["Title"] = $"Editar {user.UserName}";
        return View("Form", new UserFormViewModel
        {
            Id = user.Id,
            UserName = user.UserName ?? string.Empty,
            FullName = user.FullName,
            Email = user.Email,
            DocumentId = user.DocumentId,
            Role = role,
            IsActive = user.IsActive,
            AvailableRoles = await RoleNamesAsync(),
            RolePermissions = await RolePermissionsAsync(role),
            ExtraPermissions = claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToList(),
            IsCurrentUser = user.Id == User.GetUserId()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UserFormViewModel model)
    {
        var user = await userManager.FindByIdAsync(model.Id.ToString());
        if (user is null) return NotFound();

        ViewData["Title"] = $"Editar {user.UserName}";
        model.AvailableRoles = await RoleNamesAsync();
        model.RolePermissions = await RolePermissionsAsync(model.Role);
        model.IsCurrentUser = user.Id == User.GetUserId();

        // Nunca se debe poder dejar el sistema sin ningún administrador activo.
        var wasAdmin = await userManager.IsInRoleAsync(user, Roles.Admin);
        var losesAdmin = wasAdmin && (model.Role != Roles.Admin || !model.IsActive);
        if (losesAdmin && await CountActiveAdminsAsync() <= 1)
        {
            ModelState.AddModelError(string.Empty,
                "Este es el único administrador activo. Cree otro antes de cambiarle el rol o desactivarlo.");
        }

        if (!ModelState.IsValid) return View("Form", model);

        user.FullName = model.FullName.Trim();
        user.Email = model.Email;
        user.DocumentId = model.DocumentId;
        user.IsActive = model.IsActive;

        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded)
        {
            foreach (var error in update.Errors) ModelState.AddModelError(string.Empty, AccountController.Translate(error));
            return View("Form", model);
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        if (!currentRoles.Contains(model.Role))
        {
            await userManager.RemoveFromRolesAsync(user, currentRoles);
            await userManager.AddToRoleAsync(user, model.Role);
        }

        await SyncExtraPermissionsAsync(user, model.Role, model.ExtraPermissions);

        if (!string.IsNullOrWhiteSpace(model.Password))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var reset = await userManager.ResetPasswordAsync(user, token, model.Password);
            if (!reset.Succeeded)
            {
                foreach (var error in reset.Errors) ModelState.AddModelError(string.Empty, AccountController.Translate(error));
                return View("Form", model);
            }
            await audit.LogAsync("ContrasenaReiniciada", "Usuario", user.Id.ToString(),
                $"El administrador cambió la contraseña de {user.UserName}");
        }

        await audit.LogAsync("UsuarioEditado", "Usuario", user.Id.ToString(),
            $"{user.UserName} con rol {model.Role}");
        TempData["Success"] = $"El usuario {user.UserName} fue actualizado.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        if (user.Id == User.GetUserId())
        {
            TempData["Error"] = "No puede desactivar su propio usuario.";
            return RedirectToAction(nameof(Index));
        }

        if (user.IsActive && await userManager.IsInRoleAsync(user, Roles.Admin) && await CountActiveAdminsAsync() <= 1)
        {
            TempData["Error"] = "Es el único administrador activo. Cree otro antes de desactivarlo.";
            return RedirectToAction(nameof(Index));
        }

        user.IsActive = !user.IsActive;
        await userManager.UpdateAsync(user);

        await audit.LogAsync(user.IsActive ? "UsuarioActivado" : "UsuarioDesactivado",
            "Usuario", user.Id.ToString(), user.UserName ?? string.Empty);

        TempData["Success"] = user.IsActive
            ? $"{user.UserName} fue activado."
            : $"{user.UserName} fue desactivado y ya no puede ingresar.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Permisos base de cada rol. Cambiarlos afecta a todos sus usuarios.</summary>
    [HttpGet]
    public async Task<IActionResult> RolesPermissions()
    {
        ViewData["Title"] = "Permisos por rol";

        var roles = await roleManager.Roles.OrderBy(r => r.Name).ToListAsync();
        var model = new List<RolePermissionsViewModel>();

        foreach (var role in roles)
        {
            var claims = await roleManager.GetClaimsAsync(role);
            model.Add(new RolePermissionsViewModel
            {
                RoleId = role.Id,
                RoleName = role.Name ?? string.Empty,
                Description = role.Description,
                UserCount = (await userManager.GetUsersInRoleAsync(role.Name ?? string.Empty)).Count,
                Permissions = claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToList()
            });
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RolesPermissions(int roleId, List<string>? permissions)
    {
        var role = await roleManager.FindByIdAsync(roleId.ToString());
        if (role is null) return NotFound();

        if (role.Name == Roles.Admin)
        {
            TempData["Error"] = "El rol Administrador siempre conserva todos los permisos.";
            return RedirectToAction(nameof(RolesPermissions));
        }

        var wanted = (permissions ?? []).Where(p => Permissions.Values.Contains(p)).Distinct().ToList();
        var current = (await roleManager.GetClaimsAsync(role))
            .Where(c => c.Type == Permissions.ClaimType)
            .ToList();

        foreach (var claim in current.Where(c => !wanted.Contains(c.Value)))
        {
            await roleManager.RemoveClaimAsync(role, claim);
        }
        foreach (var permission in wanted.Where(p => current.All(c => c.Value != p)))
        {
            await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission));
        }

        await audit.LogAsync("PermisosRol", "Rol", role.Id.ToString(),
            $"{role.Name}: {wanted.Count} permisos");
        TempData["Success"] =
            $"Permisos del rol {role.Name} actualizados. Los usuarios los verán al volver a ingresar.";
        return RedirectToAction(nameof(RolesPermissions));
    }

    /// <summary>Bitácora de acciones sensibles del sistema.</summary>
    [HttpGet]
    [HasPermission(Permissions.AuditView)]
    public async Task<IActionResult> Audit(DateTime? from, DateTime? to, string? accion, int page = 1)
    {
        const int pageSize = 60;
        var start = (from ?? DateTime.Now.AddDays(-7)).Date;
        var end = (to ?? DateTime.Now).Date.AddDays(1);

        var query = db.AuditLogs
            .Where(a => a.CreatedAt >= start.ToUniversalTime() && a.CreatedAt < end.ToUniversalTime());

        if (!string.IsNullOrWhiteSpace(accion)) query = query.Where(a => a.Action == accion);

        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        ViewData["Title"] = "Bitácora del sistema";
        ViewBag.From = start;
        ViewBag.To = end.AddDays(-1);
        ViewBag.Action = accion;
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.Total = total;
        ViewBag.Actions = await db.AuditLogs.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();

        return View(await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .AsNoTracking().ToListAsync());
    }

    private async Task<List<string>> RoleNamesAsync() =>
        await roleManager.Roles.OrderBy(r => r.Name).Select(r => r.Name ?? string.Empty).ToListAsync();

    private async Task<List<string>> RolePermissionsAsync(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName)) return [];
        if (roleName == Roles.Admin) return Permissions.Values.ToList();

        var role = await roleManager.FindByNameAsync(roleName);
        if (role is null) return [];

        return (await roleManager.GetClaimsAsync(role))
            .Where(c => c.Type == Permissions.ClaimType)
            .Select(c => c.Value)
            .ToList();
    }

    /// <summary>Guarda solo los permisos extra: los que ya trae el rol no se duplican.</summary>
    private async Task SyncExtraPermissionsAsync(ApplicationUser user, string roleName, List<string> wanted)
    {
        var fromRole = await RolePermissionsAsync(roleName);
        var extras = wanted
            .Where(p => Permissions.Values.Contains(p) && !fromRole.Contains(p))
            .Distinct()
            .ToList();

        var current = (await userManager.GetClaimsAsync(user))
            .Where(c => c.Type == Permissions.ClaimType)
            .ToList();

        foreach (var claim in current.Where(c => !extras.Contains(c.Value)))
        {
            await userManager.RemoveClaimAsync(user, claim);
        }
        foreach (var permission in extras.Where(p => current.All(c => c.Value != p)))
        {
            await userManager.AddClaimAsync(user, new Claim(Permissions.ClaimType, permission));
        }
    }

    private async Task<int> CountActiveAdminsAsync()
    {
        var admins = await userManager.GetUsersInRoleAsync(Roles.Admin);
        return admins.Count(a => a.IsActive);
    }
}
