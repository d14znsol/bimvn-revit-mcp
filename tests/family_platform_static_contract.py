from pathlib import Path

root = Path(__file__).resolve().parents[1]
schema = (root / "MCP-Server/src/tools/family-platform-tools.ts").read_text(encoding="utf-8")
registry = (root / "MCP/Core/CommandRegistry.cs").read_text(encoding="utf-8")
commands = (root / "MCP/Commands/FamilyPlatformCommands.cs").read_text(encoding="utf-8")
quality = (root / "MCP/Commands/FamilyQualityPlatform.cs").read_text(encoding="utf-8")
evidence = (root / "MCP-Server/src/family-evidence.ts").read_text(encoding="utf-8")
blueprint = (root / "MCP-Server/src/family-blueprint.ts").read_text(encoding="utf-8")
catalog = (root / "MCP/Commands/FamilyCatalogGeometryBuilder.cs").read_text(encoding="utf-8")
compiler = (root / "MCP/Commands/FamilyBlueprintCompiler.cs").read_text(encoding="utf-8")
templates = (root / "MCP/Commands/FamilyTemplateResolver.cs").read_text(encoding="utf-8")
family_commands = (root / "MCP/Commands/FamilyCommands.cs").read_text(encoding="utf-8")
acceptance = (root / "MCP-Server/src/family-acceptance.ts").read_text(encoding="utf-8")
node_index = (root / "MCP-Server/src/index.ts").read_text(encoding="utf-8")
node_registry = (root / "MCP-Server/src/tools/index.ts").read_text(encoding="utf-8")
source_to_revit = (root / "MCP-Server/src/source-to-revit.ts").read_text(encoding="utf-8")
mep_write = (root / "MCP/Commands/MepWriteCommands.cs").read_text(encoding="utf-8")
mep_safety = (root / "MCP/Commands/MepSafety.cs").read_text(encoding="utf-8")

tools = [
    "family_source_inspect", "family_artifact_inspect", "family_spec_preview", "family_build_preview", "family_build_apply",
    "cad_geometry_inspect", "cad_to_revit_preview", "cad_to_revit_apply",
]
for tool in tools:
    assert tool in schema, f"missing Node schema for {tool}"
    assert tool in registry, f"missing Revit registry entry for {tool}"
    assert tool in commands, f"missing Revit command for {tool}"

assert "family_acceptance_matrix" in schema, "missing Node-local Family acceptance matrix tool schema"
assert "revit_computer_use_assess" in schema, "missing Node-local Computer Use assessment tool schema"
assert "source_to_revit_proposal" in schema, "missing Node-local Source-to-Revit proposal tool schema"
for marker in ["FAMILY_ACCEPTANCE_MATRIX_VERSION", "familyAcceptanceFixtures", "hvac_equipment", "dynamic_tag", "lod350_coordination", "family_rfa_reopen", "independent_min_nominal_max_flex", "revit_2025_unlocked", "version_sequence_valid", "phase_complete", "rfa_sha256 is required", "duplicate result"]:
    assert marker in acceptance, f"missing Family acceptance matrix/evidence gate: {marker}"
assert "assessFamilyAcceptance" in node_index and "family_acceptance_matrix" in node_index, "Node MCP must handle Family acceptance locally without a Revit write"
assert "assessComputerUse" in node_index and "revit_computer_use_assess" in node_index, "Node MCP must handle Computer Use evidence locally without driving Revit"
assert "previewSourceToRevit" in node_index and "source_to_revit_proposal" in node_index, "Node MCP must map SourceEvidence locally before any Revit preview"
assert "family_acceptance_matrix" in node_registry, "live Revit capability discovery must preserve the Node-local acceptance tool"
assert "revit_computer_use_assess" in node_registry, "live Revit capability discovery must preserve the Node-local Computer Use tool"
assert "source_to_revit_proposal" in node_registry, "live Revit capability discovery must preserve the Node-local Source-to-Revit proposal tool"
for marker in ["__dscons_source_proposal", "ready_for_fresh_revit_preview", "MepOperation.ValidatePreview", "revit_transactiongroup_rollback", "MepOperation.Apply", "post_commit_read_back"]:
    assert marker in node_index or marker in commands or marker in (root / "MCP/Commands/MepWriteCommands.cs").read_text(encoding="utf-8"), f"missing CAD route preview/apply safety marker: {marker}"
for marker in ["mep_project_reconstruction", "project_reconstruction_plan", "MEPF", "architecture_structure_role", "mep_place_equipment_batch", "mep_connect_created_batch", "mep_apply_created_envelopes_batch", "level_based_non_hosted", "Sloped source routes", "trusted_adapter_parsed", "path_sha256", "connection_requests", "insulation_requests", "lining_requests", "slope_plans", "electrical_circuit_requests", "penetration_sleeve_firestop", "independent_verification"]:
    assert marker in source_to_revit or marker in evidence or marker in mep_write, f"missing MEPF Source-to-Project contract: {marker}"
