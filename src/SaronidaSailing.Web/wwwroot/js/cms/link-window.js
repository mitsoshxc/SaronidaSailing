import { confirmDialog, el, openDialog } from './dialog.js';
import { parseLink } from './values.js';

let counter = 0;

const types = [
    { id: 'web', label: 'Web page', field: 'Web address', inputType: 'url', inputMode: 'url', autocomplete: 'url', placeholder: 'www.example.com' },
    { id: 'email', label: 'Email', field: 'Email address', inputType: 'email', inputMode: 'email', autocomplete: 'email', placeholder: 'name@example.com' },
    { id: 'phone', label: 'Phone', field: 'Phone number', inputType: 'tel', inputMode: 'tel', autocomplete: 'tel', placeholder: '+30 210 000 0000' }
];

const typeById = (id) => types.find((type) => type.id === id);

const splitUrl = (url) => {
    const lower = url.toLowerCase();
    if (lower.startsWith('https:')) {
        return { type: 'web', address: url.replace(/^https:\/\//i, '').replace(/^https:/i, '') };
    }
    if (lower.startsWith('mailto:')) {
        return { type: 'email', address: url.slice('mailto:'.length) };
    }
    return { type: 'phone', address: url.slice('tel:'.length) };
};

export const readLinkForm = (value) => {
    const link = parseLink(value);
    if (!link) {
        return { label: '', type: 'web', address: '' };
    }
    return { label: link.label, ...splitUrl(link.url) };
};

const webAddressError = 'Enter a valid web address, for example www.example.com';

export const buildUrl = (type, rawAddress) => {
    const address = rawAddress.trim();
    if (type === 'web') {
        if (/^[a-z][a-z0-9+.-]*:\/\//i.test(address) && !/^https:\/\//i.test(address)) {
            return { error: 'Use an https:// address' };
        }
        const host = address.replace(/^https:\/\//i, '');
        if (host === '') {
            return { error: 'Enter a web address' };
        }
        try {
            const parsed = new URL(`https://${host}`);
            if (parsed.protocol !== 'https:' || parsed.hostname === '') {
                return { error: webAddressError };
            }
        } catch {
            return { error: webAddressError };
        }
        return { url: `https://${host}` };
    }
    if (type === 'email') {
        return /^[^\s@]+@[^\s@]+$/.test(address)
            ? { url: `mailto:${address}` }
            : { error: 'Enter an email address like name@example.com' };
    }
    const digits = address.replace(/[\s.\-()]/g, '');
    return /^\+?\d+$/.test(digits)
        ? { url: `tel:${digits}` }
        : { error: 'Enter a phone number using digits, with an optional + at the start' };
};

const addField = (parent, labelText, input) => {
    counter += 1;
    const wrapper = el('div', 'cms-field');
    const label = el('label', 'cms-label', labelText);
    input.id = `cms-input-${counter}`;
    label.htmlFor = input.id;
    const errorElement = el('p', 'cms-error');
    errorElement.id = `cms-error-${counter}`;
    errorElement.hidden = true;
    input.setAttribute('aria-describedby', errorElement.id);
    wrapper.append(label, input, errorElement);
    parent.append(wrapper);

    return {
        setError(message) {
            errorElement.textContent = message ?? '';
            errorElement.hidden = !message;
            if (message) {
                input.setAttribute('aria-invalid', 'true');
            } else {
                input.removeAttribute('aria-invalid');
            }
        },
        setLabel(text) {
            label.textContent = text;
        }
    };
};

export const openLinkWindow = ({ element, fields, returnFocus }) => {
    const win = openDialog({ title: 'Edit link', returnFocus });
    const current = fields.valueOf(element);
    const initial = readLinkForm(current);

    const form = el('div', 'cms-form');

    const labelInput = el('input', 'cms-input');
    labelInput.type = 'text';
    labelInput.value = initial.label;
    labelInput.required = true;
    labelInput.autocomplete = 'off';
    const labelField = addField(form, 'Label', labelInput);

    const group = el('fieldset', 'cms-fieldset');
    group.append(el('legend', 'cms-legend', 'Link type'));
    counter += 1;
    const groupName = `cms-link-type-${counter}`;
    const radios = types.map((type) => {
        counter += 1;
        const row = el('div', 'cms-radio');
        const radio = el('input');
        radio.type = 'radio';
        radio.name = groupName;
        radio.id = `cms-radio-${counter}`;
        radio.value = type.id;
        radio.checked = type.id === initial.type;
        const label = el('label', '', type.label);
        label.htmlFor = radio.id;
        row.append(radio, label);
        group.append(row);
        return radio;
    });
    form.append(group);

    const addressInput = el('input', 'cms-input');
    addressInput.value = initial.address;
    addressInput.required = true;
    const addressField = addField(form, 'Web address', addressInput);

    const selectedType = () => radios.find((radio) => radio.checked).value;

    const applyType = () => {
        const type = typeById(selectedType());
        addressField.setLabel(type.field);
        addressInput.type = type.inputType;
        addressInput.inputMode = type.inputMode;
        addressInput.autocomplete = type.autocomplete;
        addressInput.placeholder = type.placeholder;
        addressField.setError(null);
    };
    for (const radio of radios) {
        radio.addEventListener('change', applyType);
    }
    applyType();

    labelInput.addEventListener('input', () => labelField.setError(null));
    addressInput.addEventListener('input', () => addressField.setError(null));

    win.body.append(form);
    win.isDirty = () =>
        labelInput.value !== initial.label
        || selectedType() !== initial.type
        || addressInput.value !== initial.address;

    const apply = () => {
        const label = labelInput.value.trim();
        const built = buildUrl(selectedType(), addressInput.value);
        labelField.setError(label === '' ? 'Enter a label' : null);
        addressField.setError(built.error ?? null);
        if (label === '') {
            labelInput.focus();
            return;
        }
        if (built.error) {
            addressInput.focus();
            return;
        }
        const existing = parseLink(current);
        if (!existing || existing.url !== built.url || existing.label !== label) {
            fields.write(element, JSON.stringify({ url: built.url, label }));
        }
        win.close();
    };

    const remove = async () => {
        const confirmed = await confirmDialog({
            title: 'Remove link?',
            message: 'The link is removed from this field. Save page to apply.',
            confirmLabel: 'Remove link',
            returnFocus: () => win.focusFirst()
        });
        if (confirmed) {
            fields.write(element, '');
            win.close();
        }
    };

    const buttons = [
        { label: 'Apply', onClick: apply, primary: true },
        { label: 'Cancel', onClick: () => win.requestCancel() }
    ];
    if (current !== '') {
        buttons.splice(1, 0, { label: 'Remove link', onClick: remove });
    }
    win.setActions(buttons);

    win.body.addEventListener('keydown', (event) => {
        if (event.key === 'Enter' && event.target instanceof HTMLInputElement && event.target.type !== 'radio') {
            event.preventDefault();
            apply();
        }
    });

    win.focusFirst();
    return win;
};
