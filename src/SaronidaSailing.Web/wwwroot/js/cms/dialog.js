let counter = 0;

const focusable = 'input:not([type="hidden"]):not([disabled]), textarea:not([disabled]), select:not([disabled]), '
    + '[contenteditable="true"], button:not([disabled])';

export const isDialogOpen = () => document.querySelector('dialog.cms-dialog[open]') !== null;

export const el = (tag, className, text) => {
    const element = document.createElement(tag);
    if (className) {
        element.className = className;
    }
    if (text !== undefined) {
        element.textContent = text;
    }
    return element;
};

export const openDialog = ({ title, subtitle = '', returnFocus = null }) => {
    counter += 1;
    const titleId = `cms-dialog-title-${counter}`;

    const dialog = el('dialog', 'cms-dialog');
    dialog.setAttribute('aria-labelledby', titleId);

    const header = el('header', 'cms-dialog-header');
    const heading = el('h2', 'cms-dialog-title', title);
    heading.id = titleId;
    const subtitleElement = el('p', 'cms-dialog-subtitle', subtitle);
    subtitleElement.hidden = subtitle === '';
    header.append(heading, subtitleElement);

    const body = el('div', 'cms-dialog-body');
    const note = el('p', 'cms-dialog-note', 'Changes are kept until you click Save page.');
    const actions = el('div', 'cms-dialog-actions');
    dialog.append(header, body, note, actions);

    const win = {
        dialog,
        body,
        actions,
        isDirty: () => false,

        setSubtitle(text) {
            subtitleElement.textContent = text;
            subtitleElement.hidden = text === '';
        },

        setActions(buttons) {
            actions.replaceChildren();
            for (const { label, onClick, primary = false, disabled = false } of buttons) {
                const button = el('button', primary ? 'cms-button cms-button-primary' : 'cms-button', label);
                button.type = 'button';
                button.disabled = disabled;
                button.addEventListener('click', onClick);
                actions.append(button);
            }
        },

        focusFirst() {
            (body.querySelector(focusable) ?? actions.querySelector('button:not([disabled])'))?.focus();
        },

        close(options) {
            if (finished) {
                return;
            }
            finished = true;
            dialog.close();
            dialog.remove();
            if (options?.restoreFocus !== false) {
                returnFocus?.();
            }
        },

        async requestCancel() {
            if (!win.isDirty()) {
                win.close();
                return;
            }
            if (confirming) {
                return;
            }
            confirming = true;
            pendingConfirm = confirmDialog({
                title: 'Discard changes?',
                message: 'Discard the changes in this window?',
                confirmLabel: 'Discard',
                cancelLabel: 'Keep editing',
                returnFocus: () => win.focusFirst()
            });
            const discard = await pendingConfirm;
            pendingConfirm = null;
            confirming = false;
            if (discard) {
                win.close();
            }
        }
    };

    let confirming = false;
    let finished = false;
    let pendingConfirm = null;

    dialog.addEventListener('cancel', (event) => {
        if (!event.cancelable) {
            return;
        }
        event.preventDefault();
        win.requestCancel();
    });

    dialog.addEventListener('close', () => {
        if (finished) {
            return;
        }
        pendingConfirm?.dismiss();
        pendingConfirm = null;
        confirming = false;
        win.close();
    });

    document.body.append(dialog);
    dialog.showModal();

    return win;
};

export const confirmDialog = ({ title, message, confirmLabel, cancelLabel = 'Cancel', returnFocus = null }) => {
    let dismiss;
    const result = new Promise((resolve) => {
        let settled = false;
        const win = openDialog({ title, returnFocus });
        win.dialog.querySelector('.cms-dialog-note').hidden = true;
        win.body.append(el('p', '', message));

        const settle = (value, options) => {
            if (settled) {
                return;
            }
            settled = true;
            win.close(options);
            resolve(value);
        };

        dismiss = () => settle(false, { restoreFocus: false });

        win.requestCancel = () => settle(false);
        win.dialog.addEventListener('close', () => settle(false));
        win.setActions([
            { label: cancelLabel, onClick: () => settle(false) },
            { label: confirmLabel, primary: true, onClick: () => settle(true) }
        ]);
        win.actions.querySelector('button')?.focus();
    });
    result.dismiss = dismiss;
    return result;
};