for marker in ["extractPdfVectorPaths", "top_left_page_points_x_right_y_down", "MAX_PDF_VECTOR_PATHS_PER_PAGE", "semantic_boundary"]:
    assert marker in evidence, f"missing bounded PDF vector extraction contract: {marker}"
for marker in ["use_revit_trusted_adapter", "InspectDwgInDisposableFamily", "temporary_document_rolled_back", "temporary_document_saved", "active_project_touched", "transaction.RollBack()", "temporary.Close(false)"]:
    assert marker in schema or marker in node_index or marker in commands, f"missing trusted disposable Revit DWG adapter boundary: {marker}"
assert "mep_place_equipment_batch" in mep_write and "mep_place_equipment_batch" in mep_safety, "MEPF equipment batch must participate in Change Set execution and target fingerprinting"
assert "mep_connect_created_batch" in mep_write and "mep_connect_created_batch" in mep_safety, "Logical-key connector batch must remain inside atomic Change Set safety"
assert "mep_apply_created_envelopes_batch" in mep_write and "mep_apply_created_envelopes_batch" in mep_safety, "Created-route insulation/lining batch must remain inside atomic Change Set safety"
for marker in ["width_mm", "height_mm", "DiameterParameter", "WidthParameter", "HeightParameter"]:
    assert marker in mep_write, f"missing MEP route size application/read-back contract: {marker}"

discontinued_markers = [
    "family" + "_tag_",
    "Family" + "TagCommands",
    "Tag" + "SeedInvalid",
    "pipe" + "_sys_dn",
]
for discontinued in discontinued_markers:
    assert discontinued not in schema
    assert discontinued not in registry
    assert discontinued not in commands
    assert discontinued not in quality
    assert discontinued not in evidence

assert not (root / "MCP-Server/src/tools" / ("family" + "-tag-tools.ts")).exists()
assert not (root / "MCP/Commands" / ("Family" + "TagCommands.cs")).exists()
assert not (root / "docs" / ("FAMILY-" + "TAG-PLATFORM.md")).exists()
assert not (root / "docs" / ("TAG-" + "RUNTIME-TEST.md")).exists()
assert not any((root / "docs/tag-seeds").glob("**/*"))

for marker in ["FamilyPlatformStore", "Sha256", "RequireInside", "LOD_300", "uncertain", "human_confirmed", "PreviewExpired", "PreviewInvalid", "TransactionGroup"]:
    assert marker in commands or marker in quality or marker in schema, f"missing safety/evidence marker: {marker}"
assert "allow-listed" in commands
assert "SyncWithCentral" not in commands
assert "Save(" not in commands
for marker in ["FamilyAdapterRegistry", "FamilyQualitySpec", "FamilyQualityValidator", "FamilyQualityPipeline", "DsconsParameterRegistry", "SharedParametersFilename", "connector_probe", "cable_tray_fitting", "sprinkler"]:
    assert marker in quality, f"missing Family quality platform component: {marker}"
assert "family_build" in commands and "FamilyBuilder.Apply" in commands
for marker in ["schema_version", "source_evidence_v2", "source_evidence_v3", "family_spec_v2", "family_build_artifact_v3", "recordFamilyBuildArtifact", "verifiedParameterInterfaces", "parameter_interfaces", "resolveNestedBlueprintArtifacts", "resolvePhotometricAssets", "ies_photometric_web", "MAX_IES_BYTES", "native_text", "rendered_page_ocr", "scale_anchors", "inspectImageSource", "inspectDxf", "trusted_adapter_required", "dxf_entity_whitelist_v1", "eng.traineddata.gz", "vie.traineddata.gz", "SourceUnreadable", "SourceEncrypted", "OcrUnavailable", "EvidenceConflict"]:
    assert marker in evidence, f"missing SourceEvidence v3/PDF-image-CAD marker: {marker}"
for marker in ["\"A\"", "\"A1\"", "\"DN1\"", "\"DN2\"", "\"H3\"", "\"T\"", "AddPumpCatalogParameters", "parametric_envelope_pilot"]:
    assert marker in evidence or marker in quality or marker in catalog, f"missing Pump evidence/pilot marker: {marker}"
for marker in ["FAMILY_BLUEPRINT_SCHEMA_VERSION", "familyCategories", "familyTemplateBehaviors", "familyPartTypes", "familyParameterGroups", "familyPrimitiveKinds", "familyReferenceTypes", "familyCoordinationZonePurposes", "familyCoordinationZoneSubcategories", "familyLightShapeStyles", "familyLightDistributionStyles", "familyLightColorPresets", "familyDetailLevelRepresentationPolicies", "buildable_by_api", "ui_fallback_required", "created_from_blueprint", "source_field_to_critical_geometry_and_connector_traceability", "required_source_fields_to_critical_parameter_usage_v1", "direct_untraced_critical_parameter_keys", "family_part_type_and_behavior_settings", "connector_flow_loss_joint_and_engagement_metadata", "bounded_two_port_break_into_accessory_routing_contract", "advanced_duct_pipe_connector_flow_factor_and_slope_metadata", "electrical_connector_load_and_circuit_parameter_association", "family_material_graphics_appearance_and_form_assignment", "family_material_physical_and_thermal_assets", "lighting_fixture_shape_distribution_photometrics_and_ies", "parameterized_non_physical_coordination_zones", "reference_plane_line_dimension_alignment_graph", "model_line_endpoint_reference_plane_bindings", "stable_named_reference_origin_and_reference_plane_graphics", "angular_radial_equal_dimension_multibranch_graph", "reference_line_driven_angular_sweep_rotation", "parameterized_blend_rectangle_circle_profiles", "parameterized_fitting_transition_geometry", "parameterized_linear_part_and_nested_array", "nested_blueprint_artifact_placement_and_instance_parameter_linking", "placement_aware_nested_blueprint_on_parent_solid_face", "interchangeable_nested_family_type_parameter", "bounded_part_and_nested_component_mirror", "void_extrusion_revolution_sweep_blend_swept_blend_cut", "symbolic_and_model_line_polyline_with_yesno_visibility_association", "coarse_medium_2d__fine_3d_representation_sets"]:
    assert marker in blueprint, f"missing Family Blueprint v3 contract: {marker}"
