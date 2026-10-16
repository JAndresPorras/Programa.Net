using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pos.Web.Models;

/// <summary>Categoria del producto (panaderia, bebidas, abarrotes...).</summary>
public class Category
{
    public int Id { get; set; }

    [Required(ErrorMessage = "El nombre es obligatorio")]
    [StringLength(80)]
    [Display(Name = "Nombre")]
    public string Name { get; set; } = string.Empty;

    [StringLength(250)]
    [Display(Name = "Descripcion")]
    public string? Description { get; set; }

    /// <summary>Color hexadecimal usado para identificar la categoria en pantalla.</summary>
    [StringLength(9)]
    [Display(Name = "Color")]
    public string Color { get; set; } = "#2563eb";

    [Display(Name = "Activa")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Categoria principal opcional. Permite agrupar, por ejemplo, Yogures y
    /// Quesos dentro de Lacteos. Solo hay dos niveles: una categoria principal
    /// no puede tener a su vez otra principal.
    /// </summary>
    [Display(Name = "Categoria principal")]
    public int? ParentCategoryId { get; set; }

    public Category? ParentCategory { get; set; }

    public ICollection<Category> SubCategories { get; set; } = new List<Category>();

    /// <summary>
    /// Si es true, sus productos se pueden elegir en la vista de inventario.
    /// No activa el control por si solo: el usuario elige producto por producto.
    /// </summary>
    [Display(Name = "Permite inventario personalizado")]
    public bool InventoryEnabled { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Product> Products { get; set; } = new List<Product>();

    /// <summary>Nombre con su categoria principal: "Lacteos › Quesos".</summary>
    [NotMapped]
    public string FullName => ParentCategory is null ? Name : $"{ParentCategory.Name} › {Name}";
}

/// <summary>
/// Maestro de productos. Cada producto puede tener uno o varios codigos de barras
/// asociados (por ejemplo presentaciones distintas del mismo articulo).
/// </summary>
public class Product
{
    public int Id { get; set; }

    [Required(ErrorMessage = "El codigo del producto es obligatorio")]
    [StringLength(40)]
    [Display(Name = "Codigo de producto")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre del producto es obligatorio")]
    [StringLength(150)]
    [Display(Name = "Nombre del producto")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Descripcion")]
    public string? Description { get; set; }

    [Display(Name = "Categoria")]
    public int CategoryId { get; set; }

    public Category? Category { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    [Range(0, 9999999, ErrorMessage = "El precio no puede ser negativo")]
    [Display(Name = "Precio de venta")]
    public decimal Price { get; set; }

    /// <summary>Costo de compra o produccion. Opcional, se usa para el margen en reportes.</summary>
    [Column(TypeName = "decimal(18,4)")]
    [Range(0, 9999999, ErrorMessage = "El costo no puede ser negativo")]
    [Display(Name = "Costo")]
    public decimal? Cost { get; set; }

    /// <summary>Ruta relativa de la imagen (opcional).</summary>
    [StringLength(260)]
    [Display(Name = "Imagen")]
    public string? ImagePath { get; set; }

    [Column(TypeName = "decimal(18,3)")]
    [Display(Name = "Stock actual")]
    public decimal Stock { get; set; }

    [Column(TypeName = "decimal(18,3)")]
    [Display(Name = "Stock minimo")]
    public decimal MinStock { get; set; }

    /// <summary>Unidad de medida: UND, KG, LT...</summary>
    [StringLength(10)]
    [Display(Name = "Unidad")]
    public string Unit { get; set; } = "UND";

    /// <summary>Si es true el producto se vende con decimales (pesado).</summary>
    [Display(Name = "Se vende por peso")]
    public bool SoldByWeight { get; set; }

    /// <summary>Si es true entra en el pedido a la planta de produccion.</summary>
    [Display(Name = "Se produce en planta")]
    public bool IsProduced { get; set; } = true;

    [Display(Name = "Activo")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Forma parte del inventario personalizado: las ventas descuentan su Stock
    /// automaticamente y nunca se bloquean por falta de existencias digitales.
    /// </summary>
    [Display(Name = "Controlar inventario")]
    public bool TrackInventory { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? CreatedByUserId { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedByUserId { get; set; }

    public ICollection<Barcode> Barcodes { get; set; } = new List<Barcode>();

    public ICollection<PriceHistory> PriceHistories { get; set; } = new List<PriceHistory>();
}

/// <summary>Codigo de barras leido por la pistola escaneadora.</summary>
public class Barcode
{
    public int Id { get; set; }

    [Required(ErrorMessage = "El codigo de barras es obligatorio")]
    [StringLength(64)]
    [Display(Name = "Codigo de barras")]
    public string Code { get; set; } = string.Empty;

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>Codigo principal que se imprime en la etiqueta.</summary>
    [Display(Name = "Principal")]
    public bool IsPrimary { get; set; }

    /// <summary>EAN13, CODE128, INTERNO...</summary>
    [StringLength(20)]
    [Display(Name = "Simbologia")]
    public string Symbology { get; set; } = "EAN13";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? CreatedByUserId { get; set; }
}

/// <summary>Historial de cambios de precio: que cambio, cuando y quien lo hizo.</summary>
public class PriceHistory
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal OldPrice { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal NewPrice { get; set; }

    /// <summary>Fecha y hora exacta del cambio (UTC).</summary>
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    public int? ChangedByUserId { get; set; }

    public ApplicationUser? ChangedByUser { get; set; }

    /// <summary>Nombre del usuario guardado como copia por si el usuario se elimina.</summary>
    [StringLength(150)]
    public string ChangedByUserName { get; set; } = string.Empty;

    [StringLength(250)]
    [Display(Name = "Motivo")]
    public string? Reason { get; set; }

    public decimal Difference => NewPrice - OldPrice;

    public decimal PercentChange => OldPrice == 0 ? 0 : Math.Round((NewPrice - OldPrice) / OldPrice * 100m, 2);
}
