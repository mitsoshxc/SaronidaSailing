const textSelector = '[data-cms-type="text"][data-cms-key]';

const normalize = (text) => text.replace(/\s+/g, ' ').trim();

export const createInline = ({ pending, locale, editMode }) => {
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

    const update = (element, state) => {
        const value = normalize(element.textContent);
        const item = element.closest('[data-cms-item]');
        pending.setField({
            key: element.dataset.cmsKey,
            locale,
            type: 'text',
            listItemId: item?.dataset.cmsItem ?? null
        }, value, state.original);

        const changed = value !== state.original;
        element.toggleAttribute('data-cms-empty', value === '');
        element.toggleAttribute('data-cms-dirty', changed);
        element.toggleAttribute('data-cms-fallback', state.hadFallback && !changed);
    };

    const enhanceElement = (element) => {
        if (states.has(element)) {
            return;
        }
        const state = {
            original: normalize(element.textContent),
            hadFallback: element.hasAttribute('data-cms-fallback'),
            focusText: ''
        };
        states.set(element, state);

        element.addEventListener('focus', () => {
            state.focusText = element.textContent;
        });
        element.addEventListener('input', () => update(element, state));
        element.addEventListener('keydown', (event) => {
            if (event.key === 'Enter') {
                event.preventDefault();
                element.blur();
            } else if (event.key === 'Escape') {
                event.preventDefault();
                element.textContent = state.focusText;
                update(element, state);
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
        if (editable?.closest('a')) {
            event.preventDefault();
        }
    }, true);

    enhance(document);

    return { enhance };
};
