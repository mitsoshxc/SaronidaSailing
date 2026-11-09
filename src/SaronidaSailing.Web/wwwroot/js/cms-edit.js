import { createPending } from './cms/pending.js';
import { createEditMode } from './cms/edit-mode.js';
import { createFields } from './cms/fields.js';
import { createInline } from './cms/inline.js';
import { createToolbar } from './cms/toolbar.js';
import { createWindows } from './cms/windows.js';
import { createFieldBar } from './cms/fieldbar.js';
import { createMissing } from './cms/missing.js';

const config = JSON.parse(document.getElementById('cms-config').textContent);
const pending = createPending();
const editMode = createEditMode();
const fields = createFields({ pending, config });

createInline({ fields, editMode });
const toolbar = createToolbar({ config, pending, editMode });
const windows = createWindows({ config, fields, pending, toolbar });
createFieldBar({ editMode, windows });
createMissing();

const settingsButton = document.querySelector('[data-cms-action="page-settings"]');
settingsButton?.addEventListener('click', () => windows.openSettings(() => settingsButton.focus()));
