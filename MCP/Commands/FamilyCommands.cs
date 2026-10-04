using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using DSCons.RevitMcp.Contracts;
using DSCons.RevitMcp.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

/// <summary>
/// Narrow, evidence-preserving Family POC. This module deliberately supports
/// only the approved four-blade axial fan; it is not an arbitrary Family API.
/// The Family document uses a category template when present, or the trusted
/// exact-year Metric Generic Model fallback with verified recategorization.
/// </summary>
internal sealed class FamilyInspectCommand : ReadCommand
{
    public FamilyInspectCommand() : base("family_inspect", "Read active Family/Project category, unit, planes, parameters, geometry and connectors.") { }
    public override JObject Execute(UIApplication app, JObject args) => FamilyData.Inspect(Document(app));
}

internal sealed class FamilyAxialFanPreviewCommand : ReadCommand
{
    public FamilyAxialFanPreviewCommand() : base("family_axial_fan_preview", "Preview the approved four-blade axial fan in a temporary Family document.") { Capability.IsWrite = true; }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var anchor = Document(app); var spec = FamilySafety.ValidateFanSpec(app.Application, anchor, args);
        var originalViewId = anchor.ActiveView.Id;
        try
        {
            var validation = FamilyBuilder.Preview(app.Application, spec);
            var token = FamilySafety.CreateToken("family_axial_fan", anchor, args, spec.ResourceFingerprint);
            BridgeServer.Current?.Store(token);
            return new JObject
            {
                ["preview_id"] = token.PreviewId, ["operation"] = token.Operation, ["expires_at_utc"] = token.ExpiresAtUtc,
                ["model_changed"] = false, ["validation_level"] = "family_document_transactiongroup_rollback",
                ["output_path"] = spec.OutputPath, ["connector_mode"] = spec.ConnectorMode,
                ["template_selection"] = new JObject { ["path"] = spec.TemplatePath, ["target_category"] = spec.TemplateCategory, ["template_category"] = spec.TemplateOriginalCategory, ["source"] = spec.TemplateSource, ["revit_version"] = spec.RevitVersion, ["category_change_required"] = spec.RequiresCategoryChange, ["category_change_mode"] = spec.RequiresCategoryChange ? "automatic_before_geometry" : "not_required", ["fallback_reason"] = string.IsNullOrWhiteSpace(spec.TemplateFallbackReason) ? null : spec.TemplateFallbackReason, ["learner_guidance"] = spec.RequiresCategoryChange ? "MCP will perform the equivalent of Create > Family Category and Parameters, then verify the target category." : "The specialized template already has the target Family Category." },
                ["validation"] = validation,
                ["next"] = "Wait for the exact user confirmation, then call family_axial_fan_apply with preview_id."
            };
        }
        finally { FamilyUiState.RestoreView(app, anchor, originalViewId); }
    }
}

internal sealed class FamilyAxialFanApplyCommand : ReadCommand
{
    public FamilyAxialFanApplyCommand() : base("family_axial_fan_apply", "Create the approved axial fan from a valid preview and read it back.") { Capability.IsWrite = true; }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var anchor = Document(app); var token = FamilySafety.TakeToken(args, "family_axial_fan", anchor);
        var spec = FamilySafety.ValidateFanSpec(app.Application, anchor, JObject.Parse(token.ArgumentsJson), true);
        FamilySafety.RequireToken(anchor, token, spec.ResourceFingerprint);
        var originalViewId = anchor.ActiveView.Id;
        try
        {
            var result = FamilyBuilder.Apply(app.Application, spec);
            result["preview_id"] = token.PreviewId; result["rolled_back"] = false;
            return result;
        }
        finally { FamilyUiState.RestoreView(app, anchor, originalViewId); }
    }
}

internal sealed class FamilyLoadPlacePreviewCommand : ReadCommand
{
    public FamilyLoadPlacePreviewCommand() : base("family_load_place_preview", "Preview Family load/place in the explicitly approved copied Project.") { Capability.IsWrite = true; }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var project = Document(app); var spec = FamilySafety.ValidatePlaceSpec(project, args);
        var validation = FamilyPlacement.Preview(project, spec);
        var token = FamilySafety.CreateToken("family_load_place", project, args, spec.ResourceFingerprint);
        BridgeServer.Current?.Store(token);
        return new JObject
        {
            ["preview_id"] = token.PreviewId, ["operation"] = token.Operation, ["expires_at_utc"] = token.ExpiresAtUtc,
            ["model_changed"] = false, ["validation_level"] = "project_transactiongroup_rollback",
            ["validation"] = validation, ["next"] = "Wait for the exact user confirmation, then call family_load_place_apply with preview_id."
        };
    }
}

internal sealed class FamilyLoadPlaceApplyCommand : ReadCommand
{
    public FamilyLoadPlaceApplyCommand() : base("family_load_place_apply", "Load/place Family from a valid preview, then read back before commit.") { Capability.IsWrite = true; }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var project = Document(app); var token = FamilySafety.TakeToken(args, "family_load_place", project);
        var spec = FamilySafety.ValidatePlaceSpec(project, JObject.Parse(token.ArgumentsJson));
        FamilySafety.RequireToken(project, token, spec.ResourceFingerprint);
        var result = FamilyPlacement.Apply(project, spec);
        result["preview_id"] = token.PreviewId; result["rolled_back"] = false;
        return result;
    }
}

internal static class FamilyUiState
{
    // Creating/closing a temporary Family document can leave Revit on Project
    // Browser. Restore the user's original project view without a transaction.
    internal static void RestoreView(UIApplication app, Document project, ElementId viewId)
    {
        try
        {
            var active = app.ActiveUIDocument;
            var view = project.GetElement(viewId) as View;
            if (active != null && ReferenceEquals(active.Document, project) && view != null && !view.IsTemplate)
                active.ActiveView = view;
        }
        catch { /* UI restoration is convenience only; it must not mask a Family result. */ }
    }
}

internal sealed class FanSpec
{
    public string TemplatePath { get; set; } = string.Empty;
    public string TemplateCategory { get; set; } = string.Empty;
    public string TemplateOriginalCategory { get; set; } = string.Empty;
    public string TemplateSource { get; set; } = string.Empty;
    public string TemplateFallbackReason { get; set; } = string.Empty;
    public bool RequiresCategoryChange { get; set; }
    public string RevitVersion { get; set; } = string.Empty;
    public string DemoDirectory { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string TypeCode { get; set; } = string.Empty;
    public double DiameterFt { get; set; }
    public double LengthFt { get; set; }
    public string ConnectorMode { get; set; } = string.Empty;
    public string ConnectorSystemClassification { get; set; } = string.Empty;
    public string ResourceFingerprint { get; set; } = string.Empty;
}

internal sealed class PlaceSpec
{
    public string FamilyPath { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public ElementId LevelId { get; set; } = ElementId.InvalidElementId;
    public XYZ Point { get; set; } = XYZ.Zero;
    public string ResourceFingerprint { get; set; } = string.Empty;
}

internal static class FamilySafety
{
    public static FanSpec ValidateFanSpec(Application application, Document anchor, JObject args, bool retainedPreview = false)
    {
        MepSafety.GuardWrite(anchor, Enumerable.Empty<Element>());
        var demo = RequireDirectory(args.Value<string>("approved_demo_directory"));
        var requestedTemplate = args.Value<string>("template_path");
        FamilyTemplateSelection selection;
        if (retainedPreview && args.Value<bool?>("__template_auto_resolved") == true)
        {
            selection = FamilyTemplateResolver.Resolve(application, "axial_fan");
            if (!string.Equals(Path.GetFullPath(requestedTemplate ?? string.Empty), selection.Path, StringComparison.OrdinalIgnoreCase))
                throw new CommandResultException(ErrorCodes.PreviewInvalid, "The automatically selected Family template changed; preview again.");
        }
        else selection = FamilyTemplateResolver.Resolve(application, "axial_fan", requestedTemplate);
        var template = selection.Path;
        args["template_path"] = template;
        args["__template_auto_resolved"] = string.IsNullOrWhiteSpace(requestedTemplate) || (retainedPreview && args.Value<bool?>("__template_auto_resolved") == true);
        args["__template_category_change_required"] = selection.RequiresCategoryChange;
        var familyName = RequireName(args.Value<string>("family_name"), "family_name");
        var typeName = RequireName(args.Value<string>("type_name"), "type_name");
        var typeCode = RequireName(args.Value<string>("type_code"), "type_code");
        var diameter = args.Value<double?>("diameter_mm") ?? 0; var length = args.Value<double?>("length_mm") ?? 0;
        if (diameter <= 0 || length <= 0) throw new CommandResultException(ErrorCodes.InvalidParam, "diameter_mm and length_mm must be positive.");
        if (args.Value<int?>("blade_count") != 4) throw new CommandResultException(ErrorCodes.InvalidParam, "Family v1 supports exactly four blades.");
        var connector = args.Value<string>("connector_mode") ?? string.Empty;
        if (connector != "none" && connector != "round_hvac") throw new CommandResultException(ErrorCodes.InvalidParam, "connector_mode must be none or round_hvac.");
        var output = Path.GetFullPath(Path.Combine(demo, familyName + ".rfa"));
        RequireInside(demo, output); if (File.Exists(output)) throw new CommandResultException(ErrorCodes.FileConflict, "The output .rfa already exists. Choose a new Family name; overwrite is blocked.");
        return new FanSpec { TemplatePath = template, TemplateCategory = selection.CategoryName, TemplateOriginalCategory = selection.TemplateCategoryName, TemplateSource = selection.Source, TemplateFallbackReason = selection.FallbackReason, RequiresCategoryChange = selection.RequiresCategoryChange, RevitVersion = selection.RevitVersion, DemoDirectory = demo, OutputPath = output, FamilyName = familyName, TypeName = typeName, TypeCode = typeCode, DiameterFt = diameter / 304.8, LengthFt = length / 304.8, ConnectorMode = connector, ResourceFingerprint = HashResource(template, demo, output, familyName, typeName, typeCode, diameter.ToString("R"), length.ToString("R"), connector) };
    }

