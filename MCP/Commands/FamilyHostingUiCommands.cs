using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DSCons.RevitMcp.Contracts;
using DSCons.RevitMcp.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

/// <summary>
/// Project hosting is a native UI behaviour.  The compiler can prove that an
/// RFA reports OneLevelBasedHosted or TwoLevelsBased, but that does not prove
/// a real instance was hosted, cut or assigned to the expected levels.  This
/// pair only preflights the RFA/target and reads the result after a controlled
/// UI placement; it never manufactures a host, changes a host, or performs a
/// void cut through the MCP API.
/// </summary>
internal sealed class FamilyHostingUiPreflightCommand : ReadCommand
{
    public FamilyHostingUiPreflightCommand() : base("family_hosting_ui_preflight", "Read-only preflight for controlled native UI placement of wall/ceiling/floor/roof or two-level Family.") { }

    public override JObject Execute(UIApplication app, JObject args)
    {
        var project = Document(app);
        var spec = FamilyHostingUiSpec.From(project, args);
        var validation = FamilyHostingUi.Preflight(app.Application, project, spec);
        args["__hosting_family_name"] = validation.Value<string>("family_name");
        args["__hosting_placement_type"] = validation.Value<string>("family_placement_type");
        var token = new PreviewToken
        {
            PreviewId = Guid.NewGuid().ToString("N"),
            Operation = "family_hosting_ui",
            ArgumentsJson = args.ToString(Formatting.None),
            DocumentFingerprint = MepSafety.DocumentFingerprint(project),
            ResourceFingerprint = spec.ResourceFingerprint,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        BridgeServer.Current?.Store(token);
        return new JObject
        {
            ["operation"] = "family_hosting_ui_preflight",
            ["model_changed"] = false,
            ["preflight_id"] = token.PreviewId,
            ["expires_at_utc"] = token.ExpiresAtUtc,
            ["validation"] = validation,
            ["ui_handoff"] = new JObject
            {
                ["required"] = true,
                ["native_hosted_placement_by_api"] = false,
                ["instructions"] = new JArray(
                    "Use only the identified copied Project and active view.",
                    "Use Revit's native placement workflow to place the exact preflighted Family Type on the identified host, or between the approved base/top levels.",
                    "Do not edit the RFA, change Family Type, substitute the host, Save or Sync while the record is active.",
                    "After placement, call family_hosting_ui_verify with the created instance ID.")
            },
            ["verification_boundary"] = "Preflight proves Family placement type and target eligibility only. It does not place, rehost, change type, rotate/mirror or cut the Project."
        };
    }
}

internal sealed class FamilyHostingUiVerifyCommand : ReadCommand
{
    public FamilyHostingUiVerifyCommand() : base("family_hosting_ui_verify", "Read back native hosted/two-level placement and optional applied void-cut relationship after a controlled UI action.") { }

    public override JObject Execute(UIApplication app, JObject args)
    {
        var project = Document(app);
        var token = FamilyHostingUi.TakePreflight(args);
        var spec = FamilyHostingUiSpec.From(project, JObject.Parse(token.ArgumentsJson));
        if (!string.Equals(token.ResourceFingerprint, spec.ResourceFingerprint, StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.PreviewInvalid, "The Family RFA, Project/view, host/levels or requested Family Type changed after hosting preflight. Run preflight again.");
        var instanceId = RevitIdCompatibility.Eid(args.Value<long>("instance_id"));
        var validation = FamilyHostingUi.Verify(project, spec, instanceId);
        return new JObject
        {
            ["operation"] = "family_hosting_ui_verify",
            ["model_changed"] = false,
            ["validation_level"] = "post_controlled_ui_read_back",
            ["validation"] = validation,
            ["verification_boundary"] = "This reads the current host/levels and optional void-cut relation. It cannot prove a rehost history, exact UI command, type change, rotate/mirror, host behaviour after edits, or LOD350/BEP coordination."
        };
    }
}

internal sealed class FamilyHostingUiSpec
{
    public string ProjectPath { get; private set; } = string.Empty;
    public string FamilyPath { get; private set; } = string.Empty;
    public string TypeName { get; private set; } = string.Empty;
    public string Mode { get; private set; } = string.Empty;
    public ElementId HostId { get; private set; } = ElementId.InvalidElementId;
    public ElementId BaseLevelId { get; private set; } = ElementId.InvalidElementId;
    public ElementId TopLevelId { get; private set; } = ElementId.InvalidElementId;
    public bool RequireVoidCut { get; private set; }
    public string FamilyName { get; private set; } = string.Empty;
    public string ExpectedPlacementType { get; private set; } = string.Empty;
    public string ResourceFingerprint { get; private set; } = string.Empty;

