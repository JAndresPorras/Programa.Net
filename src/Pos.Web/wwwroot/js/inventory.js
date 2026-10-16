/* =========================================================================
   Inventario personalizado: elegir productos, entradas y ediciones.
   ========================================================================= */
(function () {
    'use strict';

    const cfg = window.INV_CONFIG;
    if (!cfg) { return; }

    const token = document.querySelector('input[name="__RequestVerificationToken"]');
    const MAX = 99999.999;

    const el = {
        body: document.getElementById('invBody'),
        empty: document.getElementById('invEmpty'),
        table: document.getElementById('invTable'),
        count: document.getElementById('trackedCount'),
        inputHeader: document.getElementById('inputHeader'),
        modeHint: document.getElementById('modeHint'),
        save: document.getElementById('btnSave'),
        saveText: document.getElementById('saveText'),
        clear: document.getElementById('btnClearInputs'),
        note: document.getElementById('invNote'),
        errors: document.getElementById('invErrors'),
        search: document.getElementById('invSearch'),
        results: document.getElementById('invResults'),
        movesBody: document.getElementById('movesBody'),
        movesEmpty: document.getElementById('movesEmpty'),
        movesFilter: document.getElementById('movesFilter'),
        movesAll: document.getElementById('movesAll')
    };

    if (!el.body) { return; }   // Sin categorias habilitadas no hay tabla.

    let rows = cfg.rows || [];
    let mode = 'entry';
    let busy = false;
    let movesProduct = null;

    // Lo escrito en cada fila se conserva aunque la tabla se vuelva a dibujar.
    const typed = {};

    /* ------------------------------------------------------- Utilidades */
    function post(url, payload) {
        return fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token ? token.value : ''
            },
            body: JSON.stringify(payload || {})
        }).then(function (r) {
            if (!r.ok) { throw new Error('HTTP ' + r.status); }
            return r.json();
        });
    }

    function fmt(value, byWeight) {
        const n = Number(value);
        if (byWeight) { return n.toFixed(3); }
        return Number.isInteger(n) ? String(n) : n.toFixed(3);
    }

    /** Valida lo escrito con las mismas reglas que el servidor. */
    function check(row, raw) {
        if (raw === '' || raw === null || raw === undefined) { return { empty: true }; }
        const q = Number(raw);
        if (!isFinite(q)) { return { error: 'No es un número' }; }
        if (mode === 'entry' && q <= 0) { return { error: 'La entrada debe ser mayor que cero' }; }
        if (mode === 'set' && q < 0) { return { error: 'No puede ser negativa' }; }
        if (q > MAX) { return { error: 'Máximo ' + MAX }; }
        if (!row.soldByWeight && !Number.isInteger(q)) { return { error: 'Se cuenta por unidades enteras' }; }
        if (row.soldByWeight && Math.round(q * 1000) / 1000 !== q) { return { error: 'Máximo 3 decimales' }; }
        return { quantity: q };
    }

    /* ---------------------------------------------------- Modo de carga */
    function setMode(next) {
        if (next === mode) { return; }

        // Cambiar de modo cambia el significado de lo ya escrito: se avisa.
        const pending = Object.keys(typed).filter(function (k) { return typed[k] !== ''; }).length;
        if (pending > 0) {
            const what = next === 'set'
                ? 'Las cantidades escritas pasarán a REEMPLAZAR lo que hay en el sistema.'
                : 'Las cantidades escritas pasarán a SUMARSE a lo que hay en el sistema.';
            if (!window.confirm('Tiene ' + pending + ' cantidades escritas. ' + what + ' ¿Cambiar de modo?')) {
                return;
            }
        }

        mode = next;
        document.querySelectorAll('.inv-mode__btn').forEach(function (b) {
            const on = b.getAttribute('data-mode') === mode;
            b.classList.toggle('is-active', on);
            b.setAttribute('aria-checked', on ? 'true' : 'false');
        });
        el.table.classList.toggle('inv-table--entry', mode === 'entry');
        el.table.classList.toggle('inv-table--set', mode === 'set');
        el.save.classList.toggle('btn-success', mode === 'entry');
        el.save.classList.toggle('btn-warning', mode === 'set');
        render();
    }

    function modeTexts() {
        if (mode === 'entry') {
            el.inputHeader.textContent = 'Llegan (se suman)';
            el.modeHint.innerHTML = '<b>Entrada:</b> escriba lo que llegó. Se <b>suma</b> a la cantidad que ya tiene el sistema.';
            el.modeHint.className = 'inv-mode__hint inv-mode__hint--entry';
        } else {
            el.inputHeader.textContent = 'Hay contado (reemplaza)';
            el.modeHint.innerHTML = '<b>Editar cantidad:</b> escriba lo que contó en la tienda. <b>Reemplaza</b> la cantidad del sistema.';
            el.modeHint.className = 'inv-mode__hint inv-mode__hint--set';
        }
    }

    /* ---------------------------------------------------------- Tabla */
    function preview(row) {
        const state = check(row, typed[row.productId]);
        if (state.empty) { return { html: '<span class="text-muted-x">—</span>', state: state }; }
        if (state.error) { return { html: '<span class="inv-bad">' + state.error + '</span>', state: state }; }

        const now = Number(row.stock);
        if (mode === 'entry') {
            const result = now + state.quantity;
            return {
                html: '<span class="inv-calc">' + fmt(now, row.soldByWeight) + ' + ' + fmt(state.quantity, row.soldByWeight) +
                      ' = </span><b class="inv-new inv-new--entry">' + fmt(result, row.soldByWeight) + '</b>',
                state: state
            };
        }
        const diff = state.quantity - now;
        const sign = diff > 0 ? '+' : '';
        return {
            html: '<span class="inv-calc">' + fmt(now, row.soldByWeight) + ' → </span><b class="inv-new inv-new--set">' +
                  fmt(state.quantity, row.soldByWeight) + '</b>' +
                  (diff !== 0 ? ' <span class="inv-diff">(' + sign + fmt(diff, row.soldByWeight) + ')</span>' : ''),
            state: state
        };
    }

    function render() {
        modeTexts();
        el.body.innerHTML = '';
        el.empty.style.display = rows.length ? 'none' : '';
        el.count.textContent = rows.length;

        rows.forEach(function (row, index) {
            const tr = document.createElement('tr');
            tr.dataset.productId = row.productId;

            const status = row.isEmpty
                ? '<span class="chip chip--danger">Sin existencias</span>'
                : row.isLow ? '<span class="chip chip--warning">Bajo el mínimo</span>' : '';

            tr.innerHTML =
                '<td>' +
                    '<div class="inv-name"></div>' +
                    '<div class="fs-11 text-muted-x inv-meta"></div>' +
                    (row.categoryEligible ? '' : '<span class="chip chip--warning mt-1">Categoría sin inventario</span>') +
                '</td>' +
                '<td class="t-right"><b class="num inv-stock"></b> <span class="fs-11 text-muted-x inv-unit"></span><div>' + status + '</div></td>' +
                '<td class="inv-col-input">' +
                    '<input type="number" class="form-control inv-input" min="0" ' +
                        'step="' + (row.soldByWeight ? '0.001' : '1') + '" inputmode="decimal" ' +
                        'aria-label="Cantidad" />' +
                '</td>' +
                '<td class="t-right inv-preview"></td>' +
                '<td class="fs-12 inv-last"></td>' +
                '<td class="t-right" style="white-space:nowrap">' +
                    '<button type="button" class="btn btn-soft btn-sm" data-act="moves" title="Ver movimientos">' +
                        '<svg class="icon"><use href="#i-history"></use></svg></button> ' +
                    '<button type="button" class="btn btn-soft btn-sm" data-act="remove" title="Quitar del inventario">' +
                        '<svg class="icon"><use href="#i-x"></use></svg></button>' +
                '</td>';

            tr.querySelector('.inv-name').textContent = row.name;
            tr.querySelector('.inv-meta').textContent = row.code + ' · ' + row.categoryName;
            tr.querySelector('.inv-stock').textContent = row.stockText;
            tr.querySelector('.inv-unit').textContent = row.unit;
            tr.querySelector('.inv-last').textContent = row.lastManual
                ? row.lastManual + (row.lastManualBy ? ' · ' + row.lastManualBy : '')
                : 'Nunca cargado';

            const input = tr.querySelector('.inv-input');
            input.placeholder = mode === 'entry' ? 'Llegan' : 'Hay';
            input.value = typed[row.productId] || '';

            const paint = function () {
                const p = preview(row);
                tr.querySelector('.inv-preview').innerHTML = p.html;
                input.classList.toggle('is-invalid', !!p.state.error);
                tr.classList.toggle('inv-row--typed', !p.state.empty && !p.state.error);
            };

            input.addEventListener('input', function () {
                typed[row.productId] = input.value;
                paint();
                refreshSave();
            });

            // Enter pasa a la fila siguiente: se cargan muchas cantidades sin el mouse.
            input.addEventListener('keydown', function (e) {
                if (e.key !== 'Enter') { return; }
                e.preventDefault();
                if (e.ctrlKey) { save(); return; }
                const next = el.body.querySelectorAll('.inv-input')[index + 1];
                if (next) { next.focus(); next.select(); } else { el.save.focus(); }
            });

            tr.querySelector('[data-act=remove]').addEventListener('click', function () { untrack(row); });
            tr.querySelector('[data-act=moves]').addEventListener('click', function () { loadMoves(row.productId, row.name); });

            paint();
            el.body.appendChild(tr);
        });

        refreshSave();
    }

    function pendingLines() {
        const lines = [];
        let invalid = 0;
        rows.forEach(function (row) {
            const s = check(row, typed[row.productId]);
            if (s.error) { invalid++; }
            else if (!s.empty) { lines.push({ productId: row.productId, quantity: s.quantity }); }
        });
        return { lines: lines, invalid: invalid };
    }

    function refreshSave() {
        const p = pendingLines();
        const n = p.lines.length;
        const verb = mode === 'entry' ? 'Sumar entradas' : 'Reemplazar cantidades';
        el.saveText.textContent = n > 0 ? verb + ' (' + n + ')' : verb;
        el.save.disabled = busy || n === 0 || p.invalid > 0;
    }

    /* ----------------------------------------------------------- Guardar */
    function showErrors(list) {
        if (!list || !list.length) { el.errors.style.display = 'none'; return; }
        el.errors.innerHTML = '';
        const ul = document.createElement('ul');
        ul.className = 'mb-0 ps-3';
        list.forEach(function (msg) {
            const li = document.createElement('li');
            li.textContent = msg;
            ul.appendChild(li);
        });
        el.errors.appendChild(ul);
        el.errors.style.display = '';
    }

    function save() {
        const p = pendingLines();
        if (busy || p.lines.length === 0 || p.invalid > 0) { return; }

        busy = true;
        refreshSave();
        showErrors(null);

        post(cfg.urls.apply, { mode: mode, items: p.lines, note: el.note.value })
            .then(function (res) {
                if (!res.success) {
                    showErrors(res.errors);
                    window.posToast('No se guardó nada: revise las cantidades marcadas.', 'danger');
                    return;
                }
                rows = res.rows;
                Object.keys(typed).forEach(function (k) { delete typed[k]; });
                el.note.value = '';
                render();
                loadMoves(movesProduct ? movesProduct.id : null, movesProduct ? movesProduct.name : null);
                window.posToast(res.message, 'success');
            })
            .catch(function () { window.posToast('Error de conexión. No se guardó nada.', 'danger'); })
            .finally(function () { busy = false; refreshSave(); });
    }

    /* ------------------------------------------------------ Seleccion */
    function untrack(row) {
        if (!window.confirm('¿Quitar «' + row.name + '» del inventario?\n\n' +
            'Las ventas dejarán de descontarlo. Su cantidad se conserva por si lo vuelve a agregar.')) {
            return;
        }
        post(cfg.urls.untrack, { productId: row.productId }).then(function (res) {
            if (!res.success) { window.posToast(res.message, 'danger'); return; }
            delete typed[row.productId];
            rows = res.rows;
            render();
            runSearch();
            window.posToast(res.message, 'success');
        });
    }

    function track(productId) {
        post(cfg.urls.track, { productId: productId }).then(function (res) {
            if (!res.success) { window.posToast(res.message, 'danger'); return; }
            rows = res.rows;
            render();
            runSearch();
            window.posToast(res.message, 'success');
            // Deja el cursor listo para cargar su cantidad.
            const tr = el.body.querySelector('tr[data-product-id="' + productId + '"]');
            if (tr) {
                tr.classList.add('inv-row--new');
                const input = tr.querySelector('.inv-input');
                if (input) { input.focus(); }
            }
        });
    }

    let searchTimer = null;
    function runSearch() {
        const term = el.search.value.trim();
        fetch(cfg.urls.search + '?term=' + encodeURIComponent(term))
            .then(function (r) { return r.json(); })
            .then(function (items) {
                el.results.innerHTML = '';
                if (!items.length) {
                    el.results.innerHTML = '<div class="fs-13 text-muted-x py-2">No hay productos con ese criterio en las categorías habilitadas.</div>';
                    return;
                }
                items.forEach(function (item) {
                    const div = document.createElement('div');
                    div.className = 'inv-result' + (item.tracked ? ' is-tracked' : '');
                    div.innerHTML =
                        '<div class="inv-result__text"><b></b><div class="fs-11 text-muted-x"></div></div>' +
                        (item.tracked
                            ? '<span class="chip chip--success">En inventario</span>'
                            : '<button type="button" class="btn btn-primary btn-sm">' +
                              '<svg class="icon"><use href="#i-plus"></use></svg> Agregar</button>');
                    div.querySelector('b').textContent = item.name;
                    div.querySelector('.fs-11').textContent =
                        item.code + ' · ' + item.categoryName + ' · en sistema ' + item.stockText + ' ' + item.unit;
                    const btn = div.querySelector('button');
                    if (btn) { btn.addEventListener('click', function () { track(item.productId); }); }
                    el.results.appendChild(div);
                });
            });
    }

    /* ------------------------------------------------------- Movimientos */
    function loadMoves(productId, productName) {
        movesProduct = productId ? { id: productId, name: productName } : null;
        el.movesFilter.style.display = productId ? '' : 'none';
        el.movesAll.style.display = productId ? '' : 'none';
        if (productId) { el.movesFilter.textContent = productName; }

        fetch(cfg.urls.movements + (productId ? '?productId=' + productId : ''))
            .then(function (r) { return r.json(); })
            .then(function (list) {
                el.movesBody.innerHTML = '';
                el.movesEmpty.style.display = list.length ? 'none' : '';
                list.forEach(function (m) {
                    const tr = document.createElement('tr');
                    tr.innerHTML =
                        '<td class="num fs-12"></td><td></td>' +
                        '<td><span class="chip inv-type inv-type--' + m.type + '"></span>' +
                            '<div class="fs-11 text-muted-x inv-note"></div></td>' +
                        '<td class="t-right num"><b></b></td>' +
                        '<td class="t-right num fs-12"></td><td class="fs-12"></td>';
                    tr.children[0].textContent = m.when;
                    tr.children[1].textContent = m.productName;
                    tr.querySelector('.inv-type').textContent = m.typeLabel;
                    tr.querySelector('.inv-note').textContent = m.note || '';
                    tr.children[3].querySelector('b').textContent = m.quantityText + ' ' + m.unit;
                    tr.children[4].textContent = m.beforeText + ' → ' + m.afterText;
                    tr.children[5].textContent = m.userName;
                    el.movesBody.appendChild(tr);
                });
                if (productId) { el.movesBody.closest('.card-x').scrollIntoView({ behavior: 'smooth', block: 'start' }); }
            });
    }

    /* ---------------------------------------------------------- Eventos */
    document.querySelectorAll('.inv-mode__btn').forEach(function (b) {
        b.addEventListener('click', function () { setMode(b.getAttribute('data-mode')); });
    });
    el.save.addEventListener('click', save);
    el.clear.addEventListener('click', function () {
        Object.keys(typed).forEach(function (k) { delete typed[k]; });
        showErrors(null);
        render();
    });
    el.search.addEventListener('input', function () {
        clearTimeout(searchTimer);
        searchTimer = setTimeout(runSearch, 220);
    });
    // Con la pistola: el codigo llega con Enter y se agrega directo si hay uno solo.
    el.search.addEventListener('keydown', function (e) {
        if (e.key !== 'Enter') { return; }
        e.preventDefault();
        clearTimeout(searchTimer);
        runSearch();
    });
    el.movesAll.addEventListener('click', function () { loadMoves(null, null); });

    render();
    runSearch();
    loadMoves(null, null);
})();
