from pathlib import Path

root = Path(__file__).resolve().parents[1]
registry = (root / "MCP-Server/src/tools/index.ts").read_text(encoding="utf-8")
schema = (root / "MCP-Server/src/tools/family-tools.ts").read_text(encoding="utf-8")
commands = (root / "MCP/Commands/FamilyCommands.cs").read_text(encoding="utf-8")
contracts = (root / "contracts/McpContracts.cs").read_text(encoding="utf-8")

for name in ["family_inspect", "family_axial_fan_preview", "family_axial_fan_apply", "family_load_place_preview", "family_load_place_apply"]:
    assert name in schema, f"missing Family tool schema: {name}"
    assert name in commands, f"missing Revit command: {name}"
assert "registerFamilyTools" in registry
for required in ["approved_demo_directory", "family_name", "type_name", "diameter_mm", "length_mm", "blade_count", "type_code", "connector_mode"]:
    assert required in schema, f"missing required Family input: {required}"
resolver = (root / "MCP/Commands/FamilyTemplateResolver.cs").read_text(encoding="utf-8")
for mapping in ["mechanical_equipment", "air_terminal", "duct_accessory", "pipe_accessory", "plumbing_fixture", "sprinkler", "electrical_equipment", "lighting_fixture", "cable_tray_fitting"]:
    assert mapping in resolver, f"missing automatic Family template mapping: {mapping}"
assert 'required: ["approved_demo_directory"' in schema, "template_path must not be required for learner axial preview"
assert 'required: ["template_path"' not in schema, "template_path unexpectedly remains required"
for guard in ["RVT \" + version", "CommonApplicationData", "another Revit year", "autodesk_exact_year_generic_metric_fallback", "EnsureTargetCategory", "OST_GenericModel", "explicit_override"]:
    assert guard in resolver, f"missing template resolver guard: {guard}"
for marker in ["category_change_required", "automatic_before_geometry", "changed_from_generic"]:
    assert marker in commands or marker in resolver, f"missing Generic Model category-change evidence: {marker}"
for marker in ["PathBlocked", "DocumentTypeInvalid", "TemplateInvalid", "FileConflict", "VerificationFailed"]:
    assert marker in contracts, f"missing typed error: {marker}"
for guard in ["TransactionGroup", "RequireInside", "File.Move", "File.Delete", "ResourceFingerprint", "round_hvac", "blade_count"]:
    assert guard in commands, f"missing Family safety mechanism: {guard}"
assert "Save(" not in commands, "Family implementation must not save a Project"
assert "SyncWithCentral" not in commands, "Family implementation must not sync a Project"
print("PASS Family static contract: 5 tools, typed guards, staging, rollback, read-back")
