import { el } from './dialog.js';

const MAX_POSTS = 12;

export const createInstagramCount = ({ pending, toolbar }) => {
    for (const section of document.querySelectorAll('[data-cms-instagram-count]')) {
        const bar = section.querySelector('.cms-sectionbar');
        if (!bar) {
            continue;
        }

        const key = section.dataset.cmsInstagramCountKey;
        const sectionLabel = section.dataset.cmsSectionLabel;
        const original = section.dataset.cmsInstagramCount;
        const field = { key, locale: 'el', type: 'text' };
        const tiles = [...section.querySelectorAll('.instagram-feed-grid > li')];

        const wrap = el('span', 'cms-sectionbar-control');
        const label = el('label', 'cms-sectionbar-label', 'Posts shown');
        const select = el('select', 'cms-sectionbar-select');
        const hint = el('span', 'visually-hidden', 'Same for both languages.');
        select.id = `cms-instagram-count-${key.replaceAll('.', '-')}`;
        hint.id = `${select.id}-hint`;
        label.htmlFor = select.id;
        select.title = 'Same for both languages.';
        select.setAttribute('aria-label', `Posts shown: ${sectionLabel}`);
        select.setAttribute('aria-describedby', hint.id);
        for (let n = 1; n <= MAX_POSTS; n++) {
            const option = el('option', '', String(n));
            option.value = String(n);
            select.append(option);
        }
        wrap.append(label, select, hint);
        bar.append(wrap);

        const preview = () => {
            const shown = Number(select.value);
            tiles.forEach((tile, index) => {
                tile.hidden = index >= shown;
            });
        };

        const sync = () => {
            const value = pending.get(field) ?? original;
            if (select.value !== value) {
                select.value = value;
                preview();
            }
        };

        select.value = original;
        select.addEventListener('change', () => {
            pending.setField(field, select.value, original);
            preview();
            const shown = Number(select.value);
            toolbar.setStatus(
                `${sectionLabel}: ${shown} ${shown === 1 ? 'post' : 'posts'} shown (both languages). Save page to apply.`);
        });
        pending.subscribe(sync);
    }
};
