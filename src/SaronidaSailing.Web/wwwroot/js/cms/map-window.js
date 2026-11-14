import { confirmDialog, el, openDialog } from './dialog.js';

const embedPrefix = 'https://www.google.com/maps/embed';
const invalidMessage = 'This isn’t a Google Maps embed address. Use Share → Embed a map in Google Maps.';
const key = 'contact.map.embed-url';

const isValidEmbedUrl = (value) =>
    value === ''
    || (value.startsWith(embedPrefix)
        && (value.length === embedPrefix.length || '?/'.includes(value[embedPrefix.length]))
        && !/\s/.test(value));

const extractAddress = (raw) => {
    const text = raw.trim();
    if (/<iframe/i.test(text)) {
        const parsed = new DOMParser().parseFromString(text, 'text/html');
        return (parsed.querySelector('iframe')?.getAttribute('src') ?? '').trim();
    }
    const parsed = new DOMParser().parseFromString(`<!doctype html><body>${text}`, 'text/html');
    return (parsed.body.textContent ?? '').trim();
};

const openMapWindow = ({ current, onChange, returnFocus }) => {
    const win = openDialog({ title: 'Map location', subtitle: `${key} · Greek`, returnFocus });

    const form = el('div', 'cms-form');
    const howTo = el('p', 'cms-hint', 'In Google Maps: Share → Embed a map → Copy HTML, then paste it here.');
    howTo.id = 'cms-map-howto';
    const both = el('p', 'cms-hint', 'Applies to both languages.');
    both.id = 'cms-map-both';

    const field = el('div', 'cms-field');
    const label = el('label', 'cms-label', 'Embed code or address');
    const input = el('textarea', 'cms-input cms-textarea');
    input.id = 'cms-map-input';
    input.rows = 5;
    input.value = current;
    input.spellcheck = false;
    input.autocomplete = 'off';
    label.htmlFor = input.id;
    const error = el('p', 'cms-error');
    error.id = 'cms-map-error';
    error.hidden = true;
    input.setAttribute('aria-describedby', `${howTo.id} ${both.id} ${error.id}`);
    field.append(label, input, error);

    const address = el('p', 'cms-hint cms-map-address');
    const previewButton = el('button', 'cms-button', 'Show preview');
    previewButton.type = 'button';
    const previewBox = el('div', 'cms-map-preview');
    previewBox.hidden = true;

    form.append(howTo, both, field, address, previewButton, previewBox);
    win.body.append(form);

    let extracted = current;
    let valid = true;

    const update = () => {
        extracted = extractAddress(input.value);
        const hasIframe = /<iframe/i.test(input.value);
        valid = isValidEmbedUrl(extracted) && !(hasIframe && extracted === '');
        error.textContent = valid ? '' : invalidMessage;
        error.hidden = valid;
        input.toggleAttribute('aria-invalid', !valid);
        address.textContent = extracted === ''
            ? 'No address. The map will be removed.'
            : `Address: ${extracted}`;
        previewButton.disabled = !valid || extracted === '';
        previewBox.replaceChildren();
        previewBox.hidden = true;
        const apply = win.actions.querySelector('.cms-button-primary');
        if (apply) {
            apply.disabled = !valid;
        }
    };

    previewButton.addEventListener('click', () => {
        const frame = el('iframe', 'cms-map-frame');
        frame.title = 'Map preview';
        frame.loading = 'lazy';
        frame.referrerPolicy = 'no-referrer-when-downgrade';
        frame.src = extracted;
        previewBox.replaceChildren(frame);
        previewBox.hidden = false;
    });

    input.addEventListener('input', update);
    win.isDirty = () => input.value !== current;

    const finish = (value) => {
        if (value !== current) {
            onChange(value);
        }
        win.close();
    };

    const remove = async () => {
        const confirmed = await confirmDialog({
            title: 'Remove map?',
            message: 'The map is removed from the Contact page. Save page to apply.',
            confirmLabel: 'Remove map',
            returnFocus: () => win.focusFirst()
        });
        if (confirmed) {
            finish('');
        }
    };

    const buttons = [
        { label: 'Apply', onClick: () => valid && finish(extracted), primary: true },
        { label: 'Cancel', onClick: () => win.requestCancel() }
    ];
    if (current !== '') {
        buttons.splice(1, 0, { label: 'Remove map', onClick: remove });
    }
    win.setActions(buttons);
    update();
    win.focusFirst();
    return win;
};

export const createMapWindow = ({ pending, toolbar }) => {
    const button = document.querySelector('[data-cms-map-edit]');
    const source = document.querySelector('[data-map-url]');
    if (!button || !source) {
        return;
    }

    const field = { key, locale: 'el', type: 'text' };
    const original = source.textContent.trim();

    button.addEventListener('click', () => {
        openMapWindow({
            current: pending.get(field) ?? original,
            returnFocus: () => button.focus(),
            onChange: (value) => {
                pending.setField(field, value, original);
                document.dispatchEvent(new CustomEvent('saronida:map-change', { detail: { url: value } }));
                toolbar.setStatus(value === ''
                    ? 'Map removed (both languages). Save page to apply.'
                    : 'Map location changed (both languages). Save page to apply.');
            }
        });
    });
};
