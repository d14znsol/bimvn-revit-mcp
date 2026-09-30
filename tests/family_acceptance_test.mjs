import assert from "node:assert/strict";
import { assessFamilyAcceptance, familyAcceptanceFixtures } from "../MCP-Server/build/family-acceptance.js";

const empty = assessFamilyAcceptance({});
assert.equal(empty.evidence_validation.phase_complete, false);
assert.equal(empty.evidence_validation.revit_2023.pass, false);
assert.ok(empty.evidence_validation.revit_2023.missing.length > 0);

const completeEvidence = familyAcceptanceFixtures.flatMap((fixture) => ["2023", "2025"].flatMap((revitVersion) => fixture.required_gates.map((gate) => ({
  fixture_id: fixture.fixture_id,
  revit_version: revitVersion,
  gate,
  outcome: "pass",
  record_id: `${fixture.fixture_id}-${revitVersion}-${gate}`,
  completed_at_utc: revitVersion === "2023" ? "2026-09-25T01:00:00.000Z" : "2026-09-26T01:00:00.000Z",
  ...(gate === "family_rfa_reopen" ? { rfa_sha256: "a".repeat(64) } : {}),
}))));
const complete = assessFamilyAcceptance({ evidence: completeEvidence });
assert.equal(complete.evidence_validation.revit_2023.pass, true);
assert.equal(complete.evidence_validation.revit_2025.pass, true);
assert.equal(complete.evidence_validation.revit_2025_unlocked, true);
assert.equal(complete.evidence_validation.version_sequence_valid, true);
assert.equal(complete.evidence_validation.phase_complete, true);

const missingRfaHash = completeEvidence.find((record) => record.gate === "family_rfa_reopen");
assert.ok(missingRfaHash);
const { rfa_sha256: _hash, ...withoutHash } = missingRfaHash;
assert.throws(() => assessFamilyAcceptance({ evidence: [withoutHash] }), /rfa_sha256 is required/);

const only2025 = completeEvidence.filter((record) => record.revit_version === "2025");
const locked = assessFamilyAcceptance({ evidence: only2025 });
assert.equal(locked.evidence_validation.revit_2025_unlocked, false);
assert.equal(locked.evidence_validation.phase_complete, false);

assert.throws(() => assessFamilyAcceptance({ evidence: [completeEvidence[0], completeEvidence[0]] }), /duplicate result/);
assert.throws(() => assessFamilyAcceptance({ evidence: [{ ...completeEvidence[0], gate: "invented_gate" }] }), /not required/);
console.log(`PASS Family acceptance matrix: ${familyAcceptanceFixtures.length} fixtures, Revit 2023 then 2025 gate coverage`);
