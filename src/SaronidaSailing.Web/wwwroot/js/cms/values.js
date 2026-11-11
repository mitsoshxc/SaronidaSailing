const allowedLinkScheme = /^(https:|mailto:|tel:)/i;

export const normalize = (text) => text.replace(/\s+/g, ' ').trim();

export const isAllowedUrl = (url) => allowedLinkScheme.test(url.trim());

export const parseLink = (value) => {
    if (!value) {
        return null;
    }
    try {
        const parsed = JSON.parse(value);
        if (parsed && typeof parsed.url === 'string' && typeof parsed.label === 'string'
            && parsed.label.trim() !== '' && isAllowedUrl(parsed.url)) {
            return { url: parsed.url.trim(), label: parsed.label };
        }
    } catch {
        // not JSON: treated as an invalid link
    }
    return null;
};

export const fieldName = (element) => {
    if (element.dataset.cmsName) {
        return element.dataset.cmsName;
    }
    if (element.dataset.cmsType === 'image' && element.dataset.cmsLabel) {
        return element.dataset.cmsLabel;
    }
    return (element.dataset.cmsKey ?? '').split('.').at(-1).replaceAll('-', ' ');
};

export const shorten = (text, length) => (text.length > length ? `${text.slice(0, length).trimEnd()}…` : text);
