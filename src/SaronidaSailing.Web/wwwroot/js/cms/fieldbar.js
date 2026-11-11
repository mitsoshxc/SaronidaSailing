import { isDialogOpen } from './dialog.js';
import { fieldName } from './values.js';

const fieldSelector = '[data-cms-key]';
const windowTypes = ['richText', 'link', 'image'];
const directActions = { richText: 'edit-text', link: 'edit-link', image: 'edit-image' };
const gap = 4;
const margin = 8;
const scrollOptions = { capture: true, passive: true };

const actions = {
    'edit-text': { label: 'Edit text', run: (windows, element, back) => windows.openRich(element, back) },
    'edit-link': { label: 'Edit link', run: (windows, element, back) => windows.openLink(element, back) },
    'edit-image': { label: 'Edit image', run: (windows, element, back) => windows.openImage(element, back) },
    history: { label: 'History', run: (windows, element, back) => windows.openHistory(element, back) }
};

const actionsByType = {
    text: ['history'],
    richText: ['edit-text', 'history'],
    link: ['edit-link', 'history'],
    image: ['edit-image', 'history']
};

export const createFieldBar = ({ editMode, windows }) => {
    const bar = document.createElement('div');
    bar.className = 'cms-fieldbar';
    bar.setAttribute('role', 'toolbar');
    bar.setAttribute('aria-label', 'Field tools');

    let current = null;
    let dismissed = null;
    let frame = 0;

    const anchorOf = (element) => element.closest('a') ?? element;

    const place = () => {
        frame = 0;
        if (!current) {
            return;
        }
        const rect = current.getBoundingClientRect();
        const width = bar.offsetWidth;
        const height = bar.offsetHeight;
        const offscreen = rect.bottom < 0 || rect.top > window.innerHeight;
        bar.style.visibility = offscreen ? 'hidden' : 'visible';

        const toolbarBottom = document.querySelector('.cms-toolbar').getBoundingClientRect().bottom;
        let top = rect.bottom + gap;
        if (top + height > window.innerHeight - margin && rect.top - gap - height >= toolbarBottom + margin) {
            top = rect.top - gap - height;
        }
        top = Math.max(toolbarBottom + margin, Math.min(top, window.innerHeight - height - margin));
        const left = Math.max(margin, Math.min(rect.left, window.innerWidth - width - margin));
        bar.style.top = `${top}px`;
        bar.style.left = `${left}px`;
    };

    const schedulePlace = () => {
        if (current && frame === 0) {
            frame = requestAnimationFrame(place);
        }
    };

    const hide = () => {
        if (!current) {
            return;
        }
        current = null;
        bar.remove();
        window.removeEventListener('scroll', schedulePlace, scrollOptions);
        window.removeEventListener('resize', schedulePlace);
    };

    const openAction = (name, element, button) => {
        if (isDialogOpen()) {
            return;
        }
        hide();
        actions[name].run(windows, element, (replacement) => {
            const target = replacement ?? element;
            if (button) {
                show(target);
                bar.querySelector(`[data-cms-fieldaction="${name}"]`)?.focus();
            } else {
                target.focus();
            }
        });
    };

    const show = (element) => {
        if (!editMode.isEditing() || isDialogOpen()) {
            return;
        }
        if (current === element && bar.isConnected) {
            schedulePlace();
            return;
        }
        hide();
        current = element;
        bar.replaceChildren();
        for (const name of actionsByType[element.dataset.cmsType] ?? []) {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'cms-fieldbar-button';
            button.dataset.cmsFieldaction = name;
            button.textContent = actions[name].label;
            button.setAttribute('aria-label', `${actions[name].label}: ${fieldName(element)}`);
            button.addEventListener('click', () => openAction(name, element, button));
            bar.append(button);
        }
        anchorOf(element).after(bar);
        place();
        window.addEventListener('scroll', schedulePlace, scrollOptions);
        window.addEventListener('resize', schedulePlace);
    };

    const fieldOf = (target) => (target instanceof Element ? target.closest(fieldSelector) : null);

    const setFocusable = (editing) => {
        for (const element of document.querySelectorAll(fieldSelector)) {
            if (windowTypes.includes(element.dataset.cmsType)) {
                if (editing) {
                    element.setAttribute('tabindex', '0');
                } else {
                    element.removeAttribute('tabindex');
                }
            }
        }
    };

    const enhance = () => setFocusable(editMode.isEditing());

    bar.addEventListener('mousedown', (event) => event.preventDefault());

    bar.addEventListener('keydown', (event) => {
        if (event.key === 'Escape' && current) {
            event.preventDefault();
            const element = current;
            dismissed = element;
            hide();
            element.focus();
        }
    });

    document.addEventListener('focusin', (event) => {
        const field = fieldOf(event.target);
        if (bar.contains(event.target)) {
            return;
        }
        if (field !== dismissed) {
            dismissed = null;
        }
        if (field && field !== dismissed) {
            show(field);
        } else if (!field) {
            hide();
        }
    });

    document.addEventListener('focusout', (event) => {
        const to = event.relatedTarget;
        if (!current || !(event.target instanceof Node)) {
            return;
        }
        if (to instanceof Node && (bar.contains(to) || current.contains(to) || anchorOf(current).contains(to))) {
            return;
        }
        if (bar.contains(event.target) || current.contains(event.target)) {
            hide();
        }
    });

    document.addEventListener('pointerdown', (event) => {
        if (current && event.target instanceof Node && !bar.contains(event.target) && !fieldOf(event.target)) {
            hide();
        }
    });

    document.addEventListener('click', (event) => {
        if (!editMode.isEditing()) {
            return;
        }
        const field = fieldOf(event.target);
        if (!field) {
            return;
        }
        const type = field.dataset.cmsType;
        if (windowTypes.includes(type)) {
            event.preventDefault();
            openAction(directActions[type], field, null);
        } else {
            show(field);
        }
    }, true);

    document.addEventListener('keydown', (event) => {
        if (event.key !== 'Enter' || event.ctrlKey || event.metaKey || event.altKey || event.shiftKey
            || !editMode.isEditing() || !(event.target instanceof Element) || bar.contains(event.target)) {
            return;
        }
        const field = fieldOf(event.target);
        const type = field?.dataset.cmsType;
        if (field && windowTypes.includes(type)) {
            event.preventDefault();
            openAction(directActions[type], field, null);
        }
    });

    document.addEventListener('cms:fieldchange', (event) => {
        const element = event.detail.element;
        if (editMode.isEditing() && windowTypes.includes(element.dataset.cmsType)) {
            element.setAttribute('tabindex', '0');
        }
    });

    document.addEventListener('cms:modechange', (event) => {
        setFocusable(event.detail.editing);
        if (!event.detail.editing) {
            hide();
        }
    });

    enhance();

    return { enhance, hide };
};
