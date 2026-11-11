import { el } from './dialog.js';

const mediaUrl = '/admin/api/media';
const assetCache = new Map();

export const previewSource = (sources) => sources.find((source) => source.width === 960) ?? sources[sources.length - 1];

export const loadAsset = (id) => {
    if (!assetCache.has(id)) {
        const request = fetch(`${mediaUrl}/${encodeURIComponent(id)}`, { credentials: 'same-origin' }).then(
            async (response) => {
                if (response.status === 404) {
                    return null;
                }
                if (!response.ok) {
                    throw new Error(`Media request failed with ${response.status}`);
                }
                return response.json();
            }
        );
        request.catch(() => assetCache.delete(id));
        assetCache.set(id, request);
    }
    return assetCache.get(id);
};

export const rememberAsset = (asset) => {
    assetCache.set(asset.id, Promise.resolve(asset));
};

export const imagePreview = (id) => {
    const holder = el('span', 'cms-image-version');
    if (id === '') {
        holder.textContent = 'No image';
        return holder;
    }
    holder.textContent = 'Loading…';
    loadAsset(id).then((asset) => {
        if (!asset) {
            holder.textContent = 'Image no longer available';
            return;
        }
        const image = el('img', 'cms-image-version-thumb');
        image.src = asset.thumbUrl;
        image.alt = asset.originalFileName;
        image.loading = 'lazy';
        holder.replaceChildren(image);
    }).catch(() => {
        holder.textContent = 'Could not load the image';
    });
    return holder;
};

export const recordImage = ({ pending, config, imageKey, altKey, listItemId, original, result }) => {
    pending.setField({ key: imageKey, locale: 'el', type: 'image', listItemId }, result.id, original.id);
    pending.setField({ key: altKey, locale: config.locale, type: 'text', listItemId }, result.alt, original.alt);
    if (config.locale !== 'el') {
        pending.setField({ key: altKey, locale: 'el', type: 'text', listItemId }, result.altEl, original.altEl);
    }
};

export const layoutClassOf = (element) =>
    [...element.classList].filter((name) => name !== 'cms-image-placeholder').join(' ');

export const createPlaceholder = (layoutClass) => {
    const button = el('button', layoutClass === '' ? 'cms-image-placeholder' : `${layoutClass} cms-image-placeholder`, 'Add image');
    button.type = 'button';
    return button;
};

export const createImageElement = ({ layoutClass, sizes, loading, asset, alt }) => {
    const image = document.createElement('img');
    if (layoutClass !== '') {
        image.className = layoutClass;
    }
    image.src = previewSource(asset.sources).url;
    image.srcset = asset.sources.map((source) => `${source.url} ${source.width}w`).join(', ');
    image.sizes = sizes;
    image.width = asset.width;
    image.height = asset.height;
    image.alt = alt;
    image.loading = loading;
    image.decoding = 'async';
    return image;
};
