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

export const createEditMode = () => {
    const button = document.querySelector('[data-cms-action="toggle-edit"]');

    const isEditing = () => !root.classList.contains('cms-view');

    const apply = (mode) => {
        root.classList.toggle('cms-view', mode === 'off');
        button?.setAttribute('aria-pressed', mode === 'on' ? 'true' : 'false');
        document.dispatchEvent(new CustomEvent('cms:modechange', { detail: { editing: mode === 'on' } }));
    };

    button?.addEventListener('click', () => {
        const next = isEditing() ? 'off' : 'on';
        writeMode(next);
        apply(next);
    });

    root.classList.toggle('cms-view', readMode() === 'off');
    button?.setAttribute('aria-pressed', isEditing() ? 'true' : 'false');

    return { isEditing };
};
