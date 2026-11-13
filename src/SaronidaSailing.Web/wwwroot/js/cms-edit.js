import { createPending } from './cms/pending.js';
import { createEditMode } from './cms/edit-mode.js';
import { createFields } from './cms/fields.js';
import { createInline } from './cms/inline.js';
import { createToolbar } from './cms/toolbar.js';
import { createWindows } from './cms/windows.js';
import { createFieldBar } from './cms/fieldbar.js';
import { createMissing } from './cms/missing.js';
import { createLists } from './cms/lists.js';
import { createSections } from './cms/sections.js';
import { createInstagramCount } from './cms/instagram-count.js';

const config = JSON.parse(document.getElementById('cms-config').textContent);
const pending = createPending();
const editMode = createEditMode();
const fields = createFields({ pending, config });

const inline = createInline({ fields, editMode });
const toolbar = createToolbar({ config, pending, editMode });
const windows = createWindows({ config, fields, pending, toolbar });
const fieldBar = createFieldBar({ editMode, windows });
createMissing();
createSections({ pending, toolbar });
createInstagramCount({ pending, toolbar });
createLists({ pending, inline, fieldBar, toolbar, editMode, config });

const settingsButton = document.querySelector('[data-cms-action="page-settings"]');
settingsButton?.addEventListener('click', () => windows.openSettings(() => settingsButton.focus()));
