using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Pos.Web.Models;

namespace Pos.Web.ViewModels;

/// <summary>Alta y edición de un producto del maestro de códigos de barras.</summary>
public class ProductFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "El código del producto es obligatorio")]
    [StringLength(40)]
    [Display(Name = "Código de producto")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre es obligatorio")]
    [StringLength(150)]
    [Display(Name = "Nombre del producto")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Descripción")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Seleccione una categoría")]
    [Display(Name = "Categoría")]
    public int CategoryId { get; set; }

    [Range(0, 9999999, ErrorMessage = "El precio no puede ser negativo")]
    [Display(Name = "Precio de venta")]
    public decimal Price { get; set; }

    [Range(0, 9999999, ErrorMessage = "El costo no puede ser negativo")]
    [Display(Name = "Costo (opcional)")]
    public decimal? Cost { get; set; }

    [Display(Name = "Motivo del cambio de precio")]
    [StringLength(250)]
    public string? PriceChangeReason { get; set; }

    [Display(Name = "Stock actual")]
    public decimal Stock { get; set; }

    [Display(Name = "Stock mínimo")]
    public decimal MinStock { get; set; }

    [Required]
    [StringLength(10)]
    [Display(Name = "Unidad de medida")]
    public string Unit { get; set; } = "UND";

    [Display(Name = "Se vende por peso (permite decimales)")]
    public bool SoldByWeight { get; set; }

    [Display(Name = "Se fabrica en la planta de producción")]
    public bool IsProduced { get; set; } = true;

    [Display(Name = "Producto activo")]
    public bool IsActive { get; set; } = true;

    /// <summary>Códigos de barras separados por coma, espacio o salto de línea.</summary>
    [Display(Name = "Códigos de barras")]
    public string? BarcodesRaw { get; set; }

    [Display(Name = "Imagen del producto (opcional)")]
    public IFormFile? ImageFile { get; set; }

    public string? ImagePath { get; set; }

    [Display(Name = "Quitar la imagen actual")]
    public bool RemoveImage { get; set; }

    public decimal OriginalPrice { get; set; }

    /// <summary>Stock que tenia el producto al abrir el formulario.</summary>
    public decimal OriginalStock { get; set; }

    /// <summary>Esta en el inventario personalizado: su stock se maneja desde Inventario.</summary>
    public bool TrackInventory { get; set; }
    public List<Category> Categories { get; set; } = [];
    public List<PriceHistory> PriceHistory { get; set; } = [];
    public bool CanChangePrice { get; set; } = true;
}

public class ProductIndexViewModel
{
    public List<Product> Products { get; set; } = [];
    public List<Category> Categories { get; set; } = [];
    public string? Term { get; set; }
    public int? CategoryId { get; set; }
    public bool OnlyInactive { get; set; }
    public bool OnlyLowStock { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
}
