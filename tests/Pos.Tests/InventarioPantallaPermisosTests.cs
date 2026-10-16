using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pos.Tests.Infraestructura;
using Pos.Web.Models;
using Pos.Web.Security;
using Pos.Web.Services;

namespace Pos.Tests;

/// <summary>Vista de inventario, sus permisos, el rol informes_admin, Excel y categorias.</summary>
public class InventarioPantallaPermisosTests
{
    private const string ClaveInformes = "Informes123";

    [Fact]
    public async Task Informes_admin_al_ingresar_llega_a_inventario_y_no_a_acceso_denegado()
    {
        await using var app = new PosAppFactory();
        await RegresionFuncionesExistentes.CrearUsuarioAsync(app, "informes", Roles.ReportsAdmin, ClaveInformes);
        var cliente = await PosClient.LoginAsync(app, "informes", ClaveInformes);

        // El login lleva a "/" (el punto de venta); quien no vende es enviado a su pantalla.
        var inicio = await cliente.Http.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, inicio.StatusCode);
        var denegado = await cliente.Http.GetAsync(inicio.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, denegado.StatusCode);
        Assert.Equal("/Inventory", denegado.Headers.Location!.OriginalString);

        var (status, body) = await cliente.GetAsync("/Inventory");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Exportar a Excel", body);
        Assert.DoesNotMatch(EnlacePos, body);
        Assert.Matches(EnlaceInventario, body);
    }

    // Enlaces del menu lateral, tolerando los espacios del marcado.
    private static readonly Regex EnlacePos = new(@"#i-scan""></use></svg>\s*Punto de venta\s*</a>");
    private static readonly Regex EnlaceInventario = new(@"#i-coins""></use></svg>\s*Inventario\s*</a>");

    [Fact]
    public async Task El_menu_muestra_inventario_y_punto_de_venta_al_administrador()
    {
        await using var app = new PosAppFactory();
        var body = (await (await PosClient.LoginAsync(app)).GetAsync("/")).Body;
        Assert.Matches(EnlacePos, body);
        Assert.Matches(EnlaceInventario, body);
    }

    [Fact]
    public async Task Informes_admin_ve_inventario_y_reportes_pero_no_caja_configuracion_ni_usuarios()
    {
        await using var app = new PosAppFactory();
        await RegresionFuncionesExistentes.CrearUsuarioAsync(app, "informes", Roles.ReportsAdmin, ClaveInformes);
        var cliente = await PosClient.LoginAsync(app, "informes", ClaveInformes);

        foreach (var url in new[] { "/Inventory", "/Inventory/Excel", "/Reports", "/Reports/Production", "/Products" })
            Assert.True((await cliente.GetAsync(url)).Status == HttpStatusCode.OK, url);

        foreach (var url in new[] { "/Settings", "/Users", "/Categories", "/Pos/Recaudado" })
        {
            var (status, _) = await cliente.GetAsync(url);
            Assert.True(status is HttpStatusCode.Redirect or HttpStatusCode.Forbidden, $"{url} devolvio {(int)status}");
        }

        // Tampoco puede vender aunque llame directo a la API del punto de venta.
        var token = await cliente.TokenFromAsync("/Inventory");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/Pos/Scan")
        { Content = JsonContent.Create(new { barcode = await app.BarraAsync("PAN-001") }) };
        req.Headers.Add("RequestVerificationToken", token);
        var scan = await cliente.Http.SendAsync(req);
        Assert.True(scan.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task La_cajera_no_entra_al_inventario_si_no_se_lo_dan()
    {
        await using var app = new PosAppFactory();
        await RegresionFuncionesExistentes.CrearUsuarioAsync(app, "maria", Roles.Cashier);
        var cajera = await PosClient.LoginAsync(app, "maria", "Cajera123");

        var (status, _) = await cajera.GetAsync("/Inventory");
        Assert.Equal(HttpStatusCode.Redirect, status);
        var menu = (await cajera.GetAsync("/")).Body;
        Assert.DoesNotMatch(EnlaceInventario, menu);
        Assert.Matches(EnlacePos, menu);   // a la cajera no le cambia nada
    }

    [Fact]
    public async Task Supervisor_y_administrador_entran_al_inventario()
    {
        await using var app = new PosAppFactory();
        await RegresionFuncionesExistentes.CrearUsuarioAsync(app, "super", Roles.Supervisor, "Super123");
        Assert.Equal(HttpStatusCode.OK, (await (await PosClient.LoginAsync(app, "super", "Super123")).GetAsync("/Inventory")).Status);
        Assert.Equal(HttpStatusCode.OK, (await (await PosClient.LoginAsync(app)).GetAsync("/Inventory")).Status);
    }

    [Fact]
    public async Task Pantalla_agregar_cargar_y_quitar_por_la_api_web()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        var id = (await app.ProductoAsync("YOG-001")).Id;

        async Task<System.Text.Json.JsonElement> Post(string action, object body)
        {
            var token = await admin.TokenFromAsync("/Inventory");
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/Inventory/{action}") { Content = JsonContent.Create(body) };
            req.Headers.Add("RequestVerificationToken", token);
            var r = await admin.Http.SendAsync(req);
            r.EnsureSuccessStatusCode();
            return System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        var buscar = await admin.GetJsonAsync("/Inventory/Search?term=yogurt");
        Assert.Equal(2, buscar.GetArrayLength());
        var todos = await admin.GetJsonAsync("/Inventory/Search");
        Assert.All(todos.EnumerateArray(), x => Assert.Contains("Lacteos ›", x.GetProperty("categoryName").GetString()));

        var agregado = await Post("Track", new { productId = id });
        Assert.True(agregado.GetProperty("success").GetBoolean());
        Assert.Equal(1, agregado.GetProperty("rows").GetArrayLength());

        var entrada = await Post("Apply", new { mode = "entry", items = new[] { new { productId = id, quantity = 5 } }, note = "llegada" });
        Assert.True(entrada.GetProperty("success").GetBoolean());
        Assert.Equal(17m, await app.StockAsync("YOG-001"));   // 12 sembrados + 5

        var edicion = await Post("Apply", new { mode = "set", items = new[] { new { productId = id, quantity = 4 } } });
        Assert.True(edicion.GetProperty("success").GetBoolean());
        Assert.Equal(4m, await app.StockAsync("YOG-001"));

        var malo = await Post("Apply", new { mode = "sumar", items = new[] { new { productId = id, quantity = 4 } } });
        Assert.False(malo.GetProperty("success").GetBoolean());

        var movs = await admin.GetJsonAsync($"/Inventory/Movements?productId={id}");
        Assert.Equal(2, movs.GetArrayLength());
        Assert.Equal("Edición de cantidad", movs[0].GetProperty("typeLabel").GetString());
        Assert.Equal("Entrada", movs[1].GetProperty("typeLabel").GetString());

        var quitado = await Post("Untrack", new { productId = id });
        Assert.True(quitado.GetProperty("success").GetBoolean());
        Assert.Equal(0, quitado.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task Las_acciones_del_inventario_exigen_token_antifalsificacion()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        var id = (await app.ProductoAsync("YOG-001")).Id;

        var sinToken = await admin.Http.PostAsJsonAsync("/Inventory/Track", new { productId = id });

        Assert.Equal(HttpStatusCode.BadRequest, sinToken.StatusCode);
        Assert.False((await app.ProductoAsync("YOG-001")).TrackInventory);
    }

    [Fact]
    public async Task La_seleccion_se_recuerda_al_salir_y_al_reiniciar_el_servidor()
    {
        var db = Path.Combine(Path.GetTempPath(), $"pos-seleccion-{Guid.NewGuid():N}.db");
        try
        {
            await using (var app = new PosAppFactory(db))
            {
                _ = app.CreateClient();
                await app.ControlarAsync("QUE-002", 7);
                await app.ControlarAsync("YOG-002", 4);
            }

            await using (var reiniciada = new PosAppFactory(db))
            {
                var otraSesion = await PosClient.LoginAsync(reiniciada);
                var (_, body) = await otraSesion.GetAsync("/Inventory");
                Assert.Contains("Queso edam tajado 200 g", body);
                Assert.Contains("Yogurt natural 500 g", body);
                Assert.DoesNotContain("\"name\":\"Yogurt de fresa 1 L\"", body);
            }
        }
        finally
        {
            foreach (var f in new[] { db, db + "-wal", db + "-shm" }) if (File.Exists(f)) File.Delete(f);
        }
    }

    [Fact]
    public async Task Excel_trae_el_inventario_y_sus_movimientos()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        var queso = await app.ControlarAsync("QUE-001", 4.25m);
        await app.ControlarAsync("QUE-002", 0);
        await app.AplicarAsync(InventoryMode.Entry, (queso, 1.5m));

        var respuesta = await admin.Http.GetAsync("/Inventory/Excel");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            respuesta.Content.Headers.ContentType!.MediaType);
        Assert.EndsWith(".xlsx", respuesta.Content.Headers.ContentDisposition!.FileNameStar ?? respuesta.Content.Headers.ContentDisposition.FileName);

        using var libro = new XLWorkbook(await respuesta.Content.ReadAsStreamAsync());
        var hoja = libro.Worksheet("Inventario");
        var filas = hoja.Table("Inventario").DataRange.Rows().ToList();
        Assert.Equal(2, filas.Count);

        var fresco = filas.Single(r => r.Cell(1).GetString() == "QUE-001");
        Assert.Equal("Queso fresco (por kilo)", fresco.Cell(2).GetString());
        Assert.Equal("Lacteos › Quesos", fresco.Cell(3).GetString());
        Assert.Equal(5.75, fresco.Cell(5).GetDouble(), 3);
        Assert.Equal("0.000", fresco.Cell(5).Style.NumberFormat.Format);

        var edam = filas.Single(r => r.Cell(1).GetString() == "QUE-002");
        Assert.Equal("Sin existencias", edam.Cell(7).GetString());

        var movimientos = libro.Worksheet("Movimientos").Table("Movimientos").DataRange.Rows().ToList();
        Assert.Equal(3, movimientos.Count);   // dos ediciones iniciales y una entrada
        Assert.Contains(movimientos, r => r.Cell(3).GetString() == "Entrada" && Math.Abs(r.Cell(4).GetDouble() - 1.5) < 0.0001);
    }

    [Fact]
    public async Task Excel_sin_productos_no_falla()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        var respuesta = await admin.Http.GetAsync("/Inventory/Excel");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var libro = new XLWorkbook(await respuesta.Content.ReadAsStreamAsync());
        Assert.Contains("Todavía no hay productos", libro.Worksheet("Inventario").Cell(4, 1).GetString());
    }

    [Fact]
    public async Task Sin_categorias_habilitadas_la_pantalla_explica_que_hacer()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        await app.DbAsync(async db =>
        {
            await db.Categories.ExecuteUpdateAsync(s => s.SetProperty(c => c.InventoryEnabled, false));
        });

        var (status, body) = await admin.GetAsync("/Inventory");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Ninguna categoría tiene habilitado el inventario", body);
    }

    [Fact]
    public async Task Categorias_no_permiten_ciclos_ni_borrar_una_principal_con_subcategorias()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        var lacteos = await app.DbAsync(db => db.Categories.FirstAsync(c => c.Name == "Lacteos"));
        var quesos = await app.DbAsync(db => db.Categories.FirstAsync(c => c.Name == "Quesos"));

        // Lacteos dentro de Quesos: Quesos ya es subcategoria.
        var ciclo = await admin.PostFormAsync("/Categories/Edit", $"/Categories/Edit/{lacteos.Id}", new()
        {
            ["Id"] = lacteos.Id.ToString(), ["Name"] = "Lacteos", ["Color"] = "#2563eb",
            ["IsActive"] = "true", ["ParentCategoryId"] = quesos.Id.ToString()
        });
        Assert.Equal(HttpStatusCode.OK, ciclo.StatusCode);
        Assert.Null((await app.DbAsync(db => db.Categories.FirstAsync(c => c.Name == "Lacteos"))).ParentCategoryId);

        var borrar = await admin.PostFormAsync($"/Categories/Delete/{lacteos.Id}", "/Categories", new());
        Assert.Equal(HttpStatusCode.Redirect, borrar.StatusCode);
        Assert.True(await app.DbAsync(db => db.Categories.AnyAsync(c => c.Name == "Lacteos")));

        // Habilitar el inventario de una categoria desde su formulario.
        var bebidas = await app.DbAsync(db => db.Categories.FirstAsync(c => c.Name == "Bebidas"));
        await admin.PostFormAsync("/Categories/Edit", $"/Categories/Edit/{bebidas.Id}", new()
        {
            ["Id"] = bebidas.Id.ToString(), ["Name"] = "Bebidas", ["Color"] = "#0891b2",
            ["IsActive"] = "true", ["InventoryEnabled"] = "true"
        });
        Assert.True((await app.DbAsync(db => db.Categories.FirstAsync(c => c.Name == "Bebidas"))).InventoryEnabled);
    }

    [Fact]
    public async Task Instalacion_nueva_carga_la_configuracion_inicial_del_negocio()
    {
        await using var app = new PosAppFactory(); _ = app.CreateClient();
        var s = await app.ServiceAsync<ISettingsService, PosSettings>(x => x.GetAsync());
        Assert.Equal("PEN", s.CurrencyCode);
        Assert.Equal("Gracias por su compra. Vuelva pronto.", s.BusinessFooter);
        Assert.Equal("Av. Principal 123", s.BusinessAddress);
    }

    [Fact]
    public async Task Los_roles_existentes_reciben_el_permiso_nuevo_una_sola_vez()
    {
        var db = Path.Combine(Path.GetTempPath(), $"pos-permiso-{Guid.NewGuid():N}.db");
        try
        {
            await using (var app = new PosAppFactory(db))
            {
                _ = app.CreateClient();
                Assert.Contains(Permissions.InventoryManage,
                    await RegresionFuncionesExistentes.PermisosDelRolAsync(app, Roles.Supervisor));
                Assert.Equal(Roles.ReportsAdminPermissions.OrderBy(x => x),
                    (await RegresionFuncionesExistentes.PermisosDelRolAsync(app, Roles.ReportsAdmin)).OrderBy(x => x));

                // El administrador decide que el supervisor no lleve inventario.
                await app.ServiceAsync<RoleManager<ApplicationRole>, bool>(async rm =>
                {
                    var rol = await rm.FindByNameAsync(Roles.Supervisor);
                    var claim = (await rm.GetClaimsAsync(rol!)).First(c => c.Value == Permissions.InventoryManage);
                    await rm.RemoveClaimAsync(rol!, claim);
                    return true;
                });
            }

            await using (var reiniciada = new PosAppFactory(db))
            {
                _ = reiniciada.CreateClient();
                Assert.DoesNotContain(Permissions.InventoryManage,
                    await RegresionFuncionesExistentes.PermisosDelRolAsync(reiniciada, Roles.Supervisor));
            }
        }
        finally
        {
            foreach (var f in new[] { db, db + "-wal", db + "-shm" }) if (File.Exists(f)) File.Delete(f);
        }
    }

    [Fact]
    public async Task Todas_las_pantallas_nuevas_y_cambiadas_cargan()
    {
        await using var app = new PosAppFactory();
        var admin = await PosClient.LoginAsync(app);
        var queso = await app.ControlarAsync("QUE-002", 3);
        var quesos = await app.DbAsync(db => db.Categories.FirstAsync(c => c.Name == "Quesos"));

        foreach (var url in new[] { "/Inventory", "/Inventory/Search", "/Inventory/Movements",
                     $"/Products/Edit/{queso}", $"/Categories/Edit/{quesos.Id}", "/Categories", "/Categories/Create" })
        {
            var (status, body) = await admin.GetAsync(url);
            Assert.True(status == HttpStatusCode.OK && !body.Contains("Exception"), $"{url} -> {(int)status}");
        }

        var edicion = (await admin.GetAsync($"/Products/Edit/{queso}")).Body;
        Assert.Contains("está en el inventario personalizado", edicion);
    }
}
