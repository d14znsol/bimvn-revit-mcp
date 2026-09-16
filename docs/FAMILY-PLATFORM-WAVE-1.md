# DSCons Family Platform — HVAC Wave 1

Wave 1 uses one MCP Server (`MCP-Server` → `MCP` → `MCP.CoreRuntime`) and an
allow-listed shared Family pipeline. It does not expose arbitrary Revit Family
API.

| Adapter | Engineering LOD | Source | Offline state | Runtime claim |
| --- | --- | --- | --- | --- |
| Axial fan | 300 | confirmed PDF spec | existing Family v1 POC | not runtime-tested |
| Inline / centrifugal fan | 300 | confirmed PDF spec | preview contract only | not runtime-tested |
| FCU / AHU simple | 300 | confirmed PDF spec | preview contract only | not runtime-tested |
| Diffuser / damper / louver | future Wave 2 | TBD | backlog | no claim |

Shared objects are `SourceEvidence` (canonical local path, checksum, selected
page/layers, units and observed data), `FamilySpec` (confirmed/uncertain/missing
fields), `FamilyAdapter`, and `FamilyPipeline`:

`inspect → normalize → preview rollback → human confirmation → staged apply → reopen/checksum → read-back`.

Every adapter preserves the existing local-path, approved-directory,
non-UNC/non-Central, no-overwrite, resource/document fingerprint, GUID staging
and cleanup guards. `round_hvac` remains experimental until it has its own
runtime record. No Family is called supported solely from build or preview.

## Wave 1 scope boundary

The family template must be Mechanical Equipment. Parameters and geometry have
to come from an adapter mapping; the agent must not invent dimensions,
performance data or connectors. Ambiguous catalog information stays
`uncertain`/`missing`. Load/place is a separately confirmed operation.

## Runtime gate

Fire Keeper supplies a template, writable demo directory, copied project,
view/level/point and connector choice. Test Revit 2023 first, then 2025:
flex dimensions, reopen/checksum, type/parameter read-back, connectors,
load/place and rollback. Other Revit years have no runtime inference.
