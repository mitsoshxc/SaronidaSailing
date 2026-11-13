import { initGallery } from './gallery.js';
import { initTripTabs } from './trip-tabs.js';

const desktop = window.matchMedia('(min-width: 960px)');

const initMenu = () => {
    const header = document.querySelector('[data-site-header]');
    const button = header?.querySelector('[data-menu-button]');
    const panel = header?.querySelector('#site-menu');
    if (!header || !button || !panel) {
        return;
    }

    const setOpen = (open) => {
        header.classList.toggle('site-header--open', open);
        button.setAttribute('aria-expanded', String(open));
        button.setAttribute('aria-label', open ? button.dataset.labelClose : button.dataset.labelOpen);
    };
    const isOpen = () => button.getAttribute('aria-expanded') === 'true';

    header.classList.add('site-header--js');

    button.addEventListener('click', () => setOpen(!isOpen()));

    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape' && isOpen()) {
            setOpen(false);
            button.focus();
        }
    });

    document.addEventListener('click', (event) => {
        if (isOpen() && !panel.contains(event.target) && !button.contains(event.target)) {
            setOpen(false);
        }
    });

    desktop.addEventListener('change', () => setOpen(false));
};

const initBackToTop = () => {
    const button = document.querySelector('[data-to-top]');
    if (!button) {
        return;
    }

    let frame = 0;
    const update = () => {
        frame = 0;
        const offset = window.scrollY;
        if (button.hidden && offset > window.innerHeight) {
            button.hidden = false;
        } else if (!button.hidden && offset <= window.innerHeight / 2) {
            button.hidden = true;
        }
    };

    window.addEventListener('scroll', () => {
        if (!frame) {
            frame = window.requestAnimationFrame(update);
        }
    }, { passive: true });

    button.addEventListener('click', () => {
        const smooth = window.matchMedia('(prefers-reduced-motion: no-preference)').matches;
        window.scrollTo({ top: 0, behavior: smooth ? 'smooth' : 'auto' });
        const target = [...document.querySelectorAll('[data-top-focus]')]
            .find((element) => element.getClientRects().length > 0);
        target?.focus({ preventScroll: true });
    });

    update();
};

initMenu();
initBackToTop();
initTripTabs();
initGallery();
