// Предпросмотр по наведению: мышь на картинке дольше DWELL мс, и она раскрывается на весь экран.
// Работает через делегирование по атрибуту data-preview-src, поэтому не зависит от Blazor и подходит любому элементу.
// Подложка не ловит мышь (pointer-events: none), значит карточка под ней остаётся под курсором и предпросмотр не мигает.
// Только для устройств с настоящим hover: на телефонах и планшетах его нет, там картинка открывается по нажатию.
(() => {
    const DWELL = 500;      // сколько держать мышь, мс
    const PREFETCH = 180;   // когда начинать подгружать оригинал, чтобы к DWELL он уже был готов
    const canHover = () => matchMedia('(hover: hover) and (pointer: fine)').matches;

    let overlay, img, nameEl, metaEl;
    let dwellTimer = 0, prefetchTimer = 0;
    let current = null;
    let isOpen = false;

    function ensure() {
        if (overlay) return;
        overlay = document.createElement('div');
        overlay.className = 'cv-hover';
        overlay.setAttribute('aria-hidden', 'true');
        overlay.innerHTML =
            '<img class="cv-hover__img" alt="" decoding="async">' +
            '<div class="cv-hover__spin"></div>' +
            '<div class="cv-hover__cap"><span class="cv-hover__name"></span><span class="cv-hover__meta"></span></div>';
        document.body.appendChild(overlay);
        img = overlay.querySelector('.cv-hover__img');
        nameEl = overlay.querySelector('.cv-hover__name');
        metaEl = overlay.querySelector('.cv-hover__meta');
        img.addEventListener('load', () => { overlay.classList.remove('is-loading'); img.dataset.tries = '0'; });
        // Оригінал може отримати відмову (обмеження Google): одразу пробуємо запасний канал, далі чергуємо канали з нарастаючою паузою,
        // поки мишка на картинці.
        img.addEventListener('error', () => advance());
        // Основний канал інколи не відмовляє, а «висить»: чекати помилки не треба, через таймаут переходимо на інший канал.
        setInterval(() => { if (isOpen && overlay.classList.contains('is-loading') && Date.now() - Number(img.dataset.token || 0) > 3500 * (Number(img.dataset.tries || 0) + 1)) advance(); }, 700);
        function advance() {
            const sources = [img.dataset.source, img.dataset.fallback].filter(Boolean);
            const n = Number(img.dataset.tries || 0) + 1;
            img.dataset.tries = String(n);
            const next = sources[n % sources.length];
            const wait = n === 1 && sources.length > 1 ? 250 : Math.min(700 * 2 ** Math.floor(n / sources.length), 10000);
            const token = img.dataset.token;
            setTimeout(() => { if (isOpen && img.dataset.token === token) { img.removeAttribute('src'); img.setAttribute('src', next); } }, wait);
        }
    }

    function show(el) {
        ensure();
        overlay.classList.add('is-loading');
        img.dataset.tries = '0';
        img.dataset.token = String(Date.now());
        img.dataset.source = el.dataset.previewSrc;
        img.dataset.fallback = el.dataset.previewFallback || '';
        img.src = el.dataset.previewSrc;
        if (img.complete && img.naturalWidth) overlay.classList.remove('is-loading');
        nameEl.textContent = el.dataset.previewName || '';
        metaEl.textContent = el.dataset.previewMeta || '';
        overlay.classList.add('is-open');
        isOpen = true;
    }

    function hide() {
        clearTimeout(dwellTimer);
        clearTimeout(prefetchTimer);
        dwellTimer = prefetchTimer = 0;
        current = null;
        if (overlay && isOpen) {
            overlay.classList.remove('is-open');
            isOpen = false;
        }
    }

    document.addEventListener('mouseover', e => {
        if (!canHover()) return;
        const el = e.target.closest ? e.target.closest('[data-preview-src]') : null;
        // Та же картинка (в том числе после перерисовки Blazor): ничего не меняем.
        if (el && current && el.dataset.previewSrc === current.dataset.previewSrc) { current = el; return; }
        hide();
        if (!el) return;
        current = el;
        prefetchTimer = setTimeout(() => { new Image().src = el.dataset.previewSrc; }, PREFETCH);
        dwellTimer = setTimeout(() => show(el), DWELL);
    });

    // Любое действие пользователя закрывает предпросмотр: нажатие открывает обычный просмотрщик, прокрутка сдвигает карточку.
    document.addEventListener('pointerdown', hide, true);
    document.addEventListener('scroll', hide, true);
    document.addEventListener('wheel', hide, { passive: true, capture: true });
    document.addEventListener('keydown', hide, true);
    window.addEventListener('blur', hide);
    document.addEventListener('mouseleave', hide);
})();
