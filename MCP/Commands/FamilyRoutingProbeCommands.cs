using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using DSCons.RevitMcp.Contracts;
using DSCons.RevitMcp.Core;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

/// <summary>
/// Executes a deliberately temporary Project-level routing test for a newly
/// built Duct/Pipe Fitting. The RFA is loaded, placed first in the relevant
/// routing-preference group and selected by Revit to create a real fitting;
/// the enclosing TransactionGroup is always rolled back. This is evidence of
/// one routing behaviour only, not a claim that every routing or system
/// calculation behaviour of the Family is certified.
/// </summary>
internal sealed class FamilyRoutingProbePreviewCommand : ReadCommand
{
    public FamilyRoutingProbePreviewCommand() : base("family_routing_probe_preview", "Preview a copied-Project Routing Preference and connector-network probe for one Duct/Pipe Fitting RFA.") { Capability.IsWrite = true; }

    public override JObject Execute(UIApplication app, JObject args)
    {
        var project = Document(app);
        var spec = FamilyRoutingProbeSpec.From(project, args);
        var validation = FamilyRoutingProbe.Preview(project, spec);
        return new JObject
        {
            ["operation"] = "family_routing_probe",
            ["model_changed"] = false,
            ["validation_level"] = "project_transactiongroup_rollback",
            ["validation"] = validation,
            ["next"] = "The copied Project was rolled back. A separate approved load/place and a separate Break Into, system-calculation and LOD350 test are still required."
        };
    }
}

internal sealed class FamilyRoutingProbeSpec
{
    public PlaceSpec Placement { get; set; } = new();
    public string CurveKind { get; set; } = string.Empty;
    public ElementId CurveTypeId { get; set; } = ElementId.InvalidElementId;
    public ElementId SystemTypeId { get; set; } = ElementId.InvalidElementId;

    public static FamilyRoutingProbeSpec From(Document project, JObject args)
    {
        if (args.Value<bool?>("copied_project_confirmed") != true)
            throw new CommandResultException(ErrorCodes.InvalidParam, "copied_project_confirmed=true is required before a Project routing probe.");
        var placement = FamilySafety.ValidatePlaceSpec(project, args);
        var curveKind = args.Value<string>("curve_kind") ?? string.Empty;
        if (curveKind is not ("pipe" or "duct"))
            throw new CommandResultException(ErrorCodes.InvalidParam, "curve_kind must be pipe or duct.");
        var curveTypeId = RevitIdCompatibility.Eid(args.Value<long>("curve_type_id"));
        var systemTypeId = RevitIdCompatibility.Eid(args.Value<long>("system_type_id"));
        var curveType = project.GetElement(curveTypeId);
        var systemType = project.GetElement(systemTypeId);
        if (curveKind == "pipe")
        {
            if (curveType is not PipeType) throw new CommandResultException(ErrorCodes.InvalidParam, "curve_type_id must reference a Pipe Type when curve_kind=pipe.");
            if (systemType is not PipingSystemType) throw new CommandResultException(ErrorCodes.InvalidParam, "system_type_id must reference a Piping System Type when curve_kind=pipe.");
        }
        else
        {
            if (curveType is not DuctType) throw new CommandResultException(ErrorCodes.InvalidParam, "curve_type_id must reference a Duct Type when curve_kind=duct.");
            if (systemType is not MechanicalSystemType) throw new CommandResultException(ErrorCodes.InvalidParam, "system_type_id must reference a Duct System Type when curve_kind=duct.");
        }
        MepSafety.GuardWrite(project, new[] { curveType!, systemType!, project.GetElement(placement.LevelId)! });
        return new FamilyRoutingProbeSpec { Placement = placement, CurveKind = curveKind, CurveTypeId = curveTypeId, SystemTypeId = systemTypeId };
    }
}

internal static class FamilyRoutingProbe
{
    private const double LegLengthMm = 1000;
    private const double ConnectorToleranceFt = 1.0 / 304.8;

