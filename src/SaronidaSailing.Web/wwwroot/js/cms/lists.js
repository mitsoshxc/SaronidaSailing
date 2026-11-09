import { el, openDialog } from './dialog.js';

const sortableUrl = '/lib/sortablejs/sortable.core.esm.js';
const itemSelector = '[data-cms-item]';

const itemsOf = (key) =>
    [...document.querySelectorAll(itemSelector)].filter((item) => item.dataset.cmsList === key);

const isActive = (item) => item.dataset.cmsActive !== 'false';

const sameIds = (a, b) => a.length === b.length && a.every((id, index) => id === b[index]);

const focusItem = (item) => {
    const target = item.querySelector('[data-cms-key]') ?? item.querySelector('.cms-itembar button');
    target?.focus();
};

const makeButton = (text, label) => {
    const button = el('button', 'cms-itembar-button', text);
    button.type = 'button';
    button.setAttribute('aria-label', label);
    button.title = label;
    return button;
};

export const createLists = ({ pending, inline, fieldBar, toolbar, editMode, config }) => {
    const lists = new Map();

    const position = (element) => {
        if (getComputedStyle(element).position === 'static') {
            element.classList.add('cms-positioned');
        }
    };

    const announceMove = (list, item) => {
        const items = itemsOf(list.key);
        toolbar.setStatus(`Moved to position ${items.indexOf(item) + 1} of ${items.length}.`);
    };

    const refreshBars = (list) => {
        const items = itemsOf(list.key);
        items.forEach((item, index) => {
            const bar = item.querySelector(':scope > .cms-itembar');
            if (!bar) {
                return;
            }
            const setDisabled = (name, disabled) => {
                const button = bar.querySelector(`[data-cms-itemaction="${name}"]`);
                if (disabled) {
                    button.setAttribute('aria-disabled', 'true');
                } else {
                    button.removeAttribute('aria-disabled');
                }
            };
            setDisabled('up', index === 0);
            setDisabled('down', index === items.length - 1);
        });
    };

    const sync = (list) => {
        const current = itemsOf(list.key).map((item) => ({ id: item.dataset.cmsItem, active: isActive(item) }));
        const currentIds = current.map((item) => item.id);
        const originalIds = list.original.ids;
        const originalSet = new Set(originalIds);
        const currentSet = new Set(currentIds);

        const added = currentIds.filter((id) => !originalSet.has(id));
        const deleted = originalIds.filter((id) => !currentSet.has(id));

        const ops = [
            ...added.map((id) => ({ op: 'addListItem', listKey: list.key, id })),
            ...deleted.map((id) => ({ op: 'deleteListItem', listKey: list.key, id }))
        ];
        for (const { id, active } of current) {
            const changed = originalSet.has(id) ? active !== list.original.active.get(id) : !active;
            if (changed) {
                ops.push({ op: 'setListItemActive', listKey: list.key, id, isActive: active });
            }
        }
        const expected = [...originalIds.filter((id) => currentSet.has(id)), ...added];
        if (!sameIds(currentIds, expected)) {
            ops.push({ op: 'reorderList', listKey: list.key, orderedIds: currentIds });
        }

        pending.setOps(`list:${list.key}`, ops);
        refreshBars(list);
        document.dispatchEvent(new CustomEvent('cms:structurechange'));
    };

    const setActive = (list, item, active, button) => {
        if (active) {
            item.removeAttribute('data-cms-active');
        } else {
            item.setAttribute('data-cms-active', 'false');
        }
        button.textContent = active ? 'Hide' : 'Show';
        button.setAttribute('aria-label', active ? 'Hide item' : 'Show item');
        sync(list);
        toolbar.setStatus(active ? 'Item shown to visitors.' : 'Item hidden from visitors (both languages).');
    };

    const move = (list, item, direction, button) => {
        if (button.getAttribute('aria-disabled') === 'true') {
            return;
        }
        const items = itemsOf(list.key);
        const index = items.indexOf(item);
        if (direction < 0) {
            item.after(items[index - 1]);
        } else {
            item.before(items[index + 1]);
        }
        sync(list);
        button.focus();
        announceMove(list, item);
    };

    const removeItem = (list, item, win, saved) => {
        const items = itemsOf(list.key);
        const index = items.indexOf(item);
        const neighbour = items[index + 1] ?? items[index - 1] ?? null;
        pending.dropItem(item.dataset.cmsItem);
        item.remove();
        win.close({ restoreFocus: false });
        if (neighbour) {
            focusItem(neighbour);
        } else {
            list.addButton.focus();
        }
        sync(list);
        toolbar.setStatus(saved ? 'Item deleted. Save page to apply.' : 'New item removed.');
    };

    const confirmDelete = (list, item, trigger, hideButton) => {
        const saved = list.original.ids.includes(item.dataset.cmsItem);
        const bar = item.querySelector(':scope > .cms-itembar');
        bar.classList.add('cms-itembar-held');
        const win = openDialog({
            title: saved ? 'Delete item?' : 'Remove new item?',
            returnFocus: () => {
                trigger.focus();
                bar.classList.remove('cms-itembar-held');
            }
        });
        win.body.append(el('p', '', saved
            ? "It will be removed in both languages. Once you save, this can't be undone — deleted items have no history. Hide it instead to keep it for later."
            : "This item hasn't been saved yet."));

        const buttons = [{ label: 'Cancel', onClick: () => win.close() }];
        if (saved) {
            buttons.push({
                label: 'Hide instead',
                onClick: () => {
                    if (isActive(item)) {
                        setActive(list, item, false, hideButton);
                    }
                    win.close();
                }
            });
        }
        buttons.push({
            label: saved ? 'Delete' : 'Remove',
            primary: true,
            onClick: () => removeItem(list, item, win, saved)
        });
        win.setActions(buttons);
        win.actions.querySelector('button')?.focus();
    };

    const addBar = (list, item) => {
        const bar = el('div', 'cms-itembar');
        bar.setAttribute('role', 'group');
        bar.setAttribute('aria-label', 'Item tools');

        const drag = el('span', 'cms-itembar-drag', '⋮⋮');
        drag.setAttribute('aria-hidden', 'true');
        drag.title = 'Drag to move';

        const up = makeButton('↑', 'Move up');
        const down = makeButton('↓', 'Move down');
        const active = isActive(item);
        const hide = makeButton(active ? 'Hide' : 'Show', active ? 'Hide item' : 'Show item');
        const remove = el('button', 'cms-itembar-button', 'Delete');
        remove.type = 'button';
        up.dataset.cmsItemaction = 'up';
        down.dataset.cmsItemaction = 'down';
        hide.dataset.cmsItemaction = 'hide';
        remove.dataset.cmsItemaction = 'delete';

        up.addEventListener('click', () => move(list, item, -1, up));
        down.addEventListener('click', () => move(list, item, 1, down));
        hide.addEventListener('click', () => setActive(list, item, !isActive(item), hide));
        remove.addEventListener('click', () => confirmDelete(list, item, remove, hide));

        bar.append(drag, up, down, hide, remove);
        item.append(bar);
        position(item);
    };

    const addItem = (list) => {
        const item = list.template.content.firstElementChild.cloneNode(true);
        item.dataset.cmsItem = crypto.randomUUID();
        list.template.before(item);
        addBar(list, item);
        inline.enhance(item);
        fieldBar.enhance();
        item.scrollIntoView({ block: 'center' });
        focusItem(item);
        sync(list);
        toolbar.setStatus(config.locale === 'en'
            ? 'Item added. Fill in the Greek text on the Greek page too.'
            : 'Item added.');
    };

    const addAddButton = (list) => {
        const wrapper = document.createElement(list.template.content.firstElementChild.tagName.toLowerCase());
        wrapper.className = 'cms-list-add';
        const button = el('button', 'cms-button', 'Add item');
        button.type = 'button';
        button.addEventListener('click', () => addItem(list));
        wrapper.append(button);
        list.template.after(wrapper);
        list.addButton = button;
    };

    const enableDrag = (list, Sortable) => {
        const sortable = new Sortable(list.template.parentElement, {
            handle: '.cms-itembar-drag',
            draggable: `${itemSelector}[data-cms-list="${list.key}"]`,
            animation: 150,
            ghostClass: 'cms-sortable-ghost',
            chosenClass: 'cms-sortable-chosen',
            disabled: !editMode.isEditing(),
            onEnd: (event) => {
                const last = itemsOf(list.key).at(-1);
                if (last && list.template.compareDocumentPosition(last) & Node.DOCUMENT_POSITION_FOLLOWING) {
                    list.template.before(last);
                }
                if (event.oldIndex === event.newIndex) {
                    return;
                }
                sync(list);
                announceMove(list, event.item);
            }
        });
        document.addEventListener('cms:modechange', (event) => {
            sortable.option('disabled', !event.detail.editing);
        });
    };

    for (const template of document.querySelectorAll('template[data-cms-list-template]')) {
        const key = template.dataset.cmsListTemplate;
        const items = itemsOf(key);
        const list = {
            key,
            template,
            addButton: null,
            original: {
                ids: items.map((item) => item.dataset.cmsItem),
                active: new Map(items.map((item) => [item.dataset.cmsItem, isActive(item)]))
            }
        };
        lists.set(key, list);
        addAddButton(list);
        for (const item of items) {
            addBar(list, item);
        }
        refreshBars(list);
    }

    if (lists.size > 0) {
        import(sortableUrl)
            .then((module) => {
                for (const list of lists.values()) {
                    enableDrag(list, module.default);
                }
            })
            .catch(() => {
                toolbar.setStatus('Drag and drop is unavailable. Use the Move up and Move down buttons.');
            });
    }
};
