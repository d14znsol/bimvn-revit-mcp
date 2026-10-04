using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DSCons.RevitMcp.Contracts;
using DSCons.RevitMcp.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

/// <summary>Shared evidence boundary for PDF/CAD driven Family workflows.
/// Geometry adapters are deliberately allow-listed and remain un-certified
/// until a copied-model runtime record exists.</summary>
internal static class FamilyPlatformSafety
{
    public static string LocalFile(JObject args, string key, string extension, string root)
    {
        var raw = args.Value<string>(key);
        if (string.IsNullOrWhiteSpace(raw))
            throw new CommandResultException(ErrorCodes.PathBlocked, $"{key} must be a local absolute path.");
        var safeRaw = raw!;
        if (!Path.IsPathRooted(safeRaw) || safeRaw.StartsWith("\\\\", StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.PathBlocked, $"{key} must be a local absolute path.");
        var path = Path.GetFullPath(safeRaw);
        if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new CommandResultException(ErrorCodes.TemplateInvalid, $"{key} is missing or must use {extension}.");
        RequireInside(root, path);
        return path;
    }

    public static string DemoDirectory(JObject args)
    {
        var raw = args.Value<string>("approved_demo_directory");
        if (string.IsNullOrWhiteSpace(raw))
            throw new CommandResultException(ErrorCodes.PathBlocked, "approved_demo_directory must be a local absolute directory.");
        var safeRaw = raw!;
        if (!Path.IsPathRooted(safeRaw) || safeRaw.StartsWith("\\\\", StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.PathBlocked, "approved_demo_directory must be a local absolute directory.");
        var path = Path.GetFullPath(safeRaw);
        if (!Directory.Exists(path)) throw new CommandResultException(ErrorCodes.PathBlocked, "approved_demo_directory does not exist.");
        return path;
    }

    public static void RequireInside(string root, string path)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new CommandResultException(ErrorCodes.PathBlocked, "Path is outside approved_demo_directory.");
    }

    public static string Sha256(string path)
    {
        // Revit may keep an inspected Family/Project open while we verify its
        // resource fingerprint. Read sharing prevents a false lock failure
        // without allowing any write through this handle.
        using var sha = SHA256.Create(); using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }

    public static string Fingerprint(params string[] values)
    {
        using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("|", values)))).Replace("-", string.Empty).ToLowerInvariant();
    }

    public static string FamilyAuthoringAnchor(UIApplication app)
    {
        var document = app.ActiveUIDocument?.Document;
        return document != null
            ? "document|" + MepSafety.DocumentFingerprint(document)
            : "family_authoring_session|" + app.Application.VersionNumber + "|" + Process.GetCurrentProcess().Id;
    }
}

internal static class FamilyPlatformStore
{
    private sealed class Entry { public JObject Value { get; set; } = new(); public DateTimeOffset ExpiresAtUtc { get; set; } }
    private static readonly Dictionary<string, Entry> Items = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    public static void Put(string id, JObject value) { lock (Gate) Items[id] = new Entry { Value = (JObject)value.DeepClone(), ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15) }; }
    public static JObject TakeRead(string id, string kind)
    {
        lock (Gate)
        {
            if (!Items.TryGetValue(id, out var entry) || entry.ExpiresAtUtc < DateTimeOffset.UtcNow || !string.Equals(entry.Value.Value<string>("record_kind"), kind, StringComparison.Ordinal))
                throw new CommandResultException(ErrorCodes.PreviewInvalid, $"{kind} id is unknown or expired; inspect/preview again.");
            return (JObject)entry.Value.DeepClone();
        }
    }

    public static JObject TakeRead(string id, params string[] kinds)
    {
        lock (Gate)
        {
            if (!Items.TryGetValue(id, out var entry) || entry.ExpiresAtUtc < DateTimeOffset.UtcNow || !kinds.Contains(entry.Value.Value<string>("record_kind") ?? string.Empty, StringComparer.Ordinal))
                throw new CommandResultException(ErrorCodes.PreviewInvalid, "Family record id is unknown, expired or has an unexpected schema; preview again.");
            return (JObject)entry.Value.DeepClone();
        }
    }
}

internal sealed class FamilySourceInspectCommand : ReadCommand
{
    public FamilySourceInspectCommand() : base("family_source_inspect", "Inspect local PDF/DWG/DXF source and create evidence.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var demo = FamilyPlatformSafety.DemoDirectory(args);
        var kind = (args.Value<string>("source_kind") ?? Path.GetExtension(args.Value<string>("source_path") ?? string.Empty).TrimStart('.')).ToLowerInvariant();
        var ext = kind == "pdf" ? ".pdf" : kind == "dwg" ? ".dwg" : kind == "dxf" ? ".dxf" : string.Empty;
        if (ext.Length == 0) throw new CommandResultException(ErrorCodes.InvalidParam, "source_kind must be pdf, dwg or dxf.");
        var path = FamilyPlatformSafety.LocalFile(args, "source_path", ext, demo);
        var bytes = File.ReadAllBytes(path); var text = Encoding.UTF8.GetString(bytes);
        var pages = kind == "pdf" ? Regex.Matches(text, @"/Type\s*/Page\b").Count : 0;
        var evidence = new JObject { ["evidence_id"] = "src-" + FamilyPlatformSafety.Fingerprint(path, FamilyPlatformSafety.Sha256(path)).Substring(0, 16), ["source_kind"] = kind, ["canonical_path"] = path, ["sha256"] = FamilyPlatformSafety.Sha256(path), ["size_bytes"] = bytes.LongLength, ["pages"] = pages, ["units"] = args.Value<string>("units") ?? "unknown", ["layers"] = args["layers"] ?? new JArray(), ["status"] = "observed", ["uncertain_fields"] = new JArray("dimensions", "connector", "performance") };
        if (kind == "dxf") evidence["format_hint"] = text.IndexOf("$INSUNITS", StringComparison.OrdinalIgnoreCase) >= 0 ? "dxf_header_units_present" : "dxf_units_unconfirmed";
        if (kind == "dwg") evidence["format_hint"] = bytes.Length >= 6 ? Encoding.ASCII.GetString(bytes, 0, 6) : "dwg_header_missing";
        evidence["record_kind"] = "source_evidence"; FamilyPlatformStore.Put(evidence.Value<string>("evidence_id")!, evidence);
        return evidence;
    }
}

