using System.ComponentModel.DataAnnotations;

namespace Pos.Web.ViewModels;

public class UserRowViewModel
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int ExtraPermissions { get; set; }
}

public class UserFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "El usuario es obligatorio")]
    [StringLength(60)]
    [RegularExpression("^[a-zA-Z0-9._-]+$",
        ErrorMessage = "Use solo letras, números, punto, guion o guion bajo, sin espacios")]
    [Display(Name = "Usuario para ingresar")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre completo es obligatorio")]
    [StringLength(150)]
    [Display(Name = "Nombre completo")]
    public string FullName { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "El correo no es válido")]
    [Display(Name = "Correo electrónico")]
    public string? Email { get; set; }

    [StringLength(30)]
    [Display(Name = "Documento de identidad")]
    public string? DocumentId { get; set; }

    [Required(ErrorMessage = "Seleccione un rol")]
    [Display(Name = "Rol")]
    public string Role { get; set; } = string.Empty;

    [Display(Name = "Usuario activo")]
    public bool IsActive { get; set; } = true;

    [StringLength(100, MinimumLength = 6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres")]
    [DataType(DataType.Password)]
    [Display(Name = "Contraseña")]
    public string? Password { get; set; }

    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden")]
    [Display(Name = "Repita la contraseña")]
    public string? ConfirmPassword { get; set; }

    /// <summary>Permisos adicionales otorgados a este usuario por encima de su rol.</summary>
    public List<string> ExtraPermissions { get; set; } = [];

    public List<string> RolePermissions { get; set; } = [];
    public List<string> AvailableRoles { get; set; } = [];
    public bool IsCurrentUser { get; set; }
}

public class RolePermissionsViewModel
{
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int UserCount { get; set; }
    public List<string> Permissions { get; set; } = [];
}
