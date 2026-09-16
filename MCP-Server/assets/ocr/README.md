# DSCons local OCR assets

Learner releases contain these two generated assets, copied from the exact
production dependencies locked in `MCP-Server/package-lock.json`:

- `eng.traineddata.gz` from `@tesseract.js-data/eng@1.0.0`, profile
  `4.0.0_best_int`;
- `vie.traineddata.gz` from `@tesseract.js-data/vie@1.0.0`, profile
  `4.0.0_best_int`.

They are copied only by `scripts/sync-ocr-assets.ps1`; the script validates the
SHA-256 values in `ocr-assets.manifest.json`. This source repository does not
commit the language-data binaries. `family_source_inspect` returns the typed
`OcrUnavailable` error for scanned PDFs when they are absent, and never
downloads models at learner runtime.
