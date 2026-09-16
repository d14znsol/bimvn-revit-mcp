# Compatibility matrix

Compile, runtime and learner-release claims are independent facts. A successful build never grants runtime support. `revitapidocs.com` may be used to review signatures, but is not a source of licensed Revit API DLLs or runtime evidence.

| Revit | Compile/build | Runtime 28 tool / V2 | Runtime Family / CAD | Runtime Combine & Shop | Learner release |
| --- | --- | --- | --- | --- | --- |
| 2019 | net47; build PASS | Untested | Untested | Untested | Packaged in r7 |
| 2020 | net47; build PASS | Untested | Untested | Untested | Packaged in r7 |
| 2021 | net48; build PASS | No copied-model record | Untested | Untested | Packaged in r7 |
| 2022 | net48; build PASS | Untested | Untested | Untested | Packaged in r7 |
| 2023 | net48; build PASS | PASS on copied model | Axial core `none` PASS; LOD300/connector/CAD untested | Combine & Shop untested | Packaged in r7 |
| 2024 | net48; build PASS | Untested | Untested | Untested | Packaged in r7 |
| 2025 | net8.0-windows; build PASS (`MSB3277` dependency warning) | PASS on copied model | Family/CAD untested | Combine & Shop untested | Packaged in r7 |
| 2026 | net8.0-windows; build PASS (`MSB3277` dependency warning) | Untested | Untested | Untested | Packaged in r7 |
| 2027 | net10.0-windows; build/POC artifact PASS (`MSB3277` dependency warning) | Untested; POC | Untested | Untested; POC | Packaged in r7; runtime remains POC |

## Wave-specific evidence gates

- Build accepts a matching local API reference bundle via `-ApiDirectory`;
  release accepts per-year bundles via `-ApiBundleRoot`. See
  [multi-version rollout](MULTIVERSION-ROLLOUT.md). Binary identity is checked;
  this does not add runtime evidence for missing years.

- Family, CAD and Combine/Shop require separate copied-model records, beginning in Revit 2023 and repeating in Revit 2025 only after 2023 passes.
- Family template auto-resolution is a learner-workflow convenience, not runtime
  certification. It prefers the active Revit year's Metric category template;
  when that template is missing it may use exact-year Metric Generic Model and
  change/read back the target Family Category before geometry. It never uses a
  different Revit year's template.
- `round_hvac` connector certification has its own record; a failure does not make core Family geometry certified.
- The LOD300 quality engine/adapters are source/compile evidence only until
  the specified Family adapter passes the copied-model connector probe. See
  [Family Quality Platform](FAMILY-QUALITY-PLATFORM.md).
- Combine `solid_intersection` is only exact geometry evidence for each returned pair. `clearance_triage` is never a certificate or an automatic design rule.
- The learner release contains only the approved artifacts and dependencies. Private course summaries, local paths, runtime/audit/session data and public-export exclusions remain excluded.

## Family Evidence v2

`family_source_inspect` / `family_spec_preview` are source-evidence tools;
PDF evidence is not Model Family runtime evidence. The Pump/DB pilots remain
`parametric_envelope_pilot`, not LOD300, until role/form bounds, flex, staged
reopen and copied-model Runtime Test Reports pass.
