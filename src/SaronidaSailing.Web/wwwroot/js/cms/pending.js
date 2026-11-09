export const createPending = () => {
    const fields = new Map();
    const slots = new Map();
    const listeners = new Set();

    const notify = () => {
        for (const listener of listeners) {
            listener();
        }
    };

    const fieldId = ({ key, locale, listItemId }) => `${key}|${locale}|${listItemId ?? ''}`;

    const opCount = () => {
        let total = 0;
        for (const ops of slots.values()) {
            total += ops.length;
        }
        return total;
    };

    return {
        setField(field, value, originalValue) {
            const id = fieldId(field);
            if (value === originalValue) {
                if (!fields.delete(id)) {
                    return;
                }
            } else {
                fields.set(id, { ...field, value });
            }
            notify();
        },

        get: (field) => fields.get(fieldId(field))?.value,

        setOps(slot, ops) {
            const before = JSON.stringify(slots.get(slot) ?? []);
            if (before === JSON.stringify(ops)) {
                return;
            }
            if (ops.length === 0) {
                slots.delete(slot);
            } else {
                slots.set(slot, ops);
            }
            notify();
        },

        dropItem(listItemId) {
            let removed = false;
            for (const [id, field] of fields) {
                if (field.listItemId === listItemId) {
                    fields.delete(id);
                    removed = true;
                }
            }
            if (removed) {
                notify();
            }
        },

        count: () => fields.size + opCount(),

        toBatch() {
            const changes = [];
            for (const ops of slots.values()) {
                changes.push(...ops);
            }
            for (const { key, locale, type, listItemId, value } of fields.values()) {
                const change = { op: 'setField', key, locale, type, value };
                if (listItemId) {
                    change.listItemId = listItemId;
                }
                changes.push(change);
            }
            return { changes };
        },

        clear() {
            fields.clear();
            slots.clear();
            notify();
        },

        subscribe(listener) {
            listeners.add(listener);
        }
    };
};
