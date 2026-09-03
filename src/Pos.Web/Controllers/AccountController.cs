using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Pos.Web.Models;
using Pos.Web.Security;
using Pos.Web.Services;
using Pos.Web.ViewModels;

namespace Pos.Web.Controllers;

/// <summary>Acceso al sistema, perfil del usuario y cambio de contraseña.</summary>
public class AccountController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IAuditService audit,
    ILogger<AccountController> logger) : Controller
{
    [HttpGet]
    [AllowAnonymous]
    [Route("Cuenta/Ingresar")]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Pos");
        }

        ViewData["Title"] = "Ingresar";
        ViewBag.ShowDefaultCredentials = await IsFirstRunAsync();
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    /// <summary>
    /// Muestra la ayuda del primer ingreso solo mientras exista un unico usuario
    /// y siga teniendo la contrasena inicial. Deja de mostrarse apenas se cambia.
    /// </summary>
    private async Task<bool> IsFirstRunAsync()
    {
        try
        {
            if (userManager.Users.Count() != 1) return false;
            var admin = await userManager.FindByNameAsync("admin");
            return admin is not null && await userManager.CheckPasswordAsync(admin, "Admin123*");
        }
        catch
        {
            return false;
        }
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [Route("Cuenta/Ingresar")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        ViewData["Title"] = "Ingresar";
        ViewBag.ShowDefaultCredentials = await IsFirstRunAsync();
        if (!ModelState.IsValid) return View(model);

        var user = await userManager.FindByNameAsync(model.UserName.Trim());
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Usuario o contraseña incorrectos.");
            return View(model);
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "Su usuario está desactivado. Consulte con el administrador.");
            return View(model);
        }

        var result = await signInManager.PasswordSignInAsync(
            user, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            user.LastLoginAt = DateTime.UtcNow;
            await userManager.UpdateAsync(user);
            await audit.LogAsync("Ingreso", "Usuario", user.Id.ToString(), $"Inicio de sesión de {user.UserName}");
            logger.LogInformation("Ingreso correcto de {User}", user.UserName);

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }
            return RedirectToAction("Index", "Pos");
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty,
                "Demasiados intentos fallidos. Espere 10 minutos e intente nuevamente.");
            return View(model);
        }

        ModelState.AddModelError(string.Empty, "Usuario o contraseña incorrectos.");
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Cuenta/Salir")]
    public async Task<IActionResult> Logout()
    {
        await audit.LogAsync("Salida", "Usuario", User.GetUserId()?.ToString(), "Cierre de sesión");
        await signInManager.SignOutAsync();
        HttpContext.Session.Clear();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [AllowAnonymous]
    [Route("Cuenta/SinPermiso")]
    public IActionResult AccessDenied()
    {
        ViewData["Title"] = "Acceso denegado";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));

        ViewData["Title"] = "Mi perfil";
        var roles = await userManager.GetRolesAsync(user);
        return View(new ProfileViewModel
        {
            FullName = user.FullName,
            Email = user.Email,
            DocumentId = user.DocumentId,
            UserName = user.UserName ?? string.Empty,
            Role = string.Join(", ", roles),
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            Permissions = User.Claims
                .Where(c => c.Type == Permissions.ClaimType)
                .Select(c => c.Value)
                .ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel model)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));

        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "Mi perfil";
            model.UserName = user.UserName ?? string.Empty;
            return View(model);
        }

        user.FullName = model.FullName;
        user.Email = model.Email;
        user.DocumentId = model.DocumentId;
        await userManager.UpdateAsync(user);
        await signInManager.RefreshSignInAsync(user);

        TempData["Success"] = "Sus datos fueron actualizados.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpGet]
    public IActionResult ChangePassword()
    {
        ViewData["Title"] = "Cambiar contraseña";
        return View(new ChangePasswordViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        ViewData["Title"] = "Cambiar contraseña";
        if (!ModelState.IsValid) return View(model);

        var user = await userManager.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));

        var result = await userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, Translate(error));
            }
            return View(model);
        }

        await signInManager.RefreshSignInAsync(user);
        await audit.LogAsync("CambioContrasena", "Usuario", user.Id.ToString(), "Cambió su propia contraseña");
        TempData["Success"] = "Su contraseña fue cambiada correctamente.";
        return RedirectToAction(nameof(Profile));
    }

    /// <summary>Traduce al español los mensajes de Identity que ve el usuario.</summary>
    internal static string Translate(IdentityError error) => error.Code switch
    {
        "PasswordMismatch" => "La contraseña actual no es correcta.",
        "PasswordTooShort" => "La contraseña es demasiado corta.",
        "PasswordRequiresDigit" => "La contraseña debe incluir al menos un número.",
        "PasswordRequiresLower" => "La contraseña debe incluir al menos una letra minúscula.",
        "PasswordRequiresUpper" => "La contraseña debe incluir al menos una letra mayúscula.",
        "PasswordRequiresNonAlphanumeric" => "La contraseña debe incluir un símbolo.",
        "DuplicateUserName" => "Ya existe un usuario con ese nombre.",
        "DuplicateEmail" => "Ya existe un usuario con ese correo.",
        "InvalidUserName" => "El nombre de usuario contiene caracteres no permitidos.",
        "InvalidEmail" => "El correo electrónico no es válido.",
        _ => error.Description
    };
}
