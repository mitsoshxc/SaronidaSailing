import { isAllowedUrl } from './values.js';

const allowedTags = new Set(['P', 'BR', 'STRONG', 'EM', 'A', 'UL', 'OL', 'LI', 'H3']);
const droppedTags = new Set([
    'SCRIPT', 'STYLE', 'IFRAME', 'FRAME', 'FRAMESET', 'OBJECT', 'EMBED', 'APPLET', 'NOSCRIPT',
    'TEMPLATE', 'TEXTAREA', 'TITLE', 'SVG', 'MATH'
]);
const blockTags = new Set(['P', 'BR', 'UL', 'OL', 'LI', 'H3', 'DIV', 'H1', 'H2', 'H4', 'H5', 'H6', 'BLOCKQUOTE']);

const parse = (html) => {
    const template = document.createElement('template');
    template.innerHTML = html;
    return template.content;
};

const rebuild = (source, target) => {
    for (const node of source.childNodes) {
        if (node.nodeType === Node.TEXT_NODE) {
            target.append(document.createTextNode(node.data.replace(/ /g, ' ')));
        } else if (node.nodeType === Node.ELEMENT_NODE) {
            const tag = node.tagName.toUpperCase();
            if (droppedTags.has(tag)) {
                continue;
            }
            if (!allowedTags.has(tag)) {
                rebuild(node, target);
                continue;
            }
            const copy = document.createElement(tag.toLowerCase());
            if (tag === 'A') {
                const href = node.getAttribute('href');
                if (href !== null && isAllowedUrl(href)) {
                    copy.setAttribute('href', href.trim());
                }
            }
            rebuild(node, copy);
            target.append(copy);
        }
    }
};

const isEmptyParagraph = (node) =>
    node.nodeType === Node.ELEMENT_NODE
    && node.tagName === 'P'
    && [...node.childNodes].every((child) =>
        (child.nodeType === Node.TEXT_NODE && child.data.trim() === '')
        || (child.nodeType === Node.ELEMENT_NODE && child.tagName === 'BR'));

const isBlank = (fragment) =>
    [...fragment.childNodes].every((node) =>
        (node.nodeType === Node.TEXT_NODE && node.data.trim() === '') || isEmptyParagraph(node));

export const cleanFragment = (html) => {
    const fragment = document.createDocumentFragment();
    rebuild(parse(html ?? ''), fragment);
    return isBlank(fragment) ? document.createDocumentFragment() : fragment;
};

export const cleanHtml = (html) => {
    const holder = document.createElement('div');
    holder.append(cleanFragment(html));
    return holder.innerHTML.trim();
};

const collectText = (node, parts) => {
    if (node.nodeType === Node.TEXT_NODE) {
        parts.push(node.data);
        return;
    }
    if (node.nodeType !== Node.ELEMENT_NODE || droppedTags.has(node.tagName.toUpperCase())) {
        return;
    }
    const block = blockTags.has(node.tagName.toUpperCase());
    if (block) {
        parts.push(' ');
    }
    for (const child of node.childNodes) {
        collectText(child, parts);
    }
    if (block) {
        parts.push(' ');
    }
};

export const htmlToText = (html) => {
    const parts = [];
    for (const node of parse(html ?? '').childNodes) {
        collectText(node, parts);
    }
    return parts.join('').replace(/[\s ]+/g, ' ').trim();
};
