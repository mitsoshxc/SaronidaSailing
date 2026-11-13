const storageKey = 'saronida.consent';
const storageVersion = 1;
const changeEvent = 'saronida:consent-change';

const readStored = () => {
    try {
        const parsed = JSON.parse(window.localStorage.getItem(storageKey) ?? 'null');
        if (parsed && parsed.v === storageVersion && typeof parsed.external === 'boolean') {
            return { external: parsed.external };
        }
    } catch {
        return null;
    }
    return null;
};

const writeStored = (external) => {
    try {
        window.localStorage.setItem(storageKey, JSON.stringify({
            v: storageVersion,
            external,
            at: new Date().toISOString()
        }));
    } catch {
        // Blocked storage: the choice still applies until the page is closed.
    }
};

let choice = readStored();
let controller = null;

export const hasExternalConsent = () => choice?.external === true;

export const openConsentSettings = (opener, fallback = null) => controller?.openSettings(opener, fallback);

const isRendered = (element) => element instanceof HTMLElement && element.isConnected && element.getClientRects().length > 0;

export const initConsent = () => {
    const root = document.querySelector('[data-consent]');
    if (!root) {
        return;
    }

    const views = {
        banner: root.querySelector('[data-consent-view="banner"]'),
        settings: root.querySelector('[data-consent-view="settings"]')
    };
    const dialog = root.querySelector('[data-consent-dialog]');
    const settingsTitle = root.querySelector('[data-consent-settings-title]');
    const bannerTitleId = 'consent-title';
    const switchButton = root.querySelector('[data-consent-switch]');
    const statusCard = root.querySelector('[data-consent-status-card]');
    const statusMessage = root.querySelector('[data-consent-status-message]');
    const chooseButton = root.querySelector('[data-consent-action="choose"]');

    let current = 'none';
    let returnTo = 'close';
    let opener = null;
    let fallback = null;
    let switchOn = false;

    const setSwitch = (on) => {
        switchOn = on;
        switchButton.setAttribute('aria-checked', String(on));
    };

    const show = (view) => {
        current = view;
        const showDialog = view === 'banner' || view === 'settings';
        root.hidden = view === 'none';
        dialog.hidden = !showDialog;
        views.banner.hidden = view !== 'banner';
        views.settings.hidden = view !== 'settings';
        statusCard.hidden = view !== 'status';
        if (showDialog) {
            dialog.setAttribute('aria-labelledby', view === 'settings' ? settingsTitle.id : bannerTitleId);
        }
    };

    const focusOpener = () => {
        if (isRendered(opener)) {
            opener.focus();
        } else if (isRendered(fallback)) {
            fallback.focus({ preventScroll: true });
        }
    };

    const announce = (message) => {
        show('status');
        statusMessage.textContent = '';
        window.setTimeout(() => {
            statusMessage.textContent = message;
        }, 50);
    };

    const choose = (external, message) => {
        choice = { external };
        writeStored(external);
        show('none');
        document.dispatchEvent(new CustomEvent(changeEvent, { detail: { external } }));
        announce(message);
        focusOpener();
    };

    const openSettings = (from, fallbackTarget = null) => {
        returnTo = current === 'banner' ? 'banner' : 'close';
        opener = from ?? null;
        fallback = fallbackTarget;
        setSwitch(choice?.external === true);
        show('settings');
        settingsTitle.focus({ preventScroll: true });
    };

    const closeSettings = () => {
        if (returnTo === 'banner') {
            show('banner');
        } else {
            show('none');
        }
        focusOpener();
    };

    const messages = {
        accepted: root.dataset.msgAccepted,
        rejected: root.dataset.msgRejected,
        saved: root.dataset.msgSaved
    };

    const actions = {
        reject: () => choose(false, messages.rejected),
        accept: () => choose(true, messages.accepted),
        'reject-all': () => choose(false, messages.rejected),
        'accept-all': () => choose(true, messages.accepted),
        save: () => choose(switchOn, messages.saved),
        choose: () => openSettings(chooseButton),
        back: closeSettings,
        change: () => {
            const previous = opener;
            const previousFallback = fallback;
            show('none');
            openSettings(previous, previousFallback);
        },
        dismiss: () => {
            show('none');
            focusOpener();
        }
    };

    root.addEventListener('click', (event) => {
        if (!(event.target instanceof Element)) {
            return;
        }
        const button = event.target.closest('[data-consent-action]');
        if (button && root.contains(button)) {
            actions[button.dataset.consentAction]?.();
        }
    });

    switchButton.addEventListener('click', () => setSwitch(!switchOn));

    document.addEventListener('keydown', (event) => {
        if (event.key !== 'Escape' || current !== 'settings' || event.defaultPrevented) {
            return;
        }
        if (event.target instanceof Element && event.target.closest('[contenteditable]')) {
            return;
        }
        event.preventDefault();
        closeSettings();
    });

    controller = { openSettings };

    for (const button of document.querySelectorAll('[data-consent-open]')) {
        button.hidden = false;
        button.addEventListener('click', () => openSettings(button));
    }

    if (choice === null) {
        show('banner');
    }
};
