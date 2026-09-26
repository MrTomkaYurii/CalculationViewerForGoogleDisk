// Позначає картинки, які закінчили завантаження (успішно або з помилкою): клас is-loaded.
// CSS за цим класом прибирає шестерню-індикатор у мініатюрі. Події load й error не спливають, тому слухаємо в фазі занурення.
(() => {
    const mark = e => {
        const t = e.target;
        if (t && t.tagName === 'IMG') t.classList.add('is-loaded');
    };
    document.addEventListener('load', mark, true);
    document.addEventListener('error', mark, true);
})();
