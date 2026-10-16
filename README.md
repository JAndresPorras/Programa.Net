# Punto de Venta — Programa.Net

Sistema web de punto de venta hecho en **ASP.NET Core 8 MVC** para trabajar con una
**pistola lectora de código de barras** y una **impresora térmica de 80 mm**.
Todo el sistema está en español y pensado para el día a día de una caja:
se escanea, se cobra, se entrega el vuelto, se imprime el recibo y al final del
día se hace la entrega de caja al dueño.

---

## 1. Qué incluye

| Módulo | Qué resuelve |
|---|---|
| **Punto de venta** (pantalla inicial) | Se escanea con la pistola, se arma el ticket, se calcula el vuelto y se imprime el recibo. |
| **Maestro de códigos de barras** | Cada código apunta a un producto con categoría, código, nombre, precio e imagen opcional. Un producto puede tener varios códigos. |
| **Precios pagables** | Al escribir el precio se ve el **precio al público** con impuesto y el sistema avisa cuánto subir o bajar para que se pueda pagar exacto con las monedas del país. |
| **Venta por peso** | Los productos marcados por peso piden los kilos en el punto de venta (2 enteros y 3 decimales) y calculan el importe. |
| **Inventario personalizado** | Usted elige qué yogures y quesos controlar, registra entradas o corrige cantidades, y las ventas los descuentan solas sin bloquear nunca la caja. Exportable a Excel. |
| **Historial de precios** | Cada cambio de precio guarda precio anterior, precio nuevo, fecha, hora, usuario y motivo. |
| **Usuarios, roles y permisos** | 23 permisos finos agrupados por módulo. El rol da los permisos base y a cada persona se le pueden sumar permisos extra. |
| **Caja del día** | Apertura con fondo inicial, ingresos y retiros de efectivo, arqueo por denominaciones y **entrega de caja al dueño**. |
| **Recibos** | Listado del día, detalle, reimpresión y anulación con motivo. |
| **Reporte de ventas** | Por día, hora, cajera, categoría, forma de pago y productos más vendidos. Exportable a CSV. |
| **Pedido a producción** | Listado de lo vendido con la cantidad que hay que pedirle a la planta para reponer. |
| **Configuración** | Moneda (soles o pesos), datos del negocio, impuesto e impresora. Solo el administrador. |
| **Bitácora** | Quién hizo qué y cuándo: ingresos, ventas, anulaciones, cambios de precio y de caja. |

---

## 2. Cómo levantarlo

Requiere el **SDK de .NET 8**.

```bash
git clone https://github.com/JAndresPorras/Programa.Net.git
cd Programa.Net/src/Pos.Web
dotnet run
```

Abra `http://localhost:5080`.

En el primer arranque el sistema crea la base de datos, los tres roles con sus
permisos, un catálogo de ejemplo con 10 productos y el usuario administrador.

### Usuario que se entrega al inicio

| | |
|---|---|
| **Usuario** | `admin` |
| **Contraseña** | `Admin123*` |

> **Cámbiela apenas ingrese**, desde el menú del usuario → *Cambiar contraseña*.
> La ayuda del primer ingreso desaparece sola de la pantalla de acceso en cuanto
> cambia la contraseña o crea otro usuario.

Para usar otras credenciales iniciales, cámbielas en `appsettings.json` **antes**
del primer arranque:

```json
"Seed": {
  "AdminUserName": "admin",
  "AdminEmail": "admin@puntodeventa.local",
  "AdminPassword": "Admin123*"
}
```

---

## 3. Base de datos: local o en la nube

El sistema funciona con dos motores. Se elige en `appsettings.json`.

### Opción A — SQLite (por defecto, instalación local)

Un solo archivo `pos.db` junto a la aplicación. No hay que instalar nada más.
Ideal para una tienda con una o dos cajas.

```json
"Database": { "Provider": "Sqlite" },
"ConnectionStrings": { "Default": "Data Source=pos.db" }
```

### Opción B — SQL Server (base global, consultable por internet)

Para varios puntos de venta o para ver los reportes desde internet con la base
en un servidor o en Azure SQL.

```json
"Database": { "Provider": "SqlServer" },
"ConnectionStrings": {
  "Default": "Server=tcp:miservidor.database.windows.net,1433;Database=PuntoVenta;User ID=usuario;Password=clave;Encrypt=True;TrustServerCertificate=False"
}
```

Las migraciones de cada motor están separadas (`Data/Migrations/Sqlite` y
`Data/Migrations/SqlServer`) y se aplican solas al arrancar.