    public static PlaceSpec ValidatePlaceSpec(Document project, JObject args)
    {
        MepSafety.GuardWrite(project, Enumerable.Empty<Element>());
        if (project.IsFamilyDocument) throw new CommandResultException(ErrorCodes.DocumentTypeInvalid, "Open the approved copied Project, not a Family document.");
        var demo = RequireDirectory(args.Value<string>("approved_demo_directory"));
        var expectedPath = RequireExistingFile(args.Value<string>("expected_project_path"), ".rvt", ErrorCodes.PathBlocked);
        var activePath = RequireExistingFile(project.PathName ?? string.Empty, ".rvt", ErrorCodes.PathBlocked);
        RequireInside(demo, expectedPath); RequireInside(demo, activePath);
        if (!string.Equals(expectedPath, activePath, StringComparison.OrdinalIgnoreCase)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "The active Project path does not match expected_project_path.");
        if (project.ActiveView.Id.Val() != args.Value<long>("expected_view_id")) throw new CommandResultException(ErrorCodes.PreviewInvalid, "The active view changed or does not match expected_view_id.");
        var family = RequireExistingFile(args.Value<string>("family_path"), ".rfa", ErrorCodes.PathBlocked); RequireInside(demo, family);
        var typeName = RequireName(args.Value<string>("type_name"), "type_name");
        var levelId = RevitIdCompatibility.Eid(args.Value<long>("level_id"));
        if (project.GetElement(levelId) is not Level) throw new CommandResultException(ErrorCodes.InvalidParam, "level_id must resolve to an existing Level in the active Project.");
        var point = args["location"] as JObject ?? throw new CommandResultException(ErrorCodes.InvalidParam, "location is required.");
        var location = new XYZ((point.Value<double?>("x_mm") ?? 0) / 304.8, (point.Value<double?>("y_mm") ?? 0) / 304.8, (point.Value<double?>("z_mm") ?? 0) / 304.8);
        return new PlaceSpec { FamilyPath = family, TypeName = typeName, LevelId = levelId, Point = location, ResourceFingerprint = HashResource(family, demo, activePath, project.ActiveView.Id.Val().ToString(), levelId.Val().ToString(), location.X.ToString("R"), location.Y.ToString("R"), location.Z.ToString("R")) };
    }

    public static PreviewToken CreateToken(string operation, Document anchor, JObject args, string resourceFingerprint) => new()
    {
        PreviewId = Guid.NewGuid().ToString("N"), Operation = operation, ArgumentsJson = args.ToString(Formatting.None),
        DocumentFingerprint = MepSafety.DocumentFingerprint(anchor), TargetFingerprint = string.Empty, ResourceFingerprint = resourceFingerprint,
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(McpConstants.PreviewLifetimeSeconds)
    };

    public static PreviewToken TakeToken(JObject args, string operation, Document anchor)
    {
        var id = args.Value<string>("preview_id");
        if (string.IsNullOrWhiteSpace(id)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Preview does not exist, was already used, or belongs to another Revit session.");
        var bridge = BridgeServer.Current ?? throw new CommandResultException(ErrorCodes.PreviewInvalid, "Preview does not exist, was already used, or belongs to another Revit session.");
        if (!bridge.TryTake(id!, out var token)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Preview does not exist, was already used, or belongs to another Revit session.");
        if (!string.Equals(token.Operation, operation, StringComparison.OrdinalIgnoreCase)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "preview_id belongs to another Family operation.");
        if (token.ExpiresAtUtc < DateTimeOffset.UtcNow) throw new CommandResultException(ErrorCodes.PreviewExpired, "Preview expired after 90 seconds; preview again.");
        return token;
    }

    public static void RequireToken(Document anchor, PreviewToken token, string resourceFingerprint)
    {
        if (!string.Equals(token.DocumentFingerprint, MepSafety.DocumentFingerprint(anchor), StringComparison.Ordinal) || !string.Equals(token.ResourceFingerprint, resourceFingerprint, StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.PreviewInvalid, "Document, template, Family file, output path, view, Level or location changed after Preview. Preview again.");
    }

    private static string RequireDirectory(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new CommandResultException(ErrorCodes.PathBlocked, "approved_demo_directory is required.");
        var value = raw!; if (!Path.IsPathRooted(value) || IsUnc(value)) throw new CommandResultException(ErrorCodes.PathBlocked, "approved_demo_directory must be an existing local absolute directory.");
        var path = Path.GetFullPath(value); if (!Directory.Exists(path)) throw new CommandResultException(ErrorCodes.PathBlocked, "approved_demo_directory does not exist."); return path;
    }
    private static string RequireExistingFile(string? raw, string extension, string code)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new CommandResultException(code, "A local absolute path is required.");
        var value = raw!; if (!Path.IsPathRooted(value) || IsUnc(value)) throw new CommandResultException(code, "A local absolute path is required.");
        var path = Path.GetFullPath(value); if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) throw new CommandResultException(code, "Required file is missing or has the wrong extension: " + extension); return path;
    }
    private static string RequireName(string? value, string label)
    {
        var cleaned = value?.Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) throw new CommandResultException(ErrorCodes.InvalidParam, label + " is invalid.");
        var validName = cleaned!;
        if (validName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || validName.Contains("..")) throw new CommandResultException(ErrorCodes.InvalidParam, label + " is invalid.");
        return validName;
    }
    private static bool IsUnc(string path) => path.StartsWith("\\\\", StringComparison.Ordinal) || new Uri(path).IsUnc;
    private static void RequireInside(string root, string path)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new CommandResultException(ErrorCodes.PathBlocked, "Path is outside approved_demo_directory.");
    }
    private static string HashResource(params string[] values)
    {
        using var sha = SHA256.Create();
        var input = string.Join("|", values.Select(value => File.Exists(value) ? value + ":" + HashFile(value) : value));
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(input)));
    }
    private static string HashFile(string path)
    {
        // A saved Project remains open in Revit during load/place validation.
        // Read it non-destructively with sharing enabled so its fingerprint can
        // still guard the preview instead of failing on Revit's own file lock.
        using var sha = SHA256.Create();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToBase64String(sha.ComputeHash(stream));
    }
}

