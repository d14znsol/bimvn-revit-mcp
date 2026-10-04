using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using DSCons.RevitMcp.Contracts;
using DSCons.RevitMcp.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

/// <summary>
/// Revit's native Break Into command is a Family Editor/Project UI workflow,
/// not a public API placement factory.  These two commands deliberately split
/// that human action from the evidence MCP can read: preflight validates a
/// candidate RFA without changing a Project; verification reads the resulting
/// inline topology after the approved UI action.  Neither command claims that
/// an API invoked the native command or that system calculation has passed.
/// </summary>
internal sealed class FamilyBreakIntoUiPreflightCommand : ReadCommand
{
    public FamilyBreakIntoUiPreflightCommand() : base("family_break_into_ui_preflight", "Read-only preflight for a controlled native Break Into/Valve Breaks Into UI placement.") { }

    public override JObject Execute(UIApplication app, JObject args)
    {
        var project = Document(app);
        var spec = FamilyBreakIntoUiSpec.From(project, args);
        var validation = FamilyBreakIntoUi.Preflight(app.Application, spec);
        args["__break_into_family_name"] = validation.Value<string>("family_name");
        args["__break_into_part_type"] = validation.Value<string>("part_type");
        var token = new PreviewToken
        {
            PreviewId = Guid.NewGuid().ToString("N"),
            Operation = "family_break_into_ui",
            ArgumentsJson = args.ToString(Formatting.None),
            DocumentFingerprint = MepSafety.DocumentFingerprint(project),
            ResourceFingerprint = spec.ResourceFingerprint,
            // The record only authorizes a read-back, never an MCP write. Five
            // minutes is long enough for one controlled UI placement, but
            // prevents a stale RFA/project context from being reused later.
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        BridgeServer.Current?.Store(token);
        return new JObject
        {
            ["operation"] = "family_break_into_ui_preflight",
            ["model_changed"] = false,
            ["preflight_id"] = token.PreviewId,
            ["expires_at_utc"] = token.ExpiresAtUtc,
            ["validation"] = validation,
            ["ui_handoff"] = new JObject
            {
                ["required"] = true,
                ["native_break_into_placement_by_api"] = false,
                ["instructions"] = new JArray(
                    "Use only the identified copied Project and active view.",
                    "Use Revit's native Duct/Pipe Break Into workflow to place the exact preflighted Family Type on one straight matching segment.",
                    "Do not edit the RFA, family type, curve type or system while the preflight record is active.",
                    "After the UI action, call family_break_into_ui_verify with the created accessory instance ID and the two resulting segment IDs.")
            },
            ["verification_boundary"] = "Preflight proves only Family/category/Part Type/two-port geometry compatibility. It does not place the component, observe which UI command was clicked, calculate pressure loss, test route healing on removal, or certify LOD350."
        };
    }
}

internal sealed class FamilyBreakIntoUiVerifyCommand : ReadCommand
{
    public FamilyBreakIntoUiVerifyCommand() : base("family_break_into_ui_verify", "Read back the physical two-segment topology after a controlled native Break Into/Valve Breaks Into UI placement.") { }

