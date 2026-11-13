const swipeDistance = 50;

const isEditing = () =>
    document.getElementById('cms-config') !== null && !document.documentElement.classList.contains('cms-view');

export const initGallery = () => {
    const gallery = document.querySelector('[data-gallery]');
    const grid = gallery?.querySelector('[data-gallery-grid]');
    const allButton = gallery?.querySelector('[data-gallery-all]');
    const dialog = document.querySelector('[data-lightbox]');
    if (!gallery || !grid || !allButton || !dialog) {
        return;
    }

    const image = dialog.querySelector('[data-lightbox-img]');
    const counter = dialog.querySelector('[data-lightbox-counter]');
    const stage = dialog.querySelector('[data-lightbox-stage]');
    const closeButton = dialog.querySelector('[data-lightbox-close]');
    const previousButton = dialog.querySelector('[data-lightbox-prev]');
    const nextButton = dialog.querySelector('[data-lightbox-next]');

    let slides = [];
    let index = 0;
    let opener = allButton;
    let swipeStart = null;

    const collect = () => [...grid.querySelectorAll('img')]
        .filter((img) => !img.closest('[data-cms-active="false"]'));

    const show = (target) => {
        index = (target + slides.length) % slides.length;
        const slide = slides[index];
        const srcset = slide.getAttribute('srcset');
        if (srcset) {
            image.setAttribute('sizes', '100vw');
            image.setAttribute('srcset', srcset);
        } else {
            image.removeAttribute('srcset');
            image.removeAttribute('sizes');
        }
        image.setAttribute('src', slide.getAttribute('src') ?? '');
        image.setAttribute('alt', slide.getAttribute('alt') ?? '');
        counter.textContent = `${index + 1} / ${slides.length}`;
    };

    const open = (from, slide) => {
        slides = collect();
        const start = slide ? slides.indexOf(slide) : 0;
        if (start < 0) {
            return;
        }
        opener = from;
        previousButton.hidden = slides.length < 2;
        nextButton.hidden = slides.length < 2;
        show(start);
        dialog.showModal();
    };

    gallery.classList.add('boat-gallery--js');
    allButton.hidden = false;

    allButton.addEventListener('click', () => open(allButton, null));

    grid.addEventListener('click', (event) => {
        if (isEditing() || !(event.target instanceof Element)) {
            return;
        }
        const tile = event.target.closest('li');
        const slide = tile?.querySelector('img');
        if (slide) {
            open(allButton, slide);
        }
    });

    closeButton.addEventListener('click', () => dialog.close());
    previousButton.addEventListener('click', () => show(index - 1));
    nextButton.addEventListener('click', () => show(index + 1));

    dialog.addEventListener('keydown', (event) => {
        if (event.key === 'ArrowLeft') {
            event.preventDefault();
            show(index - 1);
        } else if (event.key === 'ArrowRight') {
            event.preventDefault();
            show(index + 1);
        }
    });

    stage.addEventListener('pointerdown', (event) => {
        swipeStart = event.isPrimary ? event.clientX : null;
    });
    stage.addEventListener('pointerup', (event) => {
        if (swipeStart === null) {
            return;
        }
        const distance = event.clientX - swipeStart;
        swipeStart = null;
        if (Math.abs(distance) > swipeDistance) {
            show(distance < 0 ? index + 1 : index - 1);
        }
    });
    stage.addEventListener('pointercancel', () => {
        swipeStart = null;
    });

    dialog.addEventListener('close', () => {
        image.removeAttribute('srcset');
        image.removeAttribute('src');
        opener.focus({ preventScroll: true });
    });
};
