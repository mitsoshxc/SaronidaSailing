# Commissioner (self-hosted)

- Font: Commissioner, variable font, weights 400-800 (one file per subset covers the whole range)
- Source: Google Fonts CSS2 API `https://fonts.googleapis.com/css2?family=Commissioner:wght@400..800&display=swap` (font version v24, files served from `https://fonts.gstatic.com/s/commissioner/v24/`); project: https://github.com/kosbarts/Commissioner
- Files copied (woff2): `commissioner-greek.woff2`, `commissioner-latin.woff2`, `commissioner-latin-ext.woff2`, `OFL.txt` (from `https://github.com/google/fonts/blob/main/ofl/commissioner/OFL.txt`). The Cyrillic and Vietnamese subsets are not shipped.
- Subsets and `unicode-range` (as in the `@font-face` rules at the top of `wwwroot/css/site.css`):
  - greek: `U+0370-0377, U+037A-037F, U+0384-038A, U+038C, U+038E-03A1, U+03A3-03FF`
  - latin-ext: `U+0100-02BA, U+02BD-02C5, U+02C7-02CC, U+02CE-02D7, U+02DD-02FF, U+0304, U+0308, U+0329, U+1D00-1DBF, U+1E00-1E9F, U+1EF2-1EFF, U+2020, U+20A0-20AB, U+20AD-20C4, U+2113, U+2C60-2C7F, U+A720-A7FF`
  - latin: `U+0000-00FF, U+0131, U+0152-0153, U+02BB-02BC, U+02C6, U+02DA, U+02DC, U+0304, U+0308, U+0329, U+2000-206F, U+20AC, U+2122, U+2191, U+2193, U+2212, U+2215, U+FEFF, U+FFFD`
- License: SIL Open Font License 1.1 (see `OFL.txt`)
- Added on: 2026-11-13
- `_Layout.cshtml` preloads the greek and latin files; latin-ext loads only when a page uses its characters.