/// <summary>
/// Opens one explicitly staged RFA only long enough to obtain native Revit
/// read-back. It never loads that Family into the active Project, changes the
/// source file, or saves a Family document. A higher-version file is rejected
/// from BasicFileInfo before OpenDocumentFile so a lower Revit session never
/// attempts an unsafe upgrade/open path.
/// </summary>
internal sealed class FamilyArtifactInspectCommand : ReadCommand
{
    public FamilyArtifactInspectCommand() : base("family_artifact_inspect", "Inspect one staged RFA through a disposable read-only Family document.") { }

    public override JObject Execute(UIApplication app, JObject args)
    {
        var approvedDirectory = FamilyPlatformSafety.DemoDirectory(args);
        var path = FamilyPlatformSafety.LocalFile(args, "family_path", ".rfa", approvedDirectory);
        var expectedVersion = args.Value<string>("expected_revit_version")?.Trim();
        var runningVersion = app.Application.VersionNumber;
        if (!string.IsNullOrWhiteSpace(expectedVersion) && !string.Equals(expectedVersion, runningVersion, StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.InvalidParam, "expected_revit_version must match the running Revit session.");

        var beforeHash = FamilyPlatformSafety.Sha256(path);
        var sizeBytes = new FileInfo(path).Length;
        void VerifyArtifactUnchanged()
        {
            var afterHash = FamilyPlatformSafety.Sha256(path);
            if (!string.Equals(beforeHash, afterHash, StringComparison.OrdinalIgnoreCase))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "RFA checksum changed during read-only inspection; the result is rejected.");
        }
        BasicFileInfo basic;
        try { basic = BasicFileInfo.Extract(path); }
        catch (Exception exception) when (exception is Autodesk.Revit.Exceptions.InvalidOperationException || exception is IOException)
        {
            VerifyArtifactUnchanged();
            return new JObject
            {
                ["schema_version"] = "1.0", ["inspection_kind"] = "family_artifact_revit_readback",
                ["family_path"] = path, ["source_sha256"] = beforeHash, ["size_bytes"] = sizeBytes,
                ["running_revit_version"] = runningVersion, ["status"] = "blocked_basic_file_info_unreadable",
                ["opened_family_document"] = false, ["active_project_touched"] = false,
                ["temporary_document_saved"] = false,
                ["reason"] = "BasicFileInfo could not be safely read; the RFA was not opened.",
                ["diagnostic"] = exception.Message
            };
        }

        var fileInfo = new JObject
        {
            ["saved_revit_format"] = basic.Format,
            ["is_saved_in_current_version"] = basic.IsSavedInCurrentVersion,
            ["is_saved_in_later_version"] = basic.IsSavedInLaterVersion
        };
        if (basic.IsSavedInLaterVersion)
        {
            VerifyArtifactUnchanged();
            return new JObject
            {
                ["schema_version"] = "1.0", ["inspection_kind"] = "family_artifact_revit_readback",
                ["family_path"] = path, ["source_sha256"] = beforeHash, ["size_bytes"] = sizeBytes,
                ["running_revit_version"] = runningVersion, ["file_basic_info"] = fileInfo,
                ["status"] = "blocked_higher_revit_version", ["opened_family_document"] = false,
                ["active_project_touched"] = false, ["temporary_document_saved"] = false,
                ["reason"] = "This RFA is saved in a later Revit version and was not opened in the lower-version session."
            };
        }

        var activeProject = app.ActiveUIDocument?.Document;
        var activeProjectPathBefore = activeProject?.PathName;
        var activeProjectFingerprintBefore = activeProject == null ? null : MepSafety.DocumentFingerprint(activeProject);
        Document? artifact = null;
        try
        {
            artifact = app.Application.OpenDocumentFile(path);
            if (!artifact.IsFamilyDocument)
                throw new CommandResultException(ErrorCodes.TemplateInvalid, "family_path must be a native Revit Family document.");
            var inspection = FamilyData.Inspect(artifact);
            var connectorSummary = inspection["connectors"] as JArray ?? new JArray();
            var typeNames = inspection["types"] as JArray ?? new JArray();
            return new JObject
            {
                ["schema_version"] = "1.0", ["inspection_kind"] = "family_artifact_revit_readback",
                ["family_path"] = path, ["source_sha256"] = beforeHash, ["size_bytes"] = sizeBytes,
                ["running_revit_version"] = runningVersion, ["file_basic_info"] = fileInfo,
                ["status"] = "inspected", ["opened_family_document"] = true,
                ["active_project_touched"] = false, ["temporary_document_saved"] = false,
                ["compatibility"] = new JObject
                {
                    ["current_session"] = basic.IsSavedInCurrentVersion ? "exact_current_version" : "older_version_opened_without_save",
                    ["requires_explicit_compatibility_assessment"] = true,
                    ["boundary"] = "Read-back does not approve load/place, routing preference, network behavior, family substitution, or version transfer."
                },
                ["verified_interface"] = new JObject
                {
                    ["family_name"] = inspection.Value<string>("family_name"),
                    ["category"] = inspection.Value<string>("category"), ["category_id"] = inspection["category_id"]?.Value<long?>(),
                    ["family_placement_type"] = (inspection["hosting"] as JObject)?.Value<string>("family_placement_type"),
                    ["part_type"] = (inspection["family_behavior"] as JObject)?.Value<string>("part_type"),
                    ["connector_count"] = connectorSummary.Count,
                    ["type_count"] = typeNames.Count, ["type_names"] = typeNames.DeepClone(),
                    ["lookup_table_names"] = inspection["lookup_tables"]?.DeepClone() ?? new JArray()
                },
                ["family_inspection"] = inspection
            };
        }
        finally
        {
            if (artifact != null)
                try { artifact.Close(false); }
                catch (Exception exception) { throw new CommandResultException(ErrorCodes.VerificationFailed, "Temporary RFA document could not be closed without saving: " + exception.Message); }
            VerifyArtifactUnchanged();
            if (activeProject != null && (!string.Equals(activeProjectPathBefore, activeProject.PathName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(activeProjectFingerprintBefore, MepSafety.DocumentFingerprint(activeProject), StringComparison.Ordinal)))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "The active Project changed during RFA inspection; the result is rejected.");
        }
    }
}

