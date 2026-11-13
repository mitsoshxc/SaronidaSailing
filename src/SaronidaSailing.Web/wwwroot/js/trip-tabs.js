const queryKey = 'trip';

export const initTripTabs = () => {
    const list = document.querySelector('[data-trip-tabs]');
    if (!list) {
        return;
    }

    const tabs = [...list.querySelectorAll('[data-trip-tab]')];
    const panels = new Map([...document.querySelectorAll('[data-trip-panel]')]
        .map((panel) => [panel.dataset.tripPanel, panel]));
    const prices = [...document.querySelectorAll('[data-trip-price]')];
    const initial = tabs.find((tab) => tab.hasAttribute('aria-current')) ?? tabs[0];

    list.setAttribute('role', 'tablist');
    for (const tab of tabs) {
        const panel = panels.get(tab.dataset.tripTab);
        tab.setAttribute('role', 'tab');
        tab.removeAttribute('aria-current');
        if (panel) {
            tab.setAttribute('aria-controls', panel.id);
            panel.setAttribute('role', 'tabpanel');
            panel.setAttribute('aria-labelledby', tab.id);
            panel.tabIndex = 0;
        }
    }

    const select = (selected, { focus = false, remember = true } = {}) => {
        const id = selected.dataset.tripTab;
        for (const tab of tabs) {
            const isSelected = tab === selected;
            tab.setAttribute('aria-selected', String(isSelected));
            tab.tabIndex = isSelected ? 0 : -1;
        }
        for (const [panelId, panel] of panels) {
            panel.hidden = panelId !== id;
        }
        for (const price of prices) {
            price.hidden = price.dataset.tripPrice !== id;
        }
        if (focus) {
            selected.focus();
        }
        if (remember) {
            const url = new URL(window.location.href);
            url.searchParams.set(queryKey, id);
            window.history.replaceState(window.history.state, '', url);
        }
    };

    list.addEventListener('click', (event) => {
        const tab = event.target instanceof Element ? event.target.closest('[data-trip-tab]') : null;
        if (!tab || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
            return;
        }
        event.preventDefault();
        select(tab);
    });

    list.addEventListener('keydown', (event) => {
        const current = event.target instanceof Element ? event.target.closest('[data-trip-tab]') : null;
        if (!current) {
            return;
        }
        const index = tabs.indexOf(current);
        let next = null;
        if (event.key === 'ArrowRight') {
            next = tabs[(index + 1) % tabs.length];
        } else if (event.key === 'ArrowLeft') {
            next = tabs[(index - 1 + tabs.length) % tabs.length];
        } else if (event.key === 'Home') {
            next = tabs[0];
        } else if (event.key === 'End') {
            next = tabs[tabs.length - 1];
        }
        if (next) {
            event.preventDefault();
            select(next, { focus: true });
        }
    });

    select(initial, { remember: false });
};
