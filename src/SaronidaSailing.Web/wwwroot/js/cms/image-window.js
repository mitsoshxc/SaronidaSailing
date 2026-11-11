import { el, openDialog } from './dialog.js';
import { loadAsset, rememberAsset } from './image-data.js';
import { normalize } from './values.js';

let counter = 0;

const mediaUrl = '/admin/api/media';
const accept = 'image/jpeg,image/png,image/webp';
const hintText = 'Describe what the photo shows. Repeating the visible title adds little for screen-reader users.';
const suggestedText = 'Suggested from the title — please refine';
const greekRequired = 'Enter the Greek alt text. It is required for every image.';
const signedOut = 'You are signed out. Sign in again in another tab, then try again.';
const networkError = 'Could not reach the server. Check your connection and try again.';

const uploadMessages = {
    tooLarge: 'The photo is too large. The maximum is 25 MB.',
    notAnImage: 'That file is not an image that can be read. Choose a JPEG, PNG or WebP photo.',
    unsupportedFormat: 'Only JPEG, PNG and WebP photos are supported.',
    heic: 'HEIC photos are not supported. On an iPhone, go to Settings > Camera > Formats and choose "Most Compatible", or send the photo as a JPEG.',
    tooManyPixels: 'The photo is too large. The maximum is 50 megapixels.',
    busy: 'The server is busy processing another photo. Try again in a few seconds.'
};
const genericUploadError = 'The photo could not be uploaded. Try again.';

const sendFile = (file, token, onUploaded) => new Promise((resolve, reject) => {
    const request = new XMLHttpRequest();
    request.open('POST', mediaUrl);
    request.setRequestHeader('RequestVerificationToken', token);
    request.responseType = 'json';
    request.upload.addEventListener('load', onUploaded);
    request.addEventListener('load', () => resolve({ status: request.status, body: request.response }));
    request.addEventListener('error', () => reject(new Error('network')));
    request.addEventListener('abort', () => reject(new Error('aborted')));
    const data = new FormData();
    data.append('file', file);
    request.send(data);
});

const describeUploadFailure = ({ status, body }) => {
    const code = body?.code;
    if (code === 'busy' || status === 503) {
        return { message: uploadMessages.busy, retry: true };
    }
    if (code === 'invalidAntiforgery' || status === 401) {
        return { message: signedOut, retry: false };
    }
    if (status === 403) {
        return { message: 'Your account needs two-factor authentication to edit.', retry: false };
    }
    if (uploadMessages[code]) {
        return { message: uploadMessages[code], retry: false };
    }
    return { message: status === 413 ? uploadMessages.tooLarge : genericUploadError, retry: false };
};

const elementSubject = (element, fields, config) => {
    fields.track(element);
    const values = fields.imageValues(element);
    return {
        title: `Edit image: ${element.dataset.cmsLabel}`,
        subtitle: `${element.dataset.cmsKey} · ${config.locale === 'en' ? 'English' : 'Greek'}`,
        current: {
            id: values.id,
            previewUrl: element.localName === 'img' ? (element.getAttribute('src') ?? '') : '',
            alt: values.alt,
            altEl: values.altEl
        },
        proposal: element.dataset.cmsAltProposal ?? '',
        apply: (result) => fields.writeImage(element, result)
    };
};

const addAltField = ({ parent, label, value, describedBy, note }) => {
    counter += 1;
    const wrapper = el('div', 'cms-field');
    const input = el('textarea', 'cms-input cms-textarea');
    input.id = `cms-alt-${counter}`;
    input.rows = 3;
    input.value = value;
    const labelElement = el('label', 'cms-label', label);
    labelElement.htmlFor = input.id;
    wrapper.append(labelElement);
    if (note) {
        const noteElement = el('p', 'cms-hint', note);
        noteElement.id = `cms-alt-note-${counter}`;
        wrapper.append(noteElement);
        describedBy.push(noteElement.id);
    }

    const suggested = el('p', 'cms-suggested', suggestedText);
    suggested.id = `cms-alt-suggested-${counter}`;
    suggested.hidden = true;

    const errorRegion = el('div');
    errorRegion.setAttribute('aria-live', 'polite');
    const error = el('p', 'cms-error');
    error.id = `cms-alt-error-${counter}`;
    error.hidden = true;
    errorRegion.append(error);

    input.setAttribute('aria-describedby', [...describedBy, suggested.id, error.id].join(' '));
    wrapper.append(input, suggested, errorRegion);
    parent.append(wrapper);

    return {
        input,
        setSuggested(visible) {
            suggested.hidden = !visible;
        },
        setError(message) {
            error.textContent = message ?? '';
            error.hidden = !message;
            if (message) {
                input.setAttribute('aria-invalid', 'true');
            } else {
                input.removeAttribute('aria-invalid');
            }
        }
    };
};

