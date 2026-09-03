using Microsoft.EntityFrameworkCore;
using Pos.Web.Data;
using Pos.Web.Models;

namespace Pos.Web.Services;

/// <summary>Claves de configuracion conocidas del sistema.</summary>
public static class SettingKeys
{
    public const string BusinessName = "business.name";
    public const string BusinessDocument = "business.document";
    public const string BusinessAddress = "business.address";
    public const string BusinessPhone = "business.phone";
    public const string BusinessFooter = "business.footer";

    public const string CurrencyCode = "currency.code";

    public const string TaxEnabled = "tax.enabled";
    public const string TaxRate = "tax.rate";
    public const string TaxName = "tax.name";
    public const string TaxIncluded = "tax.included";

    public const string ReceiptSeries = "receipt.series";
    public const string ReceiptPrintAuto = "receipt.printauto";
    public const string ReceiptCopies = "receipt.copies";

    public const string PrinterMode = "printer.mode";          // browser | network
    public const string PrinterHost = "printer.host";
    public const string PrinterPort = "printer.port";
    public const string PrinterCutPaper = "printer.cut";
    public const string PrinterOpenDrawer = "printer.drawer";
    public const string PrinterCharsPerLine = "printer.charsperline";

    public const string PosDefaultOpeningFloat = "pos.openingfloat";
    public const string PosStockControl = "pos.stockcontrol";
    public const string TimeZone = "app.timezone";
}

/// <summary>Configuracion tipada que consume el resto de la aplicacion.</summary>
public class PosSettings
{
    public string BusinessName { get; set; } = "Mi Negocio";
    public string BusinessDocument { get; set; } = string.Empty;
    public string BusinessAddress { get; set; } = string.Empty;
    public string BusinessPhone { get; set; } = string.Empty;
    public string BusinessFooter { get; set; } = "Gracias por su compra";

    public string CurrencyCode { get; set; } = "PEN";

    public bool TaxEnabled { get; set; }
    public decimal TaxRate { get; set; } = 18m;
    public string TaxName { get; set; } = "IGV";
    public bool TaxIncluded { get; set; } = true;

    public string ReceiptSeries { get; set; } = "B001";
    public bool ReceiptPrintAuto { get; set; } = true;
    public int ReceiptCopies { get; set; } = 1;

    public string PrinterMode { get; set; } = "browser";
    public string PrinterHost { get; set; } = "192.168.1.100";
    public int PrinterPort { get; set; } = 9100;
    public bool PrinterCutPaper { get; set; } = true;
    public bool PrinterOpenDrawer { get; set; } = true;
    public int PrinterCharsPerLine { get; set; } = 48;

    public decimal PosDefaultOpeningFloat { get; set; }
    public bool PosStockControl { get; set; }
    public string TimeZone { get; set; } = "America/Lima";

    public CurrencyInfo Currency => CurrencyInfo.Get(CurrencyCode);
}

public interface ISettingsService
{
    Task<PosSettings> GetAsync(CancellationToken ct = default);
    PosSettings Current { get; }
    Task SaveAsync(PosSettings settings, int? userId, string? userName, CancellationToken ct = default);
    void Invalidate();
}

/// <summary>
/// Lee y escribe la configuracion en base de datos y la mantiene en memoria
/// para no golpear la base en cada venta.
/// </summary>
public class SettingsService(IServiceScopeFactory scopeFactory, ILogger<SettingsService> logger) : ISettingsService
{
    private static PosSettings? _cache;
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public PosSettings Current => _cache ?? GetAsync().GetAwaiter().GetResult();

    public void Invalidate() => _cache = null;

