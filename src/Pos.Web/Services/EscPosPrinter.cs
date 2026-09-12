using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Pos.Web.Models;

namespace Pos.Web.Services;

public record PrintResult(bool Success, string Message)
{
    public static PrintResult Ok(string message = "Enviado a la impresora") => new(true, message);
    public static PrintResult Fail(string message) => new(false, message);
}

public interface IReceiptPrinter
{
    /// <summary>Envia el recibo a la impresora de red por ESC/POS (puerto 9100).</summary>
    Task<PrintResult> PrintSaleAsync(Sale sale, CancellationToken ct = default);

    /// <summary>Envia el cierre de caja a la impresora de red.</summary>
    Task<PrintResult> PrintCashCloseAsync(CashSessionSummary summary, CancellationToken ct = default);

    /// <summary>Abre el cajon de dinero (pulso por el conector RJ11 de la impresora).</summary>
    Task<PrintResult> OpenDrawerAsync(CancellationToken ct = default);

    /// <summary>Imprime una linea de prueba para verificar la conexion.</summary>
    Task<PrintResult> PrintTestAsync(CancellationToken ct = default);
}

/// <summary>
/// Impresion directa por ESC/POS contra impresoras termicas de 80mm
/// (probado contra el juego de comandos de la 3nStar RPT004) usando el
/// puerto RAW 9100. Si la impresora esta conectada por USB al equipo de la
/// cajera se usa en su lugar la impresion del navegador con la vista de 80mm.
/// </summary>
public class EscPosPrinter(ISettingsService settings, ILogger<EscPosPrinter> logger) : IReceiptPrinter
{
    // Comandos ESC/POS
    private static readonly byte[] Init = [0x1B, 0x40];
    private static readonly byte[] AlignLeft = [0x1B, 0x61, 0x00];
    private static readonly byte[] AlignCenter = [0x1B, 0x61, 0x01];
    private static readonly byte[] AlignRight = [0x1B, 0x61, 0x02];
    private static readonly byte[] BoldOn = [0x1B, 0x45, 0x01];
    private static readonly byte[] BoldOff = [0x1B, 0x45, 0x00];
    private static readonly byte[] SizeNormal = [0x1D, 0x21, 0x00];
    private static readonly byte[] SizeDoubleHeight = [0x1D, 0x21, 0x01];
    private static readonly byte[] SizeDouble = [0x1D, 0x21, 0x11];
    private static readonly byte[] FeedAndCut = [0x1D, 0x56, 0x42, 0x03];
    private static readonly byte[] DrawerKick = [0x1B, 0x70, 0x00, 0x19, 0xFA];

    public async Task<PrintResult> PrintSaleAsync(Sale sale, CancellationToken ct = default)
    {
        var config = await settings.GetAsync(ct);
        var currency = CurrencyInfo.Get(sale.CurrencyCode);
        var width = Math.Clamp(config.PrinterCharsPerLine, 32, 56);
        var buffer = new List<byte>();

        buffer.AddRange(Init);
        buffer.AddRange(AlignCenter);
        buffer.AddRange(SizeDouble);
        AddLine(buffer, config.BusinessName, width / 2);
        buffer.AddRange(SizeNormal);

        if (!string.IsNullOrWhiteSpace(config.BusinessDocument)) AddLine(buffer, config.BusinessDocument, width);
        if (!string.IsNullOrWhiteSpace(config.BusinessAddress)) AddLine(buffer, config.BusinessAddress, width);
        if (!string.IsNullOrWhiteSpace(config.BusinessPhone)) AddLine(buffer, $"Tel. {config.BusinessPhone}", width);

        AddLine(buffer, new string('=', width), width);
        buffer.AddRange(BoldOn);
        AddLine(buffer, $"RECIBO {sale.Number}", width);
        buffer.AddRange(BoldOff);
        buffer.AddRange(AlignLeft);
        AddLine(buffer, $"Fecha : {sale.SoldAt.ToLocalTime():dd/MM/yyyy HH:mm}", width);
        AddLine(buffer, $"Cajera: {sale.UserName}", width);
        if (!string.IsNullOrWhiteSpace(sale.CustomerName)) AddLine(buffer, $"Cliente: {sale.CustomerName}", width);
        AddLine(buffer, new string('-', width), width);

        foreach (var item in sale.Items)
        {
            AddLine(buffer, Sanitize(item.ProductName), width);
            var qty = FormatQuantity(item.Quantity);
            var left = $"  {qty} x {currency.FormatNumber(item.UnitPrice)}";
            var right = currency.FormatNumber(item.LineTotal);
            AddLine(buffer, Columns(left, right, width), width);
        }

        AddLine(buffer, new string('-', width), width);
        AddLine(buffer, Columns("Subtotal", currency.Format(sale.Subtotal), width), width);
        if (sale.DiscountAmount > 0)
            AddLine(buffer, Columns("Descuento", "-" + currency.Format(sale.DiscountAmount), width), width);
        if (sale.TaxAmount > 0)
            AddLine(buffer, Columns($"{config.TaxName} {config.TaxRate:0.##}%", currency.Format(sale.TaxAmount), width), width);
        if (sale.RoundingAdjustment != 0)
            AddLine(buffer, Columns("Redondeo",
                (sale.RoundingAdjustment > 0 ? "+" : "") + currency.Format(sale.RoundingAdjustment), width), width);

        buffer.AddRange(SizeDoubleHeight);
        buffer.AddRange(BoldOn);
        AddLine(buffer, Columns("TOTAL", currency.Format(sale.Total), width), width);
        buffer.AddRange(BoldOff);
        buffer.AddRange(SizeNormal);

        AddLine(buffer, Columns("Recibido", currency.Format(sale.AmountReceived), width), width);
        AddLine(buffer, Columns("Vuelto", currency.Format(sale.ChangeGiven), width), width);
        AddLine(buffer, Columns("Pago", PaymentLabel(sale.PaymentMethod), width), width);
        AddLine(buffer, new string('=', width), width);

        buffer.AddRange(AlignCenter);
        AddLine(buffer, $"{sale.Items.Count} productos / {FormatQuantity(sale.Items.Sum(i => i.Quantity))} unidades", width);
        if (!string.IsNullOrWhiteSpace(config.BusinessFooter)) AddLine(buffer, config.BusinessFooter, width);
        buffer.AddRange(Encoding.ASCII.GetBytes("\n\n\n"));

        if (config.PrinterCutPaper) buffer.AddRange(FeedAndCut);
        if (config.PrinterOpenDrawer && sale.PaymentMethod == PaymentMethod.Cash) buffer.AddRange(DrawerKick);

        return await SendAsync(buffer.ToArray(), config, ct);
    }

