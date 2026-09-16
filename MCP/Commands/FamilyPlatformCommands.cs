using System.Security.Cryptography;
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
    public FamilyBuildPreviewCommand() : base("family_build_preview", "Preview an allow-listed HVAC Family adapter and rollback.") { Capability.IsWrite = true; }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); MepSafety.GuardWrite(doc, Enumerable.Empty<Element>());
        var specId = args.Value<string>("spec_id"); if (string.IsNullOrWhiteSpace(specId)) throw new CommandResultException(ErrorCodes.InvalidParam, "spec_id is required.");
        // SourceEvidence v2 is produced Node-local so that PDF inspection does
        // not require an open Revit session. The MCP host injects this reserved
        // payload only after resolving an in-memory immutable spec_id.
        if (args["__dscons_spec_payload"] is JObject nodeSpec)
        {
            if (!string.Equals(nodeSpec.Value<string>("spec_id"), specId, StringComparison.Ordinal) || nodeSpec.Value<string>("record_kind") is not ("family_spec_v2" or "family_spec"))
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Node FamilySpec payload is missing, malformed or belongs to another spec_id.");
            var bridgeSpec = (JObject)nodeSpec.DeepClone(); bridgeSpec["record_kind"] = "family_spec"; FamilyPlatformStore.Put(specId!, bridgeSpec);
            args.Remove("__dscons_spec_payload");
        }
        var spec = FamilyPlatformStore.TakeRead(specId!, "family_spec"); if (spec.Value<string>("status") != "human_confirmed") throw new CommandResultException(ErrorCodes.InvalidParam, "Family fields must be human-confirmed before build preview.");
        var qualitySpec = FamilyQualitySpec.FromRecord(spec); qualitySpec.RequireBuildable();
        var demo = FamilyPlatformSafety.DemoDirectory(args);
        var kind = args.Value<string>("family_kind") ?? string.Empty; if (!string.Equals(kind, qualitySpec.FamilyKind, StringComparison.OrdinalIgnoreCase)) throw new CommandResultException(ErrorCodes.InvalidParam, "family_kind must match the confirmed spec_id.");
        var requestedTemplate = args.Value<string>("template_path");
        var selection = FamilyTemplateResolver.Resolve(app.Application, kind, requestedTemplate, demo, true); var template = selection.Path; args["template_path"] = template; args["__template_auto_resolved"] = string.IsNullOrWhiteSpace(requestedTemplate); args["__template_category_change_required"] = selection.RequiresCategoryChange;
        var name = args.Value<string>("family_name")?.Trim(); var type = args.Value<string>("type_name")?.Trim(); if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type)) throw new CommandResultException(ErrorCodes.InvalidParam, "family_name and type_name are required.");
        var output = Path.Combine(demo, name + ".rfa"); FamilyPlatformSafety.RequireInside(demo, output); if (File.Exists(output)) throw new CommandResultException(ErrorCodes.FileConflict, "Output Family exists; overwrite is blocked.");
        var axial = kind == "axial_fan" ? FamilyQualityPipeline.AxialSpec(qualitySpec, selection, demo, name!, type!) : null;
        var adapterValidation = FamilyQualityPipeline.Preview(app, selection, qualitySpec, axial, name!, type!);
        var fingerprint = FamilyPlatformSafety.Fingerprint(args.ToString(Formatting.None), spec.Value<string>("source_sha256") ?? string.Empty, template, FamilyPlatformSafety.Sha256(template), output);
        var token = new PreviewToken { PreviewId = Guid.NewGuid().ToString("N"), Operation = "family_build", ArgumentsJson = args.ToString(Formatting.None), DocumentFingerprint = MepSafety.DocumentFingerprint(doc), ResourceFingerprint = fingerprint, ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(McpConstants.PreviewLifetimeSeconds) };
        BridgeServer.Current?.Store(token);
        return new JObject { ["preview_id"] = token.PreviewId, ["spec_id"] = specId, ["family_kind"] = kind, ["model_changed"] = false, ["template_selection"] = new JObject { ["path"] = template, ["target_category"] = selection.CategoryName, ["template_category"] = selection.TemplateCategoryName, ["source"] = selection.Source, ["revit_version"] = selection.RevitVersion, ["category_change_required"] = selection.RequiresCategoryChange, ["category_change_mode"] = selection.RequiresCategoryChange ? "automatic_before_geometry" : "not_required", ["fallback_reason"] = string.IsNullOrWhiteSpace(selection.FallbackReason) ? null : selection.FallbackReason, ["learner_guidance"] = selection.RequiresCategoryChange ? "MCP will perform the equivalent of Create > Family Category and Parameters, then verify the target category." : "The specialized template already has the target Family Category." }, ["status"] = qualitySpec.Adapter.GeometryImplemented ? "preview_complete_runtime_certification_still_required" : "adapter_registered_geometry_not_enabled", ["validation"] = new JObject { ["geometry"] = "role_and_envelope_contract", ["parameters"] = "mapped_from_confirmed_spec", ["connectors"] = new JArray(qualitySpec.Adapter.ConnectorRoles), ["lod"] = "LOD_300", ["detail_profile"] = "dscons_mep_300_v1", ["revit_preview"] = adapterValidation }, ["next"] = "Human confirmation is required before apply. Runtime connector probes remain a separate certification gate." };
    }
}