    public static FamilyHostingUiSpec From(Document project, JObject args)
    {
        if (args.Value<bool?>("copied_project_confirmed") != true)
            throw new CommandResultException(ErrorCodes.InvalidParam, "copied_project_confirmed=true is required before a controlled hosting UI handoff.");
        if (project.IsFamilyDocument)
            throw new CommandResultException(ErrorCodes.DocumentTypeInvalid, "Open the approved copied Project, not a Family document.");
        MepSafety.GuardWrite(project, Enumerable.Empty<Element>());
        var demo = RequireDirectory(args.Value<string>("approved_demo_directory"));
        var expectedProject = RequireExistingFile(args.Value<string>("expected_project_path"), ".rvt");
        var activeProject = RequireExistingFile(project.PathName, ".rvt");
        RequireInside(demo, expectedProject); RequireInside(demo, activeProject);
        if (!string.Equals(expectedProject, activeProject, StringComparison.OrdinalIgnoreCase))
            throw new CommandResultException(ErrorCodes.PreviewInvalid, "The active Project path does not match expected_project_path.");
        if (project.ActiveView.Id.Val() != args.Value<long>("expected_view_id"))
            throw new CommandResultException(ErrorCodes.PreviewInvalid, "The active view changed or does not match expected_view_id.");
        var family = RequireExistingFile(args.Value<string>("family_path"), ".rfa"); RequireInside(demo, family);
        var typeName = RequireName(args.Value<string>("type_name"), "type_name");
        var host = args["host"] as JObject ?? throw new CommandResultException(ErrorCodes.InvalidParam, "host is required.");
        var mode = host.Value<string>("mode") ?? string.Empty;
        if (mode is not ("wall" or "ceiling" or "floor" or "roof" or "two_level"))
            throw new CommandResultException(ErrorCodes.InvalidParam, "host.mode must be wall, ceiling, floor, roof or two_level.");
        var requireVoidCut = args.Value<bool?>("require_void_cut") ?? false;
        var hostId = ElementId.InvalidElementId; var baseLevel = ElementId.InvalidElementId; var topLevel = ElementId.InvalidElementId;
        if (mode == "two_level")
        {
            if (requireVoidCut) throw new CommandResultException(ErrorCodes.InvalidParam, "require_void_cut is only supported for a physical wall/ceiling/floor/roof host, not two_level.");
            baseLevel = RevitIdCompatibility.Eid(host.Value<long>("base_level_id")); topLevel = RevitIdCompatibility.Eid(host.Value<long>("top_level_id"));
            var baseElement = project.GetElement(baseLevel) as Level; var topElement = project.GetElement(topLevel) as Level;
            if (baseElement == null || topElement == null || baseLevel == topLevel || baseElement.ProjectElevation >= topElement.ProjectElevation)
                throw new CommandResultException(ErrorCodes.InvalidParam, "two_level requires two distinct existing Levels ordered base_level_id below top_level_id.");
        }
        else
        {
            hostId = RevitIdCompatibility.Eid(host.Value<long>("host_element_id"));
            var target = project.GetElement(hostId) ?? throw new CommandResultException(ErrorCodes.InvalidParam, "host.host_element_id must resolve to an existing host element.");
            if (target.Category?.Id.IntVal() != ExpectedCategory(mode))
                throw new CommandResultException(ErrorCodes.InvalidParam, "host_element_id category does not match host.mode=" + mode + ".");
            if (requireVoidCut && !InstanceVoidCutUtils.CanBeCutWithVoid(target))
                throw new CommandResultException(ErrorCodes.InvalidParam, "The selected host cannot be cut with a Family void instance.");
        }
        return new FamilyHostingUiSpec
        {
            ProjectPath = activeProject, FamilyPath = family, TypeName = typeName, Mode = mode, HostId = hostId, BaseLevelId = baseLevel, TopLevelId = topLevel, RequireVoidCut = requireVoidCut,
            FamilyName = args.Value<string>("__hosting_family_name") ?? string.Empty,
            ExpectedPlacementType = args.Value<string>("__hosting_placement_type") ?? string.Empty,
            ResourceFingerprint = Fingerprint(activeProject, project.ActiveView.Id.Val().ToString(), family, typeName, mode, hostId.Val().ToString(), baseLevel.Val().ToString(), topLevel.Val().ToString(), requireVoidCut.ToString())
        };
    }