internal sealed class FamilySpecPreviewCommand : ReadCommand
{
    public FamilySpecPreviewCommand() : base("family_spec_preview", "Normalize source evidence into a human-confirmed FamilySpec.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var evidence = args.Value<string>("evidence_id"); if (string.IsNullOrWhiteSpace(evidence)) throw new CommandResultException(ErrorCodes.InvalidParam, "evidence_id is required.");
        var source = FamilyPlatformStore.TakeRead(evidence!, "source_evidence");
        var kind = args.Value<string>("family_kind"); if (string.IsNullOrWhiteSpace(kind)) throw new CommandResultException(ErrorCodes.InvalidParam, "family_kind is required.");
        var adapter = FamilyAdapterRegistry.Get(kind!);
        var id = "spec-" + FamilyPlatformSafety.Fingerprint(evidence!, kind!).Substring(0, 16);
        var fields = args["confirmed_fields"] as JObject ?? new JObject();
        var missing = adapter.RequiredFields.Where(key => fields[key] == null).ToArray();
        var states = new JObject(adapter.RequiredFields.Select(key => new JProperty(key, fields[key] == null ? "missing" : fields[key] is JObject item ? item.Value<string>("status") ?? "uncertain" : "confirmed")));
        var spec = new JObject { ["spec_id"] = id, ["source_evidence_id"] = evidence, ["source_sha256"] = source.Value<string>("sha256"), ["family_kind"] = kind, ["category"] = adapter.CategoryName, ["lod"] = "LOD_300", ["detail_profile"] = "dscons_mep_300_v1", ["origin"] = "adapter_defined_geometric_center", ["connector_roles"] = new JArray(adapter.ConnectorRoles), ["field_status"] = states, ["fields"] = new JObject { ["dimensions"] = fields.HasValues ? "confirmed" : "uncertain", ["connectors"] = fields["connector_mode"] != null ? "confirmed" : "uncertain", ["performance"] = fields["performance"] != null ? "confirmed" : "uncertain" }, ["confirmed_fields"] = fields, ["status"] = missing.Length == 0 ? "human_confirmed" : "awaiting_human_confirmation", ["missing_fields"] = new JArray(missing), ["adapter_certification"] = adapter.CertificationState, ["next"] = missing.Length == 0 ? "Run family_build_preview; the adapter will block uncertain connector data." : "Confirm every missing catalog field before family_build_preview." };
        spec["record_kind"] = "family_spec"; FamilyPlatformStore.Put(id, spec); return spec;
    }
}

