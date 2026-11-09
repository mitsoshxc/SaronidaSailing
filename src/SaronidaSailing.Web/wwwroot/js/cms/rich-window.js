import { el, openDialog } from './dialog.js';
import { cleanHtml } from './rich-html.js';
import { isAllowedUrl } from './values.js';

const toolbarLabels = [
    ['.ql-header', 'Heading'],
    ['.ql-bold', 'Bold'],
    ['.ql-italic', 'Italic'],
    ['.ql-list[value="ordered"]', 'Numbered list'],
    ['.ql-list[value="bullet"]', 'Bulleted list'],
    ['.ql-link', 'Link'],
    ['.ql-clean', 'Clear formatting']
];

const loadError = 'The text editor could not be loaded. Reload the page and try again.';
const linkError = 'Links must start with https://, mailto: or tel:';

const hasDisallowedLink = (html) => {
    const template = document.createElement('template');
    template.innerHTML = html;
    return [...template.content.querySelectorAll('a')].some((link) => !isAllowedUrl(link.getAttribute('href') ?? ''));
};

const configure = (Quill, host, html) => {
    const editorArea = el('div', 'cms-rich-area');
    host.append(editorArea);

    const quill = new Quill(editorArea, {
        theme: 'snow',
        formats: ['header', 'bold', 'italic', 'list', 'link'],
        modules: {
            toolbar: [[{ header: 3 }], ['bold', 'italic'], [{ list: 'ordered' }, { list: 'bullet' }], ['link'], ['clean']]
        }
    });

    quill.clipboard.addMatcher(Node.ELEMENT_NODE, (node, delta) => {
        if (/^H[1-6]$/.test(node.tagName)) {
            for (const op of delta.ops) {
                if (op.attributes?.header) {
                    op.attributes.header = 3;
                }
            }
        }
        return delta;
    });

    quill.setContents(quill.clipboard.convert({ html }), 'silent');

    const container = quill.container.parentElement;
    for (const [selector, label] of toolbarLabels) {
        container.querySelector(`.ql-toolbar ${selector}`)?.setAttribute('aria-label', label);
    }
    quill.root.setAttribute('aria-label', 'Text');
    quill.root.setAttribute('role', 'textbox');
    quill.root.setAttribute('aria-multiline', 'true');

    return quill;
};

export const openRichWindow = ({ element, fields, loadQuill, returnFocus }) => {
    const win = openDialog({ title: 'Edit text', returnFocus });
    const error = el('p', 'cms-error');
    error.setAttribute('role', 'alert');
    error.hidden = true;

    const status = el('p', 'cms-dialog-loading', 'Loading the editor…');
    win.body.append(status);
    win.setActions([{ label: 'Cancel', onClick: () => win.requestCancel() }]);
    win.focusFirst();

    let quill = null;
    let initial = null;

    const changed = () => quill !== null && initial.diff(quill.getContents()).ops.length > 0;
    win.isDirty = changed;

    const apply = () => {
        if (!changed()) {
            win.close();
            return;
        }
        const html = quill.getSemanticHTML();
        if (hasDisallowedLink(html)) {
            error.textContent = linkError;
            error.hidden = false;
            return;
        }
        fields.write(element, cleanHtml(html));
        win.close();
    };

    loadQuill().then((Quill) => {
        if (!win.dialog.isConnected) {
            return;
        }
        status.remove();
        const host = el('div', 'cms-rich-host');
        win.body.append(host, error);
        quill = configure(Quill, host, fields.valueOf(element));
        initial = quill.getContents();
        quill.on('text-change', () => {
            error.hidden = true;
        });
        win.setActions([
            { label: 'Apply', onClick: apply, primary: true },
            { label: 'Cancel', onClick: () => win.requestCancel() }
        ]);
        quill.focus();
    }).catch(() => {
        if (win.dialog.isConnected) {
            status.textContent = loadError;
            status.setAttribute('role', 'alert');
        }
    });

    return win;
};
