using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using DSCons.RevitMcp.Contracts;
using DSCons.RevitMcp.Core;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

/// <summary>
/// Proves a narrowly scoped Shared nested-Family behavior inside a copied
/// Project. It loads and places only inside a TransactionGroup, changes the
/// declared Instance Shared Parameter, verifies the direct Shared child and
/// two category schedules, then always rolls the whole group back. It never
/// creates a persistent Schedule, Tag, Family or FamilyInstance.
/// </summary>
internal sealed class FamilySharedNestedProbePreviewCommand : ReadCommand
{
    public FamilySharedNestedProbePreviewCommand() : base("family_shared_nested_probe_preview", "Preview a copied-Project Shared nested Family parameter and Schedule field probe, then roll it back.") { Capability.IsWrite = true; }

    public override JObject Execute(UIApplication app, JObject args)
    {
        var project = Document(app);
        var spec = FamilySharedNestedProbeSpec.From(project, args);
        var validation = FamilySharedNestedProbe.Preview(project, spec);
        return new JObject
        {
            ["operation"] = "family_shared_nested_probe", ["model_changed"] = false,
            ["validation_level"] = "project_transactiongroup_rollback", ["validation"] = validation,
            ["next"] = "The copied Project was rolled back. Dynamic Tag Label/placed-Tag verification still requires a separately controlled Family Editor UI workflow."
        };
    }
}

internal sealed class FamilySharedNestedProbeSpec
{
    public PlaceSpec Placement { get; set; } = new();
    public Guid SharedParameterGuid { get; set; }
    public double ChangedValueFt { get; set; }
    public int ExpectedSharedChildCount { get; set; }

    public static FamilySharedNestedProbeSpec From(Document project, JObject args)
    {
        if (args.Value<bool?>("copied_project_confirmed") != true)
            throw new CommandResultException(ErrorCodes.InvalidParam, "copied_project_confirmed=true is required before a Shared nested Project probe.");
        var placement = FamilySafety.ValidatePlaceSpec(project, args);
        if (!Guid.TryParse(args.Value<string>("shared_parameter_guid"), out var guid) || guid == Guid.Empty)
            throw new CommandResultException(ErrorCodes.InvalidParam, "shared_parameter_guid must be a non-empty GUID.");
        var changedValueMm = args.Value<double?>("changed_value_mm") ?? 0;
        if (changedValueMm <= 0 || changedValueMm > 1_000_000)
            throw new CommandResultException(ErrorCodes.InvalidParam, "changed_value_mm must be greater than zero and no greater than 1000000 mm.");
        var expectedChildCount = args.Value<int?>("expected_shared_child_count") ?? 0;
        if (expectedChildCount < 1 || expectedChildCount > 20)
            throw new CommandResultException(ErrorCodes.InvalidParam, "expected_shared_child_count must be from 1 through 20.");
        return new FamilySharedNestedProbeSpec
        {
            Placement = placement, SharedParameterGuid = guid,
            ChangedValueFt = changedValueMm / 304.8, ExpectedSharedChildCount = expectedChildCount
        };
    }
}

internal static class FamilySharedNestedProbe
{
    private const double ValueToleranceFt = 1.0 / 304.8;

    public static JObject Preview(Document project, FamilySharedNestedProbeSpec spec)
    {
        using var revisionSuppression = DocumentRevisionTracker.Suppress();
        using var group = new TransactionGroup(project, "Validate DSCons Shared nested Family Project probe"); group.Start();
        try
        {
            JObject result;
            using (var transaction = new Transaction(project, "Simulate Shared nested Family parameter and Schedule probe"))
            {
                transaction.Start();
                result = LoadPlaceMutateAndVerify(project, spec);
                if (transaction.Commit() != TransactionStatus.Committed)
                    throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rejected the Shared nested Family probe transaction.");
            }
            group.RollBack();
            result["status"] = result.Value<bool?>("all_required_checks_passed") == true ? "passed" : "partial";
            result["model_changed"] = false;
            return result;
        }
        catch (CommandResultException) { group.RollBack(); throw; }
        catch (Exception exception)
        {
            group.RollBack();
            throw new CommandResultException(ErrorCodes.TransactionFailed, "Shared nested Family Project probe rolled back: " + exception.Message);
        }
    }