> En SQLite los importes se guardan como número de punto flotante porque ese
> motor no ordena ni suma correctamente los decimales guardados como texto.
> Todos los importes se redondean a los decimales de la moneda antes de
> guardarse. En SQL Server se usa `decimal(18,4)` real.

### Publicar el sitio para consultarlo por internet

El sitio es una aplicación web normal: se puede publicar en IIS, en un servicio
de Linux con Nginx, en Azure App Service o en un contenedor.

```bash
dotnet publish -c Release -o ./publicado
```

Para que otras máquinas de la tienda entren desde la red local:

```bash
dotnet run --urls "http://0.0.0.0:5080"
```

> Al exponerlo a internet, póngalo siempre detrás de **HTTPS**
> (Nginx, IIS o App Service con certificado). La cookie de sesión ya está
> marcada como `HttpOnly`.

---

## 4. La pistola lectora (3nStar SC050)

La pistola funciona como un teclado: escribe el código y manda `Enter`.
**No hay que instalar nada.** Solo conéctela por USB.

En la pantalla del punto de venta:

- El cursor vuelve **solo** al campo de escaneo, aunque la cajera haga clic en otro lado.
- Cada disparo agrega el producto; si dispara el mismo código otra vez, suma una unidad.
- Para vender varias unidades de un tirón se puede escribir `3*7750001000013` y `Enter`.
- Si el código no está en el maestro, sale un aviso claro con el código leído.

También puede dispararle a la pistola dentro del campo *Códigos de barras* de la
pantalla de productos para dar de alta un artículo sin escribir el número.

### Atajos de teclado

| Tecla | Acción |
|---|---|
| `Enter` | Agregar el código escaneado |
| `F2` | Cobrar e imprimir |
| `F4` | Buscar producto por nombre |
| `F8` | Vaciar el ticket |
| `Esc` | Volver al campo de escaneo |

---

## 5. La impresora térmica (3nStar RPT004, 80 mm)

Hay dos modos, se eligen en **Configuración → Impresora**.

### Modo «por el navegador» (recomendado con USB)

La impresora se instala en Windows como impresora normal y el sistema manda el
recibo ya maquetado para papel de 80 mm (`@page { size: 80mm auto }`).
Al cerrar la venta el recibo se imprime solo.

Para que salga sin márgenes ni encabezados del navegador:
en el diálogo de impresión de Chrome elija la RPT004, **Márgenes: Ninguno** y
desmarque *Encabezados y pies de página*. Marque también «usar siempre esta
impresora» para que no vuelva a preguntar.

### Modo «directo por red» (impresora con cable de red)

El servidor le habla directo por **ESC/POS al puerto 9100**: no depende del
navegador ni de los drivers del equipo de la caja.

Configure la IP de la impresora y pruebe con los botones
**Probar impresora de red** y **Abrir cajón de dinero**.

Incluye corte de papel y pulso para el cajón de dinero por el conector RJ11.
Los acentos se convierten a ASCII para que el ticket salga legible en cualquier
página de códigos de la impresora.

### DIP switches de la RPT004

Según el manual del equipo: `SW-1` corte automático, `SW-2` buzzer,
`SW-3` densidad, `SW-6` caracteres por línea (**42 / 48** — deje 48 para 80 mm),
`SW-7` cajón de dinero, `SW-8` velocidad del puerto serie.

---

## 6. La moneda (soles o pesos)

En **Configuración → Moneda** el administrador elige:

| | Soles peruanos | Pesos colombianos | Dólares |
|---|---|---|---|
| Código | PEN | COP | USD |
| Símbolo | `S/` | `$` | `US$` |
| Decimales | 2 | 0 | 2 |
| Redondeo del vuelto | 0,10 | 50 | 0,01 |
| Billetes rápidos | 200, 100, 50, 20, 10 | 100.000 … 2.000 | 100 … 1 |

El cambio afecta el formato de los importes, los decimales, el redondeo del
vuelto y los botones de pago rápido. **Las ventas ya cerradas conservan la
moneda con la que se cobraron**, así que los reportes históricos no se alteran.

> Cambiar la moneda **no convierte los precios**: no es un tipo de cambio, es la
> moneda con la que trabaja la caja. Si cambia de país, revise la lista de precios.

### Precios que se puedan pagar exactos

En Perú ya no circulan las monedas de 1 y 5 céntimos: la más pequeña es
**S/ 0.10**. Un precio de S/ 4.17 obliga a perder céntimos en cada venta, así que
el sistema no lo permite y ayuda a corregirlo.

Al escribir el precio del producto aparecen tres cosas:

1. **Precio al público**, al lado derecho del campo. Si el impuesto viene
   incluido muestra además el neto; si se suma aparte, muestra precio + impuesto.
2. Un **aviso que dice exactamente cuánto ajustar**:
   *«S/ 4.17 no se puede pagar exacto. En Soles peruanos la moneda más pequeña
   es S/ 0.10, así que hay que bajar S/ 0.07 o subir S/ 0.03.»*
3. Dos **botones para aplicarlo de una vez**:
   `▼ Bajar S/ 0.07 → S/ 4.10` y `▲ Subir S/ 0.03 → S/ 4.20`.

El precio se valida también en el servidor, de modo que no entra un precio
impagable ni escribiéndolo a mano.

### Redondeo del total

Aunque todos los precios sean exactos, un pesaje o un descuento pueden dejar el
total en S/ 33.04. Con **Redondear el total al efectivo** activado el sistema
cobra S/ 33.00 y el ajuste sale detallado como *Redondeo* en el recibo, en el
detalle de la venta y en la caja, para que nunca falte ni sobre dinero en el
arqueo.

Ambas opciones se activan o desactivan en **Configuración → Moneda**.

---

## 7. Productos que se venden por peso

Marque **«Se vende por peso»** en el producto y cargue el precio **por kilo**.
En la misma pantalla aparece una **calculadora de peso** para comprobar cuánto
se le cobrará al cliente antes de guardar.

En el punto de venta, cuando la cajera dispara la pistola sobre un producto por
peso, se abre la **balanza**: escribe los kilos (hasta **2 enteros y 3
decimales**, máximo 99.999), ve el importe calculado en vivo y lo agrega al
ticket. El peso queda visible en la línea y se corrige presionándolo.

También funciona escribiendo `0.755*7750001000129` directo en el campo de
escaneo, para cuando la balanza ya imprimió el peso.

---

## 8. Inventario personalizado (Yogures y Quesos)

Un inventario **opcional**: no se activa para toda la tienda, usted elige
producto por producto qué quiere controlar. Está en el menú
**Catálogo → Inventario**.

### Qué productos se pueden elegir

Los de las categorías marcadas con **«Permite inventario personalizado»**. En una
instalación nueva vienen marcadas **Yogures** y **Quesos**, dentro de la categoría
principal **Lácteos**. Habilitar la categoría no controla nada por sí solo: hay que
agregar cada producto en la vista de inventario. Para sumar otra categoría más
adelante basta con marcarla en *Categorías*.

### La vista de inventario

- **Agregar productos**: busque por nombre, código o dispare la pistola sobre el
  código de barras, y presione *Agregar*.
- La **selección se guarda sola** en la base de datos: sigue ahí al cerrar sesión,
  al reiniciar el servidor y para cualquier usuario.
- **Quitar** un producto no borra su cantidad: si lo vuelve a agregar, sigue donde
  estaba.
- Cada fila muestra la cantidad en el sistema y cuándo y quién la cargó por última vez.

### Entrada o Editar cantidad: dos operaciones bien distintas

Arriba de la tabla se elige qué hace el número que escribe:

| Modo | Color | Qué hace | Ejemplo |
|---|---|---|---|
| **Entrada** | verde | **Suma** a lo que hay | hay 5, llegan 10 → **15** |
| **Editar cantidad** | ámbar | **Reemplaza** por lo contado | hay 5, cuenta 10 → **10** |

Antes de guardar, cada fila muestra el resultado (`5 + 10 = 15` o `5 → 10`), el
encabezado de la columna y el botón cambian según el modo (*Sumar entradas* /
*Reemplazar cantidades*), y si cambia de modo con cantidades ya escritas el
sistema le pregunta, porque cambia lo que significan. Puede cargar varias filas y
guardarlas juntas: `Enter` pasa a la fila siguiente. Si una sola cantidad es
inválida no se guarda ninguna, para no dejar un conteo a medias.

Las unidades se cuentan enteras; los productos por peso aceptan 3 decimales.

### Las ventas

- Cada venta **descuenta sola** las unidades de los productos del inventario.
- Si el sistema dice que no alcanza, **avisa pero nunca bloquea**: al escanear sale
  un aviso amarillo y la línea del ticket lo marca; al cobrar se pide confirmar.
- **Ejemplo:** el sistema dice 9 quesos y se venden 10. Al cobrar aparece
  *«El inventario digital no alcanza»* con el detalle. Al confirmar, la venta se
  cierra, el inventario se ajusta a 10 (*Ajuste automático por venta*, +1) y sale la
  venta (−10). Queda en **0**, nunca en negativo.