    public async Task<PosSettings> GetAsync(CancellationToken ct = default)
    {
        if (_cache is not null) return _cache;

        await Lock.WaitAsync(ct);
        try
        {
            if (_cache is not null) return _cache;

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rows = await db.AppSettings.AsNoTracking().ToListAsync(ct);
            var map = rows.ToDictionary(r => r.Key, r => r.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            var s = new PosSettings();
            s.BusinessName = Str(map, SettingKeys.BusinessName, s.BusinessName);
            s.BusinessDocument = Str(map, SettingKeys.BusinessDocument, s.BusinessDocument);
            s.BusinessAddress = Str(map, SettingKeys.BusinessAddress, s.BusinessAddress);
            s.BusinessPhone = Str(map, SettingKeys.BusinessPhone, s.BusinessPhone);
            s.BusinessFooter = Str(map, SettingKeys.BusinessFooter, s.BusinessFooter);
            s.CurrencyCode = Str(map, SettingKeys.CurrencyCode, s.CurrencyCode).ToUpperInvariant();
            s.TaxEnabled = Bool(map, SettingKeys.TaxEnabled, s.TaxEnabled);
            s.TaxRate = Dec(map, SettingKeys.TaxRate, s.TaxRate);
            s.TaxName = Str(map, SettingKeys.TaxName, s.TaxName);
            s.TaxIncluded = Bool(map, SettingKeys.TaxIncluded, s.TaxIncluded);
            s.ReceiptSeries = Str(map, SettingKeys.ReceiptSeries, s.ReceiptSeries);
            s.ReceiptPrintAuto = Bool(map, SettingKeys.ReceiptPrintAuto, s.ReceiptPrintAuto);
            s.ReceiptCopies = Int(map, SettingKeys.ReceiptCopies, s.ReceiptCopies);
            s.PrinterMode = Str(map, SettingKeys.PrinterMode, s.PrinterMode);
            s.PrinterHost = Str(map, SettingKeys.PrinterHost, s.PrinterHost);
            s.PrinterPort = Int(map, SettingKeys.PrinterPort, s.PrinterPort);
            s.PrinterCutPaper = Bool(map, SettingKeys.PrinterCutPaper, s.PrinterCutPaper);
            s.PrinterOpenDrawer = Bool(map, SettingKeys.PrinterOpenDrawer, s.PrinterOpenDrawer);
            s.PrinterCharsPerLine = Int(map, SettingKeys.PrinterCharsPerLine, s.PrinterCharsPerLine);
            s.PosDefaultOpeningFloat = Dec(map, SettingKeys.PosDefaultOpeningFloat, s.PosDefaultOpeningFloat);
            s.PosStockControl = Bool(map, SettingKeys.PosStockControl, s.PosStockControl);
            s.TimeZone = Str(map, SettingKeys.TimeZone, s.TimeZone);

            _cache = s;
            return s;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo leer la configuracion, se usan valores por defecto");
            return new PosSettings();
        }
        finally
        {
            Lock.Release();
        }
    }

    public async Task SaveAsync(PosSettings s, int? userId, string? userName, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var values = new Dictionary<string, string?>
        {
            [SettingKeys.BusinessName] = s.BusinessName,
            [SettingKeys.BusinessDocument] = s.BusinessDocument,
            [SettingKeys.BusinessAddress] = s.BusinessAddress,
            [SettingKeys.BusinessPhone] = s.BusinessPhone,
            [SettingKeys.BusinessFooter] = s.BusinessFooter,
            [SettingKeys.CurrencyCode] = s.CurrencyCode,
            [SettingKeys.TaxEnabled] = s.TaxEnabled.ToString(),
            [SettingKeys.TaxRate] = s.TaxRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [SettingKeys.TaxName] = s.TaxName,
            [SettingKeys.TaxIncluded] = s.TaxIncluded.ToString(),
            [SettingKeys.ReceiptSeries] = s.ReceiptSeries,
            [SettingKeys.ReceiptPrintAuto] = s.ReceiptPrintAuto.ToString(),
            [SettingKeys.ReceiptCopies] = s.ReceiptCopies.ToString(),
            [SettingKeys.PrinterMode] = s.PrinterMode,
            [SettingKeys.PrinterHost] = s.PrinterHost,
            [SettingKeys.PrinterPort] = s.PrinterPort.ToString(),
            [SettingKeys.PrinterCutPaper] = s.PrinterCutPaper.ToString(),
            [SettingKeys.PrinterOpenDrawer] = s.PrinterOpenDrawer.ToString(),
            [SettingKeys.PrinterCharsPerLine] = s.PrinterCharsPerLine.ToString(),
            [SettingKeys.PosDefaultOpeningFloat] = s.PosDefaultOpeningFloat.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [SettingKeys.PosStockControl] = s.PosStockControl.ToString(),
            [SettingKeys.TimeZone] = s.TimeZone
        };

        var existing = await db.AppSettings.ToListAsync(ct);
        foreach (var (key, value) in values)
        {
            var row = existing.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                db.AppSettings.Add(new AppSetting
                {
                    Key = key,
                    Value = value,
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedByUserId = userId,
                    UpdatedByUserName = userName
                });
            }
            else if (row.Value != value)
            {
                row.Value = value;
                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedByUserId = userId;
                row.UpdatedByUserName = userName;
            }
        }

        await db.SaveChangesAsync(ct);
        Invalidate();
    }

    private static string Str(IDictionary<string, string> m, string k, string fallback) =>
        m.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    private static bool Bool(IDictionary<string, string> m, string k, bool fallback) =>
        m.TryGetValue(k, out var v) && bool.TryParse(v, out var b) ? b : fallback;

    private static int Int(IDictionary<string, string> m, string k, int fallback) =>
        m.TryGetValue(k, out var v) && int.TryParse(v, out var i) ? i : fallback;

    private static decimal Dec(IDictionary<string, string> m, string k, decimal fallback) =>
        m.TryGetValue(k, out var v) &&
        decimal.TryParse(v, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : fallback;
}
