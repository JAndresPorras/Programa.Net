using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pos.Web.Models;
using Pos.Web.Security;
using Pos.Web.Services;

namespace Pos.Web.Data;

/// <summary>
/// Crea la base de datos, los roles con sus permisos, el usuario administrador
/// inicial y un catalogo de ejemplo la primera vez que arranca el sistema.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();

        await db.Database.MigrateAsync();

        await SeedRolesAsync(roleManager, logger);
        var admin = await SeedAdminAsync(userManager, config, logger);
        await SeedSettingsAsync(db, logger);
        await SeedCatalogAsync(db, admin, logger);
    }

    private static async Task SeedRolesAsync(RoleManager<ApplicationRole> roleManager, ILogger logger)
    {
        var definitions = new (string Name, string Description, string[] Permissions)[]
        {
            (Roles.Admin, "Acceso total al sistema", Roles.AdminPermissions),
            (Roles.Supervisor, "Administra catalogo, caja y reportes", Roles.SupervisorPermissions),
            (Roles.Cashier, "Vende en el punto de venta y cierra su caja", Roles.CashierPermissions)
        };

        foreach (var (name, description, permissions) in definitions)
        {
            var role = await roleManager.FindByNameAsync(name);
            if (role is null)
            {
                role = new ApplicationRole(name) { Description = description, IsSystemRole = true };
                var result = await roleManager.CreateAsync(role);
                if (!result.Succeeded)
                {
                    logger.LogError("No se pudo crear el rol {Role}: {Errors}", name,
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                    continue;
                }
                logger.LogInformation("Rol creado: {Role}", name);
            }

            var claims = await roleManager.GetClaimsAsync(role);
            foreach (var permission in permissions)
            {
                if (claims.Any(c => c.Type == Permissions.ClaimType && c.Value == permission)) continue;
                await roleManager.AddClaimAsync(role,
                    new System.Security.Claims.Claim(Permissions.ClaimType, permission));
            }
        }
    }

    private static async Task<ApplicationUser> SeedAdminAsync(
        UserManager<ApplicationUser> userManager, IConfiguration config, ILogger logger)
    {
        var userName = config["Seed:AdminUserName"] ?? "admin";
        var email = config["Seed:AdminEmail"] ?? "admin@puntodeventa.local";
        var password = config["Seed:AdminPassword"] ?? "Admin123*";

        var admin = await userManager.FindByNameAsync(userName);
        if (admin is not null) return admin;

        admin = new ApplicationUser
        {
            UserName = userName,
            Email = email,
            EmailConfirmed = true,
            FullName = "Administrador del sistema",
            IsActive = true
        };

        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "No se pudo crear el usuario administrador: " +
                string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        await userManager.AddToRoleAsync(admin, Roles.Admin);

        logger.LogWarning("=================================================================");
        logger.LogWarning(" USUARIO ADMINISTRADOR CREADO");
        logger.LogWarning("   Usuario:    {User}", userName);
        logger.LogWarning("   Contrasena: {Password}", password);
        logger.LogWarning("   Cambie la contrasena desde Mi perfil apenas ingrese.");
        logger.LogWarning("=================================================================");

        return admin;
    }

    private static async Task SeedSettingsAsync(ApplicationDbContext db, ILogger logger)
    {
        if (await db.AppSettings.AnyAsync()) return;

        var defaults = new Dictionary<string, string>
        {
            [SettingKeys.BusinessName] = "Mi Negocio",
            [SettingKeys.BusinessAddress] = "Av. Principal 123",
            [SettingKeys.BusinessPhone] = "",
            [SettingKeys.BusinessFooter] = "Gracias por su compra. Vuelva pronto.",
            [SettingKeys.CurrencyCode] = "PEN",
            [SettingKeys.TaxEnabled] = "False",
            [SettingKeys.TaxRate] = "18",
            [SettingKeys.TaxName] = "IGV",
            [SettingKeys.TaxIncluded] = "True",
            [SettingKeys.ReceiptSeries] = "B001",
            [SettingKeys.ReceiptPrintAuto] = "True",
            [SettingKeys.ReceiptCopies] = "1",
            [SettingKeys.PriceCashSteps] = "True",
            [SettingKeys.TotalCashRounding] = "True",
            [SettingKeys.PrinterMode] = "browser",
            [SettingKeys.PrinterHost] = "192.168.1.100",
            [SettingKeys.PrinterPort] = "9100",
            [SettingKeys.PrinterCutPaper] = "True",
            [SettingKeys.PrinterOpenDrawer] = "True",
            [SettingKeys.PrinterCharsPerLine] = "48",
            [SettingKeys.PosDefaultOpeningFloat] = "0",
            [SettingKeys.PosStockControl] = "False",
            [SettingKeys.TimeZone] = "America/Lima"
        };

        db.AppSettings.AddRange(defaults.Select(kv => new AppSetting { Key = kv.Key, Value = kv.Value }));
        await db.SaveChangesAsync();
        logger.LogInformation("Configuracion inicial creada");
    }

    private static async Task SeedCatalogAsync(ApplicationDbContext db, ApplicationUser admin, ILogger logger)
    {
        if (await db.Categories.AnyAsync()) return;

        var categories = new List<Category>
        {
            new() { Name = "Panaderia",  Description = "Pan y productos de horno", Color = "#d97706" },
            new() { Name = "Pasteleria", Description = "Tortas y postres",          Color = "#db2777" },
            new() { Name = "Bebidas",    Description = "Gaseosas, jugos y agua",    Color = "#0891b2" },
            new() { Name = "Abarrotes",  Description = "Productos de despensa",     Color = "#65a30d" }
        };
        db.Categories.AddRange(categories);
        await db.SaveChangesAsync();

        // Los precios son multiplos de la moneda mas pequena para que el total
        // siempre se pueda cobrar exacto. Los marcados por peso se venden en kilos.
        var seedProducts = new (string Cat, string Code, string Name, decimal Price, string Barcode,
                                string Unit, bool Produced, bool ByWeight)[]
        {
            ("Panaderia",  "PAN-001", "Pan frances (unidad)",      0.50m, "7750001000015", "UND", true,  false),
            ("Panaderia",  "PAN-002", "Pan integral 500g",         6.50m, "7750001000022", "UND", true,  false),
            ("Panaderia",  "PAN-003", "Ciabatta artesanal",        3.00m, "7750001000039", "UND", true,  false),
            ("Panaderia",  "PAN-004", "Pan de yema (por kilo)",   12.50m, "7750001000114", "KG",  true,  true),
            ("Pasteleria", "PAS-001", "Torta de chocolate porcion",8.00m, "7750001000046", "UND", true,  false),
            ("Pasteleria", "PAS-002", "Alfajor de manjar",         2.50m, "7750001000053", "UND", true,  false),
            ("Bebidas",    "BEB-001", "Agua mineral 625ml",        2.00m, "7750001000060", "UND", false, false),
            ("Bebidas",    "BEB-002", "Gaseosa 500ml",             3.50m, "7750001000077", "UND", false, false),
            ("Bebidas",    "BEB-003", "Jugo de naranja 1L",        9.90m, "7750001000084", "UND", false, false),
            ("Abarrotes",  "ABA-001", "Arroz extra 1kg",           4.80m, "7750001000091", "UND", false, false),
            ("Abarrotes",  "ABA-002", "Azucar rubia 1kg",          4.20m, "7750001000107", "UND", false, false),
            ("Abarrotes",  "ABA-003", "Queso fresco (por kilo)",  28.00m, "7750001000121", "KG",  false, true)
        };

        foreach (var p in seedProducts)
        {
            var category = categories.First(c => c.Name == p.Cat);
            var product = new Product
            {
                Code = p.Code,
                Name = p.Name,
                CategoryId = category.Id,
                Price = p.Price,
                Unit = p.Unit,
                Stock = 100,
                MinStock = 10,
                SoldByWeight = p.ByWeight,
                IsProduced = p.Produced,
                IsActive = true,
                CreatedByUserId = admin.Id
            };
            product.Barcodes.Add(new Barcode
            {
                Code = p.Barcode,
                IsPrimary = true,
                Symbology = "EAN13",
                CreatedByUserId = admin.Id
            });
            db.Products.Add(product);
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Catalogo de ejemplo creado: {Count} productos", seedProducts.Length);
    }
}
