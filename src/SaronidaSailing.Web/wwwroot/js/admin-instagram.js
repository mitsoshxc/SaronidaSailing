const form = document.querySelector('[data-instagram-disconnect]');

if (form) {
    const open = form.querySelector('[data-disconnect-open]');
    const confirm = form.querySelector('[data-disconnect-confirm]');
    const cancel = form.querySelector('[data-disconnect-cancel]');
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
}