internal sealed class FamilyBuildApplyCommand : ReadCommand
{
    public FamilyBuildApplyCommand() : base("family_build_apply", "Apply a confirmed Family build preview using staged output.") { Capability.IsWrite = true; }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); var id = args.Value<string>("preview_id"); if (string.IsNullOrWhiteSpace(id) || BridgeServer.Current == null || !BridgeServer.Current.TryTake(id!, out var token)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Preview is missing, expired or already used.");
        if (token.ExpiresAtUtc < DateTimeOffset.UtcNow) throw new CommandResultException(ErrorCodes.PreviewExpired, "Preview expired; preview again.");
        if (!string.Equals(token.Operation, "family_build", StringComparison.OrdinalIgnoreCase) || !string.Equals(token.DocumentFingerprint, MepSafety.DocumentFingerprint(doc), StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Document or operation fingerprint changed; preview again.");
        var previewArgs = JObject.Parse(token.ArgumentsJson); var specId = previewArgs.Value<string>("spec_id");
        if (string.IsNullOrWhiteSpace(specId)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Preview did not retain spec_id.");
        var specRecord = FamilyPlatformStore.TakeRead(specId!, "family_spec"); var qualitySpec = FamilyQualitySpec.FromRecord(specRecord); qualitySpec.RequireBuildable();
        var demo = FamilyPlatformSafety.DemoDirectory(previewArgs); var retainedTemplate = previewArgs.Value<string>("template_path") ?? string.Empty;
        FamilyTemplateSelection selection;
        if (previewArgs.Value<bool?>("__template_auto_resolved") == true)
        {
            selection = FamilyTemplateResolver.Resolve(app.Application, qualitySpec.FamilyKind);
            if (!string.Equals(Path.GetFullPath(retainedTemplate), selection.Path, StringComparison.OrdinalIgnoreCase))
                throw new CommandResultException(ErrorCodes.PreviewInvalid, "The automatically selected Family template changed; preview again.");
        }
        else selection = FamilyTemplateResolver.Resolve(app.Application, qualitySpec.FamilyKind, retainedTemplate, demo, true);
        var template = selection.Path;
        var name = previewArgs.Value<string>("family_name")?.Trim() ?? string.Empty; var type = previewArgs.Value<string>("type_name")?.Trim() ?? string.Empty;
        var output = Path.Combine(demo, name + ".rfa"); FamilyPlatformSafety.RequireInside(demo, output); if (File.Exists(output)) throw new CommandResultException(ErrorCodes.FileConflict, "Output Family exists; overwrite is blocked.");
        var fingerprint = FamilyPlatformSafety.Fingerprint(previewArgs.ToString(Formatting.None), specRecord.Value<string>("source_sha256") ?? string.Empty, template, FamilyPlatformSafety.Sha256(template), output);
        if (!string.Equals(token.ResourceFingerprint, fingerprint, StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Catalog/template/output resource fingerprint changed; preview again.");
        if (!qualitySpec.Adapter.GeometryImplemented) throw new CommandResultException(ErrorCodes.Unsupported, "This adapter is registered but has no geometry implementation. It cannot apply yet.");
        if (qualitySpec.FamilyKind == "axial_fan")
            return FamilyBuilder.Apply(app.Application, FamilyQualityPipeline.AxialSpec(qualitySpec, selection, demo, name, type));
        if (qualitySpec.FamilyKind is "pump" or "panel")
            return FamilyCatalogGeometryBuilder.Apply(app.Application, template, selection.RequiresCategoryChange, demo, qualitySpec, name, type);
        throw new CommandResultException(ErrorCodes.Unsupported, "This adapter is registered but has no certified geometry implementation. It cannot apply yet.");
    }
}


internal sealed class CadGeometryInspectCommand : ReadCommand
{
    public CadGeometryInspectCommand() : base("cad_geometry_inspect", "Inspect local DWG/DXF geometry evidence without importing it.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var demo = FamilyPlatformSafety.DemoDirectory(args); var raw = args.Value<string>("source_path") ?? string.Empty; var ext = Path.GetExtension(raw).ToLowerInvariant(); if (ext != ".dwg" && ext != ".dxf") throw new CommandResultException(ErrorCodes.InvalidParam, "source_path must be .dwg or .dxf.");
        var path = FamilyPlatformSafety.LocalFile(args, "source_path", ext, demo); var bytes = File.ReadAllBytes(path); var text = Encoding.UTF8.GetString(bytes);
        return new JObject { ["evidence_id"] = "cad-" + FamilyPlatformSafety.Fingerprint(path, FamilyPlatformSafety.Sha256(path)).Substring(0, 16), ["canonical_path"] = path, ["sha256"] = FamilyPlatformSafety.Sha256(path), ["format"] = ext.TrimStart('.'), ["units"] = args.Value<string>("units") ?? "unknown", ["layers"] = args["layers"] ?? new JArray(), ["geometry"] = new JObject { ["line_count_hint"] = Regex.Matches(text, "(^|\\n)LINE(\\r?$|\\n)", RegexOptions.IgnoreCase).Count, ["polyline_count_hint"] = Regex.Matches(text, "LWPOLYLINE|POLYLINE", RegexOptions.IgnoreCase).Count, ["bounds"] = "not_computed_for_binary_source" }, ["status"] = "observed", ["mapping"] = "reference_or_allow_listed_route_only" };
    }
}

internal sealed class CadToRevitPreviewCommand : ReadCommand
{
    public CadToRevitPreviewCommand() : base("cad_to_revit_preview", "Preview an approved CAD-to-Revit Change Set mapping.") { Capability.IsWrite = true; }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); MepSafety.GuardWrite(doc, Enumerable.Empty<Element>()); if (string.IsNullOrWhiteSpace(args.Value<string>("evidence_id")) || string.IsNullOrWhiteSpace(args.Value<string>("context_id"))) throw new CommandResultException(ErrorCodes.InvalidParam, "evidence_id and context_id are required.");
        var mapping = args.Value<string>("mapping") ?? "reference_only"; var token = new PreviewToken { PreviewId = Guid.NewGuid().ToString("N"), Operation = "cad_to_revit", ArgumentsJson = args.ToString(Formatting.None), DocumentFingerprint = MepSafety.DocumentFingerprint(doc), ResourceFingerprint = FamilyPlatformSafety.Fingerprint(args.ToString(Formatting.None)), ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(McpConstants.PreviewLifetimeSeconds) }; BridgeServer.Current?.Store(token);
        return new JObject { ["preview_id"] = token.PreviewId, ["mapping"] = mapping, ["model_changed"] = false, ["status"] = mapping == "route" ? "change_set_preview_not_committed" : "reference_only", ["findings"] = new JArray("CAD geometry is not a solid clash certificate", "Unmapped geometry requires manual engineering review"), ["next"] = "Confirm Change Set explicitly before cad_to_revit_apply." };
    }
}

internal sealed class CadToRevitApplyCommand : ReadCommand
{
    public CadToRevitApplyCommand() : base("cad_to_revit_apply", "Apply a confirmed CAD Change Set atomically and verify read-back.") { Capability.IsWrite = true; }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); var id = args.Value<string>("preview_id"); if (string.IsNullOrWhiteSpace(id) || BridgeServer.Current == null || !BridgeServer.Current.TryTake(id!, out var token)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "CAD preview is missing, expired or already used.");
        if (token.ExpiresAtUtc < DateTimeOffset.UtcNow) throw new CommandResultException(ErrorCodes.PreviewExpired, "CAD preview expired; preview again.");
        if (!string.Equals(token.DocumentFingerprint, MepSafety.DocumentFingerprint(doc), StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Document fingerprint changed; preview again.");
        throw new CommandResultException(ErrorCodes.Unsupported, "CAD route apply remains gated until adapter runtime evidence and an approved Change Set record exist.");
    }
}
