import { normalize } from './values.js';

const textSelector = '[data-cms-type="text"][data-cms-key]';

export const createInline = ({ fields, editMode }) => {
    const states = new WeakMap();

    const setEditable = (element, editing) => {
        if (editing) {
            element.setAttribute('contenteditable', 'plaintext-only');
            element.setAttribute('spellcheck', 'true');
        } else {
            element.removeAttribute('contenteditable');
            element.removeAttribute('spellcheck');
        }
    };

    const update = (element) => {
        fields.write(element, normalize(element.textContent));
    };

    const enhanceElement = (element) => {
        if (states.has(element)) {
            return;
        }
        fields.track(element);
        const state = { focusText: '' };
        states.set(element, state);

        element.addEventListener('focus', () => {
            state.focusText = element.textContent;
        });
        element.addEventListener('input', () => update(element));
        element.addEventListener('keydown', (event) => {
            if (event.key === 'Enter') {
                event.preventDefault();
                element.blur();
            } else if (event.key === 'Escape') {
                event.preventDefault();
                element.textContent = state.focusText;
                update(element);
                element.blur();
            }
        });

        setEditable(element, editMode.isEditing());
    };

    const enhance = (root) => {
        if (root.matches?.(textSelector)) {
            enhanceElement(root);
        }
        root.querySelectorAll(textSelector).forEach(enhanceElement);
    };

    document.addEventListener('cms:modechange', (event) => {
        document.querySelectorAll(textSelector).forEach((element) => {
            if (states.has(element)) {
                setEditable(element, event.detail.editing);
            }
        });
    });

    document.addEventListener('click', (event) => {
        if (!editMode.isEditing() || !(event.target instanceof Element)) {
            return;
        }
        const editable = event.target.closest('[data-cms-key]');
        if (editable?.closest('a') || editable?.closest('summary')) {
            event.preventDefault();
        }
    }, true);

    // Chromium and Firefox toggle a <details> from a Space keyup inside its <summary>; the character
    // is already inserted on keydown, so cancelling only the keyup keeps typing intact.
    document.addEventListener('keyup', (event) => {
        if (event.key === ' ' && editMode.isEditing() && event.target instanceof Element
            && event.target.closest('[data-cms-key]')?.closest('summary')) {
            event.preventDefault();
        }
    }, true);

    enhance(document);

    return { enhance };
};