    public override JObject Execute(UIApplication app, JObject args)
    {
        var project = Document(app);
        var token = FamilyBreakIntoUi.TakePreflight(args);
        var spec = FamilyBreakIntoUiSpec.From(project, JObject.Parse(token.ArgumentsJson));
        if (!string.Equals(token.ResourceFingerprint, spec.ResourceFingerprint, StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.PreviewInvalid, "The Family RFA, copied Project path/view or requested type changed after preflight. Run preflight again.");
        var instanceId = RevitIdCompatibility.Eid(args.Value<long>("accessory_instance_id"));
        var segmentIds = (args["segment_ids"] as JArray ?? new JArray()).Values<long>().Select(RevitIdCompatibility.Eid).ToList();
        if (segmentIds.Count != 2 || segmentIds.Select(item => item.Val()).Distinct().Count() != 2)
            throw new CommandResultException(ErrorCodes.InvalidParam, "segment_ids must contain exactly two distinct resulting Pipe/Duct segment IDs.");
        var validation = FamilyBreakIntoUi.Verify(project, spec, instanceId, segmentIds);
        return new JObject
        {
            ["operation"] = "family_break_into_ui_verify",
            ["model_changed"] = false,
            ["validation_level"] = "post_controlled_ui_read_back",
            ["validation"] = validation,
            ["verification_boundary"] = "The Revit API cannot prove which UI command produced this topology. This read-back proves the requested Family Type and current physical two-segment connector network only; pressure-loss/system calculation, removal healing, type-change, rotate/mirror, hosting and LOD350/BEP remain separate tests."
        };
    }
}

internal sealed class FamilyBreakIntoUiSpec
{
    public string DemoDirectory { get; private set; } = string.Empty;
    public string ProjectPath { get; private set; } = string.Empty;
    public string FamilyPath { get; private set; } = string.Empty;
    public string TypeName { get; private set; } = string.Empty;
    public string CurveKind { get; private set; } = string.Empty;
    public string FamilyName { get; private set; } = string.Empty;
    public string ExpectedPartType { get; private set; } = string.Empty;
    public string ResourceFingerprint { get; private set; } = string.Empty;

    public static FamilyBreakIntoUiSpec From(Document project, JObject args)
    {
        if (args.Value<bool?>("copied_project_confirmed") != true)
            throw new CommandResultException(ErrorCodes.InvalidParam, "copied_project_confirmed=true is required before a controlled Break Into UI handoff.");
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
        var curveKind = args.Value<string>("curve_kind") ?? string.Empty;
        if (curveKind is not ("pipe" or "duct"))
            throw new CommandResultException(ErrorCodes.InvalidParam, "curve_kind must be pipe or duct.");
        var familyName = args.Value<string>("__break_into_family_name") ?? string.Empty;
        var partType = args.Value<string>("__break_into_part_type") ?? string.Empty;
        return new FamilyBreakIntoUiSpec
        {
            DemoDirectory = demo,
            ProjectPath = activeProject,
            FamilyPath = family,
            TypeName = typeName,
            CurveKind = curveKind,
            FamilyName = familyName,
            ExpectedPartType = partType,
            ResourceFingerprint = Fingerprint(activeProject, project.ActiveView.Id.Val().ToString(), family, typeName, curveKind)
        };
    }

    private static string RequireDirectory(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new CommandResultException(ErrorCodes.PathBlocked, "approved_demo_directory must be an existing local absolute directory.");
        var input = raw!;
        if (!Path.IsPathRooted(input) || IsUnc(input))
            throw new CommandResultException(ErrorCodes.PathBlocked, "approved_demo_directory must be an existing local absolute directory.");
        var path = Path.GetFullPath(input);
        if (!Directory.Exists(path)) throw new CommandResultException(ErrorCodes.PathBlocked, "approved_demo_directory does not exist.");
        return path;
    }
    private static string RequireExistingFile(string? raw, string extension)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new CommandResultException(ErrorCodes.PathBlocked, "A local absolute path is required.");
        var input = raw!;
        if (!Path.IsPathRooted(input) || IsUnc(input))
            throw new CommandResultException(ErrorCodes.PathBlocked, "A local absolute path is required.");
        var path = Path.GetFullPath(input);
        if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new CommandResultException(ErrorCodes.PathBlocked, "Required file is missing or has the wrong extension: " + extension);
        return path;
    }
    private static string RequireName(string? value, string label)
    {
        var cleaned = value?.Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            throw new CommandResultException(ErrorCodes.InvalidParam, label + " is invalid.");
        var name = cleaned!;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(".."))
            throw new CommandResultException(ErrorCodes.InvalidParam, label + " is invalid.");
        return name;
    }
    private static void RequireInside(string root, string path)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new CommandResultException(ErrorCodes.PathBlocked, "Path is outside approved_demo_directory.");
    }
    private static bool IsUnc(string path) => path.StartsWith("\\\\", StringComparison.Ordinal) || new Uri(path).IsUnc;
    private static string Fingerprint(params string[] values)
    {
        using var sha = SHA256.Create();
        var input = string.Join("|", values.Select(value => File.Exists(value) ? value + ":" + HashFile(value) : value));
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(input)));
    }
    private static string HashFile(string path)
    {
        using var sha = SHA256.Create();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToBase64String(sha.ComputeHash(stream));
    }
}

