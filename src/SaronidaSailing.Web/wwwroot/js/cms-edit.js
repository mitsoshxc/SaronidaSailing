const storageKey = 'cms-edit-mode';
const root = document.documentElement;

const readMode = () => {
    try {
        return sessionStorage.getItem(storageKey) === 'off' ? 'off' : 'on';
    } catch {
        return 'on';
    }
};

const writeMode = (mode) => {
    try {
        sessionStorage.setItem(storageKey, mode);
    } catch {
        // storage unavailable: the mode just is not remembered
    }
};

const applyMode = (mode) => {
    root.classList.toggle('cms-view', mode === 'off');
    document.querySelector('[data-cms-action="toggle-edit"]')
        ?.setAttribute('aria-pressed', mode === 'on' ? 'true' : 'false');
};

applyMode(readMode());

document.querySelector('[data-cms-action="toggle-edit"]')?.addEventListener('click', () => {
    const next = root.classList.contains('cms-view') ? 'on' : 'off';
    writeMode(next);
    applyMode(next);
});
