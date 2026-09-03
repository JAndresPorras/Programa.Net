/* Utilidades compartidas por todas las pantallas del sistema. */
(function () {
    'use strict';

    /* ------------------------------------------------------- Avisos ----- */
    window.posToast = function (message, kind) {
        const host = document.getElementById('toastHost');
        if (!host) { return; }

        const el = document.createElement('div');
        el.className = 'toast-x' + (kind ? ' toast-x--' + kind : '');

        const icon = kind === 'danger' ? '#i-alert' : kind === 'success' ? '#i-check-circle' : '#i-info';
        el.innerHTML = '<svg class="icon"><use href="' + icon + '"></use></svg><div></div>';
        el.lastElementChild.textContent = message;

        host.appendChild(el);
        setTimeout(function () {
            el.style.transition = 'opacity .25s';
            el.style.opacity = '0';
            setTimeout(function () { el.remove(); }, 260);
        }, kind === 'danger' ? 5200 : 3200);
    };

    /* --------------------------------------------- Menú lateral móvil --- */
    const burger = document.getElementById('burger');
    const sidebar = document.getElementById('sidebar');
    if (burger && sidebar) {
        burger.addEventListener('click', function () { sidebar.classList.toggle('is-open'); });
        document.addEventListener('click', function (e) {
            if (window.innerWidth < 992 && sidebar.classList.contains('is-open') &&
                !sidebar.contains(e.target) && !burger.contains(e.target)) {
                sidebar.classList.remove('is-open');
            }
        });
    }

    /* ------ El "ojito": muestra el dinero recolectado sólo al presionar -- */
    const eye = document.getElementById('tillEye');
    const tillValue = document.getElementById('tillValue');
    const eyeIcon = document.getElementById('tillEyeIcon');
    let tillVisible = false;
    let tillHideTimer = null;

    function hideTill() {
        tillVisible = false;
        tillValue.classList.add('is-hidden');
        tillValue.textContent = (tillValue.dataset.symbol || '') + ' ••••';
        eyeIcon.querySelector('use').setAttribute('href', '#i-eye');
        if (tillHideTimer) { clearTimeout(tillHideTimer); tillHideTimer = null; }
    }

    if (eye && tillValue) {
        tillValue.dataset.symbol = (tillValue.textContent || '').replace(/[•\s]/g, '');

        eye.addEventListener('click', function () {
            if (tillVisible) { hideTill(); return; }

            tillValue.textContent = '...';
            fetch('/Pos/Recaudado', { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(function (r) { return r.ok ? r.json() : Promise.reject(r.status); })
                .then(function (data) {
                    tillVisible = true;
                    tillValue.classList.remove('is-hidden');
                    tillValue.textContent = data.formatted;
                    eyeIcon.querySelector('use').setAttribute('href', '#i-eye-off');
                    // Por seguridad se vuelve a ocultar solo a los 20 segundos.
                    tillHideTimer = setTimeout(hideTill, 20000);
                })
                .catch(function () {
                    hideTill();
                    window.posToast('No se pudo consultar el dinero recolectado.', 'danger');
                });
        });
    }

    /* ----------------------------------------- Confirmación de acciones -- */
    document.addEventListener('submit', function (e) {
        const form = e.target;
        const message = form.getAttribute('data-confirm');
        if (message && !window.confirm(message)) {
            e.preventDefault();
        }
    });

    /* --------------------------------------- Enfocar el primer campo ---- */
    const autofocus = document.querySelector('[data-autofocus]');
    if (autofocus) { autofocus.focus(); }
})();
