using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pos.Tests.Infraestructura;
using Pos.Web.Models;
using Pos.Web.Services;

namespace Pos.Tests;

/// <summary>
/// El inventario dentro de la venta real: escaneo, cobro, confirmacion del
/// faltante, anulacion, peso y ventas simultaneas.
/// </summary>
public class InventarioVentasTests
{
    private static object Cobro(decimal recibido, bool confirmar = false) =>
        new { amountReceived = recibido, discount = 0, paymentMethod = "Cash", confirmStockShortage = confirmar };

    [Fact]
    public async Task Venta_con_existencias_suficientes_descuenta_sin_preguntar()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        var id = await app.ControlarAsync("QUE-002", 15);

        await admin.PosAsync("Scan", new { barcode = await app.BarraAsync("QUE-002"), quantity = 10 });
        var venta = await admin.PosAsync("Checkout", Cobro(100));

        Assert.True(venta.GetProperty("success").GetBoolean());
        Assert.Equal(5m, await app.StockAsync("QUE-002"));
        var salida = (await app.MovimientosAsync(id)).Last();
        Assert.Equal((InventoryMovementType.Sale, -10m, 15m, 5m),
            (salida.Type, salida.Quantity, salida.StockBefore, salida.StockAfter));
        Assert.Equal(venta.GetProperty("data").GetProperty("saleId").GetInt32(), salida.SaleId);
    }

    /// <summary>El ejemplo exacto del pedido: hay 9 quesos, se venden 10, queda 0.</summary>
    [Fact]
    public async Task Hay_9_se_venden_10_advierte_y_al_confirmar_ajusta_a_10_y_queda_en_0()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        var id = await app.ControlarAsync("QUE-002", 9);

        await admin.PosAsync("Scan", new { barcode = await app.BarraAsync("QUE-002"), quantity = 10 });

        // 1) Sin confirmar: advierte, no cierra la venta y no toca nada.
        var aviso = await admin.PosAsync("Checkout", Cobro(100));
        Assert.False(aviso.GetProperty("success").GetBoolean());
        var data = aviso.GetProperty("data");
        Assert.True(data.GetProperty("needsStockConfirmation").GetBoolean());
        var faltante = data.GetProperty("shortages")[0];
        Assert.Equal(9m, faltante.GetProperty("stock").GetDecimal());
        Assert.Equal(10m, faltante.GetProperty("quantity").GetDecimal());
        Assert.Equal(1m, faltante.GetProperty("missing").GetDecimal());
        Assert.Equal(0, await app.VentasAsync());
        Assert.Equal(9m, await app.StockAsync("QUE-002"));

        // 2) Confirmado: la venta sale igual.
        var venta = await admin.PosAsync("Checkout", Cobro(100, confirmar: true));
        Assert.True(venta.GetProperty("success").GetBoolean());
        Assert.Equal(1, await app.VentasAsync());
        Assert.Equal(0m, await app.StockAsync("QUE-002"));

        // 3) Quedan los dos pasos: ajuste a 10 y salida de 10.
        var movs = (await app.MovimientosAsync(id)).TakeLast(2).ToList();
        Assert.Equal((InventoryMovementType.SaleAdjustment, 1m, 9m, 10m),
            (movs[0].Type, movs[0].Quantity, movs[0].StockBefore, movs[0].StockAfter));
        Assert.Equal((InventoryMovementType.Sale, -10m, 10m, 0m),
            (movs[1].Type, movs[1].Quantity, movs[1].StockBefore, movs[1].StockAfter));
    }

    [Fact]
    public async Task Escanear_un_producto_controlado_sin_existencias_avisa_pero_no_bloquea()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        // Aun con el control de stock global encendido, que si bloquea a los demas.
        await RegresionFuncionesExistentes.CambiarConfiguracionAsync(app, s => s.PosStockControl = true);
        await app.ControlarAsync("YOG-001", 0);

        var r = await admin.PosAsync("Scan", new { barcode = await app.BarraAsync("YOG-001"), quantity = 2 });

        Assert.True(r.GetProperty("success").GetBoolean());
        Assert.Equal("warning", r.GetProperty("messageKind").GetString());
        Assert.Contains("Puede vender igual", r.GetProperty("message").GetString());
        var linea = r.GetProperty("cart").GetProperty("lines")[0];
        Assert.True(linea.GetProperty("trackInventory").GetBoolean());
        Assert.True(linea.GetProperty("stockShort").GetBoolean());

        var venta = await admin.PosAsync("Checkout", Cobro(20, confirmar: true));
        Assert.True(venta.GetProperty("success").GetBoolean());
        Assert.Equal(0m, await app.StockAsync("YOG-001"));
    }

    [Fact]
    public async Task Producto_fuera_del_inventario_no_cambia_su_stock()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        var antes = await app.StockAsync("YOG-002");

        await admin.PosAsync("Scan", new { barcode = await app.BarraAsync("YOG-002"), quantity = 3 });
        var venta = await admin.PosAsync("Checkout", Cobro(20));

        Assert.True(venta.GetProperty("success").GetBoolean());
        Assert.Equal(antes, await app.StockAsync("YOG-002"));
        Assert.Empty(await app.MovimientosAsync((await app.ProductoAsync("YOG-002")).Id));
    }

    [Fact]
    public async Task Con_el_control_global_encendido_un_producto_controlado_se_descuenta_una_sola_vez()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        await RegresionFuncionesExistentes.CambiarConfiguracionAsync(app, s => s.PosStockControl = true);
        await app.ControlarAsync("QUE-002", 20);
        var aguaAntes = await app.StockAsync("BEB-001");

        await admin.PosAsync("Scan", new { barcode = await app.BarraAsync("QUE-002"), quantity = 3 });
        await admin.PosAsync("Scan", new { barcode = await app.BarraAsync("BEB-001"), quantity = 2 });
        await admin.PosAsync("Checkout", Cobro(50));

        Assert.Equal(17m, await app.StockAsync("QUE-002"));          // inventario personalizado
        Assert.Equal(aguaAntes - 2, await app.StockAsync("BEB-001")); // control global de siempre
    }

    [Fact]
    public async Task Anular_la_venta_devuelve_al_inventario_lo_que_desconto()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        var id = await app.ControlarAsync("QUE-002", 9);

        await admin.PosAsync("Scan", new { barcode = await app.BarraAsync("QUE-002"), quantity = 10 });
        var venta = await admin.PosAsync("Checkout", Cobro(100, confirmar: true));
        var saleId = venta.GetProperty("data").GetProperty("saleId").GetInt32();
        Assert.Equal(0m, await app.StockAsync("QUE-002"));

        var anulada = await app.ServiceAsync<ISaleService, CheckoutResult>(s => s.CancelAsync(saleId, 1, "admin", "devolucion"));

        Assert.True(anulada.Success);
        // Se vendieron 10 que estaban fisicamente: vuelven los 10.
        Assert.Equal(10m, await app.StockAsync("QUE-002"));
        var devolucion = (await app.MovimientosAsync(id)).Last();
        Assert.Equal((InventoryMovementType.SaleCancelled, 10m, saleId),
            (devolucion.Type, devolucion.Quantity, devolucion.SaleId!.Value));

        // Anular de nuevo no devuelve dos veces.
        var otra = await app.ServiceAsync<ISaleService, CheckoutResult>(s => s.CancelAsync(saleId, 1, "admin", "otra"));
        Assert.False(otra.Success);
        Assert.Equal(10m, await app.StockAsync("QUE-002"));
    }

    [Fact]
    public async Task Anular_una_venta_hecha_antes_de_controlar_el_producto_no_toca_el_inventario()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        await admin.PosAsync("Scan", new { barcode = await app.BarraAsync("QUE-002"), quantity = 2 });
        var venta = await admin.PosAsync("Checkout", Cobro(20));
        var saleId = venta.GetProperty("data").GetProperty("saleId").GetInt32();

        var id = await app.ControlarAsync("QUE-002", 6);   // recien ahora se controla
        await app.ServiceAsync<ISaleService, CheckoutResult>(s => s.CancelAsync(saleId, 1, "admin", "prueba"));

        Assert.Equal(6m, await app.StockAsync("QUE-002"));
        Assert.DoesNotContain(await app.MovimientosAsync(id), m => m.Type == InventoryMovementType.SaleCancelled);
    }

    [Fact]
    public async Task Producto_por_peso_controlado_descuenta_los_kilos()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        var id = await app.ControlarAsync("QUE-001", 2);   // 2.000 kg

        await admin.PosAsync("AddProduct", new { productId = id, quantity = 0.755 });
        var venta = await admin.PosAsync("Checkout", Cobro(30));

        Assert.True(venta.GetProperty("success").GetBoolean());
        Assert.Equal(1.245m, await app.StockAsync("QUE-001"));
    }

    [Fact]
    public async Task Stock_negativo_de_datos_viejos_queda_en_cero_y_nunca_negativo()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        var id = await app.ControlarAsync("QUE-002", 0);
        await app.FijarStockAsync("QUE-002", -2);   // como lo dejaba el control global antiguo

        await admin.PosAsync("Scan", new { barcode = await app.BarraAsync("QUE-002"), quantity = 3 });
        await admin.PosAsync("Checkout", Cobro(30, confirmar: true));

        Assert.Equal(0m, await app.StockAsync("QUE-002"));
        var ajuste = (await app.MovimientosAsync(id)).Single(m => m.Type == InventoryMovementType.SaleAdjustment);
        Assert.Equal((5m, -2m, 3m), (ajuste.Quantity, ajuste.StockBefore, ajuste.StockAfter));
    }

    [Fact]
    public async Task Diez_cajas_vendiendo_a_la_vez_no_pierden_ni_duplican_unidades()
    {
        await using var app = new PosAppFactory();
        await app.ControlarAsync("QUE-002", 20);
        var barra = await app.BarraAsync("QUE-002");

        var cajas = new List<PosClient>();
        for (var i = 0; i < 10; i++)
        {
            var c = await PosClient.LoginAsync(app);
            await c.PosAsync("Scan", new { barcode = barra, quantity = 1 });
            cajas.Add(c);
        }

        var resultados = await Task.WhenAll(cajas.Select(c => c.PosAsync("Checkout", Cobro(10))));

        Assert.All(resultados, r => Assert.True(r.GetProperty("success").GetBoolean(), r.ToString()));
        Assert.Equal(10m, await app.StockAsync("QUE-002"));
        var numeros = resultados.Select(r => r.GetProperty("data").GetProperty("number").GetString()).ToList();
        Assert.Equal(10, numeros.Distinct().Count());
    }

    [Fact]
    public async Task Diez_cajas_vendiendo_mas_de_lo_que_hay_terminan_en_cero_sin_errores()
    {
        await using var app = new PosAppFactory();
        await app.ControlarAsync("QUE-002", 3);
        var barra = await app.BarraAsync("QUE-002");

        var cajas = new List<PosClient>();
        for (var i = 0; i < 10; i++)
        {
            var c = await PosClient.LoginAsync(app);
            await c.PosAsync("Scan", new { barcode = barra, quantity = 1 });
            cajas.Add(c);
        }

        var resultados = await Task.WhenAll(cajas.Select(c => c.PosAsync("Checkout", Cobro(10, confirmar: true))));

        Assert.All(resultados, r => Assert.True(r.GetProperty("success").GetBoolean(), r.ToString()));
        Assert.Equal(0m, await app.StockAsync("QUE-002"));
    }

    [Fact]
    public async Task Editar_el_producto_no_pisa_el_stock_del_inventario()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        var id = await app.ControlarAsync("QUE-002", 9);
        var p = await app.ProductoAsync("QUE-002");

        // Alguien vende mientras otro tiene abierto el formulario del producto.
        await admin.PosAsync("Scan", new { barcode = p.Barcodes.First().Code, quantity = 4 });
        await admin.PosAsync("Checkout", Cobro(50));
        Assert.Equal(5m, await app.StockAsync("QUE-002"));

        var respuesta = await admin.PostFormAsync("/Products/Edit", $"/Products/Edit/{id}", new()
        {
            ["Id"] = id.ToString(), ["Code"] = p.Code, ["Name"] = "Queso edam tajado 200 g (nuevo nombre)",
            ["CategoryId"] = p.CategoryId.ToString(), ["Price"] = "9.90", ["OriginalPrice"] = "9.90",
            ["Stock"] = "9", ["OriginalStock"] = "9", ["MinStock"] = "3", ["Unit"] = "UND",
            ["IsActive"] = "true", ["BarcodesRaw"] = p.Barcodes.First().Code
        });

        Assert.Equal(System.Net.HttpStatusCode.Redirect, respuesta.StatusCode);
        var despues = await app.ProductoAsync("QUE-002");
        Assert.Equal("Queso edam tajado 200 g (nuevo nombre)", despues.Name);
        Assert.Equal(5m, despues.Stock);
    }

    [Fact]
    public async Task Editar_un_producto_sin_tocar_su_stock_no_pisa_el_descuento_del_control_global()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        await RegresionFuncionesExistentes.CambiarConfiguracionAsync(app, s => s.PosStockControl = true);
        var p = await app.ProductoAsync("BEB-002");   // stock 100, fuera del inventario

        await admin.PosAsync("Scan", new { barcode = p.Barcodes.First().Code, quantity = 4 });
        await admin.PosAsync("Checkout", Cobro(20));   // control global: 100 -> 96

        await admin.PostFormAsync("/Products/Edit", $"/Products/Edit/{p.Id}", new()
        {
            ["Id"] = p.Id.ToString(), ["Code"] = p.Code, ["Name"] = p.Name,
            ["CategoryId"] = p.CategoryId.ToString(), ["Price"] = "3.50", ["OriginalPrice"] = "3.50",
            ["Stock"] = "100", ["OriginalStock"] = "100", ["MinStock"] = "10", ["Unit"] = "UND",
            ["IsActive"] = "true", ["BarcodesRaw"] = p.Barcodes.First().Code
        });

        Assert.Equal(96m, await app.StockAsync("BEB-002"));

        // Pero si quien edita cambia el numero, se respeta.
        await admin.PostFormAsync("/Products/Edit", $"/Products/Edit/{p.Id}", new()
        {
            ["Id"] = p.Id.ToString(), ["Code"] = p.Code, ["Name"] = p.Name,
            ["CategoryId"] = p.CategoryId.ToString(), ["Price"] = "3.50", ["OriginalPrice"] = "3.50",
            ["Stock"] = "50", ["OriginalStock"] = "96", ["MinStock"] = "10", ["Unit"] = "UND",
            ["IsActive"] = "true", ["BarcodesRaw"] = p.Barcodes.First().Code
        });
        Assert.Equal(50m, await app.StockAsync("BEB-002"));
    }
}