    private static int ExpectedCategory(string mode) => mode switch
    {
        "wall" => (int)BuiltInCategory.OST_Walls,
        "ceiling" => (int)BuiltInCategory.OST_Ceilings,
        "floor" => (int)BuiltInCategory.OST_Floors,
        "roof" => (int)BuiltInCategory.OST_Roofs,
        _ => throw new CommandResultException(ErrorCodes.InvalidParam, "No physical category exists for hosting mode " + mode + ".")
    };
    private static string RequireDirectory(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new CommandResultException(ErrorCodes.PathBlocked, "approved_demo_directory must be an existing local absolute directory.");
        var input = raw!;
        if (!Path.IsPathRooted(input) || IsUnc(input)) throw new CommandResultException(ErrorCodes.PathBlocked, "approved_demo_directory must be an existing local absolute directory.");
        var path = Path.GetFullPath(input); if (!Directory.Exists(path)) throw new CommandResultException(ErrorCodes.PathBlocked, "approved_demo_directory does not exist."); return path;
    }
    private static string RequireExistingFile(string? raw, string extension)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new CommandResultException(ErrorCodes.PathBlocked, "A local absolute path is required.");
        var input = raw!;
        if (!Path.IsPathRooted(input) || IsUnc(input)) throw new CommandResultException(ErrorCodes.PathBlocked, "A local absolute path is required.");
        var path = Path.GetFullPath(input);
        if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) throw new CommandResultException(ErrorCodes.PathBlocked, "Required file is missing or has the wrong extension: " + extension);
        return path;
    }
    private static string RequireName(string? value, string label)
    {
        var cleaned = value?.Trim(); if (string.IsNullOrWhiteSpace(cleaned)) throw new CommandResultException(ErrorCodes.InvalidParam, label + " is invalid.");
        var name = cleaned!; if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("..")) throw new CommandResultException(ErrorCodes.InvalidParam, label + " is invalid."); return name;
    }
    private static void RequireInside(string root, string path)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new CommandResultException(ErrorCodes.PathBlocked, "Path is outside approved_demo_directory.");
    }
    private static bool IsUnc(string path) => path.StartsWith("\\\\", StringComparison.Ordinal) || new Uri(path).IsUnc;
    private static string Fingerprint(params string[] values)
    {
        using var sha = SHA256.Create(); var input = string.Join("|", values.Select(value => File.Exists(value) ? value + ":" + HashFile(value) : value)); return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(input)));
    }
    private static string HashFile(string path)
    {
        using var sha = SHA256.Create(); using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); return Convert.ToBase64String(sha.ComputeHash(stream));
    }
}