internal static class FamilyBreakIntoUi
{
    private const double MillimetreTolerance = .1;
    private const double DirectionTolerance = .999;

    public static JObject Preflight(Application application, FamilyBreakIntoUiSpec spec)
    {
        Document? family = null; var closeWhenDone = false;
        try
        {
            family = application.Documents.Cast<Document>().FirstOrDefault(item => item.IsFamilyDocument && SamePath(item.PathName, spec.FamilyPath));
            if (family == null) { family = application.OpenDocumentFile(spec.FamilyPath); closeWhenDone = true; }
            if (!family.IsFamilyDocument || family.OwnerFamily == null)
                throw new CommandResultException(ErrorCodes.DocumentTypeInvalid, "family_path did not open as a Revit Family document.");
            var owner = family.OwnerFamily;
            var expectedCategory = spec.CurveKind == "pipe" ? BuiltInCategory.OST_PipeAccessory : BuiltInCategory.OST_DuctAccessory;
            if (owner.FamilyCategory?.Id.IntVal() != (int)expectedCategory)
                throw new CommandResultException(ErrorCodes.InvalidParam, "Break Into preflight requires " + expectedCategory + " for curve_kind=" + spec.CurveKind + ".");
            if (!family.FamilyManager.Types.Cast<FamilyType>().Any(item => string.Equals(item.Name, spec.TypeName, StringComparison.Ordinal)))
                throw new CommandResultException(ErrorCodes.InvalidParam, "The RFA does not contain the requested Family Type for Break Into preflight.");
            var partType = PartTypeKey(owner);
            if (partType is not ("breaks_into" or "valve_breaks_into"))
                throw new CommandResultException(ErrorCodes.InvalidParam, "The RFA Part Type must be Breaks Into or Valve Breaks Into.");
            var inspection = FamilyData.Inspect(family);
            var connectors = (inspection["connectors"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            if (connectors.Count != 2)
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Break Into Family must expose exactly two Family connectors; decorative or auxiliary ports are not accepted for this gate.");
            var expectedDomain = spec.CurveKind == "pipe" ? "Piping" : "Hvac";
            if (connectors.Any(item => !string.Equals(item.Value<string>("domain"), expectedDomain, StringComparison.Ordinal)))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Both Family connectors must use the " + expectedDomain + " domain.");
            var profile = connectors[0].Value<string>("shape") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(profile) || connectors.Any(item => !string.Equals(item.Value<string>("shape"), profile, StringComparison.Ordinal)))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Break Into connectors must have one matching profile.");
            if (spec.CurveKind == "pipe" && profile != "Round")
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Pipe Break Into requires two Round Piping connectors.");
            if (profile is not ("Round" or "Rectangular" or "Oval"))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Break Into connector profile is unsupported for this controlled UI gate.");
            if (!SizeMatches(connectors[0], connectors[1]))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Break Into connector sizes must match before placement.");
            var firstOrigin = PointMm(connectors[0]["origin_mm"] as JObject, "first Break Into connector origin");
            var secondOrigin = PointMm(connectors[1]["origin_mm"] as JObject, "second Break Into connector origin");
            var firstNormal = Vector(connectors[0]["normal"] as JObject, "first Break Into connector normal");
            var secondNormal = Vector(connectors[1]["normal"] as JObject, "second Break Into connector normal");
            var axis = secondOrigin - firstOrigin;
            if (axis.GetLength() <= MillimetreTolerance / 304.8 || firstNormal.Normalize().DotProduct(secondNormal.Normalize()) > -DirectionTolerance ||
                Math.Abs(firstNormal.Normalize().DotProduct(axis.Normalize())) < DirectionTolerance || Math.Abs(secondNormal.Normalize().DotProduct(axis.Normalize())) < DirectionTolerance)
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Break Into connectors must be two separated, aligned and opposing End ports.");
            var classification = connectors[0].Value<string>("system_classification");
            if (string.IsNullOrWhiteSpace(classification) || !string.Equals(classification, connectors[1].Value<string>("system_classification"), StringComparison.Ordinal) || classification is not ("Fitting" or "Global"))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Break Into connectors must share the Fitting or Global system classification.");
            return new JObject
            {
                ["status"] = "passed",
                ["family_name"] = owner.Name,
                ["family_type"] = spec.TypeName,
                ["category"] = owner.FamilyCategory?.Name,
                ["part_type"] = partType,
                ["curve_kind"] = spec.CurveKind,
                ["connector_contract"] = new JObject
                {
                    ["count"] = 2, ["domain"] = expectedDomain, ["profile"] = profile,
                    ["matching_size"] = true, ["opposing_and_aligned"] = true,
                    ["system_classification"] = classification,
                    ["connectors"] = new JArray(connectors.Select(PreflightConnectorReadBack))
                },
                ["read_only"] = true
            };
        }
        catch (CommandResultException) { throw; }
        catch (Exception ex) { throw new CommandResultException(ErrorCodes.VerificationFailed, "Break Into Family preflight failed without changing the Project: " + ex.Message); }
        finally { if (closeWhenDone && family != null) try { family.Close(false); } catch { } }
    }

    public static PreviewToken TakePreflight(JObject args)
    {
        var id = args.Value<string>("preflight_id");
        if (string.IsNullOrWhiteSpace(id) || BridgeServer.Current == null || !BridgeServer.Current.TryTake(id, out var token))
            throw new CommandResultException(ErrorCodes.PreviewInvalid, "Break Into preflight does not exist, expired, was already used, or belongs to another Revit session.");
        if (!string.Equals(token.Operation, "family_break_into_ui", StringComparison.OrdinalIgnoreCase))
            throw new CommandResultException(ErrorCodes.PreviewInvalid, "preflight_id belongs to another Family operation.");
        if (token.ExpiresAtUtc < DateTimeOffset.UtcNow)
            throw new CommandResultException(ErrorCodes.PreviewExpired, "Break Into preflight expired; run it again before controlled UI placement.");
        return token;
    }

    public static JObject Verify(Document project, FamilyBreakIntoUiSpec spec, ElementId instanceId, IReadOnlyList<ElementId> segmentIds)
    {
        var instance = project.GetElement(instanceId) as FamilyInstance
            ?? throw new CommandResultException(ErrorCodes.InvalidParam, "accessory_instance_id must resolve to the newly placed Break Into FamilyInstance.");
        var expectedCategory = spec.CurveKind == "pipe" ? BuiltInCategory.OST_PipeAccessory : BuiltInCategory.OST_DuctAccessory;
        if (instance.Category?.Id.IntVal() != (int)expectedCategory)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Placed instance has an unexpected Break Into category.");
        if (!string.Equals(instance.Symbol.FamilyName, spec.FamilyName, StringComparison.Ordinal) || !string.Equals(instance.Symbol.Name, spec.TypeName, StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Placed instance does not use the exact preflighted Family and Family Type.");
        var partType = PartTypeKey(instance.Symbol.Family);
        if (partType != spec.ExpectedPartType || partType is not ("breaks_into" or "valve_breaks_into"))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Placed instance does not retain the preflighted Breaks Into/Valve Breaks Into Part Type.");
        var segments = segmentIds.Select(id => project.GetElement(id) as MEPCurve ?? throw new CommandResultException(ErrorCodes.InvalidParam, "segment_ids must resolve to MEP curves.")).ToList();
        var expectedCurveCategory = spec.CurveKind == "pipe" ? BuiltInCategory.OST_PipeCurves : BuiltInCategory.OST_DuctCurves;
        if (segments.Any(item => item.Category?.Id.IntVal() != (int)expectedCurveCategory))
            throw new CommandResultException(ErrorCodes.InvalidParam, "segment_ids must use the selected Pipe/Duct curve kind.");
        var systemTypeIds = segments.Select(SystemTypeId).ToList();
        if (systemTypeIds.Any(item => item == ElementId.InvalidElementId) || systemTypeIds.Select(item => item.Val()).Distinct().Count() != 1)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "The two resulting segments do not share one valid Pipe/Duct system type.");
        var expectedDomain = spec.CurveKind == "pipe" ? Domain.DomainPiping : Domain.DomainHvac;
        var connectors = MepData.Connectors(instance).Where(item => item.Domain == expectedDomain && item.ConnectorType == ConnectorType.End).ToList();
        if (connectors.Count != 2 || connectors.Any(item => !item.IsConnected))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Placed Break Into instance must expose exactly two connected physical End connectors.");
        if (!InlineTopology(connectors, segmentIds, out var topology))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Placed Break Into connectors are not a complete one-to-one network with the two supplied resulting segments.");
        var axis = connectors[1].Origin - connectors[0].Origin;
        var normalOne = connectors[0].CoordinateSystem.BasisZ; var normalTwo = connectors[1].CoordinateSystem.BasisZ;
        if (axis.GetLength() <= MillimetreTolerance / 304.8 || normalOne.Normalize().DotProduct(normalTwo.Normalize()) > -DirectionTolerance ||
            Math.Abs(normalOne.Normalize().DotProduct(axis.Normalize())) < DirectionTolerance || Math.Abs(normalTwo.Normalize().DotProduct(axis.Normalize())) < DirectionTolerance)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Placed Break Into connector axis is not aligned with two opposing physical ports.");
        if (!PhysicalSizeMatches(connectors[0], connectors[1]))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Placed Break Into connector sizes do not match one another.");
        var connectionReadBack = new JArray();
        foreach (var connector in connectors)
        {
            var peer = connector.AllRefs.Cast<Connector>().Where(item => item.Owner != null && item.Owner.Id != instance.Id && segmentIds.Any(id => id == item.Owner.Id)).SingleOrDefault();
            if (peer == null || peer.Domain != connector.Domain || peer.Shape != connector.Shape || !PhysicalSizeMatches(connector, peer))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "A Break Into connector does not match the profile/size of its connected resulting segment.");
            connectionReadBack.Add(new JObject { ["accessory_connector"] = MepData.ConnectorDetail(connector), ["segment_id"] = peer.Owner.Id.Val(), ["segment_connector"] = MepData.ConnectorDetail(peer), ["profile_and_size_verified"] = true });
        }
        return new JObject
        {
            ["status"] = "passed",
            ["family"] = instance.Symbol.FamilyName,
            ["family_type"] = instance.Symbol.Name,
            ["part_type"] = partType,
            ["accessory_instance_id"] = instance.Id.Val(),
            ["curve_kind"] = spec.CurveKind,
            ["system_type_id"] = systemTypeIds[0].Val(),
            ["segment_ids"] = new JArray(segmentIds.Select(item => item.Val())),
            ["physical_topology"] = topology,
            ["connections"] = connectionReadBack,
            ["native_ui_command_not_observable_by_api"] = true,
            ["not_verified_here"] = new JArray("system_calculation_and_pressure_loss", "route_healing_after_removal", "type_change_rotate_mirror", "hosting", "lod350_bep_coordination")
        };
    }

    private static bool InlineTopology(IReadOnlyList<Connector> connectors, IReadOnlyList<ElementId> segmentIds, out JObject topology)
    {
        var connectedSegments = new List<long>();
        foreach (var connector in connectors)
        {
            var peers = connector.AllRefs.Cast<Connector>().Where(item => item.Owner != null && segmentIds.Any(id => id == item.Owner.Id)).ToList();
            if (peers.Count != 1) { topology = new JObject(); return false; }
            connectedSegments.Add(peers[0].Owner.Id.Val());
        }
        var expected = segmentIds.Select(item => item.Val()).OrderBy(item => item).ToArray();
        var actual = connectedSegments.OrderBy(item => item).ToArray();
        topology = new JObject { ["expected_segment_ids"] = new JArray(expected), ["connected_segment_ids"] = new JArray(actual), ["one_to_one"] = expected.SequenceEqual(actual) };
        return expected.SequenceEqual(actual);
    }

    private static ElementId SystemTypeId(MEPCurve curve)
    {
        if (curve.MEPSystem != null) return curve.MEPSystem.GetTypeId();
        var parameter = curve.Category?.Id.IntVal() == (int)BuiltInCategory.OST_PipeCurves
            ? curve.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)
            : curve.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM);
        return parameter?.AsElementId() ?? ElementId.InvalidElementId;
    }

    private static string PartTypeKey(Family family)
    {
        var value = family.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)?.AsInteger();
        if (!value.HasValue || !Enum.IsDefined(typeof(PartType), value.Value)) return string.Empty;
        var name = ((PartType)value.Value).ToString(); var result = new StringBuilder();
        for (var index = 0; index < name.Length; index++)
        {
            if (index > 0 && char.IsUpper(name[index]) && !char.IsUpper(name[index - 1])) result.Append('_');
            result.Append(char.ToLowerInvariant(name[index]));
        }
        return result.ToString();
    }

    private static bool SizeMatches(JObject left, JObject right)
    {
        var leftSize = left["size_mm"] as JObject; var rightSize = right["size_mm"] as JObject;
        if (leftSize == null || rightSize == null) return false;
        foreach (var key in new[] { "radius", "width", "height" })
        {
            var a = leftSize.Value<double?>(key); var b = rightSize.Value<double?>(key);
            if (a.HasValue != b.HasValue || a.HasValue && Math.Abs(a.Value - b!.Value) > MillimetreTolerance) return false;
        }
        return true;
    }

    private static bool PhysicalSizeMatches(Connector left, Connector right)
    {
        if (left.Shape != right.Shape) return false;
        try
        {
            return left.Shape == ConnectorProfileType.Round
                ? Math.Abs(left.Radius - right.Radius) * 304.8 <= MillimetreTolerance
                : Math.Abs(left.Width - right.Width) * 304.8 <= MillimetreTolerance && Math.Abs(left.Height - right.Height) * 304.8 <= MillimetreTolerance;
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException) { return false; }
    }

    private static XYZ PointMm(JObject? point, string label)
    {
        if (point == null || !point.Value<double?>("x_mm").HasValue || !point.Value<double?>("y_mm").HasValue || !point.Value<double?>("z_mm").HasValue)
            throw new CommandResultException(ErrorCodes.VerificationFailed, label + " is unavailable.");
        return new XYZ(point.Value<double>("x_mm") / 304.8, point.Value<double>("y_mm") / 304.8, point.Value<double>("z_mm") / 304.8);
    }
    private static XYZ Vector(JObject? value, string label)
    {
        if (value == null || !value.Value<double?>("x").HasValue || !value.Value<double?>("y").HasValue || !value.Value<double?>("z").HasValue)
            throw new CommandResultException(ErrorCodes.VerificationFailed, label + " is unavailable.");
        var vector = new XYZ(value.Value<double>("x"), value.Value<double>("y"), value.Value<double>("z"));
        if (vector.GetLength() < 1e-9) throw new CommandResultException(ErrorCodes.VerificationFailed, label + " is zero.");
        return vector;
    }
    private static JObject PreflightConnectorReadBack(JObject source) => new()
    {
        ["id"] = source["id"], ["role"] = source["role"], ["domain"] = source["domain"], ["profile"] = source["shape"],
        ["origin_mm"] = source["origin_mm"], ["normal"] = source["normal"], ["size_mm"] = source["size_mm"], ["system_classification"] = source["system_classification"]
    };
    private static bool SamePath(string? left, string right) => !string.IsNullOrWhiteSpace(left) && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
