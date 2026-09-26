// Зум і панорамування картинки в переглядачі та показ .docx у браузері. Єдине місце, де потрібен JS:
// жести (колесо, щипок, перетягування, подвійний тап, свайп), клавіатура й рендер Word-документа працюють у браузері, а не в Blazor.

const scripts = {};
const loadScript = src => (scripts[src] ??= new Promise((resolve, reject) => {
    const s = document.createElement('script');
    s.src = src;
    s.onload = resolve;
    s.onerror = () => reject(new Error('Не вдалося завантажити ' + src));
    document.head.appendChild(s);
}));

// Вписує сторінки документа в ширину вікна (на телефоні сторінка A4 ширша за екран).
function fitDocx(container) {
    const wrapper = container.querySelector('.docx-wrapper');
    const page = wrapper?.querySelector('section.docx');
    if (!wrapper || !page) return;
    wrapper.style.zoom = 1;
    const w = page.offsetWidth;
    const avail = container.clientWidth - 24;
    wrapper.style.zoom = w > avail ? Math.max(0.3, avail / w) : 1;
}

const docxObservers = new WeakMap();

/** Малює .docx з url усередині container. Повертає false, якщо не вийшло: тоді інтерфейс покаже завантаження. */
export async function renderDocx(container, url) {
    const token = (container._renderToken = (container._renderToken || 0) + 1);
    try {
        await loadScript('lib/jszip.min.js');
        await loadScript('lib/docx-preview.min.js');
        const res = await fetch(url);
        if (!res.ok) return false;
        const buf = await res.arrayBuffer();
        if (container._renderToken !== token) return true; // користувач уже відкрив інший файл
        container.replaceChildren();
        await window.docx.renderAsync(buf, container, null, { className: 'docx', inWrapper: true, ignoreLastRenderedPageBreak: true, useBase64URL: true });
        fitDocx(container);
        container.scrollTop = 0;
        docxObservers.get(container)?.disconnect();
        const ro = new ResizeObserver(() => fitDocx(container));
        ro.observe(container);
        docxObservers.set(container, ro);
        return true;
    } catch (e) {
        console.warn('Не вдалося показати .docx', e);
        return false;
    }
}

