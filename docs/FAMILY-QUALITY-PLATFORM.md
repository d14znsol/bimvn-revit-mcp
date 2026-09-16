# DSCons Family Quality Platform — LOD 300

`LOD_300` here means dependable envelope, interface locations, parameters and
schedule data for the stated coordination/quantity use. It is not a rendering
or fabrication-LOD claim, and it is independent of Revit Coarse/Medium/Fine.

## Implemented contract

- The existing 46 MCP tool names remain unchanged. `family_build_preview` and
  `family_build_apply` now use an allow-listed `FamilySpec → Adapter → Quality
  Validator` pipeline.
- `FamilySpec` records evidence checksum, category, origin policy,
  `LOD_300`, `dscons_mep_300_v1`, confirmed/missing field state and defined
  connector roles. Missing or uncertain required fields block preview/apply.
- Adapter categories and connector roles are defined for HVAC, water, PCCC,
  electrical, conduit and cable tray. Only the axial adapter currently has
  enabled geometry. Registered adapters without fixtures return a safe
  `adapter_registered_geometry_not_enabled` result; they never create guessed
  geometry or arbitrary connectors.
- Axial fan v2 assigns geometry roles/subcategories and Revit visibility:
  casing at Coarse/Medium/Fine, motor/hub at Medium/Fine and four blades only
  at Fine. Its quality read-back verifies those roles and connector count.
- Confirmed `round_hvac` data now requires an explicit air classification;
  the two axial ports are face-hosted, outward-orientation checked, linked,
  and have one primary. Legacy axial v1 behavior remains available unchanged.
- Family inspection returns connector domain, role, profile/size, origin,
  normal, system classification, primary/link and radius-association evidence
  where the Revit API exposes it.

## Certification matrix

| Adapter wave | Source contract | Geometry/apply | Connector certification |
| --- | --- | --- | --- |
| Axial fan | Implemented | Core `connector_mode=none` has Revit 2023 evidence; LOD300 v2 source build only | `round_hvac` not certified |
| FCU/AHU/inline/centrifugal | Implemented | Blocked until sanitized catalog fixture and runtime record | Not certified |
| Pump/fixture/sprinkler | Implemented | Backlog adapter geometry | Not certified |
| Panel/light/junction/conduit/tray | Implemented | Backlog adapter geometry | Not certified |

## Runtime gate per adapter

Use an approved copied model and run Revit 2023 before 2025. The probe is a
rolled-back `TransactionGroup`: place the Family, create a 1 m duct/pipe/
conduit/cable-tray stub along each outward normal (or a test circuit for
electrical), then inspect `mep_connector_network`. Verify rotate 90/180,
mirror, copy, type/size flex and that the route exits the casing. Negative
tests cover missing classification, inward normal, duplicate primary,
duplicate connector location, non-flexing size, stale/reused token and any
route/circuit error; every case must roll back.

No Project Save or Sync is part of this workflow. The release claim may change
only after the per-adapter Runtime Test Report is reviewed by the Fire Keeper.
