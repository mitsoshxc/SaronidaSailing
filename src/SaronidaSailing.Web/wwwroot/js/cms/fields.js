import { createImageElement, createPlaceholder, layoutClassOf, recordImage } from './image-data.js';
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
            if (typeOf(element) === 'image') {
                state.alt = element.dataset.cmsAlt ?? '';
                state.altEl = element.dataset.cmsAltEl ?? '';
                state.pristine = element.cloneNode(true);
            }
            states.set(element, state);
        }
        return state;
    };

    const fieldOf = (element) => ({
        key: element.dataset.cmsKey,
        locale: typeOf(element) === 'image' ? 'el' : config.locale,
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

    const previewKeptLink = (element, link) => {
        element.removeAttribute('target');
        element.removeAttribute('rel');
        if (link) {
            element.setAttribute('href', link.url);
        } else {
            element.removeAttribute('href');
        }
        for (const slot of element.querySelectorAll('[data-cms-link-label]')) {
            slot.textContent = link ? link.label : '';
        }
        for (const notice of element.querySelectorAll(':scope > [data-cms-new-tab-notice]')) {
            notice.remove();
        }
        if (link && /^https:/i.test(link.url)) {
            element.setAttribute('target', '_blank');
            element.setAttribute('rel', 'noopener');
            const notice = document.createElement('span');
            notice.className = 'visually-hidden';
            notice.setAttribute('data-cms-new-tab-notice', '');
            notice.textContent = config.opensInNewTab;
            element.append(notice);
        }
        return link !== null;
    };

    const previewLink = (element, value) => {
        const link = parseLink(value);
        if (element.hasAttribute('data-cms-keep-content')) {
            return previewKeptLink(element, link);
        }
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

    const search = (selector, attribute, key, listItemId) => {
        const wantedItem = listItemId ? String(listItemId).toLowerCase() : null;
        for (const element of document.querySelectorAll(selector)) {
            if (element.dataset[attribute] !== key) {
                continue;
            }
            const item = itemOf(element);
            if ((item ? item.toLowerCase() : null) === wantedItem) {
                return element;
            }
        }
        return null;
    };

    const find = (key, listItemId) => search('[data-cms-key]', 'cmsKey', key, listItemId);

    const findImageByAlt = (altKey, listItemId) =>
        search('[data-cms-type="image"][data-cms-alt-key]', 'cmsAltKey', altKey, listItemId);

    const altField = (element, locale) => ({
        key: element.dataset.cmsAltKey,
        locale,
        type: 'text',
        listItemId: itemOf(element)
    });

    const imageValues = (element) => {
        const state = track(element);
        const alt = pending.get(altField(element, config.locale)) ?? state.alt;
        return {
            id: pending.get(fieldOf(element)) ?? state.original,
            alt,
            altEl: config.locale === 'el' ? alt : (pending.get(altField(element, 'el')) ?? state.altEl)
        };
    };

    const writeImage = (element, { id, asset, alt, altEl }) => {
        const state = track(element);
        const field = fieldOf(element);
        const unchanged = id === state.original && alt === state.alt && altEl === state.altEl;
        const shownAlt = config.locale === 'el' || alt !== '' ? alt : altEl;

        let target;
        if (unchanged) {
            target = state.pristine.cloneNode(true);
        } else {
            if (id === (element.dataset.cmsValue ?? '')) {
                target = element;
            } else if (id === '') {
                target = createPlaceholder(layoutClassOf(element), element.dataset.cmsLabel ?? '');
            } else {
                target = createImageElement({
                    layoutClass: layoutClassOf(element),
                    sizes: element.dataset.cmsSizes ?? '100vw',
                    loading: element.dataset.cmsLoading ?? 'lazy',
                    asset,
                    alt: shownAlt
                });
            }
            if (target !== element) {
                for (const { name, value } of element.attributes) {
                    if (name.startsWith('data-cms-')) {
                        target.setAttribute(name, value);
                    }
                }
            }
            target.dataset.cmsValue = id;
            target.dataset.cmsAlt = alt;
            target.dataset.cmsAltEl = altEl;
            if (target.localName === 'img') {
                target.alt = shownAlt;
            }
            target.toggleAttribute('data-cms-empty', id === '');
            target.toggleAttribute('data-cms-dirty', true);
            target.toggleAttribute('data-cms-fallback', config.locale === 'en' && alt === '' && altEl !== '');
        }

        if (target !== element) {
            states.set(target, state);
            element.replaceWith(target);
        }

        recordImage({
            pending,
            config,
            imageKey: field.key,
            altKey: element.dataset.cmsAltKey,
            listItemId: field.listItemId,
            original: { id: state.original, alt: state.alt, altEl: state.altEl },
            result: { id, alt, altEl }
        });
        document.dispatchEvent(new CustomEvent('cms:fieldchange', { detail: { element: target } }));
        return target;
    };

    return { track, fieldOf, valueOf, write, find, findImageByAlt, imageValues, writeImage };
};
