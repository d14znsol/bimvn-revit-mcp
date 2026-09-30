from pathlib import Path

root = Path(__file__).resolve().parents[1]
registry = (root / "MCP-Server/src/tools/index.ts").read_text(encoding="utf-8")
schema = (root / "MCP-Server/src/tools/family-tools.ts").read_text(encoding="utf-8")
commands = (root / "MCP/Commands/FamilyCommands.cs").read_text(encoding="utf-8")
routing_commands = (root / "MCP/Commands/FamilyRoutingProbeCommands.cs").read_text(encoding="utf-8")
break_into_commands = (root / "MCP/Commands/FamilyBreakIntoUiCommands.cs").read_text(encoding="utf-8")
hosting_commands = (root / "MCP/Commands/FamilyHostingUiCommands.cs").read_text(encoding="utf-8")
shared_nested_commands = (root / "MCP/Commands/FamilySharedNestedProbeCommands.cs").read_text(encoding="utf-8")
library_benchmark_commands = (root / "MCP/Commands/FamilyLibraryBenchmarkCommands.cs").read_text(encoding="utf-8")
contracts = (root / "contracts/McpContracts.cs").read_text(encoding="utf-8")

for name in ["family_inspect", "family_axial_fan_preview", "family_axial_fan_apply", "family_load_place_preview", "family_load_place_apply", "family_shared_nested_probe_preview", "family_library_benchmark_preview", "family_routing_probe_preview", "family_break_into_ui_preflight", "family_break_into_ui_verify", "family_hosting_ui_preflight", "family_hosting_ui_verify"]:
    assert name in schema, f"missing Family tool schema: {name}"
    assert name in commands or name in routing_commands or name in break_into_commands or name in hosting_commands or name in shared_nested_commands or name in library_benchmark_commands, f"missing Revit command: {name}"
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
for marker in ["copied_project_confirmed", "RoutingPreferenceManager", "AddRule", "NewElbowFitting", "NewTeeFitting", "NewCrossFitting", "NewTransitionFitting", "NewUnionFitting", "requested Family Type from Routing Preferences", "TransactionGroup", "rollback_state_read_back", "break_into_or_valve_breaks_into_ui_placement"]:
    assert marker in routing_commands, f"missing Family routing-probe safety/verification: {marker}"
for marker in ["family_break_into_ui_preflight", "family_break_into_ui_verify", "native_break_into_placement_by_api", "preflight_id", "post_controlled_ui_read_back", "native_ui_command_not_observable_by_api", "segment_ids must contain exactly two", "system_calculation_and_pressure_loss"]:
    assert marker in break_into_commands, f"missing Break Into UI safety/verification: {marker}"
for marker in ["family_hosting_ui_preflight", "family_hosting_ui_verify", "FamilyPlacementType.OneLevelBasedHosted", "FamilyPlacementType.WorkPlaneBased", "FamilyPlacementType.TwoLevelsBased", "AllowedPlacementTypes", "face_based_work_plane", "HostFace on the exact preflighted", "copied_project_confirmed", "require_void_cut", "InstanceVoidCutUtils.GetElementsBeingCut", "FAMILY_BASE_LEVEL_PARAM", "FAMILY_TOP_LEVEL_PARAM", "post_controlled_ui_read_back", "rehost_history"]:
    assert marker in hosting_commands, f"missing hosted Family UI safety/verification: {marker}"
assert "Save(" not in hosting_commands, "Hosted Family UI verification must not save a Project"
assert "SyncWithCentral" not in hosting_commands, "Hosted Family UI verification must not sync a Project"
for marker in ["family_shared_nested_probe_preview", "copied_project_confirmed", "SharedParameterElement.Lookup", "SuperComponent", "value_propagation", "shared_guid_identity_without_parent_to_child_value_propagation", "all_required_checks_passed", "CreateScheduleFieldSnapshot", "ViewSchedule.CreateSchedule", "dynamic_label_ui_and_tag_rfa_required", "TransactionGroup", "group.RollBack"]:
    assert marker in shared_nested_commands, f"missing Shared nested Project probe guard/evidence: {marker}"
assert "Save(" not in shared_nested_commands, "Shared nested Project probe must not save a Project"
assert "SyncWithCentral" not in shared_nested_commands, "Shared nested Project probe must not sync a Project"
for marker in ["family_library_benchmark_preview", "equivalent_function_confirmed", "isolated_origin_confirmed", "min, nominal and max", "FamilyPlacementType.OneLevelBased", "OpenDocumentFile", "TransactionGroup", "group.RollBack", "LoadFamily", "project.Regenerate", "GenericForm bounds", "Matching category, placement and flex values do not mathematically prove", "RejectBenchmarkFamilyLoadOptions"]:
    assert marker in library_benchmark_commands, f"missing nested library benchmark safety/evidence: {marker}"
assert "Save(" not in library_benchmark_commands, "Nested library benchmark must not save a Family or Project"
assert "SyncWithCentral" not in library_benchmark_commands, "Nested library benchmark must not sync a Project"
for marker in ["GetAllSizeTableNames", '["lookup_tables"]', '["lookup_table_count"]']:
    assert marker in commands, f"missing reopened lookup-table inspection evidence: {marker}"
print("PASS Family static contract: 12 tools, typed guards, staging, rollback, nested benchmark, routing and controlled UI read-back")