for marker in ["familyParameterDataTypes", "family_type", "family_category", "type_options", "family_type_parameter_key", "placement_mode", "host_face_point", "host_face_line", "reference_direction", "placement_line_start_mm", "parameter_order", "type_catalog", "compact_rfa", "preview_view", "performance_budget", "max_rfa_bytes", "max_nested_depth", "max_complexity_score", "max_light_sources", "max_presentation_subcategories", "presentation_subcategories", "light_source", "shape_style", "distribution_style", "photometric_web", "initial_intensity", "initial_color", "loss_factor", "coordination_zones", "non_physical_coordination_zone", "front_back", "only_when_cut", "model_lines", "endpoint_bindings", "reference_plane_key", "visibility_parameter", "profile_loop_index", "leader_point_mm", "arc_center_mm", "arc_radius_mm", "equality", "reference_plane_subcategories", "reference_type", "defines_origin", "subcategory_key", "projection_line_weight", "line_pattern_name", "tangent_intersection_mm", "start_tangent", "turn_direction", "radius_parameter", "sweep_angle_degrees", "path_start", "path_end", "axis_direction", "flow_parameter", "flow_factor_parameter", "allow_slope_adjustments", "electrical_load_classifications", "number_of_poles", "load_classification", "voltage_parameter", "apparent_load_parameter", "power_factor_state"]:
    assert marker in blueprint and marker in schema, f"missing expanded Family Blueprint publication/visibility contract: {marker}"
for marker in ["formula_dependencies", "formula_dependency_graph", "revit_type_formula_dependency_graph_v1", "formula dependency cycle", "topologicalFormulaApplicationOrder"]:
    assert marker in blueprint, f"missing Formula lifecycle/dependency validation contract: {marker}"
assert "formula_dependencies" in schema, "missing Formula dependency schema"
for marker in ["catalog_revision", "type_catalog_revisions"]:
    assert marker in evidence and marker in schema, f"missing catalog revision input schema: {marker}"
for marker in ["catalog_revision_identity_v2", "catalog_identity_sha256", "revision_provenance", "shared catalog_revision fallback is blocked"]:
    assert marker in evidence, f"missing catalog revision identity/provenance contract: {marker}"
for marker in ["tag_background", "tag_labels", "semantic_field", "shared_parameter_guid", "value_format", "rounding_mm", "show_plus"]:
    assert marker in blueprint and marker in schema, f"missing Tag Label Blueprint schema/validation contract: {marker}"
for marker in ["tag_label_field_and_format_preflight", "tag_label_editor_requires_controlled_ui", "tag_ui_plan", "tag_label_editor"]:
    assert marker in blueprint, f"missing controlled Tag Label UI handoff contract: {marker}"
for marker in ["purge_unused", "template_residue", "max_passes", "skip_with_evidence"]:
    assert marker in blueprint and marker in schema, f"missing version-aware purge contract: {marker}"
assert "version_aware_template_residue_purge_revit_2024_plus" in blueprint, "missing purge capability marker"
assert "detail_item_detail_line_endpoint_reference_plane_bindings" in blueprint, "missing Detail Item Reference-Plane-bound Detail Line capability marker"
for marker in ["endpoint_bindings", "reference_plane_key"]:
    assert marker in schema, f"missing Detail Line endpoint-binding schema marker: {marker}"
for marker in ["maintenance_clearance", "DSCons Coordination Maintenance"]:
    assert marker in blueprint, f"missing coordination-zone semantic contract: {marker}"
for marker in ["complexity_assessment", "resolved_complexity", "declarative_complexity_and_rfa_size_budget"]:
    assert marker in blueprint, f"missing derived Family Blueprint complexity contract: {marker}"
for marker in ["orthogonal_xyz_extrusion_axes", "arbitrary_direction_extrusion_frame_and_connector_faces", "parameterized_orthogonal_tee_geometry_and_three_connector_placement", "parameterized_angled_wye_lateral_cross_geometry", "parametric_flat_oval_profile_with_orientation_guard", "normalized_conduit_and_cable_tray_fitting_behaviors", "parameterized_pathway_transition_union_geometry", "bounded_literal_pathway_offset_sweep", "major-axis flex minimum", "multi-port link topology"]:
    assert marker in blueprint, f"missing Family Blueprint capability/fail-closed contract: {marker}"