    public async Task<PrintResult> PrintCashCloseAsync(CashSessionSummary summary, CancellationToken ct = default)
    {
        var config = await settings.GetAsync(ct);
        var currency = CurrencyInfo.Get(summary.Session.CurrencyCode);
        var width = Math.Clamp(config.PrinterCharsPerLine, 32, 56);
        var buffer = new List<byte>();

        buffer.AddRange(Init);
        buffer.AddRange(AlignCenter);
        buffer.AddRange(BoldOn);
        buffer.AddRange(SizeDoubleHeight);
        AddLine(buffer, "ENTREGA DE CAJA", width / 2);
        buffer.AddRange(SizeNormal);
        buffer.AddRange(BoldOff);
        AddLine(buffer, config.BusinessName, width);
        buffer.AddRange(AlignLeft);
        AddLine(buffer, new string('=', width), width);
        AddLine(buffer, $"Turno  : #{summary.Session.Id}", width);
        AddLine(buffer, $"Cajera : {summary.Session.UserName}", width);
        AddLine(buffer, $"Apertura: {summary.Session.OpenedAt.ToLocalTime():dd/MM/yyyy HH:mm}", width);
        AddLine(buffer, $"Cierre  : {(summary.Session.ClosedAt ?? DateTime.UtcNow).ToLocalTime():dd/MM/yyyy HH:mm}", width);
        AddLine(buffer, new string('-', width), width);
        AddLine(buffer, Columns("Fondo inicial", currency.Format(summary.OpeningAmount), width), width);
        AddLine(buffer, Columns("Ventas efectivo", currency.Format(summary.CashSales), width), width);
        if (summary.CardSales > 0) AddLine(buffer, Columns("Ventas tarjeta", currency.Format(summary.CardSales), width), width);
        if (summary.TransferSales > 0) AddLine(buffer, Columns("Transferencias", currency.Format(summary.TransferSales), width), width);
        if (summary.MovementsIn > 0) AddLine(buffer, Columns("Otros ingresos", currency.Format(summary.MovementsIn), width), width);
        if (summary.MovementsOut > 0) AddLine(buffer, Columns("Retiros", "-" + currency.Format(summary.MovementsOut), width), width);
        AddLine(buffer, new string('-', width), width);
        AddLine(buffer, Columns("Esperado en caja", currency.Format(summary.ExpectedCash), width), width);
        AddLine(buffer, Columns("Contado", currency.Format(summary.Session.CountedAmount ?? 0), width), width);
        AddLine(buffer, Columns("Diferencia", currency.Format(summary.Session.Difference ?? 0), width), width);
        AddLine(buffer, new string('-', width), width);
        buffer.AddRange(BoldOn);
        AddLine(buffer, Columns("ENTREGADO AL DUENO", currency.Format(summary.Session.DeliveredToOwner ?? 0), width), width);
        buffer.AddRange(BoldOff);
        AddLine(buffer, Columns("Queda en caja", currency.Format(summary.Session.LeftInDrawer ?? 0), width), width);
        AddLine(buffer, new string('=', width), width);

        AddLine(buffer, "PRODUCTOS VENDIDOS", width);
        AddLine(buffer, new string('-', width), width);
        foreach (var row in summary.Products)
        {
            AddLine(buffer, Sanitize(row.Name), width);
            AddLine(buffer, Columns($"  {FormatQuantity(row.Quantity)} {row.Unit}", currency.Format(row.Total), width), width);
        }
        AddLine(buffer, new string('=', width), width);
        AddLine(buffer, Columns("Recibos", summary.SalesCount.ToString(), width), width);
        AddLine(buffer, Columns("Total vendido", currency.Format(summary.TotalSales), width), width);

        AddLine(buffer, "", width);
        AddLine(buffer, "", width);
        AddLine(buffer, "Entrega: ______________________", width);
        AddLine(buffer, "Recibe : ______________________", width);
        buffer.AddRange(Encoding.ASCII.GetBytes("\n\n\n"));
        if (config.PrinterCutPaper) buffer.AddRange(FeedAndCut);

        return await SendAsync(buffer.ToArray(), config, ct);
    }

