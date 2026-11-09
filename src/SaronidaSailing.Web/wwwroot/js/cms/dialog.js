let counter = 0;

const focusable = 'input:not([type="hidden"]):not([disabled]), textarea:not([disabled]), select:not([disabled]), '
    + '[contenteditable="true"], button:not([disabled])';

export const isDialogOpen = () => document.querySelector('dialog.cms-dialog') !== null;

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
            if (!dialog.isConnected) {
                return;
            }
            dialog.close();
            dialog.remove();
            if (options?.restoreFocus !== false) {
                returnFocus?.();
            }
        },

        requestCancel() {
            if (!win.isDirty() || confirm('Discard the changes in this window?')) {
                win.close();
            }
        }
    };

    dialog.addEventListener('cancel', (event) => {
        event.preventDefault();
        win.requestCancel();
    });

    document.body.append(dialog);
    dialog.showModal();

    return win;
};