for marker in ["parameterized_revolution_rectangle_circle_ring_oval_profiles", "parameterized_sweep_profile_ui_preflight", "parameterized_sweep_profile_family_editor_ui", "parameterized_sweep_profile_ui_plan", "outer-diameter flex minimum", "approved flex range", "first path segment to follow +X", "both profile constraint planes are deterministic", "detail_level_representations", "coarse_medium_2d__fine_3d"]:
    assert marker in blueprint, f"missing generalized parameterized profile contract: {marker}"
for marker in ["Parameterized Sweep ProfileSketch is a controlled Family Editor UI fallback", "SketchEditScope is permitted only for Project documents", "Complete and reopen/inspect the profile dimensions through the dedicated Family Editor workflow"]:
    assert marker in compiler, "Parameterized Sweep ProfileSketch must remain explicitly fail-closed until its controlled Family Editor workflow is verified: " + marker
for marker in ["Family Blueprint requires controlled UI or an unavailable compiler capability before build preview", "before template resolution or any"]:
    assert marker in commands, "A Blueprint UI fallback must be reported as Unsupported before Family/template work: " + marker
assert "LabelledDimensionSnapshot" in compiler and "label_dimensions_mm" in compiler, "Sweep flex failures must expose the labelled-dimension read-back needed to distinguish a constraint defect from a stale solid bound."
assert "member_nested_component_keys" in blueprint and "member_nested_component_keys" in schema
assert "interchangeable_nested_family_type_parameter" in blueprint
assert "family_flip_controls" in blueprint
assert "double_horizontal_arrow" in schema
assert 'shared_parameter_map_mode: { enum: ["identity_only"] }' in schema, "Shared nested parameter mappings must publish their identity-only limitation."
for marker in ["family_spec_v3", "dscons_family_record_v1", "DSCONS_FAMILY_RECORD_DIRECTORY", "requires_capability_or_ui_fallback"]:
    assert marker in evidence, f"missing persisted Family v3 record behavior: {marker}"
