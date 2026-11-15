const setUpConfirm = (form, names) => {
    const open = form.querySelector(`[data-${names}-open]`);
    const confirm = form.querySelector(`[data-${names}-confirm]`);
    const cancel = form.querySelector(`[data-${names}-cancel]`);
    const submit = confirm.querySelector('button[type="submit"]');

    const show = (visible) => {
        confirm.hidden = !visible;
        open.hidden = visible;
    };

    open.addEventListener('click', () => {
        show(true);
        submit.focus();
    });

    cancel.addEventListener('click', () => {
        show(false);
        open.focus();
    });

    confirm.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
            show(false);
            open.focus();
        }
    });

    cancel.hidden = false;
    show(false);
};

const disconnectForm = document.querySelector('[data-instagram-disconnect]');
if (disconnectForm) {
    setUpConfirm(disconnectForm, 'disconnect');
}

const removeForm = document.querySelector('[data-instagram-remove]');
if (removeForm) {
    setUpConfirm(removeForm, 'remove');
}

const appForm = document.querySelector('[data-instagram-app]');
const appConfirm = appForm?.querySelector('[data-app-confirm]');
if (appForm && appConfirm) {
    const save = appForm.querySelector('[data-app-save]');
    const cancel = appForm.querySelector('[data-app-cancel]');
    const submit = appConfirm.querySelector('button[type="submit"]');
    const appId = appForm.querySelector('input[name="AppId"]');

    const show = (visible) => {
        appConfirm.hidden = !visible;
    };

    const cancelConfirm = () => {
        show(false);
        save.focus();
    };

    appForm.addEventListener('submit', (event) => {
        if (!appConfirm.hidden || event.submitter === submit) {
            return;
        }

        if (appId.value.trim() !== appForm.dataset.savedAppId) {
            event.preventDefault();
            show(true);
            submit.focus();
        }
    });

    cancel.addEventListener('click', cancelConfirm);

    appConfirm.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
            cancelConfirm();
        }
    });

    cancel.hidden = false;
    show(appForm.dataset.confirmNeeded === 'true');
}
