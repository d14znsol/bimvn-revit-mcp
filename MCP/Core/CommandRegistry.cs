using DSCons.RevitMcp.Commands;
using DSCons.RevitMcp.Contracts;

namespace DSCons.RevitMcp.Core;

internal static class CommandRegistry
{
    private static readonly Dictionary<string, IRevitCommand> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["system_status"] = new SystemStatusCommand(), ["document_info"] = new DocumentInfoCommand(), ["get_active_view"] = new ActiveViewCommand(), ["get_selection"] = new SelectionCommand(),
        ["get_capabilities"] = new CapabilitiesCommand(), ["mep_element_detail"] = new MepElementDetailCommand(), ["mep_connector_network"] = new MepConnectorNetworkCommand(),
        ["mep_filter_elements"] = new MepFilterCommand(), ["mep_qa_connectivity"] = new MepConnectivityQaCommand(), ["mep_preview"] = new MepPreviewCommand(), ["mep_apply_preview"] = new MepApplyPreviewCommand(),
        ["mep_create_route"] = new MepAutoApplyCommand("mep_create_route"), ["mep_connect"] = new MepAutoApplyCommand("mep_connect"), ["mep_disconnect"] = new MepAutoApplyCommand("mep_disconnect"),
        ["mep_move_route"] = new MepAutoApplyCommand("mep_move_route"), ["mep_change_type"] = new MepAutoApplyCommand("mep_change_type"), ["mep_change_size"] = new MepAutoApplyCommand("mep_change_size"),
        ["bim_context_snapshot"] = new BimContextSnapshotCommand(), ["bim_model_catalog"] = new BimModelCatalogCommand(), ["mep_network_explore"] = new MepNetworkExploreCommand(), ["coordination_links"] = new CoordinationLinksCommand(), ["coordination_scan"] = new CoordinationScanCommand(), ["quantity_takeoff"] = new QuantityTakeoffCommand(), ["documentation_plan"] = new DocumentationPlanCommand(),
        ["model_create_batch"] = new MepAutoApplyCommand("model_create_batch"), ["bim_changeset_preview"] = new BimChangeSetPreviewCommand(), ["bim_changeset_apply"] = new BimChangeSetApplyCommand(), ["documentation_apply"] = new MepAutoApplyCommand("documentation_apply")
        , ["family_inspect"] = new FamilyInspectCommand(), ["family_axial_fan_preview"] = new FamilyAxialFanPreviewCommand(), ["family_axial_fan_apply"] = new FamilyAxialFanApplyCommand(), ["family_load_place_preview"] = new FamilyLoadPlacePreviewCommand(), ["family_load_place_apply"] = new FamilyLoadPlaceApplyCommand(),
        ["family_source_inspect"] = new FamilySourceInspectCommand(), ["family_spec_preview"] = new FamilySpecPreviewCommand(), ["family_build_preview"] = new FamilyBuildPreviewCommand(), ["family_build_apply"] = new FamilyBuildApplyCommand(), ["cad_geometry_inspect"] = new CadGeometryInspectCommand(), ["cad_to_revit_preview"] = new CadToRevitPreviewCommand(), ["cad_to_revit_apply"] = new CadToRevitApplyCommand(),
        ["coordination_solid_scan"] = new CoordinationSolidScanCommand(), ["coordination_issue_report_preview"] = new CoordinationIssueReportPreviewCommand(), ["coordination_issue_report_apply"] = new CoordinationIssueReportApplyCommand(), ["coordination_section_preview"] = new CoordinationSectionPreviewCommand(), ["coordination_section_apply"] = new CoordinationSectionApplyCommand()
    };

    static CommandRegistry()
    {
        SetMetadata("system_status", ToolScopes.Session, false, Array.Empty<string>(), Array.Empty<string>());
        SetMetadata("document_info", ToolScopes.Document, false, new[] { "An active Revit document is required." }, Array.Empty<string>());
        SetMetadata("get_active_view", ToolScopes.View, false, new[] { "An active Revit document is required." }, new[] { "Re-query before any claim or action that depends on the current view." });
        SetMetadata("get_selection", ToolScopes.Selection, false, Array.Empty<string>(), new[] { "Selection may change between requests; re-query before a selection-dependent action." });
        SetMetadata("get_capabilities", ToolScopes.Session, false, Array.Empty<string>(), Array.Empty<string>());
        SetMetadata("mep_element_detail", ToolScopes.Elements, false, new[] { "element_ids must belong to the active document." }, Array.Empty<string>());
        SetMetadata("mep_connector_network", ToolScopes.Elements, false, new[] { "element_ids must belong to the active document." }, Array.Empty<string>());
        SetMetadata("mep_filter_elements", ToolScopes.Document, false, Array.Empty<string>(), new[] { "Category, system and level filters are combined with AND." });
        SetMetadata("mep_qa_connectivity", ToolScopes.Document, false, Array.Empty<string>(), new[] { "Open connectors are review findings, not automatic model errors or repair instructions." });
        SetMetadata("bim_context_snapshot", ToolScopes.Document, false, new[] { "An active Revit document is required." }, new[] { "Snapshot expires after 15 minutes and is invalidated by document/view state changes." });
        SetMetadata("bim_model_catalog", ToolScopes.Document, false, new[] { "An active Revit document is required." }, new[] { "Catalog is scoped to the active document; no type, template or title block is created." });
        SetMetadata("mep_network_explore", ToolScopes.Elements, false, new[] { "element_ids must belong to the active document." }, new[] { "Topology is limited by max_depth and limit; it does not infer engineering intent." });
        SetMetadata("coordination_links", ToolScopes.Document, false, Array.Empty<string>(), new[] { "Only loaded Revit links are visible; this tool never changes links." });
        SetMetadata("coordination_scan", ToolScopes.Document, false, Array.Empty<string>(), new[] { "Bounding-box triage can contain false positives; confirm geometry and clearance before rerouting." });
        SetMetadata("quantity_takeoff", ToolScopes.Document, false, Array.Empty<string>(), new[] { "Quantities are model evidence, not procurement/waste calculations." });
        SetMetadata("documentation_plan", ToolScopes.Document, false, Array.Empty<string>(), new[] { "Validation does not create, rename or place views/sheets." });

        var writeLimits = new[] { "Writes are blocked for direct Central, read-only, pinned, grouped and owned-by-other targets.", "Every write runs in a TransactionGroup and rolls back on failure." };
        SetMetadata("mep_preview", ToolScopes.Elements, true, new[] { "Re-anchor document context and inspect relevant target/type/connector data in the current turn." }, writeLimits);
        SetMetadata("mep_apply_preview", ToolScopes.Elements, true, new[] { "A valid, unused preview_id less than 90 seconds old is required.", "Re-anchor document context before apply." }, writeLimits);
        SetMetadata("mep_create_route", ToolScopes.Document, true, new[] { "Re-anchor document context.", "A valid loaded type_id and level_id are required; pipe and duct also require system_type_id." }, writeLimits);
        SetMetadata("mep_connect", ToolScopes.Elements, true, new[] { "Re-anchor document context and inspect connector compatibility in the current turn." }, writeLimits);
        SetMetadata("mep_disconnect", ToolScopes.Elements, true, new[] { "Re-anchor document context and inspect the existing connector network in the current turn." }, writeLimits);
        SetMetadata("mep_move_route", ToolScopes.Elements, true, new[] { "Re-anchor document context and confirm target element_ids in the current turn." }, writeLimits);
        SetMetadata("mep_change_type", ToolScopes.Elements, true, new[] { "Re-anchor document context and confirm target element_ids and type_id in the current turn." }, writeLimits);
        SetMetadata("mep_change_size", ToolScopes.Elements, true, new[] { "Re-anchor document context and confirm target element_ids and applicable dimensions in the current turn." }, writeLimits);
        SetMetadata("model_create_batch", ToolScopes.Document, true, new[] { "A fresh bim_context_snapshot context_id is required.", "Every route requires valid type_id/level_id; Pipe and Duct also require system_type_id." }, writeLimits.Concat(new[] { "A batch is atomic: one failed route rolls back every route in the batch." }));
        SetMetadata("bim_changeset_preview", ToolScopes.Document, true, new[] { "A fresh bim_context_snapshot context_id is required.", "Every included operation must be a supported DSCons write operation." }, writeLimits.Concat(new[] { "Preview executes and rolls back the entire Change Set; no element persists." }));
        SetMetadata("bim_changeset_apply", ToolScopes.Document, true, new[] { "A valid unused BIM Change Set preview_id less than 90 seconds old is required." }, writeLimits.Concat(new[] { "All operations commit together or rollback together." }));
        SetMetadata("documentation_apply", ToolScopes.Document, true, new[] { "A fresh bim_context_snapshot context_id is required.", "Use existing ViewFamilyType, View Template and Title Block type IDs from the active document." }, writeLimits.Concat(new[] { "Never Save or Sync; existing views are placed only when explicitly supplied." }));
        SetMetadata("family_inspect", ToolScopes.Document, false, new[] { "An active Revit document is required." }, new[] { "Inspection is read-only and reports only data Revit exposes from the active document." });
        var familyWriteLimits = new[] { "MCP prefers an exact-year category template; when absent it may use exact-year Metric Generic Model and verify the target Family Category before geometry. An explicit override remains optional and guarded.", "An approved demo directory is required.", "No Project Save or Sync is ever called.", "Preview tokens expire after 90 seconds, are single-use and are invalidated when document or file fingerprints change." };
        SetMetadata("family_axial_fan_preview", ToolScopes.Document, true, new[] { "Re-anchor the active document in this turn.", "MCP selects Metric Mechanical Equipment for the active Revit year; the learner does not browse for template_path.", "blade_count must be exactly 4 in v1." }, familyWriteLimits);
        SetMetadata("family_axial_fan_apply", ToolScopes.Document, true, new[] { "A valid unused family preview_id less than 90 seconds old is required.", "The user must have explicitly confirmed the preview before the AI invokes this tool." }, familyWriteLimits);
        SetMetadata("family_load_place_preview", ToolScopes.Document, true, new[] { "Re-anchor Project document and active view in this turn.", "Project copy, .rfa, Level, view and location must be explicitly supplied." }, familyWriteLimits);
        SetMetadata("family_load_place_apply", ToolScopes.Document, true, new[] { "A valid unused load/place preview_id less than 90 seconds old is required.", "The user must have explicitly confirmed the preview before the AI invokes this tool." }, familyWriteLimits.Concat(new[] { "round_hvac connectors are experimental until a dedicated runtime record passes." }));
        SetMetadata("family_source_inspect", ToolScopes.Document, false, new[] { "Provide a local PDF/DWG/DXF beneath approved_demo_directory." }, new[] { "Source evidence is local only; it never uploads or builds a model." });
        SetMetadata("family_spec_preview", ToolScopes.Document, false, new[] { "A source evidence id and an allow-listed HVAC adapter are required." }, new[] { "Unclear fields remain uncertain or missing until the user confirms them." });
        var platformLimits = familyWriteLimits.Concat(new[] { "Wave 1 adapters require copied-model runtime evidence before a support claim.", "No arbitrary Family geometry, parameter or CAD mapping is permitted." });
        SetMetadata("family_build_preview", ToolScopes.Document, true, new[] { "A human-confirmed spec_id and LOD_300 are required.", "MCP resolves the exact-year category template or Generic Model fallback and verifies Family Category; template_path is only an advanced override." }, platformLimits);
        SetMetadata("family_build_apply", ToolScopes.Document, true, new[] { "A valid unused confirmed preview_id is required.", "Fire Keeper approval and adapter runtime evidence are required before applying." }, platformLimits);
        SetMetadata("cad_geometry_inspect", ToolScopes.Document, false, new[] { "Provide a local DWG/DXF beneath approved_demo_directory." }, new[] { "CAD is inspected as reference; it is never blindly imported." });
        SetMetadata("cad_to_revit_preview", ToolScopes.Document, true, new[] { "A CAD evidence id, fresh context and confirmed units are required." }, new[] { "Only allow-listed route mappings can be previewed; unmapped geometry is reported as a finding." });
        SetMetadata("cad_to_revit_apply", ToolScopes.Document, true, new[] { "A valid unused confirmed Change Set preview_id and runtime approval are required." }, new[] { "No automatic reroute or blind CAD import is performed." });
        SetMetadata("coordination_solid_scan", ToolScopes.Document, true, new[] { "A fresh bim_context_snapshot context_id and loaded Revit links are required." }, new[] { "Bounding boxes are only a prefilter. Only positive Revit Solid intersection is exact clash evidence; clearance_triage is not a solid certificate.", "Findings are unreviewed and never auto-reroute the model." });
        SetMetadata("coordination_issue_report_preview", ToolScopes.Document, true, new[] { "A fresh context, current scan_id, complete engineer decisions and a user-approved local output directory are required." }, new[] { "Preview writes no file. Private course citations retain only path/checksum/heading and do not make a legal or engineering rule." });
        SetMetadata("coordination_issue_report_apply", ToolScopes.Document, true, new[] { "A valid unused issue-report preview_id less than 90 seconds old is required.", "The engineer must have confirmed the report output before apply." }, writeLimits.Concat(new[] { "JSON and HTML are staged with GUID names, never overwrite existing files and are cleaned up if publish fails." }));
        SetMetadata("coordination_section_preview", ToolScopes.Document, true, new[] { "A fresh context, current scan_id and existing Section ViewFamilyType are required." }, writeLimits.Concat(new[] { "Preview creates temporary sections only inside a rolled-back TransactionGroup." }));
        SetMetadata("coordination_section_apply", ToolScopes.Document, true, new[] { "A valid unused section preview_id less than 90 seconds old is required.", "The user must say exactly XÁC NHẬN TẠO MẶT CẮT COMBINE before the AI invokes apply." }, writeLimits.Concat(new[] { "No Save or Sync; use documentation_plan/documentation_apply separately for sheet placement." }));
    }

    private static void SetMetadata(string name, string scope, bool requiresFreshContext, IEnumerable<string> prerequisites, IEnumerable<string> limitations)
    {
        var capability = Commands[name].Capability;
        capability.Scope = scope;
        capability.RequiresFreshContext = requiresFreshContext;
        capability.Prerequisites = prerequisites.ToList();
        capability.Limitations = limitations.ToList();
    }

    public static IRevitCommand? Get(string name) => Commands.TryGetValue(name, out var command) ? command : null;
    public static IReadOnlyCollection<ToolCapability> Capabilities => Commands.Values.Select(x => x.Capability).OrderBy(x => x.Name).ToList();
}
