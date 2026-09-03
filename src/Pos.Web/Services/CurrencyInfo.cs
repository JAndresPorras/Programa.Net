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

    /// <summary>Multiplo al que se redondea el vuelto (la moneda mas pequena en circulacion).</summary>
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

    /// <summary>Redondea un importe a los decimales de la moneda.</summary>
    public decimal Round(decimal amount) => Math.Round(amount, Decimals, MidpointRounding.AwayFromZero);

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
