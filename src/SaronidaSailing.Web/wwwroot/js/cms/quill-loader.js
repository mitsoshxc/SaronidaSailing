const stylesheetUrl = '/lib/quill/quill.snow.css';
const scriptUrl = '/lib/quill/quill.js';

let loading = null;

export const loadQuill = () => {
    if (window.Quill) {
        return Promise.resolve(window.Quill);
    }
    loading ??= new Promise((resolve, reject) => {
        const link = document.createElement('link');
        link.rel = 'stylesheet';
        link.href = stylesheetUrl;
        const script = document.createElement('script');
        script.src = scriptUrl;

        let remaining = 2;
        const fail = () => {
            link.remove();
            script.remove();
            loading = null;
            reject(new Error('Quill could not be loaded.'));
        };
        const loaded = () => {
            remaining -= 1;
            if (remaining === 0) {
                resolve(window.Quill);
            }
        };

        link.addEventListener('load', loaded);
        script.addEventListener('load', loaded);
        link.addEventListener('error', fail);
        script.addEventListener('error', fail);
        document.head.append(link, script);
    });
    return loading;
};
