import { el } from './dialog.js';

const isActive = (element) => element.dataset.cmsActive !== 'false';

export const createSections = ({ pending, toolbar }) => {
    for (const section of document.querySelectorAll('[data-cms-section]')) {
        const key = section.dataset.cmsSection;
        const original = isActive(section);


        const bar = el('div', 'cms-sectionbar');
        bar.setAttribute('role', 'group');
        bar.setAttribute('aria-label', 'Section tools');
        const state = el('span', 'cms-sectionbar-state');
        const button = el('button', 'cms-sectionbar-button');
        button.type = 'button';
        bar.append(state, button);
        section.prepend(bar);

        const render = () => {
            const active = isActive(section);
            state.textContent = active ? '' : 'Hidden from visitors';
            button.textContent = active ? 'Hide section' : 'Show section';
        };

        button.addEventListener('click', () => {
            const active = !isActive(section);
            const links = [...document.querySelectorAll('[data-cms-section-link]')]
                .filter((link) => link.dataset.cmsSectionLink === key);
            for (const target of [section, ...links]) {
                if (active) {
                    target.removeAttribute('data-cms-active');
                } else {
                    target.setAttribute('data-cms-active', 'false');
                }
            }
            render();
            pending.setOps(`section:${key}`,
                active === original ? [] : [{ op: 'setSectionActive', sectionKey: key, isActive: active }]);
            toolbar.setStatus(active
                ? 'Section shown to visitors. Save page to apply.'
                : 'Section hidden from visitors (both languages). Save page to apply.');
            button.focus();
        });

        render();
    }
};
