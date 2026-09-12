# Precio al público + ajuste a monedas reales + calculadora de peso

Complemento para agregar a **su** sistema de punto de venta
(`C:\Project\PuntoVenta\PuntoVenta`) sin tocar el modelo, el controlador ni
reestructurar la vista. Son dos archivos que se copian y una línea que se agrega.

---

## Qué hace

En la pantalla **Editar producto / Nuevo producto**, debajo del campo
«Precio de venta»:

1. **Precio al público** — se recalcula mientras escribe. Si hay impuesto
   configurado muestra el desglose (neto + IGV, o precio + IGV según cómo lo
   maneje).
2. **Aviso de moneda** — cuando el precio no se puede pagar con las monedas que
   existen, dice exactamente cuánto ajustar:

   > **S/ 4.17 no se puede pagar exacto.** En Soles la moneda más pequeña es
   > S/ 0.10, así que hay que **bajar S/ 0.07** o **subir S/ 0.03**.

   Con dos botones que lo aplican de una vez:
   `▼ Bajar S/ 0.07 → S/ 4.10` y `▲ Subir S/ 0.03 → S/ 4.20`.
   Si el precio ya es válido, el aviso cambia a verde confirmándolo.
3. **Bloquea el guardado** de un precio impagable y sacude el aviso para que se
   note. (Esto es solo del lado del navegador: si quiere que el servidor también
   lo rechace, vea *Validación en el servidor* más abajo.)
4. **Calculadora de peso** — al marcar «Se vende por peso (kg)» aparece un campo
   de peso (2 enteros y 3 decimales, máximo 99.999) que muestra cuánto se le
   cobra al cliente con ese peso.

> **Ojo con sus precios actuales.** En Perú ya no circulan las monedas de 1 y 5
> céntimos. Precios como **4.95**, **4.96** o **4.98** no se pueden pagar
> exactos: hay que dejarlos en 4.90 o 5.00. El complemento se lo va a marcar
> producto por producto apenas abra cada ficha.

---

## Instalación (3 pasos)

1. Copie los dos archivos dentro de su proyecto:

   ```
   C:\Project\PuntoVenta\PuntoVenta\wwwroot\js\precio-publico.js
   C:\Project\PuntoVenta\PuntoVenta\wwwroot\css\precio-publico.css
   ```

2. En la vista del formulario del producto (o en `_Layout.cshtml` si prefiere
   que cargue en todo el sitio) agregue estas dos líneas:

   ```html
   <link rel="stylesheet" href="~/css/precio-publico.css" />
   <script src="~/js/precio-publico.js"></script>
   ```

   Si su vista usa secciones, va dentro de `@section Scripts { ... }`.

3. Recargue la página con **Ctrl + F5** (para que no quede el archivo viejo en
   caché del navegador).

No hace falta compilar nada más ni tocar la base de datos.

---

## Configuración

Todo lo ajustable está en las primeras líneas de `precio-publico.js`:

```js
var CONFIG = {
    simbolo: 'S/',
    decimales: 2,             // 2 soles · 0 pesos colombianos
    monedaMasChica: 0.10,     // Perú 0.10 · Colombia 50
    nombreMoneda: 'Soles',

    impuestoActivo: false,    // true si desglosa IGV / IVA
    impuestoNombre: 'IGV',
    impuestoPorcentaje: 18,
    impuestoIncluido: true,   // true: el precio ya lo incluye
                              // false: el impuesto se suma sobre el precio

    exigirMonedaExacta: true,
    pesoMaximo: 99.999
};
```

Si su negocio maneja el IGV sumado aparte (`impuestoIncluido: false`), el
complemento ajusta el **precio base** para que el precio al público caiga en el
múltiplo correcto; en ese caso el precio base puede quedar con 4 decimales
(por ejemplo 4.0678 para que el público pague S/ 4.80), así que revise que su
columna de precio acepte esa precisión.

---

## Cómo probarlo antes de instalarlo

Abra **`demo.html`** con doble clic. Es una réplica de su pantalla actual con el
complemento ya cargado: puede escribir precios, presionar los botones de ajuste
y marcar la casilla de peso sin tocar su sistema.

---

## Validación en el servidor (recomendado)

El bloqueo del navegador evita el error por descuido, pero alguien podría
guardar un precio impagable saltándose el JavaScript. Para cerrarlo del todo,
agregue esto en la acción que guarda el producto, antes de `SaveChanges`:

```csharp
// La moneda más pequeña que circula en Perú es S/ 0.10.
const decimal monedaMasChica = 0.10m;

var precio = Math.Round(model.Precio, 2, MidpointRounding.AwayFromZero);
if (precio > 0 && precio % monedaMasChica != 0m)
{
    var abajo = Math.Floor(precio / monedaMasChica) * monedaMasChica;
    var arriba = Math.Ceiling(precio / monedaMasChica) * monedaMasChica;

    ModelState.AddModelError(nameof(model.Precio),
        $"El precio S/ {precio:N2} no se puede pagar exacto: la moneda más pequeña " +
        $"es S/ {monedaMasChica:N2}. Baje S/ {precio - abajo:N2} para llegar a " +
        $"S/ {abajo:N2} o suba S/ {arriba - precio:N2} para llegar a S/ {arriba:N2}.");

    // devuelva la vista con el modelo, como hace con los demás errores
}
```

Cambie `model.Precio` por el nombre real de su propiedad.

---

## Sobre el total de la venta

Aunque todos los precios sean múltiplos de S/ 0.10, un pesaje o un descuento
puede dejar el total en S/ 33.04. La solución es redondear **el total a cobrar**
al múltiplo de S/ 0.10 más cercano y mostrar el ajuste como una línea
«Redondeo» en el recibo, para que la caja cuadre al cierre.

Eso ya está implementado y probado en el sistema de este repositorio
(`src/Pos.Web/Services/SaleService.cs`, método `CheckoutAsync`); puede copiar la
lógica desde ahí.
