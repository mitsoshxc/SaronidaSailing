import { el, openDialog } from './dialog.js';
import { htmlToText } from './rich-html.js';
import { parseLink, shorten } from './values.js';

const dateFormat = new Intl.DateTimeFormat('en-GB', { dateStyle: 'medium', timeStyle: 'short' });

const loadFailed = 'Could not load the history. Check your connection and try again.';

export const describeValue = (type, value, full = false) => {
    let text = '';
    if (type === 'richText') {
        text = htmlToText(value);
    } else if (type === 'link') {
        const link = parseLink(value);
        text = link ? (full ? `${link.label} — ${link.url}` : link.label) : value;
    } else {
        text = value;
    }
    return text.trim() === '' ? '(empty)' : text;
};

const formatDate = (iso) => dateFormat.format(new Date(iso));

const failureMessage = (status) => {
    if (status === 401) {
        return 'You are signed out. Sign in again in another tab, then try again.';
    }
    if (status === 403) {
        return 'Your account needs two-factor authentication to edit.';
    }
    return loadFailed;
};

const getJson = async (url) => {
    const response = await fetch(url, { credentials: 'same-origin' });
    let body = null;
    try {
        body = await response.json();
    } catch {
        // not JSON: only the status matters
    }
    return { status: response.status, body };
};

