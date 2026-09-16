# Family Build Report v2

Use this with [Family Build Report template](FAMILY-BUILD-REPORT-TEMPLATE.md).
Record SourceEvidence v2 checksum and citations, immutable FamilySpec v2,
phase/role bounds, flex values, visibility views, nested Family count,
connector state and all staged output checksums. Current Pump and DB pilots are
`parametric_envelope_pilot`; do not mark them `LOD_300` until the documented
role bounds/flex/runtime evidence passes.

The Model Family phase order is fixed:

`framework -> base/support -> equipment core -> drive/electrical block -> ports/flanges -> terminal/access -> visibility -> parameters/connectors -> verification`

Each role must map to an allow-listed recipe and source field/formula. No PDF
or AI request may inject arbitrary geometry. At most two non-shared nested
Families are allowed, controlled by mapped host parameters. Connectors remain
in the host on dedicated port faces.