for marker in ["FamilyBlueprintSpec", "FamilyBlueprintCompiler", "ApplyFamilyBehavior", "RequireSelectionBehavior", "VerifySourceTraceability", "immutable_family_spec_source_field_binding_read_back", "field_level_source_provenance_v1", "TypeConfirmedFields", "type_specific_required", "shared source fallback is blocked for multi-Type Family", "FieldProvenance", "Source-document provenance locator", "source_evidence_sha256", "VerifyRootFamilyPlacement", "VerifyReopenedRootFamilyPlacement", "ExpectedRootFamilyPlacementType", "OneLevelBasedHosted", "TwoLevelsBased", "template_hosting", "FAMILY_CONTENT_PART_TYPE", "FAMILY_SHARED", "FAMILY_ROUNDCONNECTOR_DIMENSIONTYPE", "NewRevolution", "NewSweep", "NewBlend", "NewSweptBlend", "NewCurveLoopsProfile", "NewCombinableElementArray", "CombineElements", "CreateCoordinationZones", "CoordinationZoneSnapshot", "VerifyReopenedCoordinationZones", "SetCoordinationZoneRole", "non_physical_coordination_geometry", "quantity_exclusion_certified", "LinearArray.Create", "ArraySnapshot", "RequireNestedArtifacts", "CreateNestedComponents", "RejectNestedOverwriteLoadOptions", "AssociateElementParameterToFamilyParameter", "VerifyNestedParameterInterface", "NestedParameterInterfaceSnapshot", "IsSharedNestedInstanceParameter", "AssociateVisibility", "IS_VISIBLE_PARAM", "NestedSnapshot", "CanMirrorElements", "MirrorElements", "CreateMirrors", "Material.Create", "MATERIAL_ID_PARAM", "AppearanceAssetEditScope", "SurfaceForegroundPatternId", "ApplyPhysicalAsset", "ApplyThermalAsset", "StructuralAsset", "ThermalAsset", "PropertySetElement.Create", "StructuralAssetId", "ThermalAssetId", "RequirePhotometricAssets", "PhotometricDependencySnapshot", "ApplyLightSource", "LightFamily.GetLightFamily", "SetLightShapeStyle", "SetLightDistributionStyle", "PhotometricWebLightDistribution", "InitialFluxIntensity", "AdvancedLossFactor", "VerifyReopenedLightSource", "FamilyLightingReadBack", "ExternalDefinitionCreationOptions", "SetDescription", "SharedParametersFilename", "FamilySizeTableManager", "ImportSizeTable", "size_lookup", "NewSymbolicCurve", "NewDetailCurve", "FilledRegion.Create", "NewModelCurve", "NewReferencePlane2", "ChangeToReferenceLine", "NewDimension", "NewRadialDimension", "NewAngularDimension", "AreSegmentsEqual", "NewAlignment", "ReferenceGraphPartKeys", "ModelLineEndpointBindingSnapshot", "VerifyModelLineEndpointOnReferencePlane", "VerifyReopenedModelLineEndpointBindings", "reference_plane_bound_model_line_endpoint_read_back", "plane_geometry", "reference_graph", "FamilyElementVisibilityType.ViewSpecific", "CreateDuctConnector", "CreatePipeConnector", "CreateElectricalConnector", "CreateConduitConnector", "CreateCableTrayConnector", "RBS_PIPE_FLOW_DIRECTION_PARAM", "RBS_DUCT_FLOW_PARAM", "RBS_PIPE_FLOW_PARAM", "RBS_FLOW_FACTOR_PARAM", "RBS_ADJUSTABLE_CONNECTOR", "CONNECTOR_JOINT_TYPE", "CONNECTOR_ENGAGEMENT_LENGTH", "CreateElectricalLoadClassifications", "ElectricalLoadClassification.Create", "RBS_ELEC_VOLTAGE", "RBS_ELEC_APPARENT_LOAD", "RBS_ELEC_NUMBER_OF_POLES", "RBS_ELEC_LOAD_CLASSIFICATION", "VerifyReopenedElectricalConnectors", "EvaluateComplexityBudget", "ManagedPresentationSubcategoryCount", "VerifyReopenedComplexity", "VerifyDetailLevelRepresentations", "VerifyReopenedDetailLevelRepresentations", "RequireDetailLevelRepresentationVisibility", "max_rfa_bytes", "max_presentation_subcategories", "presentation_subcategories", "import_instances", "aggregate_complexity_score", "approved_min_nominal_max_component_read_back", "PartReferencesParameter", "CoordinationZoneReferencesParameter", "ConnectorReferencesParameter", "SetFlexValue", "SnapshotsVary", "ExtrusionFrame", "AxisVector", "Plane.CreateByOriginAndBasis", "ResolvedProfileAt", "ResolvedProfileAtFrame", "RectangleAtFrame", "OvalAtFrame", "LabelOvalSketchProfile", "NormalizeFittingBehavior", "VerifyReopenedProfileShapes", "ViewForNormal", "ViewSection.CreateSection", "SketchPlane?.GetPlane", "useSketchPlaneOrientation", "ArcFrame", "LabelSweepPathRadius", "LabelParameterizedOffsetPath", "OffsetPathFrame", "DeclaredNominalOffsetFrame", "GetEndPointReference", "parameterized_offset_path_constraint_read_back", "reference_line_driven_angular_sweep_path_read_back", "VerifyAngularSweepPathSnapshot", "CenterPointReference", "ConnectorFaceNormal", "PathEndpointTangent", "SweepPathSnapshot", "TangentIntersection", "VerifyReopenedArcSweeps", "VerifyReopenedAngularSweeps", "VerifyReopenedTwoPortInlineFitting", "pipe_mechanical_coupling", "VerifyReopenedJunction", "VerifyReopenedMaterials", "VerifyReopenedReferenceFramework", "PrepareReferenceFrameworkTemplatePlanes", "DATUM_PLANE_DEFINES_ORIGIN", "CLINE_SUBCATEGORY", "OST_CLines", "SetLineWeight", "SetLinePatternId", "post_commit_reopen_read_back", "File.Exists(output)"]:
    assert marker in compiler, f"missing Blueprint v3 Revit compiler behavior: {marker}"
for marker in ["DescribeDimension", "family_label_read_back", "family_label_error", "unavailable_dimension_label_count"]:
    assert marker in family_commands, f"dimension label inspection must report unavailable native template dimensions: {marker}"
assert '["category_id"]' in family_commands, "Family reopen verification must expose the stable Revit category id, not only a localized name."
assert "requiredFields.Count > 0 && untraced.Count > 0" in compiler, "untraced direct critical parameters must be blocked only for source-bound Blueprints"
assert "DimensionStyleType.Diameter" in compiler and "SetElementId(\"round_connector_dimension\"" in compiler, "round connector dimension must retain a native diameter/radial DimensionType rather than an integer surrogate"
for marker in ["Committed preview Family contains duplicate declared parameter name", "Reopened Family contains duplicate declared parameter name", "declaredNames.Contains"]:
    assert marker in compiler, "Family reopen must ignore irrelevant duplicate Autodesk-template names but fail closed for a duplicate declared name: " + marker
for marker in ["Reopened Family contains duplicate declared presentation subcategory", "Autodesk templates can retain unrelated subcategories", "declaredNames.Contains"]:
    assert marker in compiler, "Presentation reopen verification must ignore unrelated duplicate template names but fail closed for a declared subcategory: " + marker
for marker in ["Reopened Family contains duplicate declared reference plane", "Native templates can have repeated unrelated plane labels", "if (expected.Count == 0) return"]:
    assert marker in compiler, "Reference-frame reopen verification must ignore unrelated duplicate template planes but fail closed for a declared plane: " + marker
for marker in ["A logical Data connector has no load/circuit properties", "requiresElectricalReadBack", "Reopened electrical connector has no load/circuit read-back"]:
    assert marker in compiler, "Logical electrical connectors must not be rejected for unimplemented load/circuit fields while declared load bindings remain fail-closed: " + marker
