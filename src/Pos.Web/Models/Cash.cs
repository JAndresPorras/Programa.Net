using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pos.Web.Models;

public enum CashSessionStatus
{
    Open = 1,
    Closed = 2
}

public enum CashMovementType
{
    /// <summary>Ingreso de dinero a la caja que no proviene de una venta.</summary>
    In = 1,

    /// <summary>Retiro de dinero de la caja (gastos, envio a boveda...).</summary>
    Out = 2
}

/// <summary>
/// Turno de caja de una cajera. Se abre al iniciar el dia con el fondo fijo
/// y se cierra al final del dia con la entrega de dinero al dueno.
/// </summary>
public class CashSession
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public ApplicationUser? User { get; set; }

    [StringLength(150)]
    public string UserName { get; set; } = string.Empty;

    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Fondo de caja con el que arranca el turno (sencillo).</summary>
    [Column(TypeName = "decimal(18,4)")]
    [Display(Name = "Fondo inicial")]
    public decimal OpeningAmount { get; set; }

    public DateTime? ClosedAt { get; set; }

    /// <summary>Dinero fisico contado por la cajera al cerrar.</summary>
    [Column(TypeName = "decimal(18,4)")]
    [Display(Name = "Efectivo contado")]
    public decimal? CountedAmount { get; set; }

    /// <summary>Lo que el sistema espera en caja: fondo + ventas en efectivo + ingresos - egresos.</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? ExpectedAmount { get; set; }

    /// <summary>Diferencia contado - esperado (positivo sobrante, negativo faltante).</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? Difference { get; set; }

    /// <summary>Dinero que se le entrega al dueno (contado menos el fondo que queda para manana).</summary>
    [Column(TypeName = "decimal(18,4)")]
    [Display(Name = "Entregado al dueno")]
    public decimal? DeliveredToOwner { get; set; }

    /// <summary>Fondo que se deja en caja para el dia siguiente.</summary>
    [Column(TypeName = "decimal(18,4)")]
    [Display(Name = "Fondo que queda en caja")]
    public decimal? LeftInDrawer { get; set; }

    public int? ClosedByUserId { get; set; }

    [StringLength(150)]
    public string? ClosedByUserName { get; set; }

    [StringLength(3)]
    public string CurrencyCode { get; set; } = "PEN";

    [StringLength(500)]
    [Display(Name = "Observaciones")]
    public string? Notes { get; set; }

    public CashSessionStatus Status { get; set; } = CashSessionStatus.Open;

    public ICollection<Sale> Sales { get; set; } = new List<Sale>();

    public ICollection<CashMovement> Movements { get; set; } = new List<CashMovement>();
}

/// <summary>Ingreso o retiro de efectivo que no es una venta.</summary>
public class CashMovement
{
    public int Id { get; set; }

    public int CashSessionId { get; set; }

    public CashSession? CashSession { get; set; }

    public CashMovementType Type { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    [Range(0.01, 99999999, ErrorMessage = "El monto debe ser mayor que cero")]
    [Display(Name = "Monto")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Indique el motivo del movimiento")]
    [StringLength(250)]
    [Display(Name = "Motivo")]
    public string Reason { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int UserId { get; set; }

    [StringLength(150)]
    public string UserName { get; set; } = string.Empty;
}