- Si se anula el recibo, las unidades vuelven al inventario.
- Los productos que **no** están en el inventario personalizado no cambian: siguen
  como siempre.

Todo queda en **Movimientos**: entradas, ediciones, ventas, ajustes y anulaciones,
con la cantidad antes y después, el usuario y el recibo.

### Exportar a Excel

El botón **Exportar a Excel** descarga un libro con dos hojas: *Inventario* (cada
producto con su cantidad, mínimo, estado y última actualización) y *Movimientos*
(los últimos 90 días), listas para filtrar.

### Quién lo usa

El permiso **«Llevar el inventario»** lo tienen el Administrador, el Supervisor y el
rol **informes_admin**. Para dárselo a otra persona, use *Usuarios y permisos*.

---

## 9. El día de trabajo

1. **Abrir caja** — *Caja del día* → fondo inicial (el sencillo con el que arranca).
2. **Vender** — escanear, escribir el dinero recibido, `F2`. El sistema muestra el
   vuelto en grande y manda el recibo a la impresora. Si el producto se vende por
   peso, primero pide los kilos en la balanza.
3. **El ojito** — en la barra superior, el botón del ojo revela cuánto lleva
   recolectado la cajera en el turno. Está oculto por defecto y se vuelve a
   ocultar solo a los 20 segundos.
4. **Ingresos y retiros** — si sale dinero de la caja para un gasto, se registra
   con su motivo para que el arqueo cuadre.
5. **Cerrar caja** — se cuentan billetes y monedas por denominación, el sistema
   calcula la diferencia contra lo esperado y **cuánto hay que entregarle al
   dueño** (lo contado menos el sencillo que queda para mañana).
   El comprobante sale con el listado completo de productos vendidos y espacio
   para las dos firmas.
6. **Pedido a producción** — *Reportes → Pedido a producción* arma el documento
   para la planta:

   ```
   cantidad a pedir = demanda estimada + stock mínimo − stock actual
   demanda estimada = el mayor entre el promedio diario del período y lo vendido hoy
   ```

---

## 10. Permisos

Cuatro roles listos para usar, editables desde *Usuarios → Permisos por rol*:

- **Administrador** — todo. Siempre conserva todos los permisos y el sistema
  no deja desactivar al último administrador activo.
- **Supervisor** — vender, catálogo, precios, caja de todos y reportes.
- **Cajera** — vender, ver su recaudado, consultar productos, abrir y cerrar su
  propia caja y sacar el pedido a producción.
- **informes_admin** — reportes de ventas y de producción, exportar, consultar
  productos y llevar el inventario. No vende ni maneja caja ni configuración; al
  ingresar entra directo a *Inventario*.

Los 23 permisos se agrupan en *Punto de venta*, *Catálogo*, *Inventario*, *Caja*,
*Reportes* y *Administración*. A una persona se le pueden dar permisos extra por encima de su
rol sin cambiarle el rol a nadie más.

Si el administrador le quita un permiso a un rol, **se mantiene quitado** aunque el
servidor se reinicie. Cuando una versión nueva del sistema trae un permiso nuevo,
se agrega a los roles una sola vez.

---

## 11. Cómo está organizado el código

```
src/Pos.Web/
├── Controllers/     Pos, Sales, Cash, Products, Categories, Inventory, Reports, Settings, Users, Account
├── Data/            DbContext (SQLite + SQL Server), migraciones y datos iniciales
├── Models/          Product, Barcode, PriceHistory, Sale, SaleItem, CashSession, AppSetting...
├── Security/        Permisos, políticas de autorización y atributo [HasPermission]
├── Services/        Carrito, ventas, inventario, caja, reportes, configuración, moneda e impresión ESC/POS
├── ViewModels/      Modelos de las pantallas
├── Views/           Vistas Razor en español
└── wwwroot/         CSS propio, JS del punto de venta y Bootstrap local (funciona sin internet)

tests/Pos.Tests/     Pruebas automáticas: levantan la aplicación real con una base temporal
```

### Pruebas automáticas

```bash
dotnet test
```

Cada prueba arranca la aplicación completa (migraciones, roles, vistas) contra su
propia base SQLite temporal y la usa como lo haría el navegador: inicia sesión,
escanea, cobra, carga inventario. Cubren lo que ya existía (para detectar si un
cambio lo rompe) y todo el inventario, incluidos los casos límite y diez cajas
vendiendo el mismo producto a la vez.

El punto de venta no depende de ningún CDN: Bootstrap está en `wwwroot/lib` y los
íconos son un sprite SVG local, así que la caja sigue funcionando sin internet.
