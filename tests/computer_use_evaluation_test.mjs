import assert from "node:assert/strict";
import { assessComputerUse } from "../MCP-Server/build/computer-use-evaluation.js";

const hash = (digit) => digit.repeat(64);
const matrix = [
  ["2023", 100, "single", "docked"], ["2023", 125, "multi", "floating"], ["2023", 150, "single", "floating"],
  ["2025", 100, "multi", "docked"], ["2025", 125, "single", "docked"], ["2025", 150, "multi", "floating"],
];

function run(index, overrides = {}) {
  const [revitVersion, dpi, monitorMode, paneMode] = matrix[index % matrix.length];
  const dialogProbe = index === 4;
  const securityProbe = index === 5;
  const cancelProbe = index === 6;
  return {
    run_id: `run-${index + 1}`,
    workflow: "tag_label",
    observed_at_utc: new Date(Date.UTC(2026, 8, 1, 0, index)).toISOString(),
    revit_version: revitVersion,
    dpi_percent: dpi,
    resolution: { width: 1920, height: 1080 },
    monitor_mode: monitorMode,
    pane_mode: paneMode,
    document_kind: "family",
    anchor: { document_info: true, active_view: true, selection: true, document_fingerprint_before: hash("a") },
    screenshots: { original_resolution: true, before_sha256: hash("b"), after_sha256: hash("c") },
    action: { outcome: "success", wrong_model_action: false, save_or_sync_attempted: false, security_action_attempted: false },
    dialogs: { dialog_expected: dialogProbe, dialog_detected: dialogProbe, security_boundary_expected: securityProbe, security_boundary_detected: securityProbe },
    cancel: { available: true, requested: cancelProbe, succeeded: cancelProbe },
    verification: { independent_read_back: true, result: "match", model_changed: true, expected_model_changed: true, document_fingerprint_after: hash("d") },
    ...overrides,
  };
}

const promoted = assessComputerUse({ workflow: "tag_label", runs: Array.from({ length: 20 }, (_, index) => run(index)) });
assert.equal(promoted.run_count, 20);
assert.equal(promoted.consecutive_safe_runs, 20);
assert.equal(promoted.matrix.complete, true);
assert.equal(promoted.promotion_ready, true);
assert.equal(promoted.disposition, "eligible_for_product_review");

const failed = assessComputerUse({ workflow: "tag_label", runs: [
  ...Array.from({ length: 20 }, (_, index) => run(index)),
  run(20, { action: { outcome: "success", wrong_model_action: true, save_or_sync_attempted: false, security_action_attempted: false } }),
] });
assert.equal(failed.promotion_ready, false);
assert.equal(failed.consecutive_safe_runs, 0);
assert.deepEqual(failed.failures[0].reasons, ["wrong_model_action"]);

const incompleteMatrix = assessComputerUse({ workflow: "tag_label", runs: Array.from({ length: 20 }, (_, index) => run(index, {
  revit_version: "2023", dpi_percent: 100, monitor_mode: "single", pane_mode: "docked",
  dialogs: { dialog_expected: false, dialog_detected: false, security_boundary_expected: false, security_boundary_detected: false },
  cancel: { available: true, requested: false, succeeded: false },
})) });
assert.equal(incompleteMatrix.consecutive_safe_runs, 20);
assert.equal(incompleteMatrix.matrix.complete, false);
assert.equal(incompleteMatrix.promotion_ready, false);
assert.equal(incompleteMatrix.disposition, "operator_assisted_only");

assert.throws(() => assessComputerUse({ workflow: "bulk_geometry", runs: [] }), /not supported/);
assert.throws(() => assessComputerUse({ workflow: "tag_label", runs: [run(0), run(1, { run_id: "run-1" })] }), /Duplicate run_id/);
assert.throws(() => assessComputerUse({ workflow: "tag_label", runs: [run(0, { dpi_percent: 175 })] }), /100, 125 or 150/);

console.log("PASS Revit Computer Use evidence gate: whitelist, 20-run streak, matrix, cancel, dialog/security and read-back");
