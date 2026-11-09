import { cleanFragment, cleanHtml } from './rich-html.js';
import { normalize, parseLink } from './values.js';

const itemOf = (element) => element.closest('[data-cms-item]')?.dataset.cmsItem ?? null;

export const createFields = ({ pending, config }) => {
    const states = new WeakMap();

    const typeOf = (element) => element.dataset.cmsType;

    const originalOf = (element) =>
        typeOf(element) === 'text' ? normalize(element.textContent) : (element.dataset.cmsValue ?? '');

    const track = (element) => {
        let state = states.get(element);
        if (!state) {
            state = {
                original: originalOf(element),
                hadFallback: element.hasAttribute('data-cms-fallback')
            };
            states.set(element, state);
        }
        return state;
    };

    const fieldOf = (element) => ({
        key: element.dataset.cmsKey,
        locale: config.locale,
        type: typeOf(element),
        listItemId: itemOf(element)
    });

    const valueOf = (element) => {
        const state = track(element);
        return pending.get(fieldOf(element)) ?? state.original;
    };

    const previewText = (element, value) => {
        if (normalize(element.textContent) !== value) {
            element.textContent = value;
        }
    };

    const previewRich = (element, value) => {
        element.replaceChildren(cleanFragment(value));
    };

    const previewLink = (element, value) => {
        const link = parseLink(value);
        element.replaceChildren();
        element.removeAttribute('target');
        element.removeAttribute('rel');
        if (!link) {
            element.removeAttribute('href');
            return false;
        }
        element.setAttribute('href', link.url);
        element.append(document.createTextNode(link.label));
        if (/^https:/i.test(link.url)) {
            element.setAttribute('target', '_blank');
            element.setAttribute('rel', 'noopener');
            const notice = document.createElement('span');
            notice.className = 'visually-hidden';
            notice.textContent = config.opensInNewTab;
            element.append(notice);
        }
        return true;
    };

    const write = (element, value) => {
        const state = track(element);
        const type = typeOf(element);
        let stored = value;
        let validLink = true;

        if (type === 'richText') {
            stored = value === state.original ? value : cleanHtml(value);
            previewRich(element, stored);
        } else if (type === 'link') {
            validLink = previewLink(element, value);
        } else {
            previewText(element, value);
        }

        if (type !== 'text') {
            if (stored === '') {
                element.removeAttribute('data-cms-value');
            } else {
                element.setAttribute('data-cms-value', stored);
            }
        }

        const changed = stored !== state.original;
        element.toggleAttribute('data-cms-empty', stored === '');
        element.toggleAttribute('data-cms-dirty', changed);
        element.toggleAttribute('data-cms-fallback', state.hadFallback && !changed);
        element.toggleAttribute('data-cms-invalid', type === 'link' && stored !== '' && !validLink);

        pending.setField(fieldOf(element), stored, state.original);
        document.dispatchEvent(new CustomEvent('cms:fieldchange', { detail: { element } }));
    };

    const find = (key, listItemId) => {
        const wantedItem = listItemId ? String(listItemId).toLowerCase() : null;
        for (const element of document.querySelectorAll('[data-cms-key]')) {
            if (element.dataset.cmsKey !== key) {
                continue;
            }
            const item = itemOf(element);
            if ((item ? item.toLowerCase() : null) === wantedItem) {
                return element;
            }
        }
        return null;
    };

    return { track, fieldOf, valueOf, write, find };
};