for marker in ["declaredMaterials.Count == 0", "materialBearingParts.Count == 0", "Do not index unrelated Autodesk-template materials"]:
    assert marker in compiler, "Family reopen must avoid indexing unrelated duplicate template materials while preserving declared-material verification: " + marker
assert "NewRadialDimension(view, arcReference, leaderPoint, radialType)" in compiler and "The selected DimensionType did not create a radial dimension" in compiler, "radial profile constraints must select and verify a native radial DimensionType"
for marker in ["profile_diameter_plane_alignment", "profile_diameter_center_width", "profile_diameter_center_height", "The plane-to-plane Diameter FamilyLabel did not read back after regeneration.", "The plane-to-plane Diameter label did not drive both circular profile arcs"]:
    assert marker in compiler, f"missing diagnostic/read-back for a rejected circular-profile diameter constraint: {marker}"
for marker in ["REVIT2023", "duct_connector_flow_parameter_revit2023_fail_closed", "CanElementParameterBeAssociated=false", "controlled Family Editor UI workflow"]:
    assert marker in compiler, f"missing Revit 2023 Duct preset-flow fail-closed guard: {marker}"
for marker in ["REVIT2019 || REVIT2020", "Electrical load-classification abbreviation authoring requires Revit 2021 or newer"]:
    assert marker in compiler, f"missing electrical load-classification version guard: {marker}"
for marker in ["ReorderParameters", "GetParameters", "VerifyReopenedParameterOrder", "SaveAsOptions", "Compact", "PreviewViewId", "IsViewIdValidForPreview", "NewControl", "ControlShape", "BuildTypeCatalog", "TypeCatalogBytes", "VerifyTypeCatalogContent", "ParseTypeCatalogCsvRow", "revit_documented_generic_type_catalog_v1", "mep_header_tokens_runtime_certification_required", "File.Exists(catalogOutput)", "SpecTypeId.AirFlow", "SpecTypeId.HvacPressure", "SpecTypeId.ElectricalPower", "SpecTypeId.ApparentPower", "IsShownInFrontBack", "IsShownInLeftRight", "IsShownInPlanRCPCut", "IsShownOnlyWhenCut"]:
    assert marker in compiler, f"missing parameter/catalog/control/publication compiler behavior: {marker}"
for marker in ["Reopened Family parameter Shared GUID mismatch", "Reopened Family Shared Parameter metadata mismatch", "is_shared", "shared_guid", "user_modifiable", "hide_when_no_value"]:
    assert marker in compiler, f"missing reopened Shared Parameter identity/metadata verification: {marker}"
for marker in ["VerifyCommittedParameterReadBack", "parameter_read_back", "shared_metadata_read_back", "unavailable_revit_api", "Committed preview Family parameter Shared GUID mismatch", "Committed preview Family Shared Parameter metadata mismatch"]:
    assert marker in compiler, f"missing rollback-preview Shared Parameter identity/metadata read-back: {marker}"
for marker in ["ExpectedParameterFormula", "formula_dependency_graph", "application_order", "Reopened Family parameter formula/lookup mismatch", "ApplyParameterFormulas", "CreateTypes(manager, spec", "manager.CurrentType = types.First();"]:
    assert marker in compiler, f"missing Family Formula lifecycle/reopen verification: {marker}"
assert compiler.index("var types = CreateTypes") < compiler.index("ApplyParameterFormulas"), "Family Types must exist before Formula/size_lookup application."
for marker in ["FamilyTypeDefinitions", "DeleteCurrentType", "VerifyExactFamilyTypeSet", "VerifyReopenedExactFamilyTypeSet", "exact_blueprint_family_type_set_v1", "template_residue_removed", "reopened_type_set_verification"]:
    assert marker in compiler, f"missing exact declared Family Type-set cleanup/reopen verification: {marker}"
for marker in ["declaredConnectorCount", "Connector declarations are optional", "correctly connector-free Family build"]:
    assert marker in compiler, f"missing connector-free Family count verification: {marker}"
for marker in ["CatalogRevisionProvenance", "catalog_revision_identity_v2", "CatalogIdentityHashWithRevisionProvenance", "revision_provenance", "Per-Type catalog revision provenance", "catalog_identity_sha256"]:
    assert marker in compiler, f"missing immutable per-Type catalog revision compiler boundary: {marker}"
for marker in ["curve_geometry", "changed segment geometry", "missing declared segment geometry"]:
    assert marker in compiler, f"missing reopened detail-level line geometry verification: {marker}"
for marker in ["CreateDetailLines", "DetailLineEndpointBindingSnapshot", "VerifyReopenedDetailLineEndpointBindings", "reference_plane_bound_detail_line_endpoint_read_back"]:
    assert marker in compiler, f"missing Detail Item endpoint-binding compiler behavior: {marker}"
