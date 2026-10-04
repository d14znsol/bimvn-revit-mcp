# Native MEPF model transfer (experimental)

This workflow is a controlled native reconstruction from **Revit 2025 to Revit
2023**. It is not Save As to an older RVT, and never uses IFC, DirectShape, a
look-alike Family, or a name-only Type match as a substitute.

## Current boundary (one runtime-certified primitive; one offline-only network candidate)

The code currently accepts only an independent, single-segment, straight,
horizontal native Pipe/Duct/Conduit/Cable Tray route. Source and destination
must have identity shared coordinates. Every Level, Type and System Type is
mapped by an explicit destination ID; Level elevation must match within 0.1 mm,
route kind and native System Classification must match, and the exact route
size contract must be present.

The next bounded adapter is implemented and regression-tested but is **not yet
runtime-certified**: exactly two straight horizontal orthogonal route legs and
one native two-port elbow in one complete physical dependency group. Both legs
must share the same Type, Level, System and exact size. The two physical edges
must bind unique route endpoints to unique elbow connector indexes, and their
origin/domain/profile/size details must match the extracted fitting signature.
The elbow requires an explicit destination FamilySymbol mapping with the same
native fitting category, route domain and `Part Type = Elbow`. Rollback Preview
must return the temporary two curves plus one fitting and pass the same semantic
geometry/connector verification used after Apply. Until the exact R25 → R23
fixture passes Preview, Apply, Save As and reopen, this remains an offline-only
candidate and does not expand the runtime certificate below.

The equipment placement plumbing exists, but equipment remains blocked until
both Revit versions expose the same independently verified Family geometry,
parameter and connector compatibility signature. Category, Family/Type display
name and `OneLevelBased` behavior are not sufficient evidence and are never
used as a look-alike substitution.

The following cause the *entire physical dependency group* to be blocked:

- any connected topology other than the exact two-route/one-elbow candidate
  above; tee, transition, union, tap, flex, three or more legs, reused/wrong
  connector indexes, an external peer, or a cross-group physical edge;
- hosted/nested/unreadable or compatibility-unverified Family, missing
  compatible resource, vertical/diagonal/slope/curve/multi-segment route,
  ambiguous/missing size, non-identity transform or unresolved reference;
- fabrication, circuit/panel, system calculation, views/tags/sheets/groups,
  phases, worksharing, or an unsupported MEP class.

This deliberate restriction prevents a disconnected collection of native
elements from being described as a transferred network. Plumbing/PCCC, HVAC
and Electrical/ELV connected-network pilot fixtures remain runtime gates; they
are not PASS merely because the adapters compile.

## Required sequence

1. In the explicit R25 source, run `model_transfer_extract` for a bounded
   scope. `ModelTransferPackage 1.1` validates that connector edges and
   dependency groups form one complete partition before it stores the
   checksum-bound semantic snapshot locally; no RVT data is uploaded.
2. In a separate R23 staging Project, run `model_transfer_destination_catalog`.
   Catalog 1.1 filters real Level/route/system resources before applying its
   global limit and reports truncation explicitly.
3. Supply explicit mappings to `model_transfer_plan` with
   `staged_project_confirmed=true`, review its ready and blocked groups, and
   select the groups to transfer.
4. Re-anchor with `bim_context_snapshot`, then use
   `model_transfer_preview`. It runs the entire eligible group in a rollback
   TransactionGroup.
5. After an explicit one-time user confirmation, use `model_transfer_apply`.
   The resulting `ModelTransferReport` contains source-to-new-destination IDs
   and checks Type, Level, System Type, centerline, exact size, open route
   endpoints and a package-bound provenance marker immediately after commit.
   For the bounded elbow candidate it also requires one-to-one IDs for both
   trimmed source route legs and the native fitting, exact peer topology, elbow
   Part Type, bounding box and connector origin/direction/profile/size signature.
   A malformed or mismatched read-back is persisted as `mismatch_after_apply`
   and blocks reopen certification. It does not Save or Sync.