internal sealed class FamilyBuildPreviewCommand : ReadCommand
{
    public FamilyBuildPreviewCommand() : base("family_build_preview", "Preview a bounded declarative MEP Family blueprint or compatible legacy adapter and rollback.") { Capability.IsWrite = true; }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var specId = args.Value<string>("spec_id"); if (string.IsNullOrWhiteSpace(specId)) throw new CommandResultException(ErrorCodes.InvalidParam, "spec_id is required.");
        // SourceEvidence v2 is produced Node-local so that PDF inspection does
        // not require an open Revit session. The MCP host injects this reserved
        // payload only after resolving an in-memory immutable spec_id.
        if (args["__dscons_spec_payload"] is JObject nodeSpec)
        {
            if (!string.Equals(nodeSpec.Value<string>("spec_id"), specId, StringComparison.Ordinal) || nodeSpec.Value<string>("record_kind") is not ("family_spec_v3" or "family_spec_v2" or "family_spec"))
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Node FamilySpec payload is missing, malformed or belongs to another spec_id.");
            var bridgeSpec = (JObject)nodeSpec.DeepClone(); if (bridgeSpec.Value<string>("record_kind") != "family_spec_v3") bridgeSpec["record_kind"] = "family_spec"; FamilyPlatformStore.Put(specId!, bridgeSpec);
            args.Remove("__dscons_spec_payload");
        }
        var spec = FamilyPlatformStore.TakeRead(specId!, "family_spec", "family_spec_v3");
        if (spec.Value<string>("status") != "human_confirmed")
        {
            // The Node boundary deliberately classifies a parameterized
            // Sweep.ProfileSketch as a controlled Family Editor workflow. Do
            // not collapse that safety result into a misleading generic
            // "confirmation" error or create a temporary Family document.
            // A caller receives Unsupported before template resolution or any
            // Revit Family transaction and can hand the exact UI plan to a
            // controlled operator.
            if (spec.Value<string>("record_kind") == "family_spec_v3"
                && (spec["blueprint_assessment"] as JObject)?.Value<bool?>("buildable_by_api") == false)
            {
                var unsupported = string.Join(", ", (spec["blueprint_assessment"]?["unsupported_features"] as JArray)?.Values<string>() ?? Array.Empty<string>());
                throw new CommandResultException(ErrorCodes.Unsupported, "Family Blueprint requires controlled UI or an unavailable compiler capability before build preview: " + unsupported + ".");
            }
            throw new CommandResultException(ErrorCodes.InvalidParam, "Family fields must be confirmed before build preview.");
        }
        var blueprintSpec = spec.Value<string>("record_kind") == "family_spec_v3" ? FamilyBlueprintSpec.FromRecord(spec) : null;
        var qualitySpec = blueprintSpec == null ? FamilyQualitySpec.FromRecord(spec) : null; qualitySpec?.RequireBuildable();
        var demo = FamilyPlatformSafety.DemoDirectory(args);
        var kind = args.Value<string>("family_kind") ?? string.Empty; var confirmedKind = blueprintSpec?.FamilyKind ?? qualitySpec!.FamilyKind; if (!string.Equals(kind, confirmedKind, StringComparison.OrdinalIgnoreCase)) throw new CommandResultException(ErrorCodes.InvalidParam, "family_kind must match the confirmed spec_id.");
        if (!string.Equals(args.Value<string>("lod"), blueprintSpec?.Lod ?? qualitySpec!.Lod, StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.InvalidParam, "lod must match the immutable FamilySpec.");
        var requestedTemplate = args.Value<string>("template_path");
        var selection = blueprintSpec != null
            ? FamilyTemplateResolver.ResolveBlueprint(app.Application, blueprintSpec.CategoryKey, blueprintSpec.TemplateBehavior, requestedTemplate, demo, true, blueprintSpec.PartType)
            : FamilyTemplateResolver.Resolve(app.Application, kind, requestedTemplate, demo, true);
        var template = selection.Path; args["template_path"] = template; args["__template_auto_resolved"] = string.IsNullOrWhiteSpace(requestedTemplate); args["__template_category_change_required"] = selection.RequiresCategoryChange;
        var name = args.Value<string>("family_name")?.Trim(); var type = args.Value<string>("type_name")?.Trim(); if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type)) throw new CommandResultException(ErrorCodes.InvalidParam, "family_name and type_name are required.");
        var output = Path.Combine(demo, name + ".rfa"); FamilyPlatformSafety.RequireInside(demo, output); if (File.Exists(output)) throw new CommandResultException(ErrorCodes.FileConflict, "Output Family exists; overwrite is blocked.");
        JObject adapterValidation;
        if (blueprintSpec != null) adapterValidation = FamilyBlueprintCompiler.Preview(app.Application, selection, demo, blueprintSpec, name!, type!);
        else
        {
            var axial = kind == "axial_fan" ? FamilyQualityPipeline.AxialSpec(qualitySpec!, selection, demo, name!, type!) : null;
            adapterValidation = FamilyQualityPipeline.Preview(app, selection, qualitySpec!, axial, name!, type!);
        }
        var fingerprint = FamilyPlatformSafety.Fingerprint(args.ToString(Formatting.None), spec.Value<string>("source_sha256") ?? string.Empty, template, FamilyPlatformSafety.Sha256(template), output);
        var token = new PreviewToken { PreviewId = Guid.NewGuid().ToString("N"), Operation = "family_build", ArgumentsJson = args.ToString(Formatting.None), DocumentFingerprint = FamilyPlatformSafety.FamilyAuthoringAnchor(app), ResourceFingerprint = fingerprint, ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(McpConstants.PreviewLifetimeSeconds) };
        BridgeServer.Current?.Store(token);
        return new JObject { ["preview_id"] = token.PreviewId, ["spec_id"] = specId, ["family_kind"] = kind, ["model_changed"] = false, ["active_project_required"] = false, ["template_selection"] = new JObject { ["path"] = template, ["target_category"] = selection.CategoryName, ["template_category"] = selection.TemplateCategoryName, ["template_behavior"] = selection.TemplateBehavior, ["source"] = selection.Source, ["revit_version"] = selection.RevitVersion, ["category_change_required"] = selection.RequiresCategoryChange, ["category_change_mode"] = selection.RequiresCategoryChange ? "automatic_before_geometry" : "not_required", ["fallback_reason"] = string.IsNullOrWhiteSpace(selection.FallbackReason) ? null : selection.FallbackReason, ["learner_guidance"] = selection.RequiresCategoryChange ? "MCP creates a dedicated Family from the matching behavior template, changes category, then verifies it before geometry." : "The Autodesk template already matches the requested Family behavior/category." }, ["status"] = blueprintSpec != null || qualitySpec!.Adapter.GeometryImplemented ? "preview_complete_runtime_certification_still_required" : "adapter_registered_geometry_not_enabled", ["validation"] = new JObject { ["schema_version"] = blueprintSpec != null ? "3.0" : "2.0", ["geometry"] = "component_role_and_bounds_read_back", ["parameters"] = "independent_flex_for_geometry_references", ["connectors"] = blueprintSpec != null ? blueprintSpec.Blueprint["connectors"] : new JArray(qualitySpec!.Adapter.ConnectorRoles), ["lod"] = blueprintSpec?.Lod ?? qualitySpec!.Lod, ["detail_profile"] = blueprintSpec?.DetailProfile ?? qualitySpec!.DetailProfile, ["revit_preview"] = adapterValidation }, ["next"] = "Human confirmation is required before apply. Project hosting/network/coordination remain separate acceptance gates." };
    }
}

