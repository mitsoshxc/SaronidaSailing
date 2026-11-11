const savedKey = 'cms-saved';
const antiforgeryError = 'Invalid antiforgery token.';
const networkError = 'Could not reach the server. Your changes are still here — try again.';

const unsaved = (count) => `${count} unsaved change${count === 1 ? '' : 's'}`;

export const createToolbar = ({ config, pending, editMode }) => {
    const counter = document.querySelector('[data-cms-counter]');
    const saveButton = document.querySelector('[data-cms-action="save"]');
    const discardButton = document.querySelector('[data-cms-action="discard"]');
    const status = document.querySelector('[data-cms-status]');
    const errors = document.querySelector('[data-cms-errors]');
    let saving = false;

    const setStatus = (text) => {
        status.textContent = text;
    };

    const showErrors = (messages) => {
        errors.replaceChildren();
        if (messages.length === 0) {
            errors.hidden = true;
            return;
        }
        const list = document.createElement('ul');
        for (const message of messages) {
            const item = document.createElement('li');
            item.textContent = message;
            list.append(item);
        }
        errors.append(list);
        errors.hidden = false;
    };

    const render = () => {
        const count = pending.count();
        counter.textContent = count === 0 ? 'No unsaved changes' : unsaved(count);
        saveButton.disabled = saving || count === 0;
        discardButton.disabled = saving || count === 0;
    };

    const send = async () => {
        const response = await fetch(config.saveUrl, {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': config.antiforgeryToken
            },
            body: JSON.stringify(pending.toBatch())
        });
        let body = null;
        try {
            body = await response.json();
        } catch {
            // not JSON: only the status matters
        }
        return { status: response.status, body };
    };

    const refreshToken = async () => {
        const response = await fetch(location.href, { credentials: 'same-origin' });
        const html = await response.text();
        const doc = new DOMParser().parseFromString(html, 'text/html');
        const fresh = JSON.parse(doc.getElementById('cms-config').textContent);
        config.antiforgeryToken = fresh.antiforgeryToken;
    };

    const isAntiforgeryFailure = (result) =>
        result.status === 400 && Array.isArray(result.body?.errors) && result.body.errors.includes(antiforgeryError);

    const fail = (messages) => {
        showErrors(messages);
        setStatus('Not saved');
        saving = false;
        render();
    };

    const save = async () => {
        if (saving || pending.count() === 0) {
            return;
        }
        saving = true;
        showErrors([]);
        setStatus('Saving…');
        render();

        let result;
        try {
            result = await send();
            if (isAntiforgeryFailure(result)) {
                await refreshToken();
                result = await send();
            }
        } catch {
            fail([networkError]);
            return;
        }

        if (result.status === 200) {
            pending.clear();
            try {
                sessionStorage.setItem(savedKey, '1');
            } catch {
                // storage unavailable: the "Saved" message is just skipped
            }
            location.reload();
        } else if (result.status === 400 && Array.isArray(result.body?.errors)) {
            fail(result.body.errors.map(String));
        } else if (result.status === 401) {
            fail(['You are signed out. Sign in again in another tab, then click Save page.']);
        } else if (result.status === 403) {
            fail(['Your account needs two-factor authentication to edit.']);
        } else if (result.status === 413) {
            fail(['Too many changes at once. Save in smaller steps.']);
        } else {
            fail([networkError]);
        }
    };

    const discard = () => {
        const count = pending.count();
        if (count === 0 || !confirm(`Discard ${unsaved(count)}?`)) {
            return;
        }
        pending.clear();
        location.reload();
    };

    const toolbar = document.querySelector('.cms-toolbar');
    const moreButton = document.querySelector('[data-cms-action="more"]');
    const morePanel = document.getElementById('cms-toolbar-more');

    const setOpen = (open) => {
        toolbar.classList.toggle('cms-toolbar-open', open);
        moreButton.setAttribute('aria-expanded', String(open));
    };

    const inDialog = (node) => node instanceof Element && node.closest('dialog.cms-dialog') !== null;

    moreButton.addEventListener('click', () => setOpen(moreButton.getAttribute('aria-expanded') !== 'true'));

    morePanel.addEventListener('keydown', (event) => {
        if (event.key === 'Escape' && moreButton.getAttribute('aria-expanded') === 'true') {
            event.preventDefault();
            setOpen(false);
            moreButton.focus();
        }
    });

    document.addEventListener('pointerdown', (event) => {
        if (event.target instanceof Node && !toolbar.contains(event.target) && !inDialog(event.target)) {
            setOpen(false);
        }
    });

    toolbar.addEventListener('focusout', (event) => {
        const to = event.relatedTarget;
        if (to instanceof Node && !toolbar.contains(to) && !inDialog(to)) {
            setOpen(false);
        }
    });

    const syncHeight = () => {
        document.documentElement.style.setProperty('--cms-toolbar-height', `${toolbar.offsetHeight}px`);
    };
    new ResizeObserver(syncHeight).observe(toolbar);
    syncHeight();

    saveButton.addEventListener('click', save);
    discardButton.addEventListener('click', discard);
    pending.subscribe(render);

    window.addEventListener('beforeunload', (event) => {
        if (pending.count() > 0) {
            event.preventDefault();
            event.returnValue = '';
        }
    });

    document.addEventListener('keydown', (event) => {
        if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === 's' && editMode.isEditing()) {
            event.preventDefault();
            save();
        }
    });

    try {
        if (sessionStorage.getItem(savedKey) === '1') {
            sessionStorage.removeItem(savedKey);
            setStatus('Saved');
            setTimeout(() => setStatus(''), 4000);
        }
    } catch {
        // storage unavailable: nothing to show
    }

    render();

    return { setStatus };
};