6. The user saves a separate staging target without overwriting the source,
   reopens it in R23, then invokes `model_transfer_reopen_verify`. This
   independently checks Type, Level, System Type, provenance, open-end topology,
   location/rotation, straight centerline and nominal size within 0.1 mm / 0.1
   degree. The target must have a real saved path and an exact source→target ID
   identity set.

Any change of the source during extraction, source/package/catalog checksum,
target context, mapping, plan hash or one-time preview invalidates the flow.
Failures roll back their complete Change Set; no automatic source repair or
network redesign is attempted.

## Family decision gate

For every source Family that is needed in a transfer, the workflow must first
ask the engineer whether they can supply a compatible target-version RFA,
approved manufacturer catalogue, or an approved Family Blueprint. It must not
search for, download, or choose an arbitrary look-alike Family by name,
category, or geometry. A supplied candidate still requires explicit comparison
of category, placement/hosting, Type and parameter contract, connector contract
and native Part Type/routing behavior before it can be mapped. If the engineer
has no suitable source and there is not enough verified data to build a bounded
target-version Family, the dependent network is blocked and reported as needing
information.

## Evidence status

Offline contract/build tests cover schema/checksum invalidation, topology
partition integrity, explicit mapping, Level/System incompatibility,
physical-network/fitting/vertical/slope/multi-segment/transform blocks, exact
size, 0.1-mm tolerance, preview cleanliness, one-time Apply, semantic
post-commit comparison and report/reopen identity. Package/catalog 1.0 records
are deliberately rejected after this hardening and must be re-extracted with
the installed 1.1 adapter.

The connected-network regression now additionally covers the exact native Pipe
elbow candidate and rejects tee/union/transition, extra legs, collinear,
vertical or diagonal legs, mismatched Type/System/size, missing or incompatible
FamilySymbol mapping, external peers, off-route connector evidence, duplicate,
reused or out-of-range connector indexes, duplicate/cross-group edges, missing
created fitting IDs, and post-Preview/post-Apply geometry or topology mismatch.
MCP Server build (63 tools), the focused ModelTransfer suite, protocol smoke,
contract tests and Revit 2023/2025 builds pass. This is source/offline evidence,
not a Revit connected-network PASS.

The first exact Revit 2025 → 2023 primitive fixture is runtime-certified: one
independent DN50 Pipe, 2,000 mm long, was extracted as package 1.1, mapped by
explicit Level/Pipe Type/Piping System Type IDs, Previewed with full rollback,
Applied once in a separate R23 staging copy, checked after commit, saved by the
user to a new file, reopened in R23 and verified again. Type, Level, native
System Classification, centerline, diameter, package-bound provenance and two
open endpoint connectors matched at the 0.1-mm contract. Size and Pipe Type
editability were also exercised in rollback Previews.

The exact bounded native Pipe elbow chain is also runtime-certified on Revit
2025 → 2023: two orthogonal DN50 Pipe legs and one native two-port elbow were
extracted as one complete dependency group, explicitly mapped to the R23
Level/Pipe Type/System Type/elbow FamilySymbol, Previewed with rollback and
Applied once. Post-commit and reopened staging-file read-back verified both
Pipe centerlines and DN50, Type/Level/System, elbow `Part Type = Elbow`,
bounding box, connector origin/direction/profile/size signature, two reciprocal
Pipe↔elbow physical edges, two outer open ends and package-bound provenance.

This is deliberately narrow evidence only. It does not certify a general
connected network, water/PCCC, HVAC or Electrical/ELV pilot, nor Duct, Conduit,
Cable Tray, tee/transition/union/tap, slope, insulation, hosted/nested
equipment, circuit/panel behavior, system calculation, or the other blocked
layers. Each remains a separate runtime gate and must not be inferred from this
PASS.
