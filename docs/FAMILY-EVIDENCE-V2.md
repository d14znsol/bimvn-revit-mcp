# Family Evidence v2 - PDF to immutable JSON

`family_source_inspect` reads an approved local PDF only. It never uploads the
catalogue, creates Revit geometry, or turns a value into an engineering claim.
The v2 result carries canonical path, SHA-256, page dimensions/rotation and
text blocks with page/bounding-box/capture method/confidence. Native text is
read with PDF.js. A page without text is rendered locally and sent only to the
bundled local Tesseract worker (`eng+vie`).

The evidence JSON is a short-lived local record, schema `2.0`. Each normalized
observation has a value, unit and state: `observed`, `inferred`, `uncertain` or
`missing`. Table ambiguity, unit ambiguity and conflicting values are warnings,
not automatic corrections.

`family_spec_preview` creates an immutable `FamilySpec v2`. The engineer must
confirm every build-critical field or supply an explicit override and reason.
The spec keeps source citation (evidence id, page/bbox where available and
source checksum). It is not buildable while a required value is uncertain or
missing. The build output must keep these three adjacent audit files:

- `<family>.source-evidence.json`
- `<family>.family-spec.json`
- `<family>.build-report.json`

Safety errors are typed: `SourceUnreadable`, `SourceEncrypted`,
`OcrUnavailable`, `EvidenceInvalid`, `EvidenceConflict` and `PathBlocked`.
The limits are 80 MB, 60 pages and 18 million OCR pixels per page. UNC,
traversal and paths outside the approved local directory are rejected.

## Local OCR packaging

The learner release installs locked production dependencies and copies
checksum-verified `eng`/`vie` fast data with `sync-ocr-assets.ps1`. No language
data or model is downloaded when a learner invokes OCR. The source-only public
export excludes language-data binaries, raw PDFs and local paths; it retains the
code, lockfile and asset manifest only. Scribe.js is deliberately not used.

## Current evidence boundary

PDF extraction is source evidence, not runtime Family certification. A Pump or
DB becomes a supported Family only after the separate preview, flex, staged
apply/reopen, placement and copied-model Runtime Test Report for that Revit
year.
