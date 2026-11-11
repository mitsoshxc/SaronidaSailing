import { openHistoryWindow } from './history-window.js';
import { openImageWindow } from './image-window.js';
import { openLinkWindow } from './link-window.js';
import { loadQuill } from './quill-loader.js';
import { openRichWindow } from './rich-window.js';
import { openSettingsWindow } from './settings-window.js';

export const createWindows = ({ config, fields, pending, toolbar }) => ({
    openRich: (element, returnFocus) => openRichWindow({ element, fields, loadQuill, returnFocus }),
    openLink: (element, returnFocus) => openLinkWindow({ element, fields, returnFocus }),
    openImage: (element, returnFocus) => openImageWindow({ element, fields, config, returnFocus }),
    openHistory: (element, returnFocus) => openHistoryWindow({
        element, fields, pending, config, setStatus: toolbar.setStatus, returnFocus
    }),
    openSettings: (returnFocus) => openSettingsWindow({
        config,
        pending,
        returnFocus,
        openImage: (subject, back) => openImageWindow({ subject, fields, config, returnFocus: back })
    })
});
