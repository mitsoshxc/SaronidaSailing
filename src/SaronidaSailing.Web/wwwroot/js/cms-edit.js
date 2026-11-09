import { createPending } from './cms/pending.js';
import { createEditMode } from './cms/edit-mode.js';
import { createInline } from './cms/inline.js';
import { createToolbar } from './cms/toolbar.js';

const config = JSON.parse(document.getElementById('cms-config').textContent);
const pending = createPending();
const editMode = createEditMode();

createInline({ pending, locale: config.locale, editMode });
createToolbar({ config, pending, editMode });
