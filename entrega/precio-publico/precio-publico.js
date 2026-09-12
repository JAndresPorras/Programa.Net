/* =========================================================================
 * PRECIO AL PUBLICO + AJUSTE A MONEDAS REALES + CALCULADORA DE PESO
 * -------------------------------------------------------------------------
 * Complemento para la pantalla "Editar producto" / "Nuevo producto".
 * No necesita cambios en el modelo, el controlador ni la vista: se engancha
 * al formulario que ya existe buscando los campos por su etiqueta.
 *
 * Instalacion:
 *   1. Copie precio-publico.js y precio-publico.css dentro de wwwroot/js y
 *      wwwroot/css de su proyecto.
 *   2. En la vista del formulario (o en el _Layout) agregue al final:
 *        <link rel="stylesheet" href="~/css/precio-publico.css" />
 *        <script src="~/js/precio-publico.js"></script>
 *
 * Lo unico que puede querer tocar es el bloque CONFIGURACION de aqui abajo.
 * ========================================================================= */
(function () {
    'use strict';

    // ---------------------------------------------------------- CONFIGURACION
    var CONFIG = {
        simbolo: 'S/',            // Simbolo de la moneda
        decimales: 2,             // 2 para soles, 0 para pesos colombianos
        monedaMasChica: 0.10,     // Peru: 0.10 (ya no circulan 1 y 5 centimos)
        nombreMoneda: 'Soles',    // Solo para el texto del aviso

        impuestoActivo: false,    // true si desglosa IGV / IVA
        impuestoNombre: 'IGV',
        impuestoPorcentaje: 18,
        impuestoIncluido: true,   // true: el precio ya lo incluye
                                  // false: el impuesto se suma sobre el precio

        exigirMonedaExacta: true, // Bloquea guardar un precio impagable
        pesoMaximo: 99.999        // 2 enteros y 3 decimales
    };

    // ------------------------------------------------------------- UTILIDADES

    /** Formatea un importe con el simbolo de la moneda. */
    function money(valor) {
        var partes = Number(valor || 0).toFixed(CONFIG.decimales).split('.');
        partes[0] = partes[0].replace(/\B(?=(\d{3})+(?!\d))/g, ',');
        return CONFIG.simbolo + ' ' + partes.join('.');
    }

    /** Verdadero cuando el impuesto se suma sobre el precio en lugar de venir incluido. */
    function impuestoAparte() {
        return CONFIG.impuestoActivo && CONFIG.impuestoPorcentaje > 0 && !CONFIG.impuestoIncluido;
    }

    /** Precio que termina pagando el cliente en el mostrador. */
    function aPublico(base) {
        return impuestoAparte() ? base * (1 + CONFIG.impuestoPorcentaje / 100) : base;
    }

    /** Precio base que hay que cargar para que el cliente pague ese importe. */
    function aBase(publico) {
        return impuestoAparte() ? publico / (1 + CONFIG.impuestoPorcentaje / 100) : publico;
    }

    /**
     * Monedas vecinas de un importe. Se calcula en enteros (x 1.000.000) porque
     * con decimales sueltos 4.17 / 0.10 no da exacto y el aviso saldria mal.
     */
    function monedaVecina(valor) {
        var paso = CONFIG.monedaMasChica;
        if (!(paso > 0)) { return { abajo: valor, arriba: valor, exacto: true }; }

        var escala = 1000000;
        var v = Math.round(valor * escala);
        var p = Math.round(paso * escala);
        var q = Math.floor(v / p);
        var exacto = (v % p) === 0;

        return {
            abajo: (q * p) / escala,
            arriba: exacto ? valor : ((q + 1) * p) / escala,
            exacto: exacto
        };
    }

    /** Busca un campo por el texto de su etiqueta, que es lo unico estable entre proyectos. */
    function campoPorEtiqueta(textos, tipo) {
        var etiquetas = Array.prototype.slice.call(document.querySelectorAll('label'));

        for (var i = 0; i < etiquetas.length; i++) {
            var texto = (etiquetas[i].textContent || '').trim().toLowerCase();
            var coincide = textos.some(function (t) { return texto.indexOf(t) === 0; });
            if (!coincide) { continue; }

            var destino = null;
            var para = etiquetas[i].getAttribute('for');
            if (para) { destino = document.getElementById(para); }
            if (!destino) { destino = etiquetas[i].querySelector('input, select'); }
            if (!destino) {
                // Etiqueta y campo hermanos dentro del mismo contenedor.
                var caja = etiquetas[i].parentElement;
                if (caja) { destino = caja.querySelector(tipo === 'checkbox' ? 'input[type=checkbox]' : 'input, select'); }
            }
            if (destino && (tipo !== 'checkbox' || destino.type === 'checkbox')) { return destino; }
        }
        return null;
    }

    // ------------------------------------------------------------- ARRANQUE

    function iniciar() {
        var precio = campoPorEtiqueta(['precio de venta', 'precio'], 'number');
        if (!precio) { return; }   // No es la pantalla del producto.

        var porPeso = campoPorEtiqueta(['se vende por peso'], 'checkbox');

        // El atributo step del navegador muestra su propio aviso en ingles y
        // duplicaria el nuestro, asi que se libera y validamos nosotros.
        precio.setAttribute('step', 'any');

        // ---------------------------------------------- Recuadro del publico
        var caja = document.createElement('div');
        caja.className = 'pp-caja';
        caja.innerHTML =
            '<div class="pp-fila">' +
                '<span class="pp-titulo">Precio al público</span>' +
                '<b class="pp-valor"></b>' +
            '</div>' +
            '<div class="pp-nota"></div>';

        var aviso = document.createElement('div');
        aviso.className = 'pp-aviso';
        aviso.style.display = 'none';
        aviso.innerHTML =
            '<div class="pp-aviso-texto"></div>' +
            '<div class="pp-aviso-botones">' +
                '<button type="button" class="pp-btn" data-dir="abajo"></button>' +
                '<button type="button" class="pp-btn" data-dir="arriba"></button>' +
            '</div>';

        var ok = document.createElement('div');
        ok.className = 'pp-ok';
        ok.style.display = 'none';

        var ancla = precio.closest('.input-group') || precio;
        ancla.parentNode.insertBefore(caja, ancla.nextSibling);
        caja.parentNode.insertBefore(aviso, caja.nextSibling);
        aviso.parentNode.insertBefore(ok, aviso.nextSibling);

        var valor = caja.querySelector('.pp-valor');
        var nota = caja.querySelector('.pp-nota');
        var avisoTexto = aviso.querySelector('.pp-aviso-texto');
        var btnAbajo = aviso.querySelector('[data-dir=abajo]');
        var btnArriba = aviso.querySelector('[data-dir=arriba]');

        // ---------------------------------------------- Calculadora de peso
        var pesoCaja = null, pesoInput = null, pesoResultado = null;
        if (porPeso) {
            pesoCaja = document.createElement('div');
            pesoCaja.className = 'pp-peso';
            pesoCaja.innerHTML =
                '<div class="pp-peso-titulo">Calculadora de peso</div>' +
                '<div class="pp-peso-fila">' +
                    '<label class="pp-peso-label">Peso a cobrar' +
                        '<input type="number" class="pp-peso-input" min="0" max="' + CONFIG.pesoMaximo +
                        '" step="0.001" value="1.000" inputmode="decimal" placeholder="0.000" />' +
                    '</label>' +
                    '<div class="pp-peso-res"><span>Se le cobra</span><b></b></div>' +
                '</div>' +
                '<div class="pp-nota">Hasta 2 enteros y 3 decimales (máximo ' +
                    CONFIG.pesoMaximo.toFixed(3) + '). En la caja se digita el peso y el sistema calcula el importe.</div>';

            var contenedorPeso = porPeso.closest('.form-check') || porPeso.parentElement;
            contenedorPeso.parentNode.insertBefore(pesoCaja, contenedorPeso.nextSibling);

            pesoInput = pesoCaja.querySelector('.pp-peso-input');
            pesoResultado = pesoCaja.querySelector('.pp-peso-res b');
        }

        // ------------------------------------------------------- Calculos

        function limpiarPeso() {
            var kilos = parseFloat(pesoInput.value);
            if (!isFinite(kilos) || kilos < 0) { kilos = 0; }
            if (kilos > CONFIG.pesoMaximo) { kilos = CONFIG.pesoMaximo; }
            return Math.round(kilos * 1000) / 1000;
        }

        function refrescarPeso() {
            if (!pesoCaja) { return; }
            pesoCaja.style.display = porPeso.checked ? '' : 'none';
            if (!porPeso.checked) { return; }

            var base = parseFloat(precio.value || '0');
            if (isNaN(base)) { base = 0; }
            pesoResultado.textContent = money(aPublico(base) * limpiarPeso());
        }

        function refrescar() {
            var base = parseFloat(precio.value || '0');
            if (isNaN(base) || base < 0) { base = 0; }

            var publico = Number(aPublico(base).toFixed(CONFIG.decimales));
            valor.textContent = money(publico);

            if (!CONFIG.impuestoActivo) {
                nota.textContent = 'Sin impuesto configurado: es el mismo precio de venta.';
            } else if (impuestoAparte()) {
                nota.textContent = money(base) + ' + ' + CONFIG.impuestoNombre + ' ' +
                                   CONFIG.impuestoPorcentaje + '% (' + money(publico - base) + ')';
            } else {
                var neto = base / (1 + CONFIG.impuestoPorcentaje / 100);
                nota.textContent = 'Incluye ' + CONFIG.impuestoNombre + ' ' + CONFIG.impuestoPorcentaje +
                                   '% (' + money(base - neto) + ') · neto ' + money(neto);
            }

            if (!CONFIG.exigirMonedaExacta || base <= 0) {
                aviso.style.display = 'none';
                ok.style.display = 'none';
                refrescarPeso();
                return;
            }

            var vecina = monedaVecina(publico);
            if (vecina.exacto) {
                aviso.style.display = 'none';
                ok.style.display = '';
                ok.textContent = money(publico) + ' se puede pagar exacto con las monedas que existen.';
            } else {
                ok.style.display = 'none';
                aviso.style.display = '';

                var bajar = publico - vecina.abajo;
                var subir = vecina.arriba - publico;

                avisoTexto.innerHTML =
                    '<b>' + money(publico) + ' no se puede pagar exacto.</b> En ' + CONFIG.nombreMoneda +
                    ' la moneda más pequeña es ' + money(CONFIG.monedaMasChica) + ', así que hay que ' +
                    '<b>bajar ' + money(bajar) + '</b> o <b>subir ' + money(subir) + '</b>.';

                btnAbajo.textContent = '▼ Bajar ' + money(bajar) + ' → ' + money(vecina.abajo);
                btnArriba.textContent = '▲ Subir ' + money(subir) + ' → ' + money(vecina.arriba);
                btnAbajo.dataset.destino = vecina.abajo;
                btnArriba.dataset.destino = vecina.arriba;
            }

            refrescarPeso();
        }

        function aplicar(boton) {
            var destino = parseFloat(boton.dataset.destino || '0');
            var base = aBase(destino);
            precio.value = impuestoAparte() ? base.toFixed(4) : base.toFixed(CONFIG.decimales);
            precio.dispatchEvent(new Event('input', { bubbles: true }));
            precio.dispatchEvent(new Event('change', { bubbles: true }));
            refrescar();
            precio.focus();
        }

        // --------------------------------------------------------- Eventos

        precio.addEventListener('input', refrescar);
        precio.addEventListener('change', refrescar);

        [btnAbajo, btnArriba].forEach(function (boton) {
            // Sin esto el boton roba el foco al precio, la validacion inserta su
            // mensaje, el boton se corre entre mousedown y mouseup y el click se pierde.
            boton.addEventListener('mousedown', function (e) { e.preventDefault(); });
            boton.addEventListener('click', function () { aplicar(boton); });
        });

        if (porPeso) {
            porPeso.addEventListener('change', refrescarPeso);
            pesoInput.addEventListener('input', refrescarPeso);
            pesoInput.addEventListener('blur', function () {
                pesoInput.value = limpiarPeso().toFixed(3);
                refrescarPeso();
            });
        }

        // No se guarda un precio que no se pueda cobrar con monedas reales.
        var formulario = precio.closest('form');
        if (formulario) {
            formulario.addEventListener('submit', function (e) {
                if (!CONFIG.exigirMonedaExacta) { return; }

                var base = parseFloat(precio.value || '0');
                if (isNaN(base) || base <= 0) { return; }

                var publico = Number(aPublico(base).toFixed(CONFIG.decimales));
                if (monedaVecina(publico).exacto) { return; }

                e.preventDefault();
                e.stopPropagation();
                refrescar();
                aviso.scrollIntoView({ block: 'center', behavior: 'smooth' });
                aviso.classList.remove('pp-latido');
                void aviso.offsetWidth;          // reinicia la animacion
                aviso.classList.add('pp-latido');
                precio.focus();
            }, true);
        }

        refrescar();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', iniciar);
    } else {
        iniciar();
    }
})();