internal static class FamilyBuilder
{
    public static JObject Preview(Application application, FanSpec spec)
    {
        Document? family = null;
        try
        {
            family = application.NewFamilyDocument(spec.TemplatePath); var categoryChanged = EnsureMechanicalEquipment(family, spec.RequiresCategoryChange);
            using var group = new TransactionGroup(family, "Validate DSCons axial fan preview"); group.Start();
            var simulated = Create(family, spec); group.RollBack();
            return new JObject { ["status"] = "passed", ["model_changed"] = false, ["category_assignment"] = new JObject { ["target"] = "Mechanical Equipment", ["changed_from_generic"] = categoryChanged, ["verified"] = true }, ["simulated_read_back"] = simulated, ["note"] = "The temporary Family document was rolled back and closed without saving." };
        }
        catch (CommandResultException) { throw; }
        catch (Exception ex) { throw new CommandResultException(ErrorCodes.TransactionFailed, "Family preview rolled back: " + ex.Message); }
        finally { if (family != null) try { family.Close(false); } catch { } }
    }

    public static JObject Apply(Application application, FanSpec spec)
    {
        Document? family = null; string? staging = null;
        try
        {
            family = application.NewFamilyDocument(spec.TemplatePath); var categoryChanged = EnsureMechanicalEquipment(family, spec.RequiresCategoryChange);
            JObject created;
            using (var group = new TransactionGroup(family, "Create DSCons axial fan"))
            {
                group.Start(); created = Create(family, spec); group.Assimilate();
            }
            staging = Path.Combine(spec.DemoDirectory, ".dscons-" + Guid.NewGuid().ToString("N") + ".rfa");
            family.SaveAs(staging, new SaveAsOptions { OverwriteExistingFile = false }); family.Close(false); family = null;
            var reopened = application.OpenDocumentFile(staging);
            JObject verification;
            try { verification = FamilyData.Inspect(reopened); }
            finally { reopened.Close(false); }
            if (!(verification["is_family_document"]?.Value<bool>() ?? false)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Saved output did not reopen as a Family document.");
            File.Move(staging, spec.OutputPath); staging = null;
            return new JObject { ["family_path"] = spec.OutputPath, ["sha256"] = HashFile(spec.OutputPath), ["category_assignment"] = new JObject { ["target"] = "Mechanical Equipment", ["changed_from_generic"] = categoryChanged, ["verified"] = true }, ["created"] = created, ["verification"] = new JObject { ["verified"] = true, ["mode"] = "post_commit_read_back", ["family"] = verification } };
        }
        catch (CommandResultException) { throw; }
        catch (Exception ex) { throw new CommandResultException(ErrorCodes.TransactionFailed, "Family creation rolled back or staging file was removed: " + ex.Message); }
        finally
        {
            if (family != null) try { family.Close(false); } catch { }
            if (!string.IsNullOrWhiteSpace(staging) && File.Exists(staging)) try { File.Delete(staging); } catch { }
        }
    }

    private static JObject Create(Document family, FanSpec spec)
    {
        using var transaction = new Transaction(family, "Create axial fan geometry"); transaction.Start();
        var manager = family.FamilyManager; var type = manager.NewType(spec.TypeName); manager.CurrentType = type;
        var diameter = AddLength(manager, "Đường kính"); var length = AddLength(manager, "Chiều dài");
        var halfLength = AddLength(manager, "_Nửa chiều dài"); var negativeHalfLength = AddLength(manager, "_Âm nửa chiều dài"); var connectorRadius = AddLength(manager, "_Bán kính connector");
        var outerRadius = AddLength(manager, "_Bán kính ngoài"); var innerRadius = AddLength(manager, "_Bán kính trong"); var hubRadiusParameter = AddLength(manager, "_Bán kính hub");
        var bladeWidthParameter = AddLength(manager, "_Bản rộng cánh"); var hubHalfLength = AddLength(manager, "_Nửa dài hub"); var negativeHubHalfLength = AddLength(manager, "_Âm nửa dài hub");
        var bladeHalfThickness = AddLength(manager, "_Nửa dày cánh"); var negativeBladeHalfThickness = AddLength(manager, "_Âm nửa dày cánh");
        var deviceName = AddText(manager, "Tên thiết bị"); var typeCode = AddText(manager, "Mã loại");
        manager.Set(diameter, spec.DiameterFt); manager.Set(length, spec.LengthFt); manager.Set(deviceName, "Quạt hướng trục"); manager.Set(typeCode, spec.TypeCode);
        manager.SetFormula(halfLength, "Chiều dài / 2"); manager.SetFormula(negativeHalfLength, "-_Nửa chiều dài"); manager.SetFormula(connectorRadius, "Đường kính / 2");
        manager.SetFormula(outerRadius, "Đường kính / 2"); manager.SetFormula(innerRadius, "Đường kính * 0.45"); manager.SetFormula(hubRadiusParameter, "Đường kính * 0.15"); manager.SetFormula(bladeWidthParameter, "Đường kính * 0.10");
        manager.SetFormula(hubHalfLength, "Chiều dài * 0.125"); manager.SetFormula(negativeHubHalfLength, "-_Nửa dài hub"); manager.SetFormula(bladeHalfThickness, "Chiều dài * 0.04"); manager.SetFormula(negativeBladeHalfThickness, "-_Nửa dày cánh");
        var plane = SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(XYZ.BasisX, XYZ.Zero));
        var outer = spec.DiameterFt * 0.5; var inner = spec.DiameterFt * 0.45; var hubRadius = spec.DiameterFt * 0.15; var bladeWidth = spec.DiameterFt * 0.10;
        var casing = family.FamilyCreate.NewExtrusion(true, RingProfile(outer, inner), plane, spec.LengthFt); ApplyQualityRole(family, casing, "Casing", true, true, true);
        casing.StartOffset = -spec.LengthFt / 2; casing.EndOffset = spec.LengthFt / 2;
        Associate(manager, casing.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM), negativeHalfLength); Associate(manager, casing.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), halfLength);
        family.Regenerate(); LabelRadialDimension(family, casing, 0, outerRadius, new XYZ(0, outer + 0.1, 0), "outer casing"); LabelRadialDimension(family, casing, 1, innerRadius, new XYZ(0, inner + 0.1, 0), "inner casing");
        var hubLength = spec.LengthFt * 0.25; var hub = family.FamilyCreate.NewExtrusion(true, CircleProfile(hubRadius), plane, hubLength); ApplyQualityRole(family, hub, "MotorHub", false, true, true); hub.StartOffset = -hubLength / 2; hub.EndOffset = hubLength / 2;
        Associate(manager, hub.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM), negativeHubHalfLength); Associate(manager, hub.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), hubHalfLength);
        family.Regenerate(); LabelRadialDimension(family, hub, 0, hubRadiusParameter, new XYZ(0, hubRadius + 0.1, 0), "hub");
        var bladeThickness = spec.LengthFt * 0.08;
        foreach (var profile in BladeProfiles(hubRadius, inner, bladeWidth))
        {
            var blade = family.FamilyCreate.NewExtrusion(true, profile, plane, bladeThickness); ApplyQualityRole(family, blade, "BladeFine", false, false, true); blade.StartOffset = -bladeThickness / 2; blade.EndOffset = bladeThickness / 2;
            Associate(manager, blade.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM), negativeBladeHalfThickness); Associate(manager, blade.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), bladeHalfThickness);
        }
        if (spec.ConnectorMode == "round_hvac") TryCreateRoundConnectors(family, casing, diameter, spec.DiameterFt, spec.ConnectorSystemClassification);
        var flex = VerifyFlex(family, manager, diameter, length, spec.DiameterFt, spec.LengthFt, spec.ConnectorMode);
        var quality = VerifyLod300(family, spec.ConnectorMode);
        flex["quality_validation"] = quality;
        var status = transaction.Commit(); if (status != TransactionStatus.Committed) throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rejected the axial-fan Family transaction.");
        return new JObject { ["family_name"] = spec.FamilyName, ["type_name"] = spec.TypeName, ["type_code"] = spec.TypeCode, ["axis"] = "X", ["forms"] = 6, ["connector_mode"] = spec.ConnectorMode, ["flex_validation"] = flex, ["parameters"] = new JObject { ["Tên thiết bị"] = "Quạt hướng trục", ["Đường kính_mm"] = Math.Round(spec.DiameterFt * 304.8, 2), ["Chiều dài_mm"] = Math.Round(spec.LengthFt * 304.8, 2) } };
    }

    private static bool EnsureMechanicalEquipment(Document doc, bool allowGenericFallback)
    {
        return FamilyTemplateResolver.EnsureTargetCategory(doc, BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment", allowGenericFallback);
    }
    private static FamilyParameter AddLength(FamilyManager manager, string name)
    {
#if REVIT2019 || REVIT2020 || REVIT2021
#pragma warning disable CS0618
        return manager.AddParameter(name, BuiltInParameterGroup.PG_GEOMETRY, ParameterType.Length, false);
#pragma warning restore CS0618
#else
        return manager.AddParameter(name, GroupTypeId.Geometry, SpecTypeId.Length, false);
#endif
    }
    private static FamilyParameter AddText(FamilyManager manager, string name)
    {
#if REVIT2019 || REVIT2020 || REVIT2021
#pragma warning disable CS0618
        return manager.AddParameter(name, BuiltInParameterGroup.PG_IDENTITY_DATA, ParameterType.Text, false);
#pragma warning restore CS0618
#else
        return manager.AddParameter(name, GroupTypeId.IdentityData, SpecTypeId.String.Text, false);
#endif
    }
    private static void Associate(FamilyManager manager, Parameter? elementParameter, FamilyParameter familyParameter)
    {
        if (elementParameter != null && manager.CanElementParameterBeAssociated(elementParameter)) manager.AssociateElementParameterToFamilyParameter(elementParameter, familyParameter);
    }
    private static void LabelRadialDimension(Document family, Extrusion extrusion, int loopIndex, FamilyParameter parameter, XYZ textPosition, string part)
    {
        var loops = extrusion.Sketch.Profile.Cast<CurveArray>().ToList();
        if (loopIndex < 0 || loopIndex >= loops.Count) throw new CommandResultException(ErrorCodes.VerificationFailed, "Axial fan profile did not retain the expected circular loop.");
        var sketchReference = loops[loopIndex].Cast<Curve>().OfType<Arc>().FirstOrDefault()?.Reference;
        if (sketchReference == null) throw new CommandResultException(ErrorCodes.VerificationFailed, "Axial fan circle is missing a dimensionable sketch reference.");
        var modelCurve = family.GetElement(sketchReference.ElementId) as ModelCurve;
        var reference = modelCurve?.GeometryCurve.Reference;
        if (reference == null) throw new CommandResultException(ErrorCodes.VerificationFailed, "Axial fan circle did not resolve to a dimensionable ModelCurve.");
        var dimensionView = new FilteredElementCollector(family).OfClass(typeof(ViewSection)).Cast<ViewSection>()
            .FirstOrDefault(view => !view.IsTemplate && Math.Abs(view.ViewDirection.X) > 0.99);
        if (dimensionView == null) throw new CommandResultException(ErrorCodes.VerificationFailed, "Family template does not provide an elevation along the axial X direction for the radial dimension.");
        try
        {
            var dimension = family.FamilyCreate.NewRadialDimension(dimensionView, reference, textPosition);
            if (dimension == null) throw new InvalidOperationException("Revit returned null.");
            dimension.FamilyLabel = parameter;
        }
        catch (Exception ex)
        {
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected the radial Family dimension for " + part + ": " + ex.Message);
        }
    }
    private static JObject VerifyFlex(Document family, FamilyManager manager, FamilyParameter diameter, FamilyParameter length, double originalDiameter, double originalLength, string connectorMode = "none")
    {
        family.Regenerate(); var before = FormBounds(family);
        try
        {
            manager.Set(diameter, originalDiameter * 1.10); manager.Set(length, originalLength * 1.10); family.Regenerate();
            var after = FormBounds(family);
            var radialChanged = Math.Abs(after["y_span"]!.Value<double>() - before["y_span"]!.Value<double>()) > 0.001 || Math.Abs(after["z_span"]!.Value<double>() - before["z_span"]!.Value<double>()) > 0.001;
            var axialChanged = Math.Abs(after["x_span"]!.Value<double>() - before["x_span"]!.Value<double>()) > 0.001;
            if (!radialChanged || !axialChanged) throw new CommandResultException(ErrorCodes.VerificationFailed, "Family parameters changed but axial-fan geometry did not flex in every required direction.");
            if (connectorMode == "round_hvac") VerifyConnectorRadius(family, originalDiameter * 1.10 * 0.5);
            return new JObject { ["verified"] = true, ["mode"] = "temporary_parameter_flex_read_back", ["before_ft"] = before, ["after_ft"] = after };
        }
        finally
        {
            manager.Set(diameter, originalDiameter); manager.Set(length, originalLength); family.Regenerate();
        }
    }
    private static JObject FormBounds(Document family)
    {
        var boxes = new FilteredElementCollector(family).OfClass(typeof(Extrusion)).Cast<Extrusion>().Select(form => form.get_BoundingBox(null)).Where(box => box != null).ToList();
        if (boxes.Count != 6) throw new CommandResultException(ErrorCodes.VerificationFailed, "Family flex read-back did not find all six axial-fan forms.");
        var minX = boxes.Min(box => box!.Min.X); var minY = boxes.Min(box => box!.Min.Y); var minZ = boxes.Min(box => box!.Min.Z);
        var maxX = boxes.Max(box => box!.Max.X); var maxY = boxes.Max(box => box!.Max.Y); var maxZ = boxes.Max(box => box!.Max.Z);
        return new JObject { ["x_span"] = maxX - minX, ["y_span"] = maxY - minY, ["z_span"] = maxZ - minZ };
    }
    private static CurveArrArray CircleProfile(double radius) { var result = new CurveArrArray(); result.Append(Circle(radius)); return result; }
    private static CurveArrArray RingProfile(double outer, double inner) { var result = new CurveArrArray(); result.Append(Circle(outer)); result.Append(Circle(inner)); return result; }
    private static CurveArray Circle(double radius)
    {
        var top = new XYZ(0, radius, 0); var bottom = new XYZ(0, -radius, 0); var right = new XYZ(0, 0, radius); var left = new XYZ(0, 0, -radius);
        var curves = new CurveArray(); curves.Append(Arc.Create(top, bottom, right)); curves.Append(Arc.Create(bottom, top, left)); return curves;
    }
    private static IEnumerable<CurveArrArray> BladeProfiles(double hub, double inner, double width)
    {
        yield return Rectangle(hub, inner, -width / 2, width / 2, true); yield return Rectangle(-inner, -hub, -width / 2, width / 2, true);
        yield return Rectangle(hub, inner, -width / 2, width / 2, false); yield return Rectangle(-inner, -hub, -width / 2, width / 2, false);
    }
    private static CurveArrArray Rectangle(double firstMin, double firstMax, double secondMin, double secondMax, bool firstIsY)
    {
        XYZ P(double a, double b) => firstIsY ? new XYZ(0, a, b) : new XYZ(0, b, a);
        var a = P(firstMin, secondMin); var b = P(firstMax, secondMin); var c = P(firstMax, secondMax); var d = P(firstMin, secondMax);
        var loop = new CurveArray(); loop.Append(Line.CreateBound(a, b)); loop.Append(Line.CreateBound(b, c)); loop.Append(Line.CreateBound(c, d)); loop.Append(Line.CreateBound(d, a)); var result = new CurveArrArray(); result.Append(loop); return result;
    }
    private static void ApplyQualityRole(Document family, GenericForm form, string role, bool coarse, bool medium, bool fine)
    {
        var category = family.Settings.Categories.get_Item(BuiltInCategory.OST_MechanicalEquipment);
        var subcategoryName = "DSCons LOD300 " + role;
        var subcategory = category.SubCategories.Contains(subcategoryName)
            ? category.SubCategories.get_Item(subcategoryName)
            : family.Settings.Categories.NewSubcategory(category, subcategoryName);
        form.Subcategory = subcategory;
        var visibility = new FamilyElementVisibility(FamilyElementVisibilityType.Model) { IsShownInCoarse = coarse, IsShownInMedium = medium, IsShownInFine = fine };
        form.SetVisibility(visibility);
    }

    private static JObject VerifyLod300(Document family, string connectorMode)
    {
        var forms = new FilteredElementCollector(family).OfClass(typeof(GenericForm)).Cast<GenericForm>().ToList();
        var roles = forms.GroupBy(item => item.Subcategory?.Name ?? "unassigned").ToDictionary(group => group.Key, group => group.Count());
        var bladeCount = roles.TryGetValue("DSCons LOD300 BladeFine", out var countedBlades) ? countedBlades : 0;
        if (!roles.ContainsKey("DSCons LOD300 Casing") || !roles.ContainsKey("DSCons LOD300 MotorHub") || bladeCount != 4)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "LOD300 role/subcategory assignment is incomplete.");
        var connectorCount = new FilteredElementCollector(family).OfClass(typeof(ConnectorElement)).GetElementCount();
        if ((connectorMode == "round_hvac" && connectorCount != 2) || (connectorMode == "none" && connectorCount != 0))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Connector count does not match the confirmed axial fan spec.");
        return new JObject { ["verified"] = true, ["envelope_tolerance_mm"] = 1, ["roles"] = JObject.FromObject(roles), ["connector_count"] = connectorCount, ["visibility"] = new JObject { ["coarse"] = "casing", ["medium"] = "casing_motor_hub", ["fine"] = "casing_motor_hub_blades" }, ["maintenance_clearance"] = "not_modelled_and_excluded_from_quantity" };
    }

    private static void TryCreateRoundConnectors(Document family, Extrusion casing, FamilyParameter diameter, double initialDiameterFt, string confirmedClassification)
    {
        try
        {
            var faces = casing.get_Geometry(new Options { ComputeReferences = true }).OfType<Solid>().SelectMany(s => s.Faces.Cast<Face>()).OfType<PlanarFace>().Where(face => Math.Abs(Math.Abs(face.FaceNormal.X) - 1) < 0.001).OrderBy(face => face.FaceNormal.X).ToList();
            if (faces.Count != 2) throw new CommandResultException(ErrorCodes.VerificationFailed, "Axial casing must expose exactly two stable axial port faces.");
            var classification = string.IsNullOrWhiteSpace(confirmedClassification) ? "SupplyAir" : confirmedClassification;
            if (classification is not ("SupplyAir" or "ReturnAir" or "ExhaustAir" or "OtherAir" or "Global")) throw new CommandResultException(ErrorCodes.InvalidParam, "Confirmed duct classification is invalid.");
            var systemType = (DuctSystemType)Enum.Parse(typeof(DuctSystemType), classification);
            var connectors = new List<ConnectorElement>();
            for (var index = 0; index < faces.Count; index++)
            {
                var face = faces[index]; var connector = ConnectorElement.CreateDuctConnector(family, systemType, ConnectorProfileType.Round, face.Reference);
                SetConnectorRole(connector, index == 0 ? "air_inlet" : "air_outlet");
                var diameterParameter = connector.get_Parameter(BuiltInParameter.CONNECTOR_DIAMETER);
                if (diameterParameter == null || diameterParameter.IsReadOnly || !diameterParameter.Set(initialDiameterFt)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Connector diameter parameter could not be initialized.");
                if (!family.FamilyManager.CanElementParameterBeAssociated(diameterParameter)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Connector Diameter does not support Family parameter association.");
                family.FamilyManager.AssociateElementParameterToFamilyParameter(diameterParameter, diameter);
                var associated = family.FamilyManager.GetAssociatedFamilyParameter(diameterParameter);
                if (associated == null || !string.Equals(associated.Definition.Name, diameter.Definition.Name, StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Connector Diameter association was rejected by Revit.");
                var classificationProperty = connector.GetType().GetProperty("SystemClassification");
                if (classificationProperty?.CanWrite == true) classificationProperty.SetValue(connector, Enum.Parse(classificationProperty.PropertyType, classification));
                EnsureOutwardDirection(connector, face.FaceNormal);
                connectors.Add(connector);
            }
            connectors[1].AssignAsPrimary(); connectors[0].SetLinkedConnectorElement(connectors[1]);
        }
        catch (Exception ex) { throw new CommandResultException(ErrorCodes.TransactionFailed, "round_hvac connector creation failed; the Family transaction was rolled back. " + ex.Message); }
    }

    private static void EnsureOutwardDirection(ConnectorElement connector, XYZ faceNormal)
    {
        var direction = connector.GetType().GetProperty("Direction")?.GetValue(connector) as XYZ;
        if (direction != null && direction.DotProduct(faceNormal) < 0.999) connector.FlipDirection();
        direction = connector.GetType().GetProperty("Direction")?.GetValue(connector) as XYZ;
        if (direction != null && direction.DotProduct(faceNormal) < 0.999) throw new CommandResultException(ErrorCodes.VerificationFailed, "Connector normal is not outward from its dedicated port face.");
    }

    private static void SetConnectorRole(ConnectorElement connector, string role)
    {
        var description = connector.get_Parameter(BuiltInParameter.RBS_CONNECTOR_DESCRIPTION);
        if (description == null || description.IsReadOnly || !description.Set(role))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Connector role could not be stored in its Description parameter.");
    }

    private static void VerifyConnectorRadius(Document family, double expectedRadiusFt)
    {
        var connectors = new FilteredElementCollector(family).OfClass(typeof(ConnectorElement)).Cast<ConnectorElement>().ToList();
        if (connectors.Count != 2 || connectors.Any(item => Math.Abs(item.Radius - expectedRadiusFt) > 0.003281))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Duct connector radius is not associated with the Diameter Family parameter within 1 mm. Expected ft=" + expectedRadiusFt.ToString("R") + "; actual ft=" + string.Join(",", connectors.Select(item => item.Radius.ToString("R"))));
    }
    private static string HashFile(string path) { using var sha = SHA256.Create(); using var stream = File.OpenRead(path); return Convert.ToBase64String(sha.ComputeHash(stream)); }
}

internal static class FamilyPlacement
{
    public static JObject Preview(Document project, PlaceSpec spec)
    {
        using var revisionSuppression = DocumentRevisionTracker.Suppress();
        using var group = new TransactionGroup(project, "Validate DSCons Family load/place preview"); group.Start();
        try
        {
            JObject simulated; using (var transaction = new Transaction(project, "Simulate Family load/place")) { transaction.Start(); simulated = LoadAndPlace(project, spec); if (transaction.Commit() != TransactionStatus.Committed) throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rejected the load/place preview."); }
            group.RollBack(); return new JObject { ["status"] = "passed", ["model_changed"] = false, ["simulated_result"] = simulated };
        }
        catch { group.RollBack(); throw; }
    }
    public static JObject Apply(Document project, PlaceSpec spec)
    {
        using var group = new TransactionGroup(project, "DSCons Family load/place"); group.Start();
        try
        {
            JObject result; using (var transaction = new Transaction(project, "Apply Family load/place")) { transaction.Start(); result = LoadAndPlace(project, spec); if (transaction.Commit() != TransactionStatus.Committed) throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rejected the Family load/place transaction."); }
            var instance = project.GetElement(RevitIdCompatibility.Eid(result.Value<long>("instance_id"))) as FamilyInstance ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Placed Family instance could not be read back.");
            result["verification"] = new JObject { ["verified"] = true, ["mode"] = "post_commit_read_back", ["family"] = instance.Symbol.FamilyName, ["type"] = instance.Symbol.Name, ["instance_id"] = instance.Id.Val(), ["level_id"] = instance.LevelId.Val(), ["location_mm"] = new JObject { ["x"] = Math.Round(((instance.Location as LocationPoint)?.Point.X ?? 0) * 304.8, 2), ["y"] = Math.Round(((instance.Location as LocationPoint)?.Point.Y ?? 0) * 304.8, 2), ["z"] = Math.Round(((instance.Location as LocationPoint)?.Point.Z ?? 0) * 304.8, 2) } };
            group.Assimilate(); return result;
        }
        catch (CommandResultException) { group.RollBack(); throw; }
        catch (Exception ex) { group.RollBack(); throw new CommandResultException(ErrorCodes.TransactionFailed, "Family load/place rolled back: " + ex.Message); }
    }
    internal static JObject LoadAndPlace(Document project, PlaceSpec spec)
    {
        var loaded = project.LoadFamily(spec.FamilyPath, new RejectOverwriteFamilyLoadOptions(), out var family); if (!loaded || family == null) throw new CommandResultException(ErrorCodes.FileConflict, "Family could not be loaded safely. A Family with the same name may already exist.");
        var symbol = new FilteredElementCollector(project).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault(item => item.Family.Id == family.Id && string.Equals(item.Name, spec.TypeName, StringComparison.Ordinal));
        if (symbol == null) throw new CommandResultException(ErrorCodes.VerificationFailed, "Loaded Family does not contain the requested Type."); if (!symbol.IsActive) symbol.Activate();
        var instance = project.Create.NewFamilyInstance(spec.Point, symbol, project.GetElement(spec.LevelId) as Level, StructuralType.NonStructural);
        return new JObject { ["family"] = family.Name, ["type"] = symbol.Name, ["instance_id"] = instance.Id.Val(), ["level_id"] = spec.LevelId.Val() };
    }
    private sealed class RejectOverwriteFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues) { overwriteParameterValues = false; return false; }
        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues) { source = FamilySource.Family; overwriteParameterValues = false; return false; }
    }
}

internal static class FamilyData
{
    public static JObject Inspect(Document doc)
    {
        var planes = new FilteredElementCollector(doc).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>().Select(item => FamilyBlueprintCompiler.ReferencePlaneSnapshot(doc, item)).ToList();
        JObject SketchSignature(Sketch? sketch)
        {
            if (sketch == null) return new JObject { ["shape"] = "missing", ["loop_count"] = 0, ["verified"] = false };
            var loops = sketch.Profile.Cast<CurveArray>().Select(loop =>
            {
                var curves = loop.Cast<Curve>().ToList(); var lines = curves.Count(item => item is Line); var arcs = curves.Count(item => item is Arc); var other = curves.Count - lines - arcs;
                var shape = lines == 4 && arcs == 0 && other == 0 ? "rectangle" : lines == 0 && arcs == 2 && other == 0 ? "circle" : lines == 2 && arcs == 2 && other == 0 ? "oval" : "custom_closed";
                return new JObject { ["shape"] = shape, ["curve_count"] = curves.Count, ["line_count"] = lines, ["arc_count"] = arcs, ["other_curve_count"] = other };
            }).ToList();
            var overall = loops.Count == 2 && loops.All(item => item.Value<string>("shape") == "circle") ? "ring" : loops.Count == 1 ? loops[0].Value<string>("shape") ?? "custom_closed" : "multi_loop";
            return new JObject { ["shape"] = overall, ["loop_count"] = loops.Count, ["loops"] = new JArray(loops), ["verified"] = loops.Count > 0 };
        }
        JObject FormProfileSignature(GenericForm form) => form switch
        {
            Extrusion extrusion => new JObject { ["primary"] = SketchSignature(extrusion.Sketch) },
            Revolution revolution => new JObject { ["primary"] = SketchSignature(revolution.Sketch) },
            Sweep sweep => new JObject { ["primary"] = SketchSignature(sweep.ProfileSketch) },
            Blend blend => new JObject { ["primary"] = SketchSignature(blend.BottomSketch), ["secondary"] = SketchSignature(blend.TopSketch) },
            SweptBlend sweptBlend => new JObject { ["primary"] = SketchSignature(sweptBlend.BottomSketch), ["secondary"] = SketchSignature(sweptBlend.TopSketch) },
            _ => new JObject { ["primary"] = new JObject { ["shape"] = "unsupported", ["verified"] = false } }
        };
        var forms = new FilteredElementCollector(doc).OfClass(typeof(GenericForm)).Cast<GenericForm>().Select(item =>
        {
            var visibility = item.GetVisibility(); var bounds = item.get_BoundingBox(null); var material = item.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM); var visible = item.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM);
            var visibilityParameter = visible == null || !doc.IsFamilyDocument ? null : doc.FamilyManager.GetAssociatedFamilyParameter(visible);
            var materialParameter = material == null || !doc.IsFamilyDocument ? null : doc.FamilyManager.GetAssociatedFamilyParameter(material);
            var subcategory = item.Subcategory?.Name;
            var presentationSubcategory = item.Subcategory == null ? null : FamilyBlueprintCompiler.PresentationSubcategorySnapshot(doc, item.Subcategory);
            var coordinationPurpose = subcategory switch
            {
                "DSCons Coordination Maintenance" => "maintenance_clearance", "DSCons Coordination Access" => "access_clearance",
                "DSCons Coordination Service" => "service_clearance", "DSCons Coordination Installation" => "installation_clearance",
                "DSCons Coordination Removal Path" => "removal_path", "DSCons Coordination Operation Swing" => "operation_swing",
                "DSCons Coordination Connection Interface" => "connection_interface", "DSCons Coordination Support Interface" => "support_interface", _ => null
            };
            return new JObject
            {
                ["id"] = item.Id.Val(), ["class"] = item.GetType().Name, ["is_solid"] = item.IsSolid,
                ["role_or_subcategory"] = subcategory, ["presentation_subcategory"] = presentationSubcategory, ["coordination_purpose"] = coordinationPurpose,
                ["coordination_semantic_intent"] = coordinationPurpose == null ? null : "non_physical_coordination_geometry",
                ["quantity_exclusion_certified"] = coordinationPurpose == null ? null : false,
                ["visibility"] = new JObject { ["coarse"] = visibility.IsShownInCoarse, ["medium"] = visibility.IsShownInMedium, ["fine"] = visibility.IsShownInFine, ["front_back"] = visibility.IsShownInFrontBack, ["left_right"] = visibility.IsShownInLeftRight, ["plan_rcp"] = visibility.IsShownInPlanRCPCut, ["only_when_cut"] = visibility.IsShownOnlyWhenCut },
                ["visibility_parameter"] = visibilityParameter == null ? null : new JObject { ["name"] = visibilityParameter.Definition.Name, ["is_instance"] = visibilityParameter.IsInstance, ["verified"] = true },
                ["material_id"] = material?.AsElementId().Val(), ["material_associated"] = materialParameter != null,
                ["material_associated_parameter"] = materialParameter == null ? null : new JObject { ["name"] = materialParameter.Definition.Name, ["is_instance"] = materialParameter.IsInstance, ["verified"] = true },
                ["profile_signature"] = FormProfileSignature(item),
                ["path"] = item is Sweep sweep ? FamilyBlueprintCompiler.SweepPathSnapshot(sweep) : null,
                ["bounds_mm"] = bounds == null ? null : new JObject { ["min"] = new JObject { ["x"] = Math.Round(bounds.Min.X * 304.8, 3), ["y"] = Math.Round(bounds.Min.Y * 304.8, 3), ["z"] = Math.Round(bounds.Min.Z * 304.8, 3) }, ["max"] = new JObject { ["x"] = Math.Round(bounds.Max.X * 304.8, 3), ["y"] = Math.Round(bounds.Max.Y * 304.8, 3), ["z"] = Math.Round(bounds.Max.Z * 304.8, 3) } }
            };
        }).ToList();
        var connectors = new FilteredElementCollector(doc).OfClass(typeof(ConnectorElement)).Cast<ConnectorElement>().Select(FamilyConnectorReadBack.Describe).ToList();
        var parameters = doc.IsFamilyDocument ? doc.FamilyManager.GetParameters().Select((item, orderIndex) =>
        {
            var external = item.Definition as ExternalDefinition;
            var hideWhenNoValue = external?.GetType().GetProperty("HideWhenNoValue")?.GetValue(external) as bool?;
            return new JObject { ["name"] = item.Definition.Name, ["order_index"] = orderIndex, ["is_instance"] = item.IsInstance, ["formula"] = item.Formula, ["storage_type"] = item.StorageType.ToString(), ["group"] = ParameterGroupKey(item.Definition), ["is_shared"] = item.IsShared, ["shared_guid"] = item.IsShared ? item.GUID.ToString("D") : null, ["description"] = external?.Description, ["visible"] = external?.Visible, ["user_modifiable"] = external?.UserModifiable, ["hide_when_no_value"] = hideWhenNoValue, ["associated_element_parameter_count"] = item.AssociatedParameters?.Size ?? 0 };
        }).ToList() : new List<JObject>();
        var types = doc.IsFamilyDocument ? doc.FamilyManager.Types.Cast<FamilyType>().Select(item => item.Name).ToList() : new List<string>();
        var identityData = doc.IsFamilyDocument ? FamilyBlueprintCompiler.BuiltInIdentityDataSnapshot(doc) : null;
        var placement = doc.IsFamilyDocument ? doc.OwnerFamily?.FamilyPlacementType.ToString() : null;
        var lookupTables = new List<string>();
        if (doc.IsFamilyDocument && doc.OwnerFamily != null)
        {
            var sizeTableManager = FamilySizeTableManager.GetFamilySizeTableManager(doc, doc.OwnerFamily.Id);
            if (sizeTableManager != null) lookupTables = sizeTableManager.GetAllSizeTableNames().OrderBy(name => name, StringComparer.Ordinal).ToList();
        }
        var primitives = forms.GroupBy(item => item.Value<string>("class") ?? "Unknown").ToDictionary(group => group.Key, group => group.Count());
        var materials = doc.IsFamilyDocument ? new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().Select(item => FamilyMaterialReadBack.Describe(doc, item)).ToList() : new List<JObject>();
        var lightSource = doc.IsFamilyDocument ? FamilyLightingReadBack.Describe(doc) : null;
        var presentationSubcategories = doc.IsFamilyDocument && doc.OwnerFamily?.FamilyCategory != null
            ? doc.OwnerFamily.FamilyCategory.SubCategories.Cast<Category>().Select(item => FamilyBlueprintCompiler.PresentationSubcategorySnapshot(doc, item)).ToList()
            : new List<JObject>();
        JObject DescribeCurve(CurveElement item, FamilyElementVisibility visibility, string kind)
        {
            var visible = item.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM);
            var associated = visible == null || !doc.IsFamilyDocument ? null : doc.FamilyManager.GetAssociatedFamilyParameter(visible);
            var curve = item.GeometryCurve;
            JObject PointMm(XYZ point) => new() { ["x_mm"] = Math.Round(point.X * 304.8, 3), ["y_mm"] = Math.Round(point.Y * 304.8, 3), ["z_mm"] = Math.Round(point.Z * 304.8, 3) };
            var curveGeometry = new JObject
            {
                ["kind"] = curve is Line ? "line" : curve.GetType().Name,
                ["start_mm"] = PointMm(curve.GetEndPoint(0)),
                ["end_mm"] = PointMm(curve.GetEndPoint(1)),
                ["length_mm"] = Math.Round(curve.Length * 304.8, 3)
            };
            var styleCategory = (item.LineStyle as GraphicsStyle)?.GraphicsStyleCategory;
            return new JObject
            {
                ["id"] = item.Id.Val(), ["kind"] = kind, ["class"] = item.GetType().Name,
                ["line_style"] = item.LineStyle?.Name, ["presentation_subcategory"] = styleCategory == null ? null : FamilyBlueprintCompiler.PresentationSubcategorySnapshot(doc, styleCategory), ["sketch_plane_id"] = item.SketchPlane?.Id.Val(), ["sketch_plane_name"] = item.SketchPlane?.Name,
                ["visibility"] = new JObject { ["coarse"] = visibility.IsShownInCoarse, ["medium"] = visibility.IsShownInMedium, ["fine"] = visibility.IsShownInFine, ["front_back"] = visibility.IsShownInFrontBack, ["left_right"] = visibility.IsShownInLeftRight, ["plan_rcp"] = visibility.IsShownInPlanRCPCut, ["only_when_cut"] = visibility.IsShownOnlyWhenCut },
                ["visibility_parameter"] = associated == null ? null : new JObject { ["name"] = associated.Definition.Name, ["is_instance"] = associated.IsInstance, ["verified"] = true },
                ["curve_geometry"] = curveGeometry
            };
        }
        // ModelCurve and SymbolicCurve are managed API types, but Revit 2023
        // rejects them as direct OfClass filters because they are not native
        // object-model classes. Collect the native CurveElement base once and
        // then use managed type filtering for the read-back classification.
        var familyCurves = doc.IsFamilyDocument
            ? new FilteredElementCollector(doc).OfClass(typeof(CurveElement)).Cast<CurveElement>().ToList()
            : new List<CurveElement>();
        var modelCurves = familyCurves.OfType<ModelCurve>().Select(item => DescribeCurve(item, item.GetVisibility(), "model")).ToList();
        var symbolicCurves = familyCurves.OfType<SymbolicCurve>().Select(item => DescribeCurve(item, item.GetVisibility(), "symbolic")).ToList();
        JObject DescribeDetailCurve(DetailCurve item)
        {
            var curve = item.GeometryCurve;
            JObject PointMm(XYZ point) => new() { ["x_mm"] = Math.Round(point.X * 304.8, 3), ["y_mm"] = Math.Round(point.Y * 304.8, 3), ["z_mm"] = Math.Round(point.Z * 304.8, 3) };
            var styleCategory = (item.LineStyle as GraphicsStyle)?.GraphicsStyleCategory;
            return new JObject
            {
                ["id"] = item.Id.Val(), ["class"] = item.GetType().Name, ["line_style"] = item.LineStyle?.Name, ["presentation_subcategory"] = styleCategory == null ? null : FamilyBlueprintCompiler.PresentationSubcategorySnapshot(doc, styleCategory),
                ["curve_geometry"] = new JObject { ["kind"] = curve is Line ? "line" : curve.GetType().Name, ["start_mm"] = PointMm(curve.GetEndPoint(0)), ["end_mm"] = PointMm(curve.GetEndPoint(1)), ["length_mm"] = Math.Round(curve.Length * 304.8, 3) }
            };
        }
        var detailCurves = familyCurves.OfType<DetailCurve>().Select(DescribeDetailCurve).ToList();
        var filledRegionCount = doc.IsFamilyDocument ? new FilteredElementCollector(doc).OfClass(typeof(FilledRegion)).GetElementCount() : 0;
        var controlCount = doc.IsFamilyDocument ? new FilteredElementCollector(doc).OfClass(typeof(Control)).GetElementCount() : 0;
        var importInstanceCount = doc.IsFamilyDocument ? new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).GetElementCount() : 0;
        var documentElementCount = doc.IsFamilyDocument ? new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount() : 0;
        JObject DescribeDimension(Dimension item)
        {
            // Autodesk templates can contain native dimensions that cannot carry
            // a FamilyLabel. Reading FamilyLabel on one of those dimensions may
            // throw even though it is unrelated to a compiler-created constraint.
            // Preserve that diagnostic per dimension instead of aborting the
            // complete inspection. Reopen validators still require expected
            // compiler labels by name, so a missing authoring label cannot pass.
            FamilyParameter? label = null; string? labelError = null;
            try { label = item.FamilyLabel; }
            catch (Exception exception) { labelError = exception.Message; }
            return new JObject
            {
                ["id"] = item.Id.Val(), ["shape"] = item.DimensionShape.ToString(), ["segment_count"] = item.NumberOfSegments,
                ["segments_equal"] = item.NumberOfSegments > 1 ? item.AreSegmentsEqual : null,
                ["family_label"] = label?.Definition.Name, ["family_label_is_instance"] = label?.IsInstance,
                ["family_label_read_back"] = labelError == null ? "available" : "unavailable", ["family_label_error"] = labelError,
                ["reference_count"] = item.References?.Size ?? 0
            };
        }
        var dimensions = doc.IsFamilyDocument ? new FilteredElementCollector(doc).OfClass(typeof(Dimension)).Cast<Dimension>().Select(DescribeDimension).ToList() : new List<JObject>();
        JObject PointMm(XYZ point) => new() { ["x_mm"] = Math.Round(point.X * 304.8, 3), ["y_mm"] = Math.Round(point.Y * 304.8, 3), ["z_mm"] = Math.Round(point.Z * 304.8, 3) };
        var nestedInstances = doc.IsFamilyDocument ? new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().Select(item =>
        {
            var hostFace = item.HostFace; string locationKind; JToken? location;
            if (item.Location is LocationPoint point) { locationKind = "point"; location = PointMm(point.Point); }
            else if (item.Location is LocationCurve curve && curve.Curve is Line line) { locationKind = "curve"; location = new JObject { ["start_mm"] = PointMm(line.GetEndPoint(0)), ["end_mm"] = PointMm(line.GetEndPoint(1)) }; }
            else { locationKind = item.Location?.GetType().Name ?? "none"; location = null; }
            var parameterInterfaces = item.Parameters.Cast<Parameter>().Select(parameter =>
            {
                var external = parameter.Definition as ExternalDefinition;
                var associated = doc.FamilyManager.CanElementParameterBeAssociated(parameter) ? doc.FamilyManager.GetAssociatedFamilyParameter(parameter) : null;
                return new JObject
                {
                    ["name"] = parameter.Definition.Name, ["is_shared"] = external != null, ["shared_guid"] = external?.GUID.ToString("D"),
                    ["associated_family_parameter"] = associated == null ? null : new JObject { ["name"] = associated.Definition.Name, ["is_instance"] = associated.IsInstance, ["is_shared"] = associated.IsShared, ["shared_guid"] = associated.IsShared ? associated.GUID.ToString("D") : null }
                };
            }).Where(parameter => parameter.Value<bool?>("is_shared") == true || parameter["associated_family_parameter"] != null).ToList();
            // Revit exposes a Shared child parameter in a nested instance by
            // the matching GUID; its proxy Definition is not consistently an
            // ExternalDefinition. Record that native lookup separately so a
            // reopen verifier never pretends it was an ordinary association.
            foreach (var parentParameter in doc.FamilyManager.Parameters.Cast<FamilyParameter>().Where(parameter => parameter.IsInstance && parameter.IsShared))
            {
                var nestedShared = item.get_Parameter(parentParameter.GUID);
                if (nestedShared == null || parameterInterfaces.Any(candidate => candidate.Value<bool?>("shared_guid_identity_lookup") == true && string.Equals(candidate.Value<string>("shared_guid"), parentParameter.GUID.ToString("D"), StringComparison.OrdinalIgnoreCase))) continue;
                parameterInterfaces.Add(new JObject
                {
                    ["name"] = nestedShared.Definition.Name, ["is_shared"] = true, ["shared_guid"] = parentParameter.GUID.ToString("D"),
                    ["associated_family_parameter"] = null, ["shared_guid_identity_lookup"] = true
                });
            }
            return new JObject { ["id"] = item.Id.Val(), ["family"] = item.Symbol.FamilyName, ["type"] = item.Symbol.Name, ["family_placement_type"] = item.Symbol.Family.FamilyPlacementType.ToString(), ["host_element_id"] = item.Host?.Id.Val(), ["host_face_element_id"] = hostFace?.ElementId.Val(), ["location_kind"] = locationKind, ["location"] = location, ["parameters"] = new JArray(parameterInterfaces) };
        }).ToList() : new List<JObject>();
        return new JObject
        {
            ["title"] = doc.Title, ["family_name"] = doc.IsFamilyDocument ? doc.OwnerFamily?.Name : null, ["path"] = doc.PathName, ["is_family_document"] = doc.IsFamilyDocument,
            ["category"] = doc.IsFamilyDocument ? doc.OwnerFamily?.FamilyCategory?.Name : null,
            // Family category display names are localized; this is the stable
            // BuiltInCategory id required for exact reopen verification.
            ["category_id"] = doc.IsFamilyDocument ? doc.OwnerFamily?.FamilyCategory?.Id.Val() : null,
            ["hosting"] = new JObject { ["family_placement_type"] = placement, ["inferred_behavior"] = placement switch { "OneLevelBased" => "level_based", "OneLevelBasedHosted" => "hosted_category_specific", "TwoLevelsBased" => "two_level_based", "ViewBased" => "view_based_annotation_or_detail", "WorkPlaneBased" => "work_plane_based", "CurveBased" => "line_based", "CurveBasedDetail" => "detail_item_line_based", "Adaptive" => "adaptive", _ => "unknown_or_project_document" }, ["verified_from_revit"] = doc.IsFamilyDocument },
            ["family_behavior"] = doc.IsFamilyDocument ? FamilyBlueprintCompiler.FamilyBehaviorSnapshot(doc) : null,
            ["unit_input"] = "mm", ["internal_unit"] = "feet", ["reference_planes"] = new JArray(planes),
            ["parameters"] = new JArray(parameters), ["types"] = new JArray(types), ["identity_data"] = identityData, ["lookup_tables"] = new JArray(lookupTables), ["forms"] = new JArray(forms), ["connectors"] = new JArray(connectors), ["materials"] = new JArray(materials), ["presentation_subcategories"] = new JArray(presentationSubcategories), ["light_source"] = lightSource, ["model_curves"] = new JArray(modelCurves), ["symbolic_curves"] = new JArray(symbolicCurves), ["detail_curves"] = new JArray(detailCurves), ["dimensions"] = new JArray(dimensions), ["nested_instances"] = new JArray(nestedInstances),
            ["capability_evidence"] = new JObject { ["observed_primitives"] = JObject.FromObject(primitives), ["document_element_count"] = documentElementCount, ["form_count"] = forms.Count, ["void_form_count"] = forms.Count(item => item.Value<bool?>("is_solid") == false), ["coordination_zone_count"] = forms.Count(item => (item.Value<string>("role_or_subcategory") ?? string.Empty).StartsWith("DSCons Coordination ", StringComparison.Ordinal)), ["connector_count"] = connectors.Count, ["parameter_count"] = parameters.Count, ["formula_parameter_count"] = parameters.Count(item => !string.IsNullOrWhiteSpace(item.Value<string>("formula"))), ["lookup_table_count"] = lookupTables.Count, ["family_type_count"] = types.Count, ["material_count"] = materials.Count, ["presentation_subcategory_count"] = presentationSubcategories.Count, ["light_source_count"] = lightSource == null ? 0 : 1, ["model_curve_count"] = modelCurves.Count, ["symbolic_curve_count"] = symbolicCurves.Count, ["detail_curve_count"] = detailCurves.Count, ["filled_region_count"] = filledRegionCount, ["control_count"] = controlCount, ["two_dimensional_curve_count"] = modelCurves.Concat(symbolicCurves).Concat(detailCurves).Select(item => item.Value<long>("id")).Distinct().Count(), ["dimension_count"] = dimensions.Count, ["unavailable_dimension_label_count"] = dimensions.Count(item => item.Value<string>("family_label_read_back") == "unavailable"), ["nested_instance_count"] = nestedInstances.Count, ["reference_plane_count"] = planes.Count, ["named_reference_plane_count"] = planes.Count(item => !new[] { "strong", "weak", "not_reference" }.Contains(item.Value<string>("reference_type"))), ["origin_reference_plane_count"] = planes.Count(item => item.Value<bool?>("defines_origin") == true), ["styled_reference_plane_count"] = planes.Count(item => item["subcategory"] != null), ["equal_dimension_count"] = dimensions.Count(item => item.Value<bool?>("segments_equal") == true), ["visibility_parameter_binding_count"] = forms.Concat(modelCurves).Concat(symbolicCurves).Count(item => item["visibility_parameter"] != null), ["parameter_binding_count"] = parameters.Sum(item => item.Value<int?>("associated_element_parameter_count") ?? 0), ["import_instance_count"] = importInstanceCount, ["inspection_only"] = true },
            ["verification_boundary"] = "This read-back proves observed Family content only. Coordination-zone solids are semantic aids and do not certify quantity exclusion, Project/BEP clearance, access, support, hosting, network connectivity or full LOD350."
        };
    }

    private static string ParameterGroupKey(Definition definition)
    {
#if REVIT2019 || REVIT2020 || REVIT2021
        return definition.ParameterGroup switch
        {
            BuiltInParameterGroup.PG_CONSTRAINTS => "constraints", BuiltInParameterGroup.PG_GEOMETRY => "geometry", BuiltInParameterGroup.PG_MATERIALS => "materials",
            BuiltInParameterGroup.PG_MECHANICAL => "mechanical", BuiltInParameterGroup.PG_MECHANICAL_AIRFLOW => "mechanical_airflow", BuiltInParameterGroup.PG_ELECTRICAL => "electrical",
            BuiltInParameterGroup.PG_PLUMBING => "plumbing", BuiltInParameterGroup.PG_DATA => "data", BuiltInParameterGroup.PG_GRAPHICS => "graphics", BuiltInParameterGroup.PG_GENERAL => "general",
            BuiltInParameterGroup.PG_IDENTITY_DATA => "identity_data", _ => definition.ParameterGroup.ToString()
        };
#else
        var group = definition.GetGroupTypeId();
        if (group == GroupTypeId.Constraints) return "constraints"; if (group == GroupTypeId.Geometry) return "geometry"; if (group == GroupTypeId.Materials) return "materials";
        if (group == GroupTypeId.Mechanical) return "mechanical"; if (group == GroupTypeId.MechanicalAirflow) return "mechanical_airflow"; if (group == GroupTypeId.Electrical) return "electrical";
        if (group == GroupTypeId.Plumbing) return "plumbing"; if (group == GroupTypeId.Data) return "data"; if (group == GroupTypeId.Graphics) return "graphics"; if (group == GroupTypeId.General) return "general";
        if (group == GroupTypeId.IdentityData) return "identity_data"; return group.TypeId;
#endif
    }
}