    private static JObject LoadPlaceMutateAndVerify(Document project, FamilySharedNestedProbeSpec spec)
    {
        var placement = FamilyPlacement.LoadAndPlace(project, spec.Placement);
        var parent = project.GetElement(RevitIdCompatibility.Eid(placement.Value<long>("instance_id"))) as FamilyInstance
            ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Placed Shared nested parent could not be read back.");
        project.Regenerate();

        var sharedDefinition = SharedParameterElement.Lookup(project, spec.SharedParameterGuid)
            ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "The declared Shared Parameter GUID was not materialized in the copied Project after Family load.");
        if (sharedDefinition.GuidValue != spec.SharedParameterGuid)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "The resolved Project Shared Parameter element does not match the declared GUID.");

        var parentParameter = RequireSharedLengthParameter(parent, sharedDefinition, spec.SharedParameterGuid, "parent", requireWritable: true);
        var children = new FilteredElementCollector(project).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
            .Where(instance => instance.Id.Val() != parent.Id.Val() && instance.SuperComponent?.Id.Val() == parent.Id.Val()).ToList();
        if (children.Count != spec.ExpectedSharedChildCount)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Expected " + spec.ExpectedSharedChildCount + " direct Shared nested child instance(s), but Revit exposed " + children.Count + ".");
        // Revit exposes a Shared nested child parameter as a read-only project
        // proxy. Only the parent interface is writable; the child is evidence
        // of GUID/value propagation and must not be rejected for read-only.
        var childParameters = children.Select(child => RequireSharedLengthParameter(child, sharedDefinition, spec.SharedParameterGuid, "nested child " + child.Id.Val(), requireWritable: false)).ToList();
        if (childParameters.Any(parameter => !string.Equals(parameter.Definition.Name, parentParameter.Definition.Name, StringComparison.Ordinal)))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Parent and Shared nested child parameter names differ after Project load.");

        var parentBefore = ParameterSnapshot(parentParameter);
        var childrenBefore = new JArray(childParameters.Select(ParameterSnapshot));
        if (!parentParameter.Set(spec.ChangedValueFt))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected the temporary Shared parent Instance Parameter value.");
        project.Regenerate();
        var parentAfter = ParameterSnapshot(parentParameter);
        var childrenAfter = new JArray(childParameters.Select(ParameterSnapshot));
        if (Math.Abs(parentParameter.AsDouble() - spec.ChangedValueFt) > ValueToleranceFt)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Shared parent Instance Parameter did not retain the requested temporary value.");
        // A common Shared GUID proves definition identity, but Revit can expose
        // the child as a read-only proxy with its own retained value. Record
        // that outcome rather than claiming a parent-to-child value binding.
        var childValuePropagated = childParameters.All(parameter => Math.Abs(parameter.AsDouble() - spec.ChangedValueFt) <= ValueToleranceFt);

        var parentSchedule = CreateScheduleFieldSnapshot(project, parent.Category?.Id, sharedDefinition.Id, parentParameter.Definition.Name, "parent");
        var childSchedule = children.All(child => child.Category?.Id.Val() == parent.Category?.Id.Val())
            ? (JObject)parentSchedule.DeepClone()
            : CreateScheduleFieldSnapshot(project, children[0].Category?.Id, sharedDefinition.Id, parentParameter.Definition.Name, "shared_child");
        childSchedule["same_category_schedule_as_parent"] = children.All(child => child.Category?.Id.Val() == parent.Category?.Id.Val());

        return new JObject
        {
            ["family"] = placement.Value<string>("family"), ["type"] = placement.Value<string>("type"),
            ["parent_instance_id"] = parent.Id.Val(), ["level_id"] = parent.LevelId.Val(),
            ["shared_parameter"] = new JObject { ["guid"] = spec.SharedParameterGuid.ToString("D"), ["name"] = parentParameter.Definition.Name, ["shared_parameter_element_id"] = sharedDefinition.Id.Val(), ["changed_value_mm"] = Math.Round(spec.ChangedValueFt * 304.8, 3) },
            ["value_propagation"] = new JObject { ["parent_before"] = parentBefore, ["parent_after"] = parentAfter, ["children_before"] = childrenBefore, ["children_after"] = childrenAfter, ["verified"] = childValuePropagated, ["status"] = childValuePropagated ? "parent_to_child_shared_guid_value_propagated" : "shared_guid_identity_without_parent_to_child_value_propagation", ["boundary"] = childValuePropagated ? null : "Matching Shared GUIDs do not by themselves create a writable parent-to-child value association in this Revit Project probe." },
            ["nested_children"] = new JArray(children.Select(child => new JObject { ["instance_id"] = child.Id.Val(), ["family"] = child.Symbol.FamilyName, ["type"] = child.Symbol.Name, ["category_id"] = child.Category?.Id.Val() })),
            ["schedule_fields"] = new JObject { ["parent"] = parentSchedule, ["shared_child"] = childSchedule, ["verified"] = true },
            ["tag"] = new JObject { ["verified"] = false, ["status"] = "dynamic_label_ui_and_tag_rfa_required", ["boundary"] = "A Schedule field proves only schedule availability. It does not prove a placed Tag displays this shared value." },
            ["all_required_checks_passed"] = childValuePropagated,
            ["capability_status"] = childValuePropagated ? "shared_guid_value_propagation_and_schedule_field_verified" : "shared_guid_identity_and_schedule_field_only"
        };
    }

    private static Parameter RequireSharedLengthParameter(FamilyInstance instance, SharedParameterElement sharedDefinition, Guid guid, string role, bool requireWritable)
    {
        var parameter = instance.get_Parameter(guid)
            ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "The " + role + " has no parameter for the declared Shared GUID.");
        if (!parameter.IsShared || parameter.Id.Val() != sharedDefinition.Id.Val())
            throw new CommandResultException(ErrorCodes.VerificationFailed, "The " + role + " parameter is not the exact Project Shared Parameter definition for the declared GUID.");
        if (parameter.StorageType != StorageType.Double)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "The " + role + " Shared Parameter must be a Length/Double value for this probe.");
        if (requireWritable && parameter.IsReadOnly)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "The parent Shared Parameter must be writable for this propagation probe.");
        return parameter;
    }

    private static JObject ParameterSnapshot(Parameter parameter) => new()
    {
        ["name"] = parameter.Definition.Name, ["parameter_id"] = parameter.Id.Val(),
        ["is_shared"] = parameter.IsShared, ["is_read_only"] = parameter.IsReadOnly, ["value_mm"] = Math.Round(parameter.AsDouble() * 304.8, 3)
    };

    private static JObject CreateScheduleFieldSnapshot(Document project, ElementId? categoryId, ElementId sharedParameterId, string expectedName, string role)
    {
        if (categoryId == null || categoryId == ElementId.InvalidElementId)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "The " + role + " Family category is unavailable for Schedule verification.");
        var schedule = ViewSchedule.CreateSchedule(project, categoryId);
        var definition = schedule.Definition;
        var matchingFields = definition.GetSchedulableFields().Where(field => field.ParameterId.Val() == sharedParameterId.Val()).ToList();
        if (matchingFields.Count != 1)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "The " + role + " category did not expose exactly one schedulable field for the Shared Parameter GUID.");
        var field = matchingFields[0];
        var fieldName = field.GetName(project);
        if (!string.Equals(fieldName, expectedName, StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "The " + role + " schedulable Shared Parameter field name did not match the loaded Family parameter.");
        definition.AddField(field);
        if (definition.GetFieldCount() != 1)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "The " + role + " Schedule did not retain exactly one added Shared Parameter field.");
        return new JObject
        {
            ["schedule_id"] = schedule.Id.Val(), ["category_id"] = categoryId.Val(), ["parameter_id"] = sharedParameterId.Val(),
            ["field_name"] = fieldName, ["field_count"] = definition.GetFieldCount(), ["verified"] = true
        };
    }
}
