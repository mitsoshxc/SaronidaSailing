# SortableJS (vendored)

- Library: SortableJS, drag-and-drop reordering
- Version: 1.15.6
- Source: npm package `sortablejs` (`npm pack sortablejs@1.15.6`)
- Files copied: `modular/sortable.core.esm.js` (as `sortable.core.esm.js`), `LICENSE`
- License: MIT (see `LICENSE`)
- Vendored on: 2026-11-09
- Why the core build: it is the ES module with the `Sortable` class and no plugins mounted (MultiDrag, Swap and AutoScroll are exported but not mounted), so it is the smallest build that does what the list editor needs. The page therefore does not auto-scroll while dragging; the Move up and Move down buttons cover long lists.
- CSP: the file never creates `<style>` elements or `style=""` attributes; it only writes `element.style` through the CSSOM, which the own-origin-only CSP allows.
- Loaded lazily by `wwwroot/js/cms/lists.js` with `import('/lib/sortablejs/sortable.core.esm.js')`, only when the page has at least one list (admin only).
