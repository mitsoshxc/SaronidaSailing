import { el, openDialog } from './dialog.js';
import { imagePreview, recordImage } from './image-data.js';
import { normalize } from './values.js';

let counter = 0;

const addSetting = ({ parent, label, input, guidance, greek }) => {
    counter += 1;
    const wrapper = el('div', 'cms-field');
    input.id = `cms-setting-${counter}`;
    const labelElement = el('label', 'cms-label', label);
    labelElement.htmlFor = input.id;
    wrapper.append(labelElement);

    const describedBy = [];
    if (greek) {
        const note = el('p', 'cms-hint');
        note.id = `cms-setting-missing-${counter}`;
        note.append(el('span', 'cms-flag', 'EN missing'), ' The Greek text is shown until you write an English one.');
        wrapper.append(note);
        describedBy.push(note.id);
        input.placeholder = greek;
    }

    const hint = el('p', 'cms-hint', guidance);
    hint.id = `cms-setting-hint-${counter}`;
    const count = el('p', 'cms-count');
    count.id = `cms-setting-count-${counter}`;
    describedBy.push(hint.id, count.id);
    input.setAttribute('aria-describedby', describedBy.join(' '));

    const updateCount = () => {
        const length = normalize(input.value).length;
        count.textContent = `${length} character${length === 1 ? '' : 's'}`;
    };
    input.addEventListener('input', updateCount);
    updateCount();

    wrapper.append(input, hint, count);
    parent.append(wrapper);
};

const addImageRow = ({ parent, staged, choose }) => {
    counter += 1;
    const wrapper = el('div', 'cms-field');
    wrapper.setAttribute('role', 'group');
    const labelId = `cms-setting-image-label-${counter}`;
    const label = el('span', 'cms-label', 'Link preview image');
    label.id = labelId;
    wrapper.setAttribute('aria-labelledby', labelId);

    const row = el('div', 'cms-settings-image');
    const preview = el('div', 'cms-settings-image-preview');
    const button = el('button', 'cms-button', 'Choose image…');
    button.type = 'button';
    button.addEventListener('click', choose);
    row.append(preview, button);

    const hint = el('p', 'cms-hint', 'Shown when the page is shared on Facebook, WhatsApp and similar.');
    wrapper.append(label, row, hint);
    parent.append(wrapper);

    return {
        button,
        render() {
            preview.replaceChildren();
            if (staged.id === '') {
                preview.append(el('span', 'cms-image-version', 'No image'));
            } else if (staged.previewUrl !== '') {
                const image = el('img', 'cms-image-version-thumb');
                image.src = staged.previewUrl;
                image.alt = '';
                preview.append(image);
            } else {
                preview.append(imagePreview(staged.id));
            }
        }
    };
};

export const openSettingsWindow = ({ config, pending, returnFocus, openImage }) => {
    const seo = config.seo;
    const language = config.locale === 'en' ? 'English' : 'Greek';
    const win = openDialog({ title: 'Page settings', subtitle: `${seo.pageKey} · ${language}`, returnFocus });

    const fieldFor = (name) => ({
        key: `${seo.pageKey}.seo.${name}`,
        locale: config.locale,
        type: 'text',
        listItemId: null
    });
    const titleField = fieldFor('title');
    const descriptionField = fieldFor('description');

    const titleStart = pending.get(titleField) ?? seo.title.value;
    const descriptionStart = pending.get(descriptionField) ?? seo.description.value;

    const form = el('div', 'cms-form');
    const titleInput = el('input', 'cms-input');
    titleInput.type = 'text';
    titleInput.value = titleStart;
    titleInput.autocomplete = 'off';
    const descriptionInput = el('textarea', 'cms-input cms-textarea');
    descriptionInput.rows = 4;
    descriptionInput.value = descriptionStart;

    addSetting({
        parent: form,
        label: 'Page title (browser tab and search results)',
        input: titleInput,
        guidance: 'Up to about 60 characters show in search results',
        greek: titleStart === '' ? seo.title.greek : null
    });
    addSetting({
        parent: form,
        label: 'Description (search results snippet)',
        input: descriptionInput,
        guidance: 'Up to about 155 characters show in search results',
        greek: descriptionStart === '' ? seo.description.greek : null
    });
    const imageKey = `${seo.pageKey}.seo.image`;
    const altKey = `${imageKey}-alt`;
    const imageField = { key: imageKey, locale: 'el', type: 'image', listItemId: null };
    const altFieldFor = (locale) => ({ key: altKey, locale, type: 'text', listItemId: null });
    const originalImage = { id: seo.image.value, alt: seo.imageAlt.value, altEl: seo.imageAlt.greek };
    const pendingId = pending.get(imageField);
    const pendingAlt = pending.get(altFieldFor(config.locale));
    const pendingGreek = pending.get(altFieldFor('el'));
    const imageStart = {
        id: pendingId ?? originalImage.id,
        alt: pendingAlt ?? originalImage.alt,
        altEl: config.locale === 'el' ? (pendingAlt ?? originalImage.altEl) : (pendingGreek ?? originalImage.altEl)
    };
    const staged = {
        ...imageStart,
        previewUrl: imageStart.id === originalImage.id ? seo.image.previewUrl : ''
    };

    const imageRow = addImageRow({
        parent: form,
        staged,
        choose: () => openImage({
            title: 'Link preview image',
            subtitle: `${seo.pageKey} · ${language}`,
            current: { id: staged.id, previewUrl: staged.previewUrl, alt: staged.alt, altEl: staged.altEl },
            proposal: '',
            apply: (result) => {
                staged.id = result.id;
                staged.alt = result.alt;
                staged.altEl = result.altEl;
                staged.previewUrl = result.asset?.previewUrl ?? (result.id === imageStart.id ? staged.previewUrl : '');
                imageRow.render();
            }
        }, () => imageRow.button.focus())
    });
    imageRow.render();
    win.body.append(form);

    win.isDirty = () => titleInput.value !== titleStart
        || descriptionInput.value !== descriptionStart
        || staged.id !== imageStart.id
        || staged.alt !== imageStart.alt
        || staged.altEl !== imageStart.altEl;

    const apply = () => {
        const title = normalize(titleInput.value);
        const description = normalize(descriptionInput.value);
        pending.setField(titleField, title, seo.title.value);
        pending.setField(descriptionField, description, seo.description.value);
        recordImage({
            pending,
            config,
            imageKey,
            altKey,
            listItemId: null,
            original: originalImage,
            result: { id: staged.id, alt: staged.alt, altEl: staged.altEl }
        });
        win.close();
    };

    win.setActions([
        { label: 'Apply', onClick: apply, primary: true },
        { label: 'Cancel', onClick: () => win.requestCancel() }
    ]);
    win.focusFirst();
    return win;
};
