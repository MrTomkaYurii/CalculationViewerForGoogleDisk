// Надійне завантаження мініатюр і індикатор завантаження.
//
// Проблема: Google обмежує частоту запитів з однієї адреси і на пачку з 20-30 мініатюр відповідає HTTP 429 «Too Many Requests»
// (це видно на замірах: 16-20 відмов із 23). Порада Google для 429 така: не слати все одразу й повторювати запит із нарастаючою
// паузою (експоненційний backoff). Тому мініатюри грузяться не самим браузером, а цією чергою:
//
//  1. Картинка з data-thumb ставиться в чергу, коли підходить до екрана (IntersectionObserver). Видимі йдуть першими.
//  2. Черга працює невеликими партіями. Швидкість підлаштовується сама: відмова 429 = пауза для всієї черги, що росте
//     (1,5 с, 3 с, 6 с … до 32 с) і вдвічі менше паралельних запитів. Успіхи повільно повертають швидкість.
//  3. Невдалий запит не здається ніколи: пробує ще, після кількох невдач із основної адреси береться запасна (data-fallbacks).
//     Поки картинки немає, у клітинці крутиться шестерня. «Битої» іконки не буває.
//  4. Отримана мініатюра зберігається в Cache Storage браузера (ключ data-key): наступний захід у папку не робить запитів до Google.
//
// Також: повтор для великих зображень переглядача (img.cv-viewer__img) і позначка is-loaded для шестерні.
(() => {
    const CACHE_NAME = 'cv-thumbs-v1';
    const MAX_CONCURRENCY = 6;
    const MIN_CONCURRENCY = 1;
    const BASE_BACKOFF = 1500;
    const MAX_BACKOFF = 32000;
    const FALLBACK_AFTER = 3; // після скількох невдач поспіль переходимо на запасну адресу

    const memory = new Map();          // key -> objectURL (пам'ять сторінки)
    const items = new WeakMap();       // img -> елемент черги
    const queue = [];
    let active = 0;
    let limit = 3;
    let backoff = 0;
    let pausedUntil = 0;
    let okStreak = 0;
    let generation = 0;                // номер «хвилі» відмов: збільшується при кожній новій паузі
    let timer = 0;
    let cachePromise = null;

    const openCache = () => (cachePromise ??= ('caches' in self ? caches.open(CACHE_NAME).catch(() => null) : Promise.resolve(null)));
    const cacheUrl = key => `https://cv.local/thumb/${encodeURIComponent(key)}`;

    // ---------- застосування результату ----------

    function apply(img, objectUrl) {
        if (!img.isConnected) return;
        img.src = objectUrl; // подія load додасть is-loaded
    }

    async function fromCache(key) {
        if (!key) return null;
        if (memory.has(key)) return memory.get(key);
        const cache = await openCache();
        if (!cache) return null;
        try {
            const hit = await cache.match(cacheUrl(key));
            if (!hit) return null;
            const url = URL.createObjectURL(await hit.blob());
            memory.set(key, url);
            return url;
        } catch { return null; }
    }

    async function save(key, blob) {
        const url = URL.createObjectURL(blob);
        if (key) {
            memory.set(key, url);
            const cache = await openCache();
            if (cache) cache.put(cacheUrl(key), new Response(blob, { headers: { 'Content-Type': blob.type } })).catch(() => { });
        }
        return url;
    }

    // ---------- планувальник ----------

    function schedule() {
        clearTimeout(timer);
        timer = setTimeout(pump, Math.max(0, pausedUntil - Date.now()));
    }

    function pump() {
        if (Date.now() < pausedUntil) return schedule();
        while (active < limit && queue.length) {
            const it = queue.shift();
            if (!it.img.isConnected || it.img.classList.contains('is-loaded')) continue;
            active++;
            run(it).catch(() => { }).finally(() => { active--; pump(); });
        }
    }

    function enqueue(img, urgent) {
        let it = items.get(img);
        if (!it) {
            it = { img, key: img.dataset.key || '', url: img.dataset.thumb, fallbacks: (img.dataset.fallbacks || '').split('|').filter(Boolean), tries: 0, queued: false };
            items.set(img, it);
        }
        if (img.classList.contains('is-loaded')) return;
        if (it.queued) { if (urgent) { queue.splice(queue.indexOf(it), 1); queue.unshift(it); } return; }
        it.queued = true;
        if (urgent) queue.unshift(it); else queue.push(it);
        pump();
    }

    function onSuccess() {
        backoff = 0;
        okStreak++;
        if (okStreak % 3 === 0 && limit < MAX_CONCURRENCY) limit++;
    }

    function onFailure(it, startedInGeneration) {
        // Пауза для всієї черги: ліміт діє на адресу в цілому, а не на окрему картинку.
        // Кілька відмов однієї «хвилі» (запити, що стартували до паузи) не подвоюють паузу повторно.
        if (startedInGeneration === generation) {
            backoff = backoff ? Math.min(backoff * 2, MAX_BACKOFF) : BASE_BACKOFF;
            pausedUntil = Date.now() + backoff + Math.random() * 1000;
            limit = Math.max(MIN_CONCURRENCY, limit >> 1);
            okStreak = 0;
            generation++;
        }
        it.tries++;
        it.queued = true;
        queue.unshift(it); // повторюємо першою
    }

    function sourceFor(it) {
        const all = [it.url, ...it.fallbacks];
        const index = Math.floor(it.tries / FALLBACK_AFTER) % all.length;
        return all[index];
    }

    async function run(it) {
        it.queued = false;
        const { img } = it;
        const cached = await fromCache(it.key);
        if (cached) { apply(img, cached); return; }

        const generationAtStart = generation;
        try {
            const res = await fetch(sourceFor(it), { mode: 'cors', credentials: 'omit' });
            if (!res.ok) throw new Error('HTTP ' + res.status);
            const blob = await res.blob();
            if (!blob.type.startsWith('image/')) throw new Error('не зображення: ' + blob.type);
            apply(img, await save(it.key, blob));
            onSuccess();
        } catch (e) {
            if (img.isConnected) onFailure(it, generationAtStart);
        }
    }

    // ---------- виявлення картинок ----------

    const near = new IntersectionObserver(entries => {
        for (const e of entries) if (e.isIntersecting) { near.unobserve(e.target); enqueue(e.target, false); }
    }, { rootMargin: '900px 0px' });

    // Ті, що вже на екрані, обганяють решту в черзі.
    const visible = new IntersectionObserver(entries => {
        for (const e of entries) if (e.isIntersecting) { visible.unobserve(e.target); enqueue(e.target, true); }
    }, { rootMargin: '0px' });

    function watch(img) {
        if (img.__cvWatched === img.dataset.thumb) return;
        img.__cvWatched = img.dataset.thumb;
        items.delete(img);
        img.classList.remove('is-loaded');
        if (img.dataset.thumb) { near.observe(img); visible.observe(img); }
    }

    function scan(root) {
        if (root.nodeType !== 1) return;
        if (root.matches?.('img[data-thumb]')) watch(root);
        root.querySelectorAll?.('img[data-thumb]').forEach(watch);
    }

    new MutationObserver(records => {
        for (const r of records) {
            if (r.type === 'attributes') { if (r.target.matches('img[data-thumb]')) watch(r.target); continue; }
            r.addedNodes.forEach(scan);
        }
    }).observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['data-thumb'] });
    document.addEventListener('DOMContentLoaded', () => scan(document.documentElement));

    // ---------- індикатор і повтор для звичайних зображень ----------

    // Велике зображення переглядача теж може отримати відмову (ліміт Google, тимчасове обмеження). Спершу одразу пробуємо
    // запасний канал (data-fallbacks), далі чергуємо канали з нарастаючою паузою, поки зображення не завантажиться.
    const retryViewerImage = img => {
        const primary = (img.dataset.primary ||= img.getAttribute('src'));
        const sources = [primary, ...(img.dataset.fallbacks || '').split('|').filter(Boolean)];
        const n = Number(img.dataset.retry || 0) + 1;
        img.dataset.retry = String(n);
        const next = sources[n % sources.length];
        const wait = n === 1 && sources.length > 1 ? 250 : Math.min(800 * 2 ** Math.floor(n / sources.length), 15000) + Math.random() * 500;
        setTimeout(() => { if (img.isConnected) { img.removeAttribute('src'); img.setAttribute('src', next); } }, wait);
    };

    const handler = e => {
        const t = e.target;
        if (!t || t.tagName !== 'IMG') return;
        if (e.type === 'load') {
            t.classList.add('is-loaded');
            delete t.dataset.retry;
            delete t.dataset.primary;
            return;
        }
        if (t.classList.contains('cv-viewer__img')) return retryViewerImage(t);
        // Мініатюрами керує черга, інші картинки просто позначаємо завершеними.
        if (!t.hasAttribute('data-thumb')) t.classList.add('is-loaded');
    };

    document.addEventListener('load', handler, true);
    document.addEventListener('error', handler, true);

    // Для налагодження: cvThumbs.stats() у консолі браузера.
    window.cvThumbs = { stats: () => ({ active, limit, queued: queue.length, backoff, pausedIn: Math.max(0, pausedUntil - Date.now()), cachedInMemory: memory.size }) };
})();
