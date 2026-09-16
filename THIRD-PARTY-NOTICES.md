# Third-party notices

DSCons Revit MCP uses third-party packages pinned by
`MCP-Server/package-lock.json`. Their own licenses govern those components.

| Package | Pinned version | License |
| --- | ---: | --- |
| `@modelcontextprotocol/sdk` | 1.30.0 | MIT |
| `pdfjs-dist` | 4.10.38 | Apache-2.0 |
| `tesseract.js` | 5.1.1 | Apache-2.0 |
| `@tesseract.js-data/eng` | 1.0.0 | MIT |
| `@tesseract.js-data/vie` | 1.0.0 | MIT |
| `@napi-rs/canvas` | 0.1.67 | MIT |

The lockfile records the production dependency graph and the installed npm
packages retain their upstream license texts. The learner release may include
checksum-verified English and Vietnamese Tesseract language data copied from
the pinned language-data packages for offline use.

Autodesk Revit, Revit family templates, `RevitAPI.dll`, `RevitAPIUI.dll`, and
Autodesk's copy of `Newtonsoft.Json.dll` are prerequisites supplied by the
licensed Revit installation. They are not distributed in this repository or
the learner release.
