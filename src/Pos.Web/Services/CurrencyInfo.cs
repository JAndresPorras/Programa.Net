using System.Globalization;

namespace Pos.Web.Services;

/// <summary>
/// Definicion de una moneda soportada por el punto de venta.
/// Se usa para formatear importes, para los botones de billetes rapidos
/// y para el arqueo de caja al cierre del dia.
/// </summary>
public sealed class CurrencyInfo
{
    public required string Code { get; init; }
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public required string CultureName { get; init; }
    public int Decimals { get; init; } = 2;

    /// <summary>
    /// Moneda mas pequena que existe en circulacion. En Peru es S/ 0.10 porque
    /// los centimos de 1 y 5 ya no circulan, y en Colombia son 50 pesos.
    /// Todo importe que se cobre en efectivo tiene que ser multiplo de este valor.
    /// </summary>
    public decimal CashRounding { get; init; } = 0.10m;

    /// <summary>Billetes, de mayor a menor, para los botones de pago rapido.</summary>
    public required decimal[] Bills { get; init; }

    /// <summary>Monedas, de mayor a menor, para el conteo del arqueo.</summary>
    public required decimal[] Coins { get; init; }

    public IEnumerable<decimal> Denominations => Bills.Concat(Coins);

    private CultureInfo BuildCulture()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo(CultureName).Clone();
        culture.NumberFormat.CurrencySymbol = Symbol;
        culture.NumberFormat.CurrencyDecimalDigits = Decimals;
        culture.NumberFormat.NumberDecimalDigits = Decimals;
        culture.NumberFormat.CurrencyPositivePattern = 2; // S/ 1,234.50
        culture.NumberFormat.CurrencyNegativePattern = 9; // S/ -1,234.50
        return culture;
    }

    private CultureInfo? _culture;
    public CultureInfo Culture => _culture ??= BuildCulture();

    /// <summary>Formatea con simbolo: "S/ 12.50" o "$ 12.500".</summary>
    public string Format(decimal amount) => amount.ToString("C", Culture);

    /// <summary>Formatea sin simbolo: "12.50".</summary>
    public string FormatNumber(decimal amount) => amount.ToString("N", Culture);

    /// <summary>Redondea al multiplo de efectivo mas cercano (COP redondea a 50).</summary>
    public decimal RoundCash(decimal amount)
    {
        if (CashRounding <= 0) return Math.Round(amount, Decimals, MidpointRounding.AwayFromZero);
        var steps = Math.Round(amount / CashRounding, 0, MidpointRounding.AwayFromZero);
        return Math.Round(steps * CashRounding, Decimals, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Redondea un total a cobrar al efectivo, pero nunca lo deja en cero cuando
    /// habia importe: un ticket de 10 pesos con moneda minima de 50 se cobra 50,
    /// no se regala.
    /// </summary>
    public decimal RoundCashTotal(decimal amount)
    {
        var rounded = RoundCash(amount);
        return amount > 0 && rounded <= 0 ? CeilCash(amount) : rounded;
    }

    /// <summary>Redondea un importe a los decimales de la moneda.</summary>
    public decimal Round(decimal amount) => Math.Round(amount, Decimals, MidpointRounding.AwayFromZero);

    /// <summary>Baja el importe al multiplo de moneda anterior. S/ 4.17 -> S/ 4.10</summary>
    public decimal FloorCash(decimal amount)
    {
        if (CashRounding <= 0) return Round(amount);
        return Round(Math.Floor(amount / CashRounding) * CashRounding);
    }

    /// <summary>Sube el importe al multiplo de moneda siguiente. S/ 4.17 -> S/ 4.20</summary>
    public decimal CeilCash(decimal amount)
    {
        if (CashRounding <= 0) return Round(amount);
        return Round(Math.Ceiling(amount / CashRounding) * CashRounding);
    }

    /// <summary>
    /// Verdadero si el importe se puede pagar exacto con las monedas que existen.
    /// S/ 4.20 si, S/ 4.17 no (haria falta un centimo que ya no circula).
    /// </summary>
    public bool IsCashAmount(decimal amount)
    {
        if (CashRounding <= 0) return true;
        return Round(amount) % CashRounding == 0m;
    }

    /// <summary>Cuanto hay que bajar el importe para que se pueda pagar exacto.</summary>
    public decimal DownTo(decimal amount) => Round(amount - FloorCash(amount));

    /// <summary>Cuanto hay que subir el importe para que se pueda pagar exacto.</summary>
    public decimal UpTo(decimal amount) => Round(CeilCash(amount) - amount);

    /// <summary>Sugerencias de pago rapido: el importe exacto y los billetes utiles por encima.</summary>
    public IReadOnlyList<decimal> QuickAmounts(decimal total)
    {
        var list = new List<decimal>();
        if (total > 0) list.Add(Round(total));

        foreach (var bill in Bills.OrderBy(b => b))
        {
            if (bill > total && list.Count < 6 && !list.Contains(bill)) list.Add(bill);
        }

        // Redondeos "de bolsillo" hacia arriba (la siguiente decena o millar).
        var step = Code == "COP" ? 1000m : 10m;
        var rounded = Math.Ceiling(total / step) * step;
        if (rounded > total && !list.Contains(rounded)) list.Insert(Math.Min(1, list.Count), rounded);

        return list.OrderBy(x => x).Take(6).ToList();
    }

    public static readonly CurrencyInfo Pen = new()
    {
        Code = "PEN",
        Symbol = "S/",
        Name = "Soles peruanos",
        CultureName = "es-PE",
        Decimals = 2,
        CashRounding = 0.10m,
        Bills = [200m, 100m, 50m, 20m, 10m],
        Coins = [5m, 2m, 1m, 0.50m, 0.20m, 0.10m]
    };

    public static readonly CurrencyInfo Cop = new()
    {
        Code = "COP",
        Symbol = "$",
        Name = "Pesos colombianos",
        CultureName = "es-CO",
        Decimals = 0,
        CashRounding = 50m,
        Bills = [100000m, 50000m, 20000m, 10000m, 5000m, 2000m],
        Coins = [1000m, 500m, 200m, 100m, 50m]
    };

    public static readonly CurrencyInfo Usd = new()
    {
        Code = "USD",
        Symbol = "US$",
        Name = "Dolares",
        CultureName = "en-US",
        Decimals = 2,
        CashRounding = 0.01m,
        Bills = [100m, 50m, 20m, 10m, 5m, 1m],
        Coins = [0.25m, 0.10m, 0.05m, 0.01m]
    };

    public static readonly IReadOnlyList<CurrencyInfo> Supported = [Pen, Cop, Usd];

    public static CurrencyInfo Get(string? code) =>
        Supported.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)) ?? Pen;
}
