"""Static regression for the 60-minute Pipe/Schedule demo extensions."""
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
WRITE = (ROOT / "MCP" / "Commands" / "MepWriteCommands.cs").read_text(encoding="utf-8")
SAFETY = (ROOT / "MCP" / "Commands" / "MepSafety.cs").read_text(encoding="utf-8")
SCHEDULE = (ROOT / "MCP" / "Commands" / "DocumentationScheduleSupport.cs").read_text(encoding="utf-8")
SHEET = (ROOT / "MCP" / "Commands" / "DocumentationSheetSupport.cs").read_text(encoding="utf-8")
CATALOG = (ROOT / "MCP" / "Commands" / "BimV2Commands.cs").read_text(encoding="utf-8")
MEP_SCHEMA = (ROOT / "MCP-Server" / "src" / "tools" / "mep-tools.ts").read_text(encoding="utf-8")
BIM_SCHEMA = (ROOT / "MCP-Server" / "src" / "tools" / "bim-tools.ts").read_text(encoding="utf-8")

for marker in [
    '"diameter_mm"', '"demo_tag"', '"inline_accessory"',
    '"created_curve_ids"', '"created_fitting_ids"', '"created_accessory_ids"',
    "CreateInlinePipeAccessory", "PipeAccessoryConnectors", "SetRouteDiameter",
    "exactly two round Piping End connectors", "offset_mm must lie strictly inside",
]:
    assert marker in WRITE or marker in MEP_SCHEMA or marker in BIM_SCHEMA, marker

assert 'args["inline_accessory"]' in SAFETY, "valve type must participate in preview target fingerprint"
assert 'args.Value<long?>("system_type_id")' in SAFETY and 'args.Value<long?>("level_id")' in SAFETY
assert "DocumentRevisionTracker.Revision" in SAFETY, "stale preview guard must remain active"

for marker in [
    "ViewSchedule.CreateSchedule", "GetSchedulableFields", "ScheduleFilterType.Equal",
    "ALL_MODEL_INSTANCE_COMMENTS", "comments.IsHidden = true", "GetFilters",
    "Schedule name already exists or is duplicated", "Schedule fields must be unique",
    '"system_abbreviation"', '"area"', "ScheduleSortGroupField", "ScheduleFieldDisplayType.Totals",
    '"learner_requirements_confirmed"', '"sort_by"', '"total_fields"', "CanTotal()",
    "requires an explicit sort_by array", "requires an explicit total_fields array",
]:
    assert marker in SCHEDULE, marker

assert '"created_schedule_ids"' in WRITE
assert '"mep_family_symbols"' in CATALOG
assert "scheduleSchema" in BIM_SCHEMA and 'schedules: { type: "array", items: scheduleSchema }' in BIM_SCHEMA
assert "schedule_discovery_categories" in BIM_SCHEMA
assert "learner_requirements_confirmed" in BIM_SCHEMA and 'required: ["name", "category", "fields", "sort_by", "total_fields", "learner_requirements_confirmed", "filter"]' in BIM_SCHEMA
assert "schedule_field_catalog" in CATALOG and "learner_workflow" in CATALOG

for marker in [
    "DiscoverTitleBlocks", '"sheet_width_mm"', '"sheet_height_mm"',
    '"learner_title_block_confirmed"', '"learner_layout_confirmed"',
    '"fit_to_title_block"', '"fit_element_ids"', '"allowed_scales"',
    "CropToElements", "Viewport.Create", "GetBoxOutline", "GetLabelOutline",
    '"selected_scale"', '"fits_usable_region"', '"viewport_bounds_mm"', '"usable_region_mm"',
    "The view does not fit the selected title block at any allowed scale",
]:
    assert marker in SHEET, marker

assert "discover_title_blocks" in BIM_SCHEMA and "sheetPlacementSchema" in BIM_SCHEMA and "sheetSchema" in BIM_SCHEMA
assert "DocumentationSheetSupport.ValidatePlans" in CATALOG
assert "DocumentationSheetSupport.PlaceView" in WRITE and '"placement_read_back"' in WRITE

print("PASS demo workflow static contract: learner-confirmed schedules/title blocks and crop-scale viewport auto-fit")