export const openHistoryWindow = ({ element, fields, pending, config, setStatus, returnFocus }) => {
    const field = fields.fieldOf(element);
    const language = config.locale === 'en' ? 'English' : 'Greek';
    const present = fields.valueOf(element);
    const win = openDialog({
        title: 'History',
        subtitle: `${shorten(describeValue(field.type, present), 60)} (${language})`,
        returnFocus
    });

    const listView = el('div', 'cms-history');
    const confirmView = el('div', 'cms-history');
    confirmView.hidden = true;
    win.body.append(listView, confirmView);

    let undoRequest = 0;

    const showList = () => {
        undoRequest += 1;
        confirmView.hidden = true;
        listView.hidden = false;
        win.setActions([{ label: 'Close', onClick: () => win.close() }]);
    };

    const showMessage = (view, text, retry) => {
        const message = el('p', 'cms-error', text);
        message.setAttribute('role', 'alert');
        view.replaceChildren(message);
        if (retry) {
            const button = el('button', 'cms-button', 'Retry');
            button.type = 'button';
            button.addEventListener('click', retry);
            view.append(button);
            button.focus();
        }
    };

    const restore = (version) => {
        fields.write(element, version.value);
        win.close();
        setStatus('Version restored — click Save page to keep it.');
    };

    const openUndo = async (version) => {
        undoRequest += 1;
        const request = undoRequest;
        listView.hidden = true;
        confirmView.hidden = false;
        confirmView.replaceChildren(el('p', 'cms-dialog-loading', 'Loading…'));
        win.setActions([{ label: 'Back', onClick: showList }]);

        let result;
        try {
            result = await getJson(`${config.savesUrl}${version.saveId}`);
        } catch {
            if (request !== undoRequest) {
                return;
            }
            showMessage(confirmView, loadFailed, () => openUndo(version));
            return;
        }
        if (request !== undoRequest) {
            return;
        }
        if (result.status === 404) {
            showMessage(confirmView, 'That save no longer has any fields to undo.');
            return;
        }
        if (result.status !== 200) {
            showMessage(confirmView, failureMessage(result.status), () => openUndo(version));
            return;
        }
        renderUndo(result.body, version);
    };

    const labelOf = (saved) => {
        const target = saved.locale === config.locale ? fields.find(saved.key, saved.listItemId) : null;
        const name = target ? shorten(describeValue(saved.type, fields.valueOf(target)), 60) : saved.key;
        return `${name} (${saved.locale === 'en' ? 'English' : 'Greek'})`;
    };

    const renderUndo = (save, version) => {
        const toUndo = save.fields.filter((saved) => saved.currentValue !== saved.previousValue);
        const skipped = save.fields.filter((saved) => saved.currentValue === saved.previousValue);

        confirmView.replaceChildren();
        confirmView.append(el('p', 'cms-undo-question',
            `Undo the save of ${formatDate(save.createdAt)} by ${save.changedBy}?`));

        if (toUndo.length === 0) {
            confirmView.append(el('p', 'cms-hint', 'Nothing to undo: every field is already as before.'));
        }

        const list = el('ul', 'cms-undo-list');
        for (const saved of toUndo) {
            const item = el('li', 'cms-undo-item');
            item.append(el('strong', 'cms-undo-field', labelOf(saved)));
            item.append(el('span', 'cms-undo-line', `now: ${shorten(describeValue(saved.type, saved.currentValue, true), 120)}`));
            item.append(el('span', 'cms-undo-line', `back to: ${shorten(describeValue(saved.type, saved.previousValue, true), 120)}`));
            if (saved.currentValue !== saved.value) {
                item.append(el('span', 'cms-flag', 'Changed again by a later save — undo overwrites that change'));
            }
            list.append(item);
        }
        confirmView.append(list);

        if (skipped.length > 0) {
            confirmView.append(el('p', 'cms-hint', 'Already as before (skipped):'));
            const skippedList = el('ul', 'cms-undo-list cms-undo-skipped');
            for (const saved of skipped) {
                skippedList.append(el('li', 'cms-undo-item', labelOf(saved)));
            }
            confirmView.append(skippedList);
        }

        win.setActions([
            { label: 'Undo these changes', onClick: () => undo(toUndo), primary: true, disabled: toUndo.length === 0 },
            { label: 'Back', onClick: showList }
        ]);
        win.actions.querySelector('button:not([disabled])')?.focus();
    };

    const undo = (toUndo) => {
        for (const saved of toUndo) {
            const target = saved.locale === config.locale ? fields.find(saved.key, saved.listItemId) : null;
            if (target) {
                fields.write(target, saved.previousValue);
            } else {
                pending.setField({
                    key: saved.key,
                    locale: saved.locale,
                    type: saved.type,
                    listItemId: saved.listItemId
                }, saved.previousValue, saved.currentValue);
            }
        }
        win.close();
        setStatus('Save undone in the editor — click Save page to keep it.');
    };

    const renderVersions = (versions) => {
        listView.replaceChildren();
        if (versions.length === 0) {
            listView.append(el('p', 'cms-hint', 'No saved versions in this language yet.'));
            return;
        }
        const list = el('ol', 'cms-history-list');
        versions.forEach((version, index) => {
            const row = el('li', 'cms-history-row');
            const meta = el('p', 'cms-history-meta');
            if (index === 0) {
                meta.append(el('strong', 'cms-history-badge', 'Saved now'), ' · ');
            }
            const time = el('time', '', formatDate(version.createdAt));
            time.dateTime = version.createdAt;
            meta.append(time, ` · ${version.changedBy}`);
            row.append(meta, el('p', 'cms-history-preview', shorten(describeValue(field.type, version.value, true), 300)));

            const actions = el('div', 'cms-history-actions');
            if (version.value !== present) {
                const restoreButton = el('button', 'cms-button', 'Restore this version');
                restoreButton.type = 'button';
                restoreButton.addEventListener('click', () => restore(version));
                actions.append(restoreButton);
            }
            const undoButton = el('button', 'cms-button', 'Undo this whole save');
            undoButton.type = 'button';
            undoButton.addEventListener('click', () => openUndo(version));
            actions.append(undoButton);
            row.append(actions);
            list.append(row);
        });
        listView.append(list);
    };

    const load = async () => {
        listView.replaceChildren(el('p', 'cms-dialog-loading', 'Loading…'));
        const params = new URLSearchParams({ key: field.key, locale: field.locale });
        if (field.listItemId) {
            params.set('listItemId', field.listItemId);
        }
        let result;
        try {
            result = await getJson(`${config.historyUrl}?${params}`);
        } catch {
            showMessage(listView, loadFailed, load);
            return;
        }
        if (result.status !== 200 || !Array.isArray(result.body)) {
            showMessage(listView, failureMessage(result.status), load);
            return;
        }
        renderVersions(result.body);
        win.focusFirst();
    };

    showList();
    win.focusFirst();
    load();
    return win;
};