for marker in ["CaptureElementIds", "ApplyPurgePolicy", "VerifyReopenedPurgePolicy", "GetUnusedElements", "Purge unused Autodesk template residue", "blueprint_created_elements_protected", "api_minimum_revit_version", "skipped_api_unavailable", "REVIT2024 || REVIT2025 || REVIT2026 || REVIT2027"]:
    assert marker in compiler, f"missing bounded Revit-version-aware purge behavior: {marker}"
for marker in ["AddFamilyTypeParameter", "GetFamilyTypeParameterValues", "ELEM_TYPE_PARAM", "ELEM_FAMILY_AND_TYPE_PARAM", "resolved_options", "TypeSelections", "AsElementId"]:
    assert marker in compiler, f"missing interchangeable nested Family Type behavior: {marker}"
for marker in ["member_nested_component_keys", "nestedComponent.Instance.Id"]:
    assert marker in compiler, f"missing nested linear-array compiler behavior: {marker}"
for marker in ["FamilyPlacementType", "ExpectedNestedPlacementType", "host_face_point", "host_face_line", "NewFamilyInstance(hostFace.Reference, insertion", "NewFamilyInstance(hostFace.Reference, line", "RequirePointOnFace", "RequireCurveOnFace", "HostFace", "LocationPoint", "LocationCurve", "HostedPlacementSnapshot", "VerifyReopenedNestedPlacements", "FindNestedInstanceParameter", "FindNestedSharedGuidParameter", "shared_guid_identity", "identity_only", "definition_identity_only", "value_propagation_verified", "instance.get_Parameter(guid)", "mapped_parameter_interfaces", "nested_instances"]:
    assert marker in compiler, f"missing placement-aware nested compiler behavior: {marker}"
for behavior_mapping in [
    '"level_based" => FamilyPlacementType.OneLevelBased',
    '"face_based" or "work_plane_based" => FamilyPlacementType.WorkPlaneBased',
    '"wall_based" or "ceiling_based" or "floor_based" or "roof_based" => FamilyPlacementType.OneLevelBasedHosted',
    '"line_based" => FamilyPlacementType.CurveBased',
    '"two_level_based" => FamilyPlacementType.TwoLevelsBased',
]:
    assert behavior_mapping in compiler, f"missing root Family template behavior placement mapping: {behavior_mapping}"
assert compiler.count('VerifyRootFamilyPlacement(family, spec, "new_from_autodesk_template")') == 2, "Preview and Apply must both verify the newly opened Autodesk Family template."
assert "VerifyReopenedRootFamilyPlacement(spec, readBack)" in compiler, "Apply must verify root Family placement after staged RFA reopen."
for marker in ["VerifyReopenedBreakIntoAccessory", "breaks_into", "valve_breaks_into", "routing body", "lost reciprocal connector linkage", "system_classification", "flow_configuration"]:
    assert marker in compiler, f"missing reopened Break Into accessory verification: {marker}"
for marker in ["connectors must share one inline routing body", "connectors must use start and end faces", "system_classification=Fitting or Global"]:
    assert marker in blueprint, f"missing bounded Break Into accessory topology validation: {marker}"
for marker in ["ResolvedProfileAt", "AssociateBlendProfileParameters", "BottomSketch", "TopSketch", "LabelSketchProfile", "NewRadialDimension", "parameterized blend"]:
    assert marker in compiler, f"missing parameterized blend profile behavior: {marker}"
for marker in ["revolutionProfile", "revolution.Sketch", "bottomCurves", "topCurves", "sweptBlend.BottomSketch", "sweptBlend.TopSketch", "LabelCircularLoop", "outer_diameter_parameter", "inner_diameter_parameter", "profile dimension/formula binding"]:
    assert marker in compiler, f"missing parameterized revolution/straight-sweep profile behavior: {marker}"
for marker in ["boundedParameterizedProfile", '"revolution" => shape is "rectangle" or "circle" or "ring" or "oval"', '"sweep" when pathKind is "line" or "polyline"', '"swept_blend" => shape is "rectangle" or "circle" or "oval"']:
    assert marker in compiler, f"missing Revit-side buildability gate for supported parameterized primitive: {marker}"
assert "Parameterized non-extrusion profiles require a future constraint operation" not in compiler, "stale Revit buildability gate still rejects supported parameterized primitives"
assert compiler.index("var nested = CreateNestedComponents") < compiler.index("var arrays = CreateArrays"), "nested components must be created before their arrays"
for marker in ["GetParameters", "order_index", "IsShownInFrontBack", "IsShownInLeftRight", "IsShownInPlanRCPCut", "IsShownOnlyWhenCut", "model_curves", "symbolic_curves", "detail_curves", "curve_geometry", "GeometryCurve", "nested_instances", "associated_family_parameter", "nested_instance_count", "coordination_zone_count", "coordination_purpose", "coordination_semantic_intent", "quantity_exclusion_certified", "material_id", "material_associated", "profile_signature", "SketchSignature", "light_source", "light_source_count", "FamilyLightingReadBack", "HostFace", "LocationPoint", "LocationCurve", "visibility_parameter_binding_count", "dimension_count", "equal_dimension_count", "void_form_count", "formula_parameter_count", "document_element_count", "import_instance_count", "ReferencePlaneSnapshot", "named_reference_plane_count", "origin_reference_plane_count", "styled_reference_plane_count"]:
    assert marker in family_commands, f"missing expanded Family inspection read-back: {marker}"
