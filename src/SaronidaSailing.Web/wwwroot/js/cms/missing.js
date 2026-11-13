const selector = '[data-cms-fallback]';

export const createMissing = () => {
    const button = document.querySelector('[data-cms-action="next-missing"]');
    if (!button) {
        return;
    }

    let last = null;

    const render = () => {
        const fields = new Set();
        for (const element of document.querySelectorAll(selector)) {
            fields.add(`${element.dataset.cmsKey}|${element.closest('[data-cms-item]')?.dataset.cmsItem ?? ''}`);
        }
        const count = fields.size;
        button.hidden = count === 0;
        button.textContent = `${count} missing in English`;
    };

    const nextAfter = (elements, from) => {
        if (!from) {
            return elements[0];
        }
        const following = elements.find((element) =>
            element !== from && Boolean(from.compareDocumentPosition(element) & Node.DOCUMENT_POSITION_FOLLOWING));
        return following ?? elements[0];
    };

    button.addEventListener('click', () => {
        const elements = [...document.querySelectorAll(selector)];
        if (elements.length === 0) {
            return;
        }
        last = nextAfter(elements, last?.isConnected ? last : null);
        last.scrollIntoView({ block: 'center' });
        last.focus({ preventScroll: true });
    });

    document.addEventListener('cms:fieldchange', render);
    document.addEventListener('cms:structurechange', render);
    render();
};
