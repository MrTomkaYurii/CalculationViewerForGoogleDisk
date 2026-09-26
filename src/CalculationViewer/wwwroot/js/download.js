// Збереження файлу на комп'ютер штатним скачуванням Google: файл приходить з оригінальним іменем і байтами, без обмежень API.
// Адресу відкриваємо в прихованому фреймі, тому сторінка застосунку лишається на місці й нічого не блимає.
// Якщо це справжнє скачування (відповідь із Content-Disposition: attachment), браузер не генерує подію load. Якщо ж Google віддав
// сторінку (тимчасове обмеження, підтвердження для великого файлу), load спрацює, і ми відкриваємо адресу у видимій вкладці.

const START_TIMEOUT_MS = 4000;

function openVisibly(url) {
    const a = document.createElement('a');
    a.href = url;
    a.target = '_blank';
    a.rel = 'noopener';
    document.body.appendChild(a);
    a.click();
    a.remove();
}

/**
 * @returns "started" якщо браузер почав скачування, "page" якщо Google віддав сторінку і адресу відкрито у видимій вкладці
 */
export function save(url) {
    return new Promise(resolve => {
        const frame = document.createElement('iframe');
        frame.style.display = 'none';
        frame.setAttribute('aria-hidden', 'true');
        let settled = false;

        frame.addEventListener('load', () => {
            if (settled) return;
            // Скачування лишає фрейм порожнім (about:blank), він доступний для читання. Сторінка Google це чужий документ:
            // звернення до нього кидає помилку доступу. Лише тоді це справді «сторінка замість файлу».
            let foreignPage = false;
            try { void frame.contentWindow.location.href; } catch { foreignPage = true; }
            if (!foreignPage) return;
            settled = true;
            frame.remove();
            openVisibly(url);
            resolve('page');
        });

        setTimeout(() => {
            if (settled) return;
            settled = true;
            setTimeout(() => frame.remove(), 120000); // даємо завантаженню спокійно почати
            resolve('started');
        }, START_TIMEOUT_MS);

        frame.src = url;
        document.body.appendChild(frame);
    });
}
