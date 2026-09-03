namespace Pos.Web.Security;

/// <summary>
/// Catalogo de permisos finos del sistema. Cada permiso se guarda como un claim
/// del rol, de modo que el administrador puede decidir exactamente que puede
/// hacer cada usuario sin tocar el codigo.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    // Punto de venta
    public const string PosSell = "pos.sell";
    public const string PosDiscount = "pos.discount";
    public const string PosCancelSale = "pos.cancel";
    public const string PosViewCollected = "pos.viewcollected";

    // Catalogo
    public const string ProductsView = "products.view";
    public const string ProductsCreate = "products.create";
    public const string ProductsEdit = "products.edit";
    public const string ProductsDelete = "products.delete";
    public const string ProductsChangePrice = "products.price";
    public const string CategoriesManage = "categories.manage";

    // Caja
    public const string CashOpen = "cash.open";
    public const string CashClose = "cash.close";
    public const string CashMovements = "cash.movements";
    public const string CashViewAll = "cash.viewall";

    // Reportes
    public const string ReportsSales = "reports.sales";
    public const string ReportsProduction = "reports.production";
    public const string ReportsExport = "reports.export";

    // Administracion
    public const string SettingsManage = "settings.manage";
    public const string UsersManage = "users.manage";
    public const string AuditView = "audit.view";

    public sealed record PermissionInfo(string Value, string Group, string Name, string Description);

    /// <summary>Lista completa usada por la pantalla de roles y usuarios.</summary>
    public static readonly IReadOnlyList<PermissionInfo> All = new List<PermissionInfo>
    {
        new(PosSell,            "Punto de venta", "Vender",                 "Escanear productos y cerrar ventas"),
        new(PosDiscount,        "Punto de venta", "Aplicar descuentos",     "Descontar un monto sobre el total de la venta"),
        new(PosCancelSale,      "Punto de venta", "Anular ventas",          "Anular un recibo ya cerrado"),
        new(PosViewCollected,   "Punto de venta", "Ver recaudado del turno","Mostrar el dinero acumulado del turno con el ojito"),

        new(ProductsView,       "Catalogo", "Ver productos",        "Consultar el maestro de codigos de barras"),
        new(ProductsCreate,     "Catalogo", "Crear productos",      "Dar de alta productos y codigos de barras"),
        new(ProductsEdit,       "Catalogo", "Editar productos",     "Modificar datos del producto"),
        new(ProductsDelete,     "Catalogo", "Eliminar productos",   "Desactivar o borrar productos"),
        new(ProductsChangePrice,"Catalogo", "Cambiar precios",      "Modificar el precio de venta (queda en el historial)"),
        new(CategoriesManage,   "Catalogo", "Administrar categorias","Crear y editar categorias"),

        new(CashOpen,           "Caja", "Abrir caja",              "Iniciar el turno con el fondo inicial"),
        new(CashClose,          "Caja", "Cerrar caja",             "Hacer el arqueo y la entrega del dia"),
        new(CashMovements,      "Caja", "Ingresos y retiros",      "Registrar movimientos de efectivo"),
        new(CashViewAll,        "Caja", "Ver cajas de todos",      "Consultar los turnos de otras cajeras"),

        new(ReportsSales,       "Reportes", "Reporte de ventas",   "Ver ventas por dia, usuario y categoria"),
        new(ReportsProduction,  "Reportes", "Pedido a produccion", "Generar el listado de lo vendido para la planta"),
        new(ReportsExport,      "Reportes", "Exportar",            "Descargar los reportes en CSV"),

        new(SettingsManage,     "Administracion", "Configuracion", "Cambiar moneda, datos del negocio e impresora"),
        new(UsersManage,        "Administracion", "Usuarios y roles","Crear usuarios y asignar permisos"),
        new(AuditView,          "Administracion", "Bitacora",      "Ver el registro de acciones del sistema")
    };

    public static IEnumerable<string> Values => All.Select(p => p.Value);

    public static IEnumerable<IGrouping<string, PermissionInfo>> Grouped => All.GroupBy(p => p.Group);

    public static string DisplayName(string value) =>
        All.FirstOrDefault(p => p.Value == value)?.Name ?? value;
}

/// <summary>Roles predefinidos que se crean al iniciar el sistema.</summary>
public static class Roles
{
    public const string Admin = "Administrador";
    public const string Supervisor = "Supervisor";
    public const string Cashier = "Cajera";

    /// <summary>El administrador tiene absolutamente todos los permisos.</summary>
    public static string[] AdminPermissions => Permissions.Values.ToArray();

    public static string[] SupervisorPermissions =>
    [
        Permissions.PosSell, Permissions.PosDiscount, Permissions.PosCancelSale, Permissions.PosViewCollected,
        Permissions.ProductsView, Permissions.ProductsCreate, Permissions.ProductsEdit, Permissions.ProductsChangePrice,
        Permissions.CategoriesManage,
        Permissions.CashOpen, Permissions.CashClose, Permissions.CashMovements, Permissions.CashViewAll,
        Permissions.ReportsSales, Permissions.ReportsProduction, Permissions.ReportsExport
    ];

    public static string[] CashierPermissions =>
    [
        Permissions.PosSell, Permissions.PosViewCollected,
        Permissions.ProductsView,
        Permissions.CashOpen, Permissions.CashClose,
        Permissions.ReportsProduction
    ];
}