    public async Task<PrintResult> OpenDrawerAsync(CancellationToken ct = default)
    {
        var config = await settings.GetAsync(ct);
        return await SendAsync([.. Init, .. DrawerKick], config, ct);
    }

    public async Task<PrintResult> PrintTestAsync(CancellationToken ct = default)
    {
        var config = await settings.GetAsync(ct);
        var width = Math.Clamp(config.PrinterCharsPerLine, 32, 56);
        var buffer = new List<byte>();
        buffer.AddRange(Init);
        buffer.AddRange(AlignCenter);
        buffer.AddRange(BoldOn);
        AddLine(buffer, "PRUEBA DE IMPRESION", width);
        buffer.AddRange(BoldOff);
        AddLine(buffer, config.BusinessName, width);
        AddLine(buffer, DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture), width);
        AddLine(buffer, new string('=', width), width);
        AddLine(buffer, $"Ancho configurado: {width} caracteres", width);
        buffer.AddRange(Encoding.ASCII.GetBytes("\n\n\n"));
        if (config.PrinterCutPaper) buffer.AddRange(FeedAndCut);
        return await SendAsync(buffer.ToArray(), config, ct);
    }

    private async Task<PrintResult> SendAsync(byte[] payload, PosSettings config, CancellationToken ct)
    {
        if (!string.Equals(config.PrinterMode, "network", StringComparison.OrdinalIgnoreCase))
        {
            return PrintResult.Fail(
                "La impresora esta configurada en modo navegador. Cambie a modo red en Configuracion para imprimir directo.");
        }

        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));

            await client.ConnectAsync(config.PrinterHost, config.PrinterPort, timeout.Token);
            await using var stream = client.GetStream();
            await stream.WriteAsync(payload, timeout.Token);
            await stream.FlushAsync(timeout.Token);

            logger.LogInformation("Enviados {Bytes} bytes a la impresora {Host}:{Port}",
                payload.Length, config.PrinterHost, config.PrinterPort);
            return PrintResult.Ok($"Enviado a {config.PrinterHost}:{config.PrinterPort}");
        }
        catch (OperationCanceledException)
        {
            return PrintResult.Fail($"La impresora {config.PrinterHost}:{config.PrinterPort} no respondio a tiempo.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fallo la impresion en {Host}:{Port}", config.PrinterHost, config.PrinterPort);
            return PrintResult.Fail($"No se pudo conectar con la impresora: {ex.Message}");
        }
    }

    private static void AddLine(List<byte> buffer, string text, int width)
    {
        var clean = Sanitize(text);
        if (clean.Length > width) clean = clean[..width];
        buffer.AddRange(Encoding.ASCII.GetBytes(clean + "\n"));
    }

    /// <summary>Deja dos columnas alineadas a los extremos del papel.</summary>
    internal static string Columns(string left, string right, int width)
    {
        left = Sanitize(left);
        right = Sanitize(right);
        var space = width - left.Length - right.Length;
        if (space < 1)
        {
            var available = Math.Max(1, width - right.Length - 1);
            left = left.Length > available ? left[..available] : left;
            space = Math.Max(1, width - left.Length - right.Length);
        }
        return left + new string(' ', space) + right;
    }

    /// <summary>
    /// Unidades enteras se muestran sin decimales y los pesajes siempre con los
    /// tres decimales de la balanza: 3 unidades, pero 0.755 kg y 1.200 kg.
    /// </summary>
    internal static string FormatQuantity(decimal quantity) =>
        quantity == Math.Floor(quantity)
            ? ((int)quantity).ToString(CultureInfo.InvariantCulture)
            : quantity.ToString("0.000", CultureInfo.InvariantCulture);

    internal static string PaymentLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Efectivo",
        PaymentMethod.Card => "Tarjeta",
        PaymentMethod.Transfer => "Transferencia",
        PaymentMethod.Mixed => "Mixto",
        _ => method.ToString()
    };

    /// <summary>
    /// Las termicas de 80mm no comparten pagina de codigos con el navegador, asi que
    /// se quitan tildes y enes para que el ticket salga legible en cualquier equipo.
    /// </summary>
    internal static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(c <= 127 ? c : '?');
        }
        return builder.ToString().Normalize(NormalizationForm.FormC).Replace("\r", string.Empty);
    }
}