export const openImageWindow = ({ element, subject, fields, config, returnFocus }) => {
    const info = subject ?? elementSubject(element, fields, config);
    let focusTarget;
    const win = openDialog({
        title: info.title,
        subtitle: info.subtitle,
        returnFocus: () => returnFocus(focusTarget)
    });

    const isEnglish = config.locale === 'en';
    const proposal = info.proposal ?? '';
    let choice = { id: info.current.id, previewUrl: info.current.previewUrl, asset: null };
    let busy = false;
    let lastActions = '';

    const form = el('div', 'cms-form');
    win.body.append(form);

    const previewSection = el('div', 'cms-image-section');
    previewSection.append(el('h3', 'cms-image-heading', 'Photo'));
    const previewBox = el('div', 'cms-image-preview-box');
    previewSection.append(previewBox);
    const status = el('p', 'cms-image-status');
    status.setAttribute('role', 'status');
    previewSection.append(status);

    const uploadField = el('div', 'cms-field');
    counter += 1;
    const fileInput = el('input', 'cms-input cms-file-input');
    fileInput.type = 'file';
    fileInput.accept = accept;
    fileInput.id = `cms-upload-${counter}`;
    const fileLabel = el('label', 'cms-label', 'Upload new');
    fileLabel.htmlFor = fileInput.id;
    const uploadErrorRegion = el('div');
    uploadErrorRegion.setAttribute('aria-live', 'polite');
    const uploadError = el('div', 'cms-error cms-image-error');
    uploadError.hidden = true;
    const uploadErrorText = el('p', 'cms-image-error-text');
    const retryButton = el('button', 'cms-button', 'Try again');
    retryButton.type = 'button';
    uploadError.append(uploadErrorText, retryButton);
    uploadErrorRegion.append(uploadError);
    uploadField.append(fileLabel, fileInput, uploadErrorRegion);
    previewSection.append(uploadField);

    const libraryToggle = el('button', 'cms-button', 'Choose existing');
    libraryToggle.type = 'button';
    counter += 1;
    const libraryId = `cms-library-${counter}`;
    libraryToggle.setAttribute('aria-expanded', 'false');
    libraryToggle.setAttribute('aria-controls', libraryId);
    const libraryPanel = el('div', 'cms-media-library');
    libraryPanel.id = libraryId;
    libraryPanel.hidden = true;
    const grid = el('ul', 'cms-media-grid');
    const libraryStatus = el('p', 'cms-hint');
    libraryStatus.setAttribute('role', 'status');
    const libraryErrorRegion = el('div');
    libraryErrorRegion.setAttribute('aria-live', 'polite');
    const libraryError = el('div', 'cms-error cms-image-error');
    libraryError.hidden = true;
    const libraryErrorText = el('p', 'cms-image-error-text');
    const libraryRetry = el('button', 'cms-button', 'Try again');
    libraryRetry.type = 'button';
    libraryError.append(libraryErrorText, libraryRetry);
    libraryErrorRegion.append(libraryError);
    const loadMore = el('button', 'cms-button cms-media-more', 'Load more');
    loadMore.type = 'button';
    loadMore.hidden = true;
    libraryPanel.append(grid, libraryStatus, libraryErrorRegion, loadMore);

    const removeButton = el('button', 'cms-button', 'Remove image');
    removeButton.type = 'button';

    const buttonRow = el('div', 'cms-image-buttons');
    buttonRow.append(libraryToggle, removeButton);
    previewSection.append(buttonRow, libraryPanel);

    const altSection = el('div', 'cms-image-section');
    altSection.append(el('h3', 'cms-image-heading', 'Alt text'));
    const hint = el('p', 'cms-hint', hintText);
    counter += 1;
    hint.id = `cms-alt-hint-${counter}`;
    altSection.append(hint);
    if (isEnglish) {
        altSection.append(el('p', 'cms-hint', 'The photo is shared by both languages.'));
    }

    const altFields = el('div', 'cms-form');
    altSection.append(altFields);

    const prefill = (value) => (value === '' && proposal !== '' ? proposal : value);
    let englishField = null;
    let englishStart = '';
    if (isEnglish) {
        englishStart = prefill(info.current.alt);
        englishField = addAltField({
            parent: altFields,
            label: 'Alt text (English)',
            value: englishStart,
            describedBy: [hint.id],
            note: 'Optional. Visitors see the Greek text when this is empty.'
        });
    }
    const greekStart = isEnglish ? info.current.altEl : prefill(info.current.altEl);
    const greekField = addAltField({
        parent: altFields,
        label: 'Alt text (Greek)',
        value: greekStart,
        describedBy: [hint.id]
    });

    form.append(previewSection, altSection);

    const previewImage = () => {
        const greek = normalize(greekField.input.value);
        const own = englishField ? normalize(englishField.input.value) : greek;
        return own === '' ? greek : own;
    };

    const updateSuggested = () => {
        greekField.setSuggested(!isEnglish && proposal !== '' && greekField.input.value === proposal);
        englishField?.setSuggested(proposal !== '' && englishField.input.value === proposal);
    };

    const renderPreview = () => {
        previewBox.replaceChildren();
        if (choice.id === '') {
            previewBox.append(el('p', 'cms-image-none', 'No image'));
            return;
        }
        if (choice.previewUrl === '') {
            const requested = choice.id;
            previewBox.append(el('p', 'cms-image-none', 'Loading…'));
            loadAsset(requested).then((asset) => {
                if (choice.id !== requested || !win.dialog.isConnected) {
                    return;
                }
                if (asset) {
                    choice.previewUrl = asset.previewUrl;
                    renderPreview();
                } else {
                    previewBox.replaceChildren(el('p', 'cms-image-none', 'Image no longer available'));
                }
            }).catch(() => {
                if (choice.id === requested && win.dialog.isConnected) {
                    previewBox.replaceChildren(el('p', 'cms-image-none', 'Could not load the preview'));
                }
            });
            return;
        }
        const image = el('img', 'cms-image-preview');
        image.src = choice.previewUrl;
        image.alt = previewImage();
        previewBox.append(image);
    };

    const syncPreviewAlt = () => {
        const image = previewBox.querySelector('img');
        if (image) {
            image.alt = previewImage();
        }
    };

    const renderActions = () => {
        const label = choice.id === '' ? 'Apply' : 'Use this image';
        const key = `${label}|${busy}`;
        if (key === lastActions) {
            return;
        }
        lastActions = key;
        win.setActions([
            { label, onClick: apply, primary: true, disabled: busy },
            { label: 'Cancel', onClick: () => win.requestCancel() }
        ]);
    };

    const refresh = () => {
        renderPreview();
        for (const button of grid.querySelectorAll('.cms-media-thumb')) {
            button.setAttribute('aria-pressed', String(button.dataset.id === choice.id));
        }
        removeButton.hidden = choice.id === '';
        renderActions();
    };

    const refreshBusy = () => {
        fileInput.disabled = busy;
        libraryToggle.disabled = busy;
        removeButton.disabled = busy;
        loadMore.disabled = busy;
        for (const button of grid.querySelectorAll('.cms-media-thumb')) {
            button.disabled = busy;
        }
        renderActions();
    };

    const select = (asset) => {
        rememberAsset(asset);
        choice = { id: asset.id, previewUrl: asset.previewUrl, asset };
        refresh();
    };

    const addThumb = (asset, first) => {
        if (grid.querySelector(`[data-id="${asset.id}"]`)) {
            return;
        }
        const item = el('li', 'cms-media-item');
        const button = el('button', 'cms-media-thumb');
        button.type = 'button';
        button.dataset.id = asset.id;
        button.setAttribute('aria-label', asset.originalFileName);
        button.setAttribute('aria-pressed', String(asset.id === choice.id));
        button.disabled = busy;
        const image = el('img', 'cms-media-image');
        image.src = asset.thumbUrl;
        image.alt = '';
        image.loading = 'lazy';
        button.append(image);
        button.addEventListener('click', () => select(asset));
        item.append(button);
        if (first) {
            grid.prepend(item);
        } else {
            grid.append(item);
        }
    };

    let libraryPage = 0;
    let libraryLoaded = false;
    let libraryLoading = false;

    const showLibraryError = (text) => {
        libraryErrorText.textContent = text;
        libraryError.hidden = false;
    };

    const loadLibraryPage = async () => {
        if (libraryLoading) {
            return;
        }
        libraryLoading = true;
        libraryError.hidden = true;
        libraryStatus.textContent = 'Loading…';
        loadMore.hidden = true;
        let hasMore = false;
        try {
            const response = await fetch(`${mediaUrl}?page=${libraryPage + 1}`, { credentials: 'same-origin' });
            if (!response.ok) {
                throw new Error(String(response.status));
            }
            const body = await response.json();
            if (!win.dialog.isConnected) {
                return;
            }
            libraryPage += 1;
            libraryLoaded = true;
            hasMore = body.hasMore === true;
            for (const asset of body.items) {
                rememberAsset(asset);
                addThumb(asset, false);
            }
            libraryStatus.textContent = grid.children.length === 0 ? 'No photos have been uploaded yet.' : '';
        } catch (error) {
            if (win.dialog.isConnected) {
                libraryStatus.textContent = '';
                showLibraryError(error.message === '401' ? signedOut : 'Could not load the photos. Check your connection and try again.');
            }
        } finally {
            libraryLoading = false;
            loadMore.hidden = !hasMore;
        }
    };

    libraryToggle.addEventListener('click', () => {
        const open = libraryPanel.hidden;
        libraryPanel.hidden = !open;
        libraryToggle.setAttribute('aria-expanded', String(open));
        if (open && !libraryLoaded && !libraryLoading) {
            loadLibraryPage();
        }
    });
    loadMore.addEventListener('click', loadLibraryPage);
    libraryRetry.addEventListener('click', loadLibraryPage);

    removeButton.addEventListener('click', () => {
        choice = { id: '', previewUrl: '', asset: null };
        refresh();
        fileInput.focus();
    });

    let lastFile = null;

    const startUpload = async (file) => {
        lastFile = file;
        uploadError.hidden = true;
        busy = true;
        status.textContent = 'Uploading…';
        refreshBusy();

        let result;
        try {
            result = await sendFile(file, config.antiforgeryToken, () => {
                status.textContent = 'Processing…';
            });
        } catch {
            if (!win.dialog.isConnected) {
                return;
            }
            busy = false;
            status.textContent = '';
            uploadErrorText.textContent = networkError;
            retryButton.hidden = false;
            uploadError.hidden = false;
            refreshBusy();
            return;
        }
        if (!win.dialog.isConnected) {
            return;
        }
        busy = false;
        if (result.status === 200 && result.body && typeof result.body.id === 'string') {
            status.textContent = 'Photo uploaded and selected.';
            if (libraryLoaded) {
                addThumb(result.body, true);
            }
            select(result.body);
        } else {
            const failure = describeUploadFailure(result);
            status.textContent = '';
            uploadErrorText.textContent = failure.message;
            retryButton.hidden = !failure.retry;
            uploadError.hidden = false;
        }
        refreshBusy();
    };

    fileInput.addEventListener('change', () => {
        const file = fileInput.files[0];
        fileInput.value = '';
        if (file) {
            startUpload(file);
        }
    });
    retryButton.addEventListener('click', () => {
        if (lastFile) {
            startUpload(lastFile);
        }
    });

    const onAltInput = () => {
        greekField.setError(null);
        updateSuggested();
        syncPreviewAlt();
    };
    greekField.input.addEventListener('input', onAltInput);
    englishField?.input.addEventListener('input', onAltInput);

    win.isDirty = () => choice.id !== info.current.id
        || normalize(greekField.input.value) !== normalize(greekStart)
        || (englishField !== null && normalize(englishField.input.value) !== normalize(englishStart));

    const apply = () => {
        if (busy) {
            return;
        }
        const altEl = normalize(greekField.input.value);
        const alt = englishField ? normalize(englishField.input.value) : altEl;
        if (choice.id !== '' && altEl === '') {
            greekField.setError(greekRequired);
            greekField.input.focus();
            return;
        }
        if (choice.id !== info.current.id || alt !== info.current.alt || altEl !== info.current.altEl) {
            const replacement = info.apply({ id: choice.id, asset: choice.asset, alt, altEl });
            if (replacement) {
                focusTarget = replacement;
            }
        }
        win.close();
    };

    updateSuggested();
    refresh();
    win.focusFirst();
    return win;
};
