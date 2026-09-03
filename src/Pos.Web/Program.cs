using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Pos.Web.Data;
using Pos.Web.Models;
using Pos.Web.Security;
using Pos.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Base de datos: SQLite para una instalacion local en la tienda (un solo
// archivo, sin servidor) o SQL Server cuando la base vive en la nube y el
// sitio se consulta por internet desde varios puntos de venta.
// ---------------------------------------------------------------------------
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("Default")
                       ?? "Data Source=pos.db";

if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContext<ApplicationDbContext, SqlServerApplicationDbContext>(options =>
        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
}
else
{
    builder.Services.AddDbContext<ApplicationDbContext, SqliteApplicationDbContext>(options =>
        options.UseSqlite(connectionString));
}

// ---------------------------------------------------------------------------
// Identidad, roles y permisos
// ---------------------------------------------------------------------------
builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireDigit = true;
        options.User.RequireUniqueEmail = false;
        options.Lockout.MaxFailedAccessAttempts = 8;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddClaimsPrincipalFactory<PosClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Cuenta/Ingresar";
    options.LogoutPath = "/Cuenta/Salir";
    options.AccessDeniedPath = "/Cuenta/SinPermiso";
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = true;
    options.Cookie.Name = "PosBarras.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

// ---------------------------------------------------------------------------
// Servicios de la aplicacion
// ---------------------------------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = "PosBarras.Session";
});

builder.Services.AddSingleton<ISettingsService, SettingsService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ICashSessionService, CashSessionService>();
builder.Services.AddScoped<ISaleService, SaleService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IReceiptPrinter, EscPosPrinter>();

builder.Services.AddControllersWithViews(options =>
{
    // Todo el sitio exige inicio de sesion salvo lo marcado con [AllowAnonymous].
    options.Filters.Add(new AuthorizeFilterPolicy());
}).AddViewOptions(options => options.HtmlHelperOptions.ClientValidationEnabled = true);

var app = builder.Build();

// Fechas y numeros en formato latinoamericano.
var culture = new CultureInfo("es-PE");
var localization = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(culture),
    SupportedCultures = [culture],
    SupportedUICultures = [culture]
};
app.UseRequestLocalization(localization);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// La pantalla inicial del sistema es el punto de venta.
app.MapControllerRoute(name: "default", pattern: "{controller=Pos}/{action=Index}/{id?}");

// ---------------------------------------------------------------------------
// Migraciones y datos iniciales
// ---------------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await DbSeeder.SeedAsync(scope.ServiceProvider, app.Configuration, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error al preparar la base de datos");
        throw;
    }
}

app.Run();

/// <summary>Filtro que exige usuario autenticado en todo el sitio.</summary>
public class AuthorizeFilterPolicy : Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter
{
    public AuthorizeFilterPolicy()
        : base(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    {
    }
}

/// <summary>Agrega el nombre completo del usuario a sus claims para mostrarlo en pantalla.</summary>
public class PosClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    Microsoft.Extensions.Options.IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>(userManager, roleManager, options)
{
    protected override async Task<System.Security.Claims.ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new System.Security.Claims.Claim("full_name", user.FullName));
        return identity;
    }
}
