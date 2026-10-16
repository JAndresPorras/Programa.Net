using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Tests.Infraestructura;
using Pos.Web.Models;
using Pos.Web.Security;
using Pos.Web.Services;

namespace Pos.Tests;

/// <summary>
/// Lo que el sistema ya hacia antes del inventario. Estas pruebas se corren
/// antes y despues de cada cambio: si alguna se rompe, se afecto algo existente.
/// </summary>
public class RegresionFuncionesExistentes
{
    private static decimal Dec(JsonElement e) => e.GetDecimal();

    [Fact]
    public async Task Todas_las_pantallas_cargan_para_el_administrador()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);

        await admin.PosAsync("Scan", new { barcode = await app.BarraAsync("PAN-001"), quantity = 2 });
        var venta = await admin.PosAsync("Checkout", new { amountReceived = 10, discount = 0, paymentMethod = "Cash" });
        var saleId = venta.GetProperty("data").GetProperty("saleId").GetInt32();
        var productId = (await app.ProductoAsync("PAN-001")).Id;
        var categoryId = (await app.ProductoAsync("PAN-001")).CategoryId;

        var urls = new[]
        {
            "/", "/Pos", "/Sales", $"/Sales/Details/{saleId}", $"/Sales/Receipt/{saleId}",
            "/Cash", "/Cash/History", "/Products", "/Products/Create", $"/Products/Edit/{productId}",
            $"/Products/History/{productId}", "/Categories", "/Categories/Create", $"/Categories/Edit/{categoryId}",
            "/Reports", "/Reports/Production", "/Settings", "/Users", "/Users/Create", "/Users/Edit/1",
            "/Users/RolesPermissions", "/Users/Audit", "/Account/Profile", "/Account/ChangePassword",
            "/Reports/ExportSales", "/Reports/ExportProduction"
        };

        var fallas = new List<string>();
        foreach (var url in urls)
        {
            var (status, body) = await admin.GetAsync(url);
            if (status != HttpStatusCode.OK || body.Contains("Exception"))
                fallas.Add($"{url} -> {(int)status}");
        }
        Assert.True(fallas.Count == 0, "Pantallas con error: " + string.Join(", ", fallas));
    }

    [Fact]
    public async Task Escanear_y_cobrar_calcula_total_y_vuelto()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);

        await admin.PosAsync("Scan", new { barcode = await app.BarraAsync("PAN-001"), quantity = 3 }); // 1.50
        var r = await admin.PosAsync("Scan", new { barcode = await app.BarraAsync("BEB-001"), quantity = 2 }); // 4.00
        Assert.Equal(5.50m, Dec(r.GetProperty("cart").GetProperty("total")));

        var venta = await admin.PosAsync("Checkout", new { amountReceived = 10, discount = 0, paymentMethod = "Cash" });
        Assert.True(venta.GetProperty("success").GetBoolean());
        Assert.Equal(4.50m, Dec(venta.GetProperty("data").GetProperty("change")));
        Assert.Equal("B001-000001", venta.GetProperty("data").GetProperty("number").GetString());
    }

    [Fact]
    public async Task Codigo_desconocido_se_rechaza_con_mensaje_claro()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        var r = await admin.PosAsync("Scan", new { barcode = "0000000000000" });
        Assert.False(r.GetProperty("success").GetBoolean());
        Assert.Contains("no está registrado", r.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Producto_por_peso_pide_la_balanza_sin_agregarlo()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        var r = await admin.PosAsync("Scan", new { barcode = await app.BarraAsync("PAN-004") });
        Assert.True(r.GetProperty("data").GetProperty("needsWeight").GetBoolean());
        Assert.Equal(0, r.GetProperty("cart").GetProperty("lines").GetArrayLength());
    }

    [Fact]
    public async Task Total_se_redondea_al_efectivo_y_queda_en_la_venta()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        var pid = (await app.ProductoAsync("PAN-004")).Id;   // S/ 12.50 por kilo

        var r = await admin.PosAsync("AddProduct", new { productId = pid, quantity = 0.755 }); // 9.4375 -> 9.44
        Assert.Equal(9.40m, Dec(r.GetProperty("cart").GetProperty("total")));
        Assert.Equal(-0.04m, Dec(r.GetProperty("cart").GetProperty("rounding")));

        var venta = await admin.PosAsync("Checkout", new { amountReceived = 10, discount = 0, paymentMethod = "Cash" });
        Assert.Equal(0.60m, Dec(venta.GetProperty("data").GetProperty("change")));
        var ajuste = await app.DbAsync(db => db.Sales.Select(s => s.RoundingAdjustment).FirstAsync());
        Assert.Equal(-0.04m, ajuste);
    }

    [Fact]
    public async Task Precio_impagable_se_rechaza_al_editar_el_producto()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        var p = await app.ProductoAsync("PAN-001");

        var response = await admin.PostFormAsync("/Products/Edit", $"/Products/Edit/{p.Id}", new()
        {
            ["Id"] = p.Id.ToString(), ["Code"] = p.Code, ["Name"] = p.Name,
            ["CategoryId"] = p.CategoryId.ToString(), ["Price"] = "4.17", ["OriginalPrice"] = "0.50",
            ["Stock"] = "100", ["MinStock"] = "10", ["Unit"] = "UND", ["IsActive"] = "true",
            ["IsProduced"] = "true", ["BarcodesRaw"] = p.Barcodes.First().Code
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no se puede pagar exacto", await response.Content.ReadAsStringAsync());
        Assert.Equal(0.50m, (await app.ProductoAsync("PAN-001")).Price);
    }

    [Fact]
    public async Task Caja_abrir_retirar_y_cerrar_calcula_la_entrega()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);

        await admin.PostFormAsync("/Cash/Open", "/Cash", new() { ["openingAmount"] = "50" });
        await admin.PosAsync("Scan", new { barcode = await app.BarraAsync("PAN-002"), quantity = 2 }); // 13.00
        await admin.PosAsync("Scan", new { barcode = await app.BarraAsync("PAS-001"), quantity = 1 }); // 8.00
        await admin.PosAsync("Checkout", new { amountReceived = 50, discount = 0, paymentMethod = "Cash" });

        var sessionId = await app.DbAsync(db => db.CashSessions.Select(c => c.Id).FirstAsync());
        await admin.PostFormAsync("/Cash/Movement", "/Cash", new()
        {
            ["sessionId"] = sessionId.ToString(), ["type"] = "Out", ["amount"] = "10", ["reason"] = "Bolsas"
        });
        Assert.Equal(61m, Dec((await admin.GetJsonAsync("/Pos/Recaudado")).GetProperty("amount")));

        await admin.PostFormAsync("/Cash/Close", $"/Cash/Close/{sessionId}", new()
        {
            ["id"] = sessionId.ToString(), ["countedAmount"] = "61", ["leftInDrawer"] = "50"
        });
        var caja = await app.DbAsync(db => db.CashSessions.AsNoTracking().FirstAsync());
        Assert.Equal(CashSessionStatus.Closed, caja.Status);
        Assert.Equal(0m, caja.Difference);
        Assert.Equal(11m, caja.DeliveredToOwner);
    }

    [Fact]
    public async Task Cambiar_moneda_a_pesos_y_volver_a_soles()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        await CambiarConfiguracionAsync(app, s => s.CurrencyCode = "COP");

        var r = await admin.PosAsync("Scan", new { barcode = await app.BarraAsync("BEB-002"), quantity = 1 });
        Assert.StartsWith("$", r.GetProperty("cart").GetProperty("totalText").GetString());

        await CambiarConfiguracionAsync(app, s => s.CurrencyCode = "PEN");
        r = await admin.PosAsync("Clear");
        Assert.StartsWith("S/", r.GetProperty("cart").GetProperty("totalText").GetString());
    }

    [Fact]
    public async Task La_cajera_no_entra_a_pantallas_de_administracion()
    {
        await using var app = new PosAppFactory();
        await CrearUsuarioAsync(app, "maria", Roles.Cashier);
        var cajera = await PosClient.LoginAsync(app, "maria", "Cajera123");

        Assert.Equal(HttpStatusCode.OK, (await cajera.GetAsync("/")).Status);
        foreach (var url in new[] { "/Settings", "/Users", "/Reports", "/Categories" })
        {
            var (status, _) = await cajera.GetAsync(url);
            Assert.True(status is HttpStatusCode.Redirect or HttpStatusCode.Forbidden, $"{url} devolvio {(int)status}");
        }
    }

    [Fact]
    public async Task Anular_una_venta_y_no_permitir_anularla_dos_veces()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        await admin.PosAsync("Scan", new { barcode = await app.BarraAsync("PAN-001"), quantity = 1 });
        var venta = await admin.PosAsync("Checkout", new { amountReceived = 1, discount = 0, paymentMethod = "Cash" });
        var id = venta.GetProperty("data").GetProperty("saleId").GetInt32();

        var resultado = await app.ServiceAsync<ISaleService, CheckoutResult>(s => s.CancelAsync(id, 1, "admin", "prueba"));
        Assert.True(resultado.Success);
        var segunda = await app.ServiceAsync<ISaleService, CheckoutResult>(s => s.CancelAsync(id, 1, "admin", "prueba"));
        Assert.False(segunda.Success);
    }

    [Fact]
    public async Task Control_de_stock_global_existente_sigue_funcionando_igual()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        await CambiarConfiguracionAsync(app, s => s.PosStockControl = true);
        await app.FijarStockAsync("BEB-001", 1);
        var barra = await app.BarraAsync("BEB-001");

        Assert.True((await admin.PosAsync("Scan", new { barcode = barra, quantity = 1 })).GetProperty("success").GetBoolean());
        var segunda = await admin.PosAsync("Scan", new { barcode = barra, quantity = 1 });
        Assert.False(segunda.GetProperty("success").GetBoolean());
        Assert.Contains("No hay stock suficiente", segunda.GetProperty("message").GetString());

        await admin.PosAsync("Checkout", new { amountReceived = 2, discount = 0, paymentMethod = "Cash" });
        Assert.Equal(0m, await app.StockAsync("BEB-001"));
    }

    // ---------------------------------------------------- Fallos encontrados

    [Fact]
    public async Task Quitar_un_permiso_a_un_rol_se_mantiene_despues_de_reiniciar()
    {
        var db = Path.Combine(Path.GetTempPath(), $"pos-reinicio-{Guid.NewGuid():N}.db");
        try
        {
            await using (var app = new PosAppFactory(db))
            {
                var admin = await PosClient.LoginAsync(app);
                var rol = await app.ServiceAsync<RoleManager<ApplicationRole>, ApplicationRole>(
                    async rm => (await rm.FindByNameAsync(Roles.Cashier))!);
                var permisos = Roles.CashierPermissions.Where(p => p != Permissions.PosViewCollected)
                    .Select(p => new KeyValuePair<string, string>("permissions", p));

                await admin.PostFormAsync("/Users/RolesPermissions", "/Users/RolesPermissions",
                    new() { ["roleId"] = rol.Id.ToString() }, permisos);
                Assert.DoesNotContain(Permissions.PosViewCollected, await PermisosDelRolAsync(app, Roles.Cashier));
            }

            // Reinicio del servidor: misma base de datos, aplicacion nueva.
            await using (var reiniciada = new PosAppFactory(db))
            {
                _ = reiniciada.CreateClient();
                Assert.DoesNotContain(Permissions.PosViewCollected, await PermisosDelRolAsync(reiniciada, Roles.Cashier));
            }
        }
        finally
        {
            foreach (var f in new[] { db, db + "-wal", db + "-shm" }) if (File.Exists(f)) File.Delete(f);
        }
    }

    [Fact]
    public async Task La_pagina_de_error_existe_para_los_errores_en_produccion()
    {
        await using var app = new PosAppFactory();
        var (status, body) = await PosClient.Anonymous(app).GetAsync("/Home/Error");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Ocurrió un error", body);
    }

    [Fact]
    public async Task Los_codigos_de_barras_de_ejemplo_son_EAN13_validos()
    {
        await using var app = new PosAppFactory();
        _ = app.CreateClient();
        var codigos = await app.DbAsync(db => db.Barcodes.Select(b => b.Code).ToListAsync());
        var invalidos = codigos.Where(c => c.Length == 13 && !Ean13Valido(c)).ToList();
        Assert.True(invalidos.Count == 0, "Digito verificador incorrecto en: " + string.Join(", ", invalidos));
    }

    // ---------------------------------------------------------- Utilidades

    internal static bool Ean13Valido(string c)
    {
        var suma = c[..12].Select((d, i) => (d - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return (10 - suma % 10) % 10 == c[12] - '0';
    }

    internal static Task<List<string>> PermisosDelRolAsync(PosAppFactory app, string rolNombre) =>
        app.ServiceAsync<RoleManager<ApplicationRole>, List<string>>(async rm =>
        {
            var rol = await rm.FindByNameAsync(rolNombre);
            return (await rm.GetClaimsAsync(rol!)).Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToList();
        });

    internal static Task CambiarConfiguracionAsync(PosAppFactory app, Action<PosSettings> cambio) =>
        app.ServiceAsync<ISettingsService, bool>(async s =>
        {
            var actual = await s.GetAsync();
            cambio(actual);
            await s.SaveAsync(actual, 1, "admin");
            return true;
        });

    internal static Task CrearUsuarioAsync(PosAppFactory app, string usuario, string rol, string clave = "Cajera123") =>
        app.ServiceAsync<UserManager<ApplicationUser>, bool>(async um =>
        {
            var u = new ApplicationUser { UserName = usuario, FullName = usuario, IsActive = true };
            var r = await um.CreateAsync(u, clave);
            if (!r.Succeeded) throw new InvalidOperationException(string.Join(", ", r.Errors.Select(e => e.Description)));
            await um.AddToRoleAsync(u, rol);
            return true;
        });
}
