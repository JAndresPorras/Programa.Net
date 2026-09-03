/* =========================================================================
   Punto de venta: lectura con pistola, ticket, cobro y vuelto.
   ========================================================================= */
(function () {
    'use strict';

    const cfg = window.POS_CONFIG;
    const token = document.querySelector('input[name="__RequestVerificationToken"]');

    const el = {
        scanBox: document.getElementById('scanBox'),
        scanInput: document.getElementById('scanInput'),
        body: document.getElementById('ticketBody'),
        empty: document.getElementById('ticketEmpty'),
        count: document.getElementById('ticketCount'),
        subtotal: document.getElementById('sumSubtotal'),
        discountRow: document.getElementById('rowDiscount'),
        discountText: document.getElementById('sumDiscount'),
        units: document.getElementById('sumUnits'),
        total: document.getElementById('sumTotal'),
        received: document.getElementById('receivedInput'),
        quick: document.getElementById('quickGrid'),
        changeBox: document.getElementById('changeBox'),
        changeValue: document.getElementById('changeValue'),
        checkout: document.getElementById('btnCheckout'),
        cashBlock: document.getElementById('cashBlock'),
        discountInput: document.getElementById('discountInput')
    };

    let cart = cfg.initialCart || { lines: [], total: 0 };
    let paymentMethod = 'Cash';
    let busy = false;

    /* --------------------------------------------------------- Utilidades */
    function money(value) {
        const d = cfg.currency.decimals;
        const fixed = Number(value || 0).toFixed(d);
        const parts = fixed.split('.');
        parts[0] = parts[0].replace(/\B(?=(\d{3})+(?!\d))/g, ',');
        return cfg.currency.symbol + ' ' + parts.join('.');
    }

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

    function focusScanner() {
        if (document.querySelector('.modal.show')) { return; }
        el.scanInput.focus();
        el.scanInput.select();
    }

    /* ------------------------------------------------- Pintado del ticket */
    function render(newProductId) {
        el.body.innerHTML = '';
        const lines = cart.lines || [];

        el.empty.style.display = lines.length ? 'none' : '';
        el.count.textContent = lines.length === 1 ? '1 producto' : lines.length + ' productos';

        lines.forEach(function (line) {
            const tr = document.createElement('tr');
            if (line.productId === newProductId) { tr.className = 'is-new'; }

            const step = line.soldByWeight ? '0.001' : '1';
            tr.innerHTML =
                '<td>' +
                    '<div class="line-name"></div>' +
                    '<div class="line-meta"></div>' +
                '</td>' +
                '<td class="t-right money">' + line.unitPriceText + '</td>' +
                '<td class="t-center">' +
                    '<div class="qty">' +
                        '<button type="button" data-act="dec" aria-label="Quitar uno">' +
                            '<svg class="icon"><use href="#i-minus"></use></svg></button>' +
                        '<input type="number" min="0" step="' + step + '" value="' + line.quantity + '" aria-label="Cantidad" />' +
                        '<button type="button" data-act="inc" aria-label="Agregar uno">' +
                            '<svg class="icon"><use href="#i-plus"></use></svg></button>' +
                    '</div>' +
                '</td>' +
                '<td class="t-right line-total">' + line.lineTotalText + '</td>' +
                '<td class="t-center">' +
                    '<button type="button" class="line-del" data-act="del" aria-label="Quitar del ticket">' +
                        '<svg class="icon"><use href="#i-trash"></use></svg></button>' +
                '</td>';

            tr.querySelector('.line-name').textContent = line.name;
            tr.querySelector('.line-meta').textContent =
                line.code + (line.categoryName ? ' · ' + line.categoryName : '') + ' · ' + line.unit;

            tr.querySelectorAll('[data-act]').forEach(function (btn) {
                btn.addEventListener('click', function () {
                    const act = btn.getAttribute('data-act');
                    if (act === 'del') { removeLine(line.productId); }
                    if (act === 'inc') { setQuantity(line.productId, Number(line.quantity) + 1); }
                    if (act === 'dec') { setQuantity(line.productId, Number(line.quantity) - 1); }
                });
            });

            const input = tr.querySelector('.qty input');
            input.addEventListener('change', function () { setQuantity(line.productId, Number(input.value)); });
            input.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') { e.preventDefault(); input.blur(); }
            });

            el.body.appendChild(tr);
        });

        el.subtotal.textContent = cart.subtotalText;
        el.total.textContent = cart.totalText;
        el.units.textContent = cart.units;

        if (cart.discount > 0) {
            el.discountRow.style.display = '';
            el.discountText.textContent = '-' + cart.discountText;
        } else {
            el.discountRow.style.display = 'none';
        }

        renderQuickAmounts();
        updateChange();
    }

    function renderQuickAmounts() {
        el.quick.innerHTML = '';
        (cart.quickAmounts || []).forEach(function (q, index) {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'quick-btn' + (index === 0 ? ' quick-btn--exact' : '');
            btn.textContent = q.text;
            btn.title = index === 0 ? 'Importe exacto' : 'Pago con ' + q.text;
            btn.addEventListener('click', function () {
                el.received.value = q.value;
                updateChange();
                el.received.focus();
            });
            el.quick.appendChild(btn);
        });
    }

    /* ------------------------------------------- Cálculo del vuelto ------ */
    function updateChange() {
        const total = Number(cart.total || 0);
        const received = Number(el.received.value || 0);
        const isCash = paymentMethod === 'Cash';
        const change = received - total;

        if (!isCash) {
            el.changeBox.classList.remove('is-short');
            el.changeValue.textContent = money(0);
            el.checkout.disabled = busy || total <= 0;
            return;
        }

        if (received > 0 && change < 0) {
            el.changeBox.classList.add('is-short');
            el.changeValue.textContent = 'Faltan ' + money(Math.abs(change));
        } else {
            el.changeBox.classList.remove('is-short');
            el.changeValue.textContent = money(Math.max(0, change));
        }

        el.checkout.disabled = busy || total <= 0 || received < total;
    }

    /* --------------------------------------------------- Acciones del POS */
    function handle(promise, okMessage) {
        busy = true;
        el.checkout.disabled = true;
        return promise.then(function (res) {
            if (!res.success) {
                window.posToast(res.message || 'No se pudo completar la operación.', 'danger');
                return res;
            }
            if (res.cart) { cart = res.cart; }
            const added = res.data && res.data.addedProductId;
            render(added);
            if (okMessage || res.message) { window.posToast(res.message || okMessage, res.messageKind || 'success'); }
            return res;
        }).catch(function () {
            window.posToast('Error de conexión con el servidor.', 'danger');
        }).finally(function () {
            busy = false;
            updateChange();
            focusScanner();
        });
    }

    function scan(code, quantity) {
        if (!code) { return; }
        handle(post(cfg.urls.scan, { barcode: code, quantity: quantity || 1 }));
    }

    function setQuantity(productId, quantity) {
        handle(post(cfg.urls.setQuantity, { productId: productId, quantity: quantity }));
    }

    function removeLine(productId) {
        handle(post(cfg.urls.remove, { productId: productId }));
    }

    function clearCart() {
        if (!(cart.lines || []).length) { focusScanner(); return; }
        if (!window.confirm('¿Vaciar el ticket completo?')) { return; }
        el.received.value = '';
        handle(post(cfg.urls.clear, {}));
    }

    /* ------------------------------------- Entrada de la pistola lectora - */
    el.scanInput.addEventListener('keydown', function (e) {
        if (e.key !== 'Enter') { return; }
        e.preventDefault();

        let raw = el.scanInput.value.trim();
        el.scanInput.value = '';
        if (!raw) { return; }

        // Permite "3*7501234567890" para vender 3 unidades de un tirón.
        let quantity = 1;
        const multiplier = raw.match(/^(\d+(?:[.,]\d+)?)\s*[*xX]\s*(.+)$/);
        if (multiplier) {
            quantity = Number(multiplier[1].replace(',', '.'));
            raw = multiplier[2].trim();
        }
        scan(raw, quantity);
    });

    el.scanInput.addEventListener('focus', function () { el.scanBox.classList.add('is-hot'); });
    el.scanInput.addEventListener('blur', function () { el.scanBox.classList.remove('is-hot'); });

    // La pistola escribe muy rápido: si el foco se perdió, se recupera sola.
    document.addEventListener('keydown', function (e) {
        const target = e.target;
        const typingElsewhere = target && (target.tagName === 'INPUT' || target.tagName === 'SELECT' ||
                                           target.tagName === 'TEXTAREA' || target.isContentEditable);

        if (e.key === 'F2') {
            e.preventDefault();
            if (!el.checkout.disabled) { checkout(); } else { el.received.focus(); el.received.select(); }
            return;
        }
        if (e.key === 'F4') { e.preventDefault(); openSearch(); return; }
        if (e.key === 'F8') { e.preventDefault(); clearCart(); return; }
        if (e.key === 'Escape' && !document.querySelector('.modal.show')) { focusScanner(); return; }

        if (!typingElsewhere && e.key.length === 1 && !e.ctrlKey && !e.altKey && !e.metaKey) {
            focusScanner();
        }
    });

    document.addEventListener('click', function (e) {
        if (e.target.closest('button, a, input, select, textarea, .modal')) { return; }
        focusScanner();
    });

    /* ------------------------------------------------- Forma de pago ----- */
    document.querySelectorAll('.pay-method').forEach(function (btn) {
        btn.addEventListener('click', function () {
            document.querySelectorAll('.pay-method').forEach(function (b) { b.classList.remove('is-active'); });
            btn.classList.add('is-active');
            paymentMethod = btn.getAttribute('data-method');
            el.cashBlock.style.display = paymentMethod === 'Cash' ? '' : 'none';
            el.changeBox.style.display = paymentMethod === 'Cash' ? '' : 'none';
            updateChange();
        });
    });

    el.received.addEventListener('input', updateChange);
    el.received.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') { e.preventDefault(); if (!el.checkout.disabled) { checkout(); } }
    });

    document.getElementById('btnClear').addEventListener('click', clearCart);

    if (el.discountInput) {
        document.getElementById('btnDiscount').addEventListener('click', function () {
            handle(post(cfg.urls.discount, { amount: Number(el.discountInput.value || 0) }));
        });
    }

    /* ------------------------------------------------- Buscar producto --- */
    const searchModal = new bootstrap.Modal(document.getElementById('searchModal'));
    const searchTerm = document.getElementById('searchTerm');
    const searchCategory = document.getElementById('searchCategory');
    const searchResults = document.getElementById('searchResults');
    let searchTimer = null;

    function openSearch() {
        searchModal.show();
        setTimeout(function () { searchTerm.focus(); searchTerm.select(); }, 220);
        runSearch();
    }

    function runSearch() {
        const url = cfg.urls.search + '?term=' + encodeURIComponent(searchTerm.value) +
                    '&categoryId=' + encodeURIComponent(searchCategory.value);

        fetch(url).then(function (r) { return r.json(); }).then(function (items) {
            searchResults.innerHTML = '';
            if (!items.length) {
                searchResults.innerHTML =
                    '<div class="empty"><p>No se encontraron productos con ese criterio.</p></div>';
                return;
            }
            items.forEach(function (item) {
                const btn = document.createElement('button');
                btn.type = 'button';
                btn.className = 'search-item';
                btn.innerHTML =
                    '<span class="cat-dot" style="background:' + (item.categoryColor || '#94a3b8') + '"></span>' +
                    '<span><span class="search-item__name"></span><br /><span class="search-item__meta"></span></span>' +
                    '<span class="search-item__price">' + item.priceText + '</span>';
                btn.querySelector('.search-item__name').textContent = item.name;
                btn.querySelector('.search-item__meta').textContent =
                    item.code + (item.barcode ? ' · ' + item.barcode : '') + ' · ' + item.category;

                btn.addEventListener('click', function () {
                    searchModal.hide();
                    handle(post(cfg.urls.addProduct, { productId: item.id, quantity: 1 }));
                });
                searchResults.appendChild(btn);
            });
        });
    }

    document.getElementById('btnSearch').addEventListener('click', openSearch);
    searchTerm.addEventListener('input', function () {
        clearTimeout(searchTimer);
        searchTimer = setTimeout(runSearch, 220);
    });
    searchCategory.addEventListener('change', runSearch);
    searchTerm.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            const first = searchResults.querySelector('.search-item');
            if (first) { first.click(); }
        }
    });
    document.getElementById('searchModal').addEventListener('hidden.bs.modal', focusScanner);

    /* ----------------------------------------------- Cierre de la venta -- */
    const changeModal = new bootstrap.Modal(document.getElementById('changeModal'));

    function checkout() {
        if (busy) { return; }
        const total = Number(cart.total || 0);
        if (total <= 0) { window.posToast('El ticket está vacío.', 'danger'); return; }

        const received = paymentMethod === 'Cash' ? Number(el.received.value || 0) : total;
        if (paymentMethod === 'Cash' && received < total) {
            window.posToast('El dinero recibido es menor que el total.', 'danger');
            el.received.focus();
            return;
        }

        busy = true;
        el.checkout.disabled = true;

        post(cfg.urls.checkout, {
            amountReceived: received,
            discount: Number(el.discountInput ? el.discountInput.value || 0 : 0),
            paymentMethod: paymentMethod,
            customerName: null,
            customerDocument: null,
            notes: null
        }).then(function (res) {
            if (!res.success) {
                window.posToast(res.message || 'No se pudo cerrar la venta.', 'danger');
                return;
            }

            cart = res.cart;
            el.received.value = '';
            if (el.discountInput) { el.discountInput.value = 0; }
            render();

            const data = res.data;
            document.getElementById('modalChange').textContent = data.changeText;
            document.getElementById('modalNumber').textContent = data.number;
            document.getElementById('modalTotal').textContent = data.totalText;
            document.getElementById('modalReceived').textContent = data.receivedText;
            document.getElementById('modalPrintLink').href = data.receiptUrl;
            changeModal.show();

            if (data.autoPrint) { printReceipt(data.receiptUrl); }
        }).catch(function () {
            window.posToast('Error de conexión al cerrar la venta.', 'danger');
        }).finally(function () {
            busy = false;
            updateChange();
        });
    }

    /* Imprime el recibo de 80 mm en una ventana oculta. */
    function printReceipt(url) {
        const frame = document.getElementById('printFrame');
        frame.src = url + (url.indexOf('?') >= 0 ? '&' : '?') + 'auto=1';
    }

    el.checkout.addEventListener('click', checkout);
    document.getElementById('btnNextSale').addEventListener('click', focusScanner);
    document.getElementById('changeModal').addEventListener('hidden.bs.modal', focusScanner);

    /* ------------------------------------------------------- Arranque --- */
    render();
    focusScanner();
    setInterval(function () {
        if (!document.querySelector('.modal.show') && document.activeElement === document.body) {
            focusScanner();
        }
    }, 2500);
})();
