# Quill (vendored)

- Library: Quill rich-text editor
- Version: 2.0.3
- Source: npm package `quill` (`npm pack quill@2.0.3`)
- Files copied: `dist/quill.js` (standalone UMD build, defines `window.Quill`), `dist/quill.js.LICENSE.txt` (licenses of the libraries bundled into `quill.js`), `dist/quill.snow.css`, `LICENSE`. The trailing `sourceMappingURL` comment was removed from both files; source maps are not shipped.
- License: BSD-3-Clause (see `LICENSE`)
- Vendored on: 2026-11-09
- Loaded lazily by `wwwroot/js/cms/quill-loader.js` the first time a rich-text window opens (admin only).
