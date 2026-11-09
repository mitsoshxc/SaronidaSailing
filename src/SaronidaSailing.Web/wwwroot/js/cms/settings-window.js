import { el, openDialog } from './dialog.js';
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

export const openSettingsWindow = ({ config, pending, returnFocus }) => {
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
    win.body.append(form);

    win.isDirty = () => titleInput.value !== titleStart || descriptionInput.value !== descriptionStart;

    const apply = () => {
        const title = normalize(titleInput.value);
        const description = normalize(descriptionInput.value);
        pending.setField(titleField, title, seo.title.value);
        pending.setField(descriptionField, description, seo.description.value);
        win.close();
    };

    win.setActions([
        { label: 'Apply', onClick: apply, primary: true },
        { label: 'Cancel', onClick: () => win.requestCancel() }
    ]);
    win.focusFirst();
    return win;
};