internal sealed class FamilyBuildApplyCommand : ReadCommand
{
    public FamilyBuildApplyCommand() : base("family_build_apply", "Apply a confirmed Family blueprint/adapter preview using staged no-overwrite output.") { Capability.IsWrite = true; }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var id = args.Value<string>("preview_id"); if (string.IsNullOrWhiteSpace(id) || BridgeServer.Current == null || !BridgeServer.Current.TryTake(id!, out var token)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Preview is missing, expired or already used.");
        if (token.ExpiresAtUtc < DateTimeOffset.UtcNow) throw new CommandResultException(ErrorCodes.PreviewExpired, "Preview expired; preview again.");
        if (!string.Equals(token.Operation, "family_build", StringComparison.OrdinalIgnoreCase) || !string.Equals(token.DocumentFingerprint, FamilyPlatformSafety.FamilyAuthoringAnchor(app), StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Revit authoring session, document anchor or operation changed; preview again.");
        var previewArgs = JObject.Parse(token.ArgumentsJson); var specId = previewArgs.Value<string>("spec_id");
        if (string.IsNullOrWhiteSpace(specId)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Preview did not retain spec_id.");
        var specRecord = FamilyPlatformStore.TakeRead(specId!, "family_spec", "family_spec_v3");
        var blueprintSpec = specRecord.Value<string>("record_kind") == "family_spec_v3" ? FamilyBlueprintSpec.FromRecord(specRecord) : null;
        var qualitySpec = blueprintSpec == null ? FamilyQualitySpec.FromRecord(specRecord) : null; qualitySpec?.RequireBuildable();
        var demo = FamilyPlatformSafety.DemoDirectory(previewArgs); var retainedTemplate = previewArgs.Value<string>("template_path") ?? string.Empty;
        FamilyTemplateSelection selection;
        if (previewArgs.Value<bool?>("__template_auto_resolved") == true)
        {
            selection = blueprintSpec != null
                ? FamilyTemplateResolver.ResolveBlueprint(app.Application, blueprintSpec.CategoryKey, blueprintSpec.TemplateBehavior, null, null, false, blueprintSpec.PartType)
                : FamilyTemplateResolver.Resolve(app.Application, qualitySpec!.FamilyKind);
            if (!string.Equals(Path.GetFullPath(retainedTemplate), selection.Path, StringComparison.OrdinalIgnoreCase))
                throw new CommandResultException(ErrorCodes.PreviewInvalid, "The automatically selected Family template changed; preview again.");
        }
        else selection = blueprintSpec != null
            ? FamilyTemplateResolver.ResolveBlueprint(app.Application, blueprintSpec.CategoryKey, blueprintSpec.TemplateBehavior, retainedTemplate, demo, true, blueprintSpec.PartType)
            : FamilyTemplateResolver.Resolve(app.Application, qualitySpec!.FamilyKind, retainedTemplate, demo, true);
        var template = selection.Path;
        var name = previewArgs.Value<string>("family_name")?.Trim() ?? string.Empty; var type = previewArgs.Value<string>("type_name")?.Trim() ?? string.Empty;
        var output = Path.Combine(demo, name + ".rfa"); FamilyPlatformSafety.RequireInside(demo, output); if (File.Exists(output)) throw new CommandResultException(ErrorCodes.FileConflict, "Output Family exists; overwrite is blocked.");
        var fingerprint = FamilyPlatformSafety.Fingerprint(previewArgs.ToString(Formatting.None), specRecord.Value<string>("source_sha256") ?? string.Empty, template, FamilyPlatformSafety.Sha256(template), output);
        if (!string.Equals(token.ResourceFingerprint, fingerprint, StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Catalog/template/output resource fingerprint changed; preview again.");
        if (blueprintSpec != null) return FamilyBlueprintCompiler.Apply(app.Application, selection, demo, blueprintSpec, name, type);
        if (!qualitySpec!.Adapter.GeometryImplemented) throw new CommandResultException(ErrorCodes.Unsupported, "This adapter is registered but has no geometry implementation. It cannot apply yet.");
        if (qualitySpec.FamilyKind == "axial_fan")
            return FamilyBuilder.Apply(app.Application, FamilyQualityPipeline.AxialSpec(qualitySpec, selection, demo, name, type));
        if (qualitySpec.FamilyKind is "pump" or "panel")
            return FamilyCatalogGeometryBuilder.Apply(app.Application, template, selection.RequiresCategoryChange, demo, qualitySpec, name, type);
        throw new CommandResultException(ErrorCodes.Unsupported, "This adapter is registered but has no certified geometry implementation. It cannot apply yet.");
    }
}


internal sealed class CadGeometryInspectCommand : ReadCommand
{
    public CadGeometryInspectCommand() : base("cad_geometry_inspect", "Inspect local DWG/DXF geometry evidence without importing it into the active Project.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var demo = FamilyPlatformSafety.DemoDirectory(args); var raw = args.Value<string>("source_path") ?? string.Empty; var ext = Path.GetExtension(raw).ToLowerInvariant(); if (ext != ".dwg" && ext != ".dxf") throw new CommandResultException(ErrorCodes.InvalidParam, "source_path must be .dwg or .dxf.");
        var path = FamilyPlatformSafety.LocalFile(args, "source_path", ext, demo);
        if (ext == ".dwg" && args.Value<bool?>("use_revit_trusted_adapter") == true) return InspectDwgInDisposableFamily(app, args, path);
        var bytes = File.ReadAllBytes(path); var text = Encoding.UTF8.GetString(bytes);
        return new JObject { ["evidence_id"] = "cad-" + FamilyPlatformSafety.Fingerprint(path, FamilyPlatformSafety.Sha256(path)).Substring(0, 16), ["canonical_path"] = path, ["sha256"] = FamilyPlatformSafety.Sha256(path), ["format"] = ext.TrimStart('.'), ["units"] = args.Value<string>("units") ?? "unknown", ["layers"] = args["layers"] ?? new JArray(), ["geometry"] = new JObject { ["line_count_hint"] = Regex.Matches(text, "(^|\\n)LINE(\\r?$|\\n)", RegexOptions.IgnoreCase).Count, ["polyline_count_hint"] = Regex.Matches(text, "LWPOLYLINE|POLYLINE", RegexOptions.IgnoreCase).Count, ["bounds"] = "not_computed_for_binary_source" }, ["status"] = "observed", ["mapping"] = "reference_or_allow_listed_route_only" };
    }

    private static JObject InspectDwgInDisposableFamily(UIApplication app, JObject args, string path)
    {
        var units = (args.Value<string>("units") ?? string.Empty).ToLowerInvariant();
        var importUnit = units switch
        {
            "mm" => ImportUnit.Millimeter, "cm" => ImportUnit.Centimeter, "m" => ImportUnit.Meter,
            "inch" => ImportUnit.Inch, "ft" => ImportUnit.Foot,
            _ => throw new CommandResultException(ErrorCodes.InvalidParam, "Trusted Revit DWG inspection requires engineer-confirmed units: mm, cm, m, inch or ft.")
        };
        var selection = FamilyTemplateResolver.ResolveBlueprint(app.Application, "generic_model", "level_based");
        Document? temporary = null;
        try
        {
            temporary = app.Application.NewFamilyDocument(selection.Path);
            var entities = new JArray(); var layers = new HashSet<string>(StringComparer.Ordinal); var skipped = new Dictionary<string, int>(StringComparer.Ordinal);
            using var transaction = new Transaction(temporary, "DSCons trusted DWG inspection"); transaction.Start();
            var options = new DWGImportOptions { Unit = importUnit, Placement = ImportPlacement.Origin, ThisViewOnly = false };
            if (!temporary.Import(path, options, temporary.ActiveView, out var importedId)) throw new CommandResultException(ErrorCodes.SourceUnreadable, "Revit could not import the DWG into the disposable inspection document.");
            temporary.Regenerate();
            var imported = temporary.GetElement(importedId) ?? throw new CommandResultException(ErrorCodes.SourceUnreadable, "Revit returned no ImportInstance for the disposable DWG inspection.");
            var geometry = imported.get_Geometry(new Options { IncludeNonVisibleObjects = true, DetailLevel = ViewDetailLevel.Fine });
            if (geometry != null) CollectCadGeometry(temporary, geometry, units, entities, layers, skipped);
            transaction.RollBack();
            var manifest = new JObject
            {
                ["adapter"] = "autodesk_revit_link", ["adapter_version"] = app.Application.VersionNumber + "." + app.Application.VersionBuild,
                ["source_sha256"] = FamilyPlatformSafety.Sha256(path), ["units"] = units,
                ["coordinate_system"] = new JObject { ["origin"] = new JArray(0, 0, 0), ["rotation_degrees"] = 0, ["policy"] = "temporary_family_import_origin_rollback_close_without_save" },
                ["layers"] = new JArray(layers.OrderBy(value => value, StringComparer.Ordinal)), ["layouts"] = new JArray("Model"), ["xrefs"] = new JArray(), ["entities"] = entities
            };
            return new JObject
            {
                ["trusted_adapter_manifest"] = manifest, ["model_changed"] = false, ["active_project_touched"] = false,
                ["temporary_document_rolled_back"] = true, ["temporary_document_saved"] = false,
                ["entity_count"] = entities.Count, ["skipped_geometry"] = JObject.FromObject(skipped),
                ["boundary"] = "Temporary Revit import exposes tessellated visible CAD geometry and GraphicsStyle layers only. Blocks, attributes, dimensions, xrefs and native DWG design intent are not certified by this adapter."
            };
        }
        finally { if (temporary != null && temporary.IsValidObject) temporary.Close(false); }
    }

    private static void CollectCadGeometry(Document document, GeometryElement geometry, string units, JArray entities, HashSet<string> layers, Dictionary<string, int> skipped)
    {
        foreach (var item in geometry)
        {
            if (entities.Count >= 50000) throw new CommandResultException(ErrorCodes.InvalidParam, "Trusted DWG inspection exceeds the 50,000-entity safety limit.");
            if (item is GeometryInstance instance) { var nested = instance.GetInstanceGeometry(); if (nested != null) CollectCadGeometry(document, nested, units, entities, layers, skipped); continue; }
            var layer = LayerName(document, item); layers.Add(layer);
            if (item is Line line)
            {
                entities.Add(new JObject { ["type"] = "LINE", ["layer"] = layer, ["start"] = Point(line.GetEndPoint(0), units), ["end"] = Point(line.GetEndPoint(1), units) }); continue;
            }
            if (item is Arc arc)
            {
                var entity = new JObject { ["type"] = arc.IsBound ? "ARC" : "CIRCLE", ["layer"] = layer, ["center"] = Point(arc.Center, units), ["radius"] = Length(arc.Radius, units) };
                if (arc.IsBound) { entity["start_angle"] = Angle(arc.Center, arc.GetEndPoint(0)); entity["end_angle"] = Angle(arc.Center, arc.GetEndPoint(1)); }
                entities.Add(entity); continue;
            }
            if (item is PolyLine polyline)
            {
                var vertices = new JArray(polyline.GetCoordinates().Select(point => Point(point, units))); if (vertices.Count >= 2) entities.Add(new JObject { ["type"] = "POLYLINE3D", ["layer"] = layer, ["vertices"] = vertices, ["closed"] = false }); continue;
            }
            var name = item.GetType().Name; skipped[name] = skipped.TryGetValue(name, out var count) ? count + 1 : 1;
        }
    }

    private static string LayerName(Document document, GeometryObject item)
    {
        var style = item.GraphicsStyleId == ElementId.InvalidElementId ? null : document.GetElement(item.GraphicsStyleId) as GraphicsStyle;
        return string.IsNullOrWhiteSpace(style?.GraphicsStyleCategory?.Name) ? "0" : style!.GraphicsStyleCategory.Name;
    }

    private static double SourceFactor(string units) => units switch { "mm" => 1.0, "cm" => 10.0, "m" => 1000.0, "inch" => 25.4, "ft" => 304.8, _ => 1.0 };
    private static double Length(double feet, string units) => Math.Round(feet * 304.8 / SourceFactor(units), 9);
    private static JArray Point(XYZ point, string units) => new(Length(point.X, units), Length(point.Y, units), Length(point.Z, units));
    private static double Angle(XYZ center, XYZ point)
    {
        var degrees = Math.Atan2(point.Y - center.Y, point.X - center.X) * 180.0 / Math.PI;
        return Math.Round(degrees < 0 ? degrees + 360 : degrees, 9);
    }
}

internal sealed class CadToRevitPreviewCommand : ReadCommand
{
    public CadToRevitPreviewCommand() : base("cad_to_revit_preview", "Preview an approved CAD-to-Revit Change Set mapping.") { Capability.IsWrite = true; }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); MepSafety.GuardWrite(doc, Enumerable.Empty<Element>());
        var evidenceId = args.Value<string>("evidence_id"); var contextId = args.Value<string>("context_id");
        if (string.IsNullOrWhiteSpace(evidenceId) || string.IsNullOrWhiteSpace(contextId)) throw new CommandResultException(ErrorCodes.InvalidParam, "evidence_id and context_id are required.");
        BimContextStore.Require(doc, contextId);
        var mapping = args.Value<string>("mapping") ?? "reference_only";
        if (mapping == "reference_only") return new JObject { ["mapping"] = mapping, ["model_changed"] = false, ["status"] = "reference_only", ["findings"] = new JArray("No Revit element or Apply token was created.", "CAD geometry is not a solid clash certificate."), ["next"] = "Review the SourceEvidence and provide an explicit bounded route mapping if model geometry is required." };
        if (mapping != "route") throw new CommandResultException(ErrorCodes.InvalidParam, "mapping must be route or reference_only.");

        var proposal = args["__dscons_source_proposal"] as JObject ?? throw new CommandResultException(ErrorCodes.InvalidParam, "A Node-validated SourceEvidence route proposal is required.");
        if (proposal.Value<string>("record_kind") != "source_to_revit_proposal" || proposal.Value<string>("status") != "ready_for_fresh_revit_preview" || proposal.Value<bool?>("model_changed") != false)
            throw new CommandResultException(ErrorCodes.InvalidParam, "Source proposal is not ready for a fresh Revit rollback preview.");
        var changeSet = BuildChangeSet(args, proposal);
        var validation = MepOperation.ValidatePreview(doc, "bim_changeset", changeSet);
        var targets = MepSafety.Targets(doc, "bim_changeset", changeSet).ToList();
        var envelope = new JObject { ["evidence_id"] = evidenceId, ["source_proposal"] = proposal.DeepClone(), ["change_set"] = changeSet.DeepClone() };
        var token = new PreviewToken
        {
            PreviewId = Guid.NewGuid().ToString("N"), Operation = "cad_to_revit", ArgumentsJson = envelope.ToString(Formatting.None),
            DocumentFingerprint = MepSafety.DocumentFingerprint(doc), TargetFingerprint = MepSafety.TargetFingerprint(targets),
            ResourceFingerprint = ResourceFingerprint(envelope), ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(McpConstants.PreviewLifetimeSeconds)
        };
        BridgeServer.Current?.Store(token);
        return new JObject
        {
            ["preview_id"] = token.PreviewId, ["mapping"] = mapping, ["evidence_id"] = evidenceId,
            ["proposal_id"] = proposal.Value<string>("proposal_id"), ["segment_count"] = ((proposal["project_change_set_proposal"] as JObject)?["segments"] as JArray)?.Count ?? 0,
            ["expires_at_utc"] = token.ExpiresAtUtc, ["model_changed"] = false, ["status"] = "change_set_preview_not_committed",
            ["validation_level"] = "revit_transactiongroup_rollback", ["validation"] = validation,
            ["citations"] = proposal["citations"]?.DeepClone(),
            ["findings"] = new JArray("CAD geometry is not a solid clash certificate.", "Fittings, slope, reroute and unmapped geometry require independent engineering review."),
            ["next"] = "Confirm this exact one-time Change Set token before cad_to_revit_apply."
        };
    }

    internal static JObject BuildChangeSet(JObject args, JObject proposal)
    {
        var route = proposal["project_change_set_proposal"] as JObject ?? throw new CommandResultException(ErrorCodes.InvalidParam, "Source proposal has no route payload.");
        var kind = route.Value<string>("kind") ?? string.Empty;
        if (!string.Equals(kind, args.Value<string>("kind"), StringComparison.OrdinalIgnoreCase)) throw new CommandResultException(ErrorCodes.InvalidParam, "Route kind changed between source proposal and Revit preview.");
        foreach (var key in new[] { "type_id", "level_id" }) if (route.Value<long?>(key) != args.Value<long?>(key)) throw new CommandResultException(ErrorCodes.InvalidParam, key + " changed between source proposal and Revit preview.");
        if (kind is "pipe" or "duct" && route.Value<long?>("system_type_id") != args.Value<long?>("system_type_id")) throw new CommandResultException(ErrorCodes.InvalidParam, "system_type_id changed between source proposal and Revit preview.");
        var segments = (route["segments"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        if (segments.Count == 0 || segments.Count > 50) throw new CommandResultException(ErrorCodes.InvalidParam, "A CAD route proposal must contain 1..50 bounded segments.");
        var operations = new JArray();
        foreach (var segment in segments)
        {
            var points = segment["points_mm"] as JArray ?? new JArray();
            if (points.Count < 2 || points.Count > 100) throw new CommandResultException(ErrorCodes.InvalidParam, "Each CAD route segment must contain 2..100 points.");
            var routeArgs = new JObject
            {
                ["kind"] = kind, ["type_id"] = route.Value<long>("type_id"), ["level_id"] = route.Value<long>("level_id"),
                ["points"] = points.DeepClone(), ["source_tolerance_mm"] = route.Value<double>("tolerance_mm"),
                ["demo_tag"] = "DSCons SourceEvidence " + proposal.Value<string>("proposal_id")
            };
            if (route["system_type_id"]?.Type != JTokenType.Null) routeArgs["system_type_id"] = route.Value<long>("system_type_id");
            operations.Add(new JObject { ["operation"] = "mep_create_route", ["arguments"] = routeArgs });
        }
        return new JObject { ["context_id"] = args.Value<string>("context_id"), ["name"] = "CAD SourceEvidence route " + proposal.Value<string>("proposal_id"), ["operations"] = operations };
    }

    internal static string ResourceFingerprint(JObject envelope) => FamilyPlatformSafety.Fingerprint(envelope.ToString(Formatting.None));
}

internal sealed class CadToRevitApplyCommand : ReadCommand
{
    public CadToRevitApplyCommand() : base("cad_to_revit_apply", "Apply a confirmed CAD Change Set atomically and verify read-back.") { Capability.IsWrite = true; }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); var id = args.Value<string>("preview_id"); if (string.IsNullOrWhiteSpace(id) || BridgeServer.Current == null || !BridgeServer.Current.TryTake(id!, out var token)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "CAD preview is missing, expired or already used.");
        if (!string.Equals(token.Operation, "cad_to_revit", StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "preview_id does not belong to a CAD route Change Set.");
        if (token.ExpiresAtUtc < DateTimeOffset.UtcNow) throw new CommandResultException(ErrorCodes.PreviewExpired, "CAD preview expired; preview again.");
        if (!string.Equals(token.DocumentFingerprint, MepSafety.DocumentFingerprint(doc), StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Document fingerprint changed; preview again.");
        var envelope = JObject.Parse(token.ArgumentsJson);
        if (!string.Equals(token.ResourceFingerprint, CadToRevitPreviewCommand.ResourceFingerprint(envelope), StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "CAD SourceEvidence proposal changed; preview again.");
        var changeSet = envelope["change_set"] as JObject ?? throw new CommandResultException(ErrorCodes.PreviewInvalid, "CAD Change Set payload is missing.");
        BimContextStore.Require(doc, changeSet.Value<string>("context_id"));
        var targets = MepSafety.Targets(doc, "bim_changeset", changeSet).ToList();
        if (!string.Equals(token.TargetFingerprint, MepSafety.TargetFingerprint(targets), StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "CAD route type, system, level or target state changed; preview again.");
        var derived = new PreviewToken
        {
            PreviewId = token.PreviewId, Operation = "bim_changeset", ArgumentsJson = changeSet.ToString(Formatting.None),
            DocumentFingerprint = token.DocumentFingerprint, TargetFingerprint = token.TargetFingerprint, ExpiresAtUtc = token.ExpiresAtUtc
        };
        var result = MepOperation.Apply(doc, derived, false);
        result["source_evidence_id"] = envelope.Value<string>("evidence_id");
        result["source_proposal_id"] = (envelope["source_proposal"] as JObject)?.Value<string>("proposal_id");
        result["mapping"] = "route";
        result["source_boundary"] = "Applied only the reviewed LINE/LWPOLYLINE centerline proposal. CAD remains evidence, not a solid clash or reroute certificate.";
        return result;
    }
}