for marker in ["ResolveBlueprint", "templateBehavior", "NoGenericRecategorization", "wall_based", "face_based", "two_level_based", "detail_item", "annotation"]:
    assert marker in templates, f"missing behavior-first template resolver behavior: {marker}"
for marker in ["Metric Duct Elbow.rft", "Metric Duct Cross.rft", "Metric Duct Tee.rft", "Metric Duct Transition.rft", "Metric Pipe Elbow.rft", "Metric Pipe Cross.rft", "Metric Pipe Tee.rft", "Metric Pipe Transition.rft", "pipe_mechanical_coupling", "No native "]:
    assert marker in templates, f"missing primitive-specific MEP fitting template guard: {marker}"
for marker in ["FamilyAuthoringAnchor", "active_project_required", "family_spec_v3"]:
    assert marker in commands, f"missing no-active-Project Blueprint workflow: {marker}"
for marker in ["family_placement_type", "family_behavior", "role_or_subcategory", "associated_element_parameter_count", "materials", "verification_boundary"]:
    assert marker in family_commands, f"missing expanded family_inspect evidence: {marker}"
assert "OfClass(typeof(ModelCurve))" not in family_commands, "Revit 2023 rejects ModelCurve as a native OfClass filter"
assert "OfClass(typeof(SymbolicCurve))" not in family_commands, "Revit 2023 rejects SymbolicCurve as a native OfClass filter"
assert "OfClass(typeof(DetailCurve))" not in family_commands, "Revit 2023 rejects DetailCurve as a native OfClass filter"
assert "OfClass(typeof(CurveElement))" in family_commands, "family_inspect must collect the native CurveElement base before managed curve classification"
for marker in ["FamilyArtifactInspectCommand", "BasicFileInfo.Extract", "blocked_higher_revit_version", "artifact.Close(false)", "active_project_touched", "source_sha256"]:
    assert marker in commands, f"missing staged-RFA read-only inspection guard: {marker}"
assert "CompileAssemblyFromSource" not in compiler
assert "CSharpCodeProvider" not in compiler
assert "original.Value * 1.10" not in compiler
assert compiler.index('(Name: "min"') < compiler.index('(Name: "nominal"') < compiler.index('(Name: "max"')
for marker in ["appearanceBitmap", "appearanceBump", "texture", "bump", "file_name", "sha256"]:
    assert marker in schema, f"missing texture/bump Blueprint schema marker: {marker}"
for marker in ["resolveAppearanceAssets", "appearance_texture", "appearance_bump", "MAX_APPEARANCE_IMAGE_BYTES", "direct_child_of_approved_demo_directory"]:
    assert marker in evidence, f"missing controlled material image evidence marker: {marker}"
for marker in ["RequireAppearanceAssets", "RequireAppearanceImage", "RecognizedAppearanceImage", "SetAppearanceBitmap", "ClearAppearanceBitmap", "GenericBumpMap", "UnifiedBitmap", "AttachAppearanceDependencyEvidence", "ExternalDependencySnapshot", "external_path_redacted"]:
    assert marker in compiler, f"missing texture/bump Appearance compiler marker: {marker}"
assert 'else ClearAppearanceBitmap(editable, Generic.GenericDiffuse);' in compiler, "undeclared texture must clear the inherited Generic diffuse bitmap"
assert 'else ClearAppearanceBitmap(editable, Generic.GenericBumpMap);' in compiler, "undeclared bump must clear the inherited Generic bump bitmap"

for marker in ["identity_data", "revit_builtin_type_identity_data_v1", "native_revit_type_identity_data"]:
    assert marker in blueprint and marker in schema, f"missing native Revit Identity Data contract: {marker}"
for marker in ["ApplyIdentityData", "BuiltInIdentityDataSnapshot", "VerifyReopenedIdentityData", "ALL_MODEL_MANUFACTURER", "OMNICLASS_CODE", "native_type_field_write_read_back"]:
    assert marker in compiler, f"missing native Revit Identity Data compiler behavior: {marker}"
for marker in ["tap_perpendicular", "tap_adjustable", "bounded_tap_fitting_geometry", "multi-port link topology is runtime-certified"]:
    assert marker in blueprint, f"missing bounded tap fitting Blueprint contract: {marker}"
for marker in ["tap_perpendicular", "tap_adjustable", "VerifyReopenedJunction"]:
    assert marker in compiler, f"missing bounded tap fitting compiler verification: {marker}"
for marker in ["pants", "bounded_symmetric_pants_fitting_geometry", "outlet axis_direction vectors must be symmetric"]:
    assert marker in blueprint, f"missing bounded symmetric pants Blueprint contract: {marker}"
assert '"pants"' in compiler and "VerifyReopenedJunction" in compiler, "missing pants reopen verification"

print("PASS Family Blueprint v3/static contract: persisted evidence, bounded compiler, acceptance matrix, behavior-first templates and no Save/Sync")