    public static JObject Preview(Document project, FamilyRoutingProbeSpec spec)
    {
        using var revisionSuppression = DocumentRevisionTracker.Suppress();
        var beforeRollback = RollbackSnapshot(project, spec);
        using var group = new TransactionGroup(project, "Validate DSCons Family routing preference");
        group.Start();
        try
        {
            JObject simulated;
            using (var transaction = new Transaction(project, "Simulate DSCons Family routing preference"))
            {
                transaction.Start();
                simulated = Probe(project, spec);
                if (transaction.Commit() != TransactionStatus.Committed)
                    throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rejected the temporary routing-preference Family probe.");
            }
            group.RollBack();
            var afterRollback = RollbackSnapshot(project, spec);
            if (!JToken.DeepEquals(beforeRollback, afterRollback))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "The temporary Family routing probe did not restore the copied Project routing/family inventory after rollback.");
            return new JObject
            {
                ["status"] = "passed",
                ["model_changed"] = false,
                ["simulated_result"] = simulated,
                ["rollback_state_read_back"] = new JObject { ["verified"] = true, ["before"] = beforeRollback, ["after"] = afterRollback },
                ["note"] = "The RFA load, routing-preference rule and temporary network were committed only inside a TransactionGroup and then rolled back. Temporary element IDs are not persistent."
            };
        }
        catch (CommandResultException)
        {
            group.RollBack();
            throw;
        }
        catch (Exception ex)
        {
            group.RollBack();
            throw new CommandResultException(ErrorCodes.TransactionFailed, "Family routing-preference probe rolled back: " + ex.Message);
        }
    }

    private static JObject Probe(Document project, FamilyRoutingProbeSpec spec)
    {
        if (!project.LoadFamily(spec.Placement.FamilyPath, new RejectOverwriteFamilyLoadOptions(), out var family) || family == null)
            throw new CommandResultException(ErrorCodes.FileConflict, "The probe only accepts an unloaded RFA. A Family with the same name is already loaded or Revit rejected the load; use an isolated copied Project and a unique Family name.");
        var symbol = new FilteredElementCollector(project).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .SingleOrDefault(item => item.Family.Id == family.Id && string.Equals(item.Name, spec.Placement.TypeName, StringComparison.Ordinal));
        if (symbol == null) throw new CommandResultException(ErrorCodes.VerificationFailed, "Loaded RFA does not contain the requested Family Type for routing verification.");
        if (!symbol.IsActive) symbol.Activate();
        project.Regenerate();

        var expectedCategory = spec.CurveKind == "pipe" ? BuiltInCategory.OST_PipeFitting : BuiltInCategory.OST_DuctFitting;
        if (family.FamilyCategory?.Id.IntVal() != (int)expectedCategory)
            throw new CommandResultException(ErrorCodes.InvalidParam, "The RFA category must be " + expectedCategory + " for curve_kind=" + spec.CurveKind + ". Pipe/Duct Accessories use the separate inline Break Into acceptance gate.");
        var partType = ReadPartType(family);
        var ruleGroup = RoutingGroupFor(partType);
        var manager = RoutingManager(project, spec);
        manager.AddRule(ruleGroup, new RoutingPreferenceRule(symbol.Id, "DSCons temporary routing probe"), 0);
        var installed = manager.GetRule(ruleGroup, 0);
        if (installed.MEPPartId != symbol.Id)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "The requested Family Type was not placed first in the temporary Routing Preference group.");

        var curves = CreateScenario(project, spec, ruleGroup);
        project.Regenerate();
        var fitting = CreateFitting(project, curves, spec.Placement.Point, ruleGroup);
        project.Regenerate();
        if (fitting.Symbol.Id != symbol.Id)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit created a routing fitting, but it did not select the requested Family Type from Routing Preferences.");
        if (fitting.Category?.Id.IntVal() != (int)expectedCategory)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit selected a fitting with an unexpected category.");

        var expectedCurveIds = curves.Select(item => item.Id.Val()).OrderBy(id => id).ToArray();
        var connectedCurveIds = MepData.Connectors(fitting)
            .SelectMany(connector => connector.AllRefs.Cast<Connector>())
            .Select(connector => connector.Owner)
            .Where(owner => owner != null && curves.Any(curve => curve.Id == owner!.Id))
            .Select(owner => owner!.Id.Val())
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        if (!expectedCurveIds.SequenceEqual(connectedCurveIds))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "The selected Family Type did not expose a complete physical connector network for the temporary routing scenario.");
        var fittingConnectors = MepData.Connectors(fitting).ToList();
        if (fittingConnectors.Count < expectedCurveIds.Length || fittingConnectors.Any(connector => !connector.IsConnected))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "The selected Family Type has an unconnected fitting connector after Revit routing.");

        return new JObject
        {
            ["curve_kind"] = spec.CurveKind,
            ["family"] = family.Name,
            ["family_type"] = symbol.Name,
            ["family_symbol_id"] = symbol.Id.Val(),
            ["part_type"] = PartTypeKey(partType),
            ["routing_preference"] = new JObject { ["curve_type_id"] = spec.CurveTypeId.Val(), ["group"] = ruleGroup.ToString(), ["rule_index"] = 0, ["selected_symbol_id"] = installed.MEPPartId.Val(), ["verified"] = true },
            ["network"] = new JObject { ["fitting_id"] = fitting.Id.Val(), ["expected_curve_ids"] = new JArray(expectedCurveIds), ["connected_curve_ids"] = new JArray(connectedCurveIds), ["fitting_connector_count"] = fittingConnectors.Count, ["verified"] = true },
            ["not_verified_here"] = new JArray("break_into_or_valve_breaks_into_ui_placement", "system_calculation_and_pressure_loss", "type_change_rotate_mirror", "lod350_bep_coordination")
        };
    }

    private static RoutingPreferenceManager RoutingManager(Document project, FamilyRoutingProbeSpec spec) => spec.CurveKind == "pipe"
        ? ((PipeType)project.GetElement(spec.CurveTypeId)).RoutingPreferenceManager
        : ((DuctType)project.GetElement(spec.CurveTypeId)).RoutingPreferenceManager;

    private static JObject RollbackSnapshot(Document project, FamilyRoutingProbeSpec spec)
    {
        var manager = RoutingManager(project, spec);
        var ruleCounts = new JObject();
        foreach (RoutingPreferenceRuleGroupType group in Enum.GetValues(typeof(RoutingPreferenceRuleGroupType)))
        {
            if (group == RoutingPreferenceRuleGroupType.Undefined) continue;
            ruleCounts[group.ToString()] = manager.GetNumberOfRules(group);
        }
        return new JObject
        {
            ["document_element_count"] = new FilteredElementCollector(project).GetElementCount(),
            ["loaded_family_count"] = new FilteredElementCollector(project).OfClass(typeof(Family)).GetElementCount(),
            ["routing_rule_counts"] = ruleCounts
        };
    }

    private static PartType ReadPartType(Family family)
    {
        var value = family.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)?.AsInteger();
        if (!value.HasValue || !Enum.IsDefined(typeof(PartType), value.Value) || (PartType)value.Value == PartType.Undefined)
            throw new CommandResultException(ErrorCodes.InvalidParam, "The loaded RFA has no supported Family Part Type for a Routing Preference test.");
        return (PartType)value.Value;
    }

    private static RoutingPreferenceRuleGroupType RoutingGroupFor(PartType partType) => partType.ToString() switch
    {
        "Elbow" => RoutingPreferenceRuleGroupType.Elbows,
        "Tee" or "Wye" or "LateralTee" or "TapAdjustable" or "TapPerpendicular" => RoutingPreferenceRuleGroupType.Junctions,
        "Cross" or "LateralCross" => RoutingPreferenceRuleGroupType.Crosses,
        "Transition" => RoutingPreferenceRuleGroupType.Transitions,
        "Union" => RoutingPreferenceRuleGroupType.Unions,
        _ => throw new CommandResultException(ErrorCodes.Unsupported, "Part Type " + partType + " does not have a bounded automated Routing Preference probe. Break Into and Valve Breaks Into require their own Project acceptance gate.")
    };

    private static List<MEPCurve> CreateScenario(Document project, FamilyRoutingProbeSpec spec, RoutingPreferenceRuleGroupType group)
    {
        var center = spec.Placement.Point;
        var length = LegLengthMm / 304.8;
        var west = center - XYZ.BasisX * length;
        var east = center + XYZ.BasisX * length;
        var south = center - XYZ.BasisY * length;
        var north = center + XYZ.BasisY * length;
        return group switch
        {
            RoutingPreferenceRuleGroupType.Elbows => new List<MEPCurve> { CreateCurve(project, spec, west, center), CreateCurve(project, spec, center, north) },
            RoutingPreferenceRuleGroupType.Junctions => new List<MEPCurve> { CreateCurve(project, spec, west, center), CreateCurve(project, spec, center, east), CreateCurve(project, spec, center, north) },
            RoutingPreferenceRuleGroupType.Crosses => new List<MEPCurve> { CreateCurve(project, spec, west, center), CreateCurve(project, spec, center, east), CreateCurve(project, spec, south, center), CreateCurve(project, spec, center, north) },
            RoutingPreferenceRuleGroupType.Transitions or RoutingPreferenceRuleGroupType.Unions => new List<MEPCurve> { CreateCurve(project, spec, west, center), CreateCurve(project, spec, center, east) },
            _ => throw new CommandResultException(ErrorCodes.Unsupported, "The requested routing preference group has no bounded scenario.")
        };
    }

    private static MEPCurve CreateCurve(Document project, FamilyRoutingProbeSpec spec, XYZ start, XYZ end) => spec.CurveKind == "pipe"
        ? Pipe.Create(project, spec.SystemTypeId, spec.CurveTypeId, spec.Placement.LevelId, start, end)
        : Duct.Create(project, spec.SystemTypeId, spec.CurveTypeId, spec.Placement.LevelId, start, end);

    private static FamilyInstance CreateFitting(Document project, IReadOnlyList<MEPCurve> curves, XYZ center, RoutingPreferenceRuleGroupType group)
    {
        var connector = curves.Select(curve => ConnectorAt(curve, center)).ToArray();
        try
        {
            return group switch
            {
                RoutingPreferenceRuleGroupType.Elbows => project.Create.NewElbowFitting(connector[0], connector[1]),
                RoutingPreferenceRuleGroupType.Junctions => project.Create.NewTeeFitting(connector[0], connector[1], connector[2]),
                RoutingPreferenceRuleGroupType.Crosses => project.Create.NewCrossFitting(connector[0], connector[1], connector[2], connector[3]),
                RoutingPreferenceRuleGroupType.Transitions => project.Create.NewTransitionFitting(connector[0], connector[1]),
                RoutingPreferenceRuleGroupType.Unions => project.Create.NewUnionFitting(connector[0], connector[1]),
                _ => throw new CommandResultException(ErrorCodes.Unsupported, "The requested routing preference group has no fitting factory.")
            };
        }
        catch (CommandResultException) { throw; }
        catch (Exception ex)
        {
            throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit could not create the routing fitting. Check the selected Pipe/Duct Type, system, size/profile compatibility and Family connector topology. " + ex.Message);
        }
    }

    private static Connector ConnectorAt(MEPCurve curve, XYZ point)
    {
        var connector = MepData.Connectors(curve).OrderBy(item => item.Origin.DistanceTo(point)).FirstOrDefault();
        if (connector == null || connector.Origin.DistanceTo(point) > ConnectorToleranceFt)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Temporary curve does not expose a connector at the routing junction.");
        return connector;
    }

    private static string PartTypeKey(PartType value)
    {
        var name = value.ToString();
        var result = new System.Text.StringBuilder();
        for (var index = 0; index < name.Length; index++)
        {
            if (index > 0 && char.IsUpper(name[index]) && !char.IsUpper(name[index - 1])) result.Append('_');
            result.Append(char.ToLowerInvariant(name[index]));
        }
        return result.ToString();
    }

    private sealed class RejectOverwriteFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues) { overwriteParameterValues = false; return false; }
        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues) { source = FamilySource.Family; overwriteParameterValues = false; return false; }
    }
}
