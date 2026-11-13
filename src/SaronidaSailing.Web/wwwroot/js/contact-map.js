import { hasExternalConsent, openConsentSettings } from './consent.js';

const embedPrefix = 'https://www.google.com/maps/embed';

export const initContactMap = () => {
    const map = document.querySelector('[data-contact-map]');
    const button = map?.querySelector('[data-map-settings]');
    if (!map || !button) {
        return;
    }

    const url = document.querySelector('[data-map-url]')?.textContent.trim() ?? '';
    let frame = null;

    const sync = () => {
        const wanted = hasExternalConsent() && url.startsWith(embedPrefix);
        if (wanted && !frame) {
            frame = document.createElement('iframe');
            frame.className = 'contact-map__frame';
            frame.title = map.dataset.mapTitle ?? '';
            frame.loading = 'lazy';
            frame.referrerPolicy = 'no-referrer-when-downgrade';
            frame.src = url;
            map.append(frame);
            map.classList.add('contact-map--live');
        } else if (!wanted && frame) {
            frame.remove();
            frame = null;
            map.classList.remove('contact-map--live');
        }
    };

    button.hidden = false;
    button.addEventListener('click', () => openConsentSettings(button, map));
    document.addEventListener('saronida:consent-change', sync);
    sync();
};