export function attach(root, dotnet) {
    const stage = root.querySelector('.cv-viewer__stage');
    let s = 1, x = 0, y = 0;
    const ptrs = new Map();
    let drag = null, pinchDist = 0, lastTap = { t: 0, x: 0, y: 0 };

    const img = () => stage.querySelector('img.cv-viewer__img');
    const clamp = (v, a, b) => Math.min(b, Math.max(a, v));

    function apply() {
        const i = img();
        if (i) i.style.transform = `translate(${x}px, ${y}px) scale(${s})`;
        root.querySelectorAll('.js-zoom-label').forEach(e => { e.textContent = Math.round(s * 100) + '%'; });
        stage.classList.toggle('is-zoomed', s > 1.001);
    }

    function limits() {
        const i = img();
        const W = stage.clientWidth, H = stage.clientHeight;
        const r = (i.naturalWidth || W) / (i.naturalHeight || H);
        const w0 = W / H > r ? H * r : W;
        const h0 = W / H > r ? H : W / r;
        return { mx: Math.max(0, (w0 * s - W) / 2), my: Math.max(0, (h0 * s - H) / 2) };
    }

    function clampPan() {
        if (!img()) return;
        const l = limits();
        x = clamp(x, -l.mx, l.mx);
        y = clamp(y, -l.my, l.my);
    }

    function reset() { s = 1; x = 0; y = 0; apply(); }

    function zoomAt(f, cx, cy) {
        if (!img()) return;
        const s2 = clamp(s * f, 1, 10);
        const k = s2 / s;
        x = cx - (cx - x) * k;
        y = cy - (cy - y) * k;
        s = s2;
        if (s === 1) { x = 0; y = 0; }
        clampPan();
        apply();
    }

    const rel = e => {
        const r = stage.getBoundingClientRect();
        return [e.clientX - r.left - r.width / 2, e.clientY - r.top - r.height / 2];
    };

    const onWheel = e => {
        if (!img()) return;
        e.preventDefault();
        const [cx, cy] = rel(e);
        zoomAt(Math.exp(-e.deltaY * 0.0016), cx, cy);
    };

    const onDown = e => {
        if (!img() || e.target.closest('button, a')) return;
        stage.setPointerCapture(e.pointerId);
        ptrs.set(e.pointerId, { x: e.clientX, y: e.clientY });
        if (ptrs.size === 1) drag = { x: e.clientX, y: e.clientY, tx: x, ty: y, t: Date.now(), moved: false };
        if (ptrs.size === 2) { drag = null; pinchDist = dist(); }
    };

    const dist = () => { const [a, b] = [...ptrs.values()]; return Math.hypot(a.x - b.x, a.y - b.y); };

    const onMove = e => {
        if (!ptrs.has(e.pointerId)) return;
        ptrs.set(e.pointerId, { x: e.clientX, y: e.clientY });
        if (ptrs.size === 2) {
            const d = dist();
            const [a, b] = [...ptrs.values()];
            const r = stage.getBoundingClientRect();
            const cx = (a.x + b.x) / 2 - r.left - r.width / 2;
            const cy = (a.y + b.y) / 2 - r.top - r.height / 2;
            if (pinchDist) zoomAt(d / pinchDist, cx, cy);
            pinchDist = d;
        } else if (drag) {
            const dx = e.clientX - drag.x, dy = e.clientY - drag.y;
            if (Math.abs(dx) + Math.abs(dy) > 6) drag.moved = true;
            if (s > 1.001) { x = drag.tx + dx; y = drag.ty + dy; clampPan(); apply(); }
        }
    };

    const onUp = e => {
        if (!ptrs.has(e.pointerId)) return;
        ptrs.delete(e.pointerId);
        if (ptrs.size < 2) pinchDist = 0;
        if (!drag) return;
        const d = drag; drag = null;
        const dx = e.clientX - d.x, dy = e.clientY - d.y, dt = Date.now() - d.t;
        if (!d.moved) {
            const now = Date.now();
            if (now - lastTap.t < 320 && Math.hypot(e.clientX - lastTap.x, e.clientY - lastTap.y) < 30) {
                if (s > 1.001) reset(); else { const [cx, cy] = rel(e); zoomAt(2.5, cx, cy); }
                lastTap = { t: 0, x: 0, y: 0 };
            } else lastTap = { t: now, x: e.clientX, y: e.clientY };
        } else if (s <= 1.001 && Math.abs(dx) > 70 && Math.abs(dy) < 60 && dt < 600) {
            dotnet.invokeMethodAsync('NavigateBy', dx < 0 ? 1 : -1);
        }
    };

    const onClick = e => {
        const b = e.target.closest('[data-zoom]');
        if (!b || !root.contains(b)) return;
        const v = b.dataset.zoom;
        if (v === 'in') zoomAt(1.4, 0, 0);
        else if (v === 'out') zoomAt(1 / 1.4, 0, 0);
        else reset();
    };

    const onKey = e => {
        if (document.querySelector('.mud-dialog-container')) return;
        const tag = (e.target.tagName || '').toLowerCase();
        if (tag === 'input' || tag === 'textarea') return;
        switch (e.key) {
            case 'Escape': dotnet.invokeMethodAsync('CloseFromJs'); break;
            case 'ArrowLeft': dotnet.invokeMethodAsync('NavigateBy', -1); break;
            case 'ArrowRight': dotnet.invokeMethodAsync('NavigateBy', 1); break;
            case '+': case '=': zoomAt(1.4, 0, 0); break;
            case '-': case '_': zoomAt(1 / 1.4, 0, 0); break;
            case '0': reset(); break;
            default: return;
        }
        e.preventDefault();
    };

    stage.addEventListener('wheel', onWheel, { passive: false });
    stage.addEventListener('pointerdown', onDown);
    stage.addEventListener('pointermove', onMove);
    stage.addEventListener('pointerup', onUp);
    stage.addEventListener('pointercancel', onUp);
    stage.addEventListener('load', reset, true);
    root.addEventListener('click', onClick);
    document.addEventListener('keydown', onKey);
    document.documentElement.classList.add('cv-noscroll');
    root.focus({ preventScroll: true });
    apply();

    // Поки велика картинка вантажиться, у переглядачі крутиться шестерня (клас is-loading на сцені).
    function watchImage() {
        const i = img();
        if (!i || (i.complete && i.naturalWidth)) { stage.classList.remove('is-loading'); return; }
        stage.classList.add('is-loading');
        const done = () => stage.classList.remove('is-loading');
        i.addEventListener('load', done, { once: true });
        i.addEventListener('error', done, { once: true });
    }

    return {
        reset,
        watchImage,
        revealCurrent() {
            const cur = root.querySelector('.cv-strip__item.is-current');
            if (cur) cur.scrollIntoView({ block: 'nearest', inline: 'center' });
        },
        dispose() {
            stage.removeEventListener('wheel', onWheel);
            stage.removeEventListener('pointerdown', onDown);
            stage.removeEventListener('pointermove', onMove);
            stage.removeEventListener('pointerup', onUp);
            stage.removeEventListener('pointercancel', onUp);
            stage.removeEventListener('load', reset, true);
            root.removeEventListener('click', onClick);
            document.removeEventListener('keydown', onKey);
            document.documentElement.classList.remove('cv-noscroll');
        },
    };
}