internal static class FamilyHostingUi
{
    public static JObject Preflight(Application application, Document project, FamilyHostingUiSpec spec)
    {
        Document? family = null; var closeWhenDone = false;
        try
        {
            family = application.Documents.Cast<Document>().FirstOrDefault(item => item.IsFamilyDocument && SamePath(item.PathName, spec.FamilyPath));
            if (family == null) { family = application.OpenDocumentFile(spec.FamilyPath); closeWhenDone = true; }
            if (!family.IsFamilyDocument || family.OwnerFamily == null) throw new CommandResultException(ErrorCodes.DocumentTypeInvalid, "family_path did not open as a Revit Family document.");
            var owner = family.OwnerFamily;
            if (!family.FamilyManager.Types.Cast<FamilyType>().Any(item => string.Equals(item.Name, spec.TypeName, StringComparison.Ordinal)))
                throw new CommandResultException(ErrorCodes.InvalidParam, "The RFA does not contain the requested Family Type for hosting preflight.");
            var allowedPlacements = AllowedPlacementTypes(spec.Mode);
            if (!allowedPlacements.Contains(owner.FamilyPlacementType))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "RFA FamilyPlacementType is " + owner.FamilyPlacementType + "; hosting mode " + spec.Mode + " requires " + PlacementDescription(allowedPlacements) + ".");
            var inspection = FamilyData.Inspect(family);
            var behavior = inspection["family_behavior"] as JObject ?? new JObject();
            if (spec.RequireVoidCut && (behavior.Value<bool?>("cut_with_voids_when_loaded") != true || inspection["capability_evidence"]?.Value<int?>("void_form_count") is not int voidCount || voidCount < 1))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "require_void_cut needs Cut with Voids When Loaded=true and at least one native void form in the RFA.");
            var target = spec.Mode == "two_level" ? null : project.GetElement(spec.HostId);
            return new JObject
            {
                ["status"] = "passed", ["family_name"] = owner.Name, ["family_type"] = spec.TypeName,
                ["family_placement_type"] = owner.FamilyPlacementType.ToString(), ["allowed_family_placement_types"] = new JArray(allowedPlacements.Select(item => item.ToString())), ["hosting_mode"] = spec.Mode,
                ["hosting_strategy"] = owner.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased ? "face_based_work_plane" : spec.Mode == "two_level" ? "two_levels" : "element_hosted",
                ["require_void_cut"] = spec.RequireVoidCut,
                ["target"] = spec.Mode == "two_level"
                    ? new JObject { ["base_level_id"] = spec.BaseLevelId.Val(), ["top_level_id"] = spec.TopLevelId.Val(), ["base_level_name"] = (project.GetElement(spec.BaseLevelId) as Level)?.Name, ["top_level_name"] = (project.GetElement(spec.TopLevelId) as Level)?.Name }
                    : new JObject { ["host_element_id"] = spec.HostId.Val(), ["host_category"] = target?.Category?.Name, ["void_cut_eligible"] = spec.RequireVoidCut ? InstanceVoidCutUtils.CanBeCutWithVoid(target!) : null },
                ["family_behavior"] = behavior, ["read_only"] = true
            };
        }
        catch (CommandResultException) { throw; }
        catch (Exception ex) { throw new CommandResultException(ErrorCodes.VerificationFailed, "Family hosting preflight failed without changing the Project: " + ex.Message); }
        finally { if (closeWhenDone && family != null) try { family.Close(false); } catch { } }
    }

    public static PreviewToken TakePreflight(JObject args)
    {
        var id = args.Value<string>("preflight_id");
        if (string.IsNullOrWhiteSpace(id) || BridgeServer.Current == null || !BridgeServer.Current.TryTake(id, out var token)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Hosting preflight does not exist, expired, was already used, or belongs to another Revit session.");
        if (!string.Equals(token.Operation, "family_hosting_ui", StringComparison.OrdinalIgnoreCase)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "preflight_id belongs to another Family operation.");
        if (token.ExpiresAtUtc < DateTimeOffset.UtcNow) throw new CommandResultException(ErrorCodes.PreviewExpired, "Hosting preflight expired; run it again before controlled UI placement.");
        return token;
    }

    public static JObject Verify(Document project, FamilyHostingUiSpec spec, ElementId instanceId)
    {
        var instance = project.GetElement(instanceId) as FamilyInstance ?? throw new CommandResultException(ErrorCodes.InvalidParam, "instance_id must resolve to the newly placed FamilyInstance.");
        if (!string.Equals(instance.Symbol.FamilyName, spec.FamilyName, StringComparison.Ordinal) || !string.Equals(instance.Symbol.Name, spec.TypeName, StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Placed instance does not use the exact preflighted Family and Family Type.");
        var actualPlacement = instance.Symbol.Family.FamilyPlacementType.ToString();
        if (!string.Equals(actualPlacement, spec.ExpectedPlacementType, StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Placed instance FamilyPlacementType no longer matches the preflighted RFA.");
        if (spec.Mode == "two_level") return VerifyTwoLevel(instance, spec, actualPlacement);
        return VerifyHosted(instance, project, spec, actualPlacement);
    }

    private static JObject VerifyHosted(FamilyInstance instance, Document project, FamilyHostingUiSpec spec, string actualPlacement)
    {
        var host = instance.Host;
        if (host == null || host.Id != spec.HostId) throw new CommandResultException(ErrorCodes.VerificationFailed, "Placed instance is not hosted by the exact preflighted Project element.");
        if (host.Category?.Id.IntVal() != ExpectedCategory(spec.Mode)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Placed instance host category no longer matches hosting mode " + spec.Mode + ".");
        var hostFace = instance.HostFace;
        if (string.Equals(actualPlacement, FamilyPlacementType.WorkPlaneBased.ToString(), StringComparison.Ordinal) && (hostFace == null || hostFace.ElementId != host.Id))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "A face/work-plane-based Family must retain a HostFace on the exact preflighted Project host.");
        var voidCut = new JObject { ["requested"] = spec.RequireVoidCut, ["verified"] = false };
        if (spec.RequireVoidCut)
        {
            if (!InstanceVoidCutUtils.IsVoidInstanceCuttingElement(instance)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Placed instance is not exposed by Revit as a void-cutting Family instance.");
            var cutIds = InstanceVoidCutUtils.GetElementsBeingCut(instance).Select(item => item.Val()).OrderBy(item => item).ToArray();
            if (!cutIds.Contains(host.Id.Val())) throw new CommandResultException(ErrorCodes.VerificationFailed, "The preflighted host is not in InstanceVoidCutUtils.GetElementsBeingCut for this Family instance.");
            voidCut = new JObject { ["requested"] = true, ["verified"] = true, ["cut_host_ids"] = new JArray(cutIds) };
        }
        return new JObject
        {
            ["status"] = "passed", ["family"] = instance.Symbol.FamilyName, ["family_type"] = instance.Symbol.Name,
            ["hosting_mode"] = spec.Mode, ["family_placement_type"] = actualPlacement,
            ["host"] = new JObject { ["element_id"] = host.Id.Val(), ["category"] = host.Category?.Name, ["host_face_element_id"] = hostFace?.ElementId.Val(), ["host_face_available"] = hostFace != null, ["face_host_verified"] = string.Equals(actualPlacement, FamilyPlacementType.WorkPlaneBased.ToString(), StringComparison.Ordinal), ["wall_host_parameter"] = spec.Mode == "wall" ? instance.HostParameter : null, ["verified"] = true },
            ["void_cut"] = voidCut,
            ["capabilities_only"] = new JObject { ["can_flip_facing"] = instance.CanFlipFacing, ["can_flip_hand"] = instance.CanFlipHand, ["can_flip_work_plane"] = instance.CanFlipWorkPlane, ["can_rotate"] = instance.CanRotate },
            ["not_verified_here"] = new JArray("rehost_history", "type_change", "rotate_mirror_operation", "host_cut_after_edit", "lod350_bep_coordination")
        };
    }

    private static JObject VerifyTwoLevel(FamilyInstance instance, FamilyHostingUiSpec spec, string actualPlacement)
    {
        var baseLevel = instance.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.AsElementId() ?? ElementId.InvalidElementId;
        var topLevel = instance.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)?.AsElementId() ?? ElementId.InvalidElementId;
        if (baseLevel != spec.BaseLevelId || topLevel != spec.TopLevelId) throw new CommandResultException(ErrorCodes.VerificationFailed, "Placed two-level Family does not retain the exact preflighted Base Level and Top Level.");
        return new JObject
        {
            ["status"] = "passed", ["family"] = instance.Symbol.FamilyName, ["family_type"] = instance.Symbol.Name,
            ["hosting_mode"] = "two_level", ["family_placement_type"] = actualPlacement,
            ["levels"] = new JObject { ["base_level_id"] = baseLevel.Val(), ["top_level_id"] = topLevel.Val(), ["verified"] = true },
            ["capabilities_only"] = new JObject { ["can_flip_facing"] = instance.CanFlipFacing, ["can_flip_hand"] = instance.CanFlipHand, ["can_rotate"] = instance.CanRotate },
            ["not_verified_here"] = new JArray("rehost_history", "type_change", "rotate_mirror_operation", "lod350_bep_coordination")
        };
    }

    private static int ExpectedCategory(string mode) => mode switch
    {
        "wall" => (int)BuiltInCategory.OST_Walls, "ceiling" => (int)BuiltInCategory.OST_Ceilings,
        "floor" => (int)BuiltInCategory.OST_Floors, "roof" => (int)BuiltInCategory.OST_Roofs,
        _ => throw new CommandResultException(ErrorCodes.InvalidParam, "No physical category exists for hosting mode " + mode + ".")
    };
    private static IReadOnlyCollection<FamilyPlacementType> AllowedPlacementTypes(string mode) => mode == "two_level"
        ? new[] { FamilyPlacementType.TwoLevelsBased }
        : new[] { FamilyPlacementType.OneLevelBasedHosted, FamilyPlacementType.WorkPlaneBased };
    private static string PlacementDescription(IEnumerable<FamilyPlacementType> values) => string.Join(" or ", values.Select(item => item.ToString()));
    private static bool SamePath(string? left, string right) => !string.IsNullOrWhiteSpace(left) && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
