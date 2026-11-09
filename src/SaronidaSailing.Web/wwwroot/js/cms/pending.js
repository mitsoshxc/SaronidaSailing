export const createPending = () => {
    const fields = new Map();
    const ops = [];
    const listeners = new Set();

    const notify = () => {
        for (const listener of listeners) {
            listener();
        }
    };

    const fieldId = ({ key, locale, listItemId }) => `${key}|${locale}|${listItemId ?? ''}`;

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

        addOp(op) {
            ops.push(op);
            notify();
        },

        count: () => fields.size + ops.length,

        toBatch() {
            const deleted = new Set(
                ops.filter((op) => op.op === 'deleteListItem').map((op) => op.id));
            const changes = [...ops];
            for (const { key, locale, type, listItemId, value } of fields.values()) {
                if (listItemId && deleted.has(listItemId)) {
                    continue;
                }
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
            ops.length = 0;
            notify();
        },

        subscribe(listener) {
            listeners.add(listener);
        }
    };
};
