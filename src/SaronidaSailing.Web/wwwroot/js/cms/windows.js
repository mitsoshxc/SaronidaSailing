import { openHistoryWindow } from './history-window.js';
import { openLinkWindow } from './link-window.js';
import { loadQuill } from './quill-loader.js';
import { openRichWindow } from './rich-window.js';
import { openSettingsWindow } from './settings-window.js';

export const createWindows = ({ config, fields, pending, toolbar }) => ({
    openRich: (element, returnFocus) => openRichWindow({ element, fields, loadQuill, returnFocus }),
    openLink: (element, returnFocus) => openLinkWindow({ element, fields, returnFocus }),
    openHistory: (element, returnFocus) => openHistoryWindow({
        element, fields, pending, config, setStatus: toolbar.setStatus, returnFocus
    }),
    openSettings: (returnFocus) => openSettingsWindow({ config, pending, returnFocus })
});
