using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pos.Web.Models;

public enum SaleStatus
{
    Completed = 1,
    Cancelled = 2
}

public enum PaymentMethod
{
    Cash = 1,
    Card = 2,
    Transfer = 3,
    Mixed = 4
}

/// <summary>Venta cerrada. Cada venta imprime un recibo para el cliente.</summary>
public class Sale
{
    public int Id { get; set; }

    /// <summary>Numero correlativo visible del recibo, ej. B001-000123.</summary>
    [StringLength(30)]
    public string Number { get; set; } = string.Empty;

    public int? CashSessionId { get; set; }

    public CashSession? CashSession { get; set; }

    public int UserId { get; set; }

    public ApplicationUser? User { get; set; }

    /// <summary>Copia del nombre de la cajera para el historico.</summary>
    [StringLength(150)]
    public string UserName { get; set; } = string.Empty;

    public DateTime SoldAt { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "decimal(18,4)")]
    public decimal Subtotal { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal DiscountAmount { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal TaxAmount { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal Total { get; set; }

    /// <summary>Dinero recibido de manos del cliente.</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal AmountReceived { get; set; }

    /// <summary>Vuelto entregado al cliente.</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal ChangeGiven { get; set; }

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    /// <summary>Moneda vigente al momento de la venta (PEN / COP).</summary>
    [StringLength(3)]
    public string CurrencyCode { get; set; } = "PEN";

    public SaleStatus Status { get; set; } = SaleStatus.Completed;

    [StringLength(150)]
    [Display(Name = "Cliente")]
    public string? CustomerName { get; set; }

    [StringLength(30)]
    [Display(Name = "Documento del cliente")]
    public string? CustomerDocument { get; set; }

    [StringLength(300)]
    public string? Notes { get; set; }

    public DateTime? CancelledAt { get; set; }

    public int? CancelledByUserId { get; set; }

    [StringLength(250)]
    public string? CancelReason { get; set; }

    public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();

    [NotMapped]
    public int TotalUnits => Items.Sum(i => (int)Math.Ceiling(i.Quantity));
}

/// <summary>Linea del recibo. Guarda copia de los datos del producto al momento de vender.</summary>
public class SaleItem
{
    public int Id { get; set; }

    public int SaleId { get; set; }

    public Sale? Sale { get; set; }

    public int? ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>Codigo de barras que se escaneo realmente.</summary>
    [StringLength(64)]
    public string? ScannedBarcode { get; set; }

    [StringLength(40)]
    public string ProductCode { get; set; } = string.Empty;

    [StringLength(150)]
    public string ProductName { get; set; } = string.Empty;

    [StringLength(80)]
    public string CategoryName { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,4)")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "decimal(18,3)")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal LineDiscount { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal LineTotal { get; set; }

    [StringLength(10)]
    public string Unit { get; set; } = "UND";
}
