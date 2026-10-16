using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pos.Web.Models;

public enum InventoryMovementType
{
    /// <summary>Entrada manual: suma a lo que habia.</summary>
    Entry = 1,

    /// <summary>Edicion manual: reemplaza la cantidad por la contada.</summary>
    Set = 2,

    /// <summary>Salida por una venta.</summary>
    Sale = 3,

    /// <summary>
    /// Ajuste automatico previo a una venta: el inventario digital no alcanzaba,
    /// pero el producto estaba fisicamente, asi que se sube a lo vendido.
    /// </summary>
    SaleAdjustment = 4,

    /// <summary>Devolucion de las unidades al anular el recibo.</summary>
    SaleCancelled = 5
}

/// <summary>
/// Cada cambio de existencias de un producto del inventario personalizado,
/// con la cantidad antes y despues, quien lo hizo y de que venta viene.
/// </summary>
public class InventoryMovement
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    public InventoryMovementType Type { get; set; }

    /// <summary>Cambio con signo: +10 en una entrada, -3 en una venta.</summary>
    [Column(TypeName = "decimal(18,3)")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(18,3)")]
    public decimal StockBefore { get; set; }

    [Column(TypeName = "decimal(18,3)")]
    public decimal StockAfter { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? UserId { get; set; }

    [StringLength(150)]
    public string UserName { get; set; } = string.Empty;

    public int? SaleId { get; set; }

    [StringLength(250)]
    public string? Note { get; set; }
}
