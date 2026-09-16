using Autodesk.Revit.DB;
using DSCons.RevitMcp.Contracts;
using DSCons.RevitMcp.Core;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

internal static class DocumentationScheduleSupport
{
    private static readonly IReadOnlyDictionary<string, BuiltInCategory> Categories =
        new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
        {
            ["pipes"] = BuiltInCategory.OST_PipeCurves,
            ["pipe_fittings"] = BuiltInCategory.OST_PipeFitting,
            ["pipe_accessories"] = BuiltInCategory.OST_PipeAccessory
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<BuiltInParameter>> Fields =
        new Dictionary<string, IReadOnlyList<BuiltInParameter>>(StringComparer.OrdinalIgnoreCase)
        {
            ["family_and_type"] = new[] { BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM },
            ["system_type"] = new[] { BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM },
            // Pipe schedules expose their Reference Level through the MEP
            // curve start-level parameter rather than the generic family
            // instance reference-level parameter.
            ["reference_level"] = new[] { BuiltInParameter.RBS_START_LEVEL_PARAM },
            ["diameter"] = new[] { BuiltInParameter.RBS_PIPE_DIAMETER_PARAM },
            ["length"] = new[] { BuiltInParameter.CURVE_ELEM_LENGTH },
            ["system_name"] = new[] { BuiltInParameter.RBS_SYSTEM_NAME_PARAM },
            ["system_abbreviation"] = new[] { BuiltInParameter.RBS_DUCT_PIPE_SYSTEM_ABBREVIATION_PARAM, BuiltInParameter.RBS_SYSTEM_ABBREVIATION_PARAM },
            ["size"] = new[] { BuiltInParameter.RBS_CALCULATED_SIZE },
            ["area"] = new[] { BuiltInParameter.RBS_CURVE_SURFACE_AREA, BuiltInParameter.HOST_AREA_COMPUTED },
            ["comments"] = new[] { BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS }
        };

    public static JArray Discover(Document doc, IEnumerable<string> requestedCategories)
    {
        var categories = requestedCategories.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var result = new JArray();
        foreach (var category in categories)
        {
            if (!Categories.TryGetValue(category, out var builtInCategory))
            {
                result.Add(new JObject { ["category"] = category, ["supported"] = false, ["available_fields"] = new JArray() });
                continue;
            }

            var ownsTransaction = !doc.IsModifiable;
            using var transaction = ownsTransaction ? new Transaction(doc, "DSCons schedule field discovery") : null;
            if (ownsTransaction) transaction!.Start();
            ViewSchedule? schedule = null;
            try
            {
                schedule = ViewSchedule.CreateSchedule(doc, RevitIdCompatibility.Eid((long)(int)builtInCategory));
                var definition = schedule.Definition;
                var schedulable = definition.GetSchedulableFields();
                var available = new JArray();
                foreach (var pair in Fields.Where(x => !string.Equals(x.Key, "comments", StringComparison.OrdinalIgnoreCase)))
                {
                    var source = FindSchedulable(schedulable, pair.Value);
                    if (source == null) continue;
                    var field = definition.AddField(source);
                    available.Add(new JObject
                    {
                        ["key"] = pair.Key,
                        ["display_name"] = source.GetName(doc),
                        ["parameter_id"] = source.ParameterId.Val(),
                        ["can_total"] = field.CanTotal(),
                        ["can_sort"] = definition.CanSortByField(field.FieldId)
                    });
                }
                var count = definition.AddField(ScheduleFieldType.Count);
                available.Add(new JObject { ["key"] = "count", ["display_name"] = count.GetName(), ["parameter_id"] = null, ["can_total"] = count.CanTotal(), ["can_sort"] = definition.CanSortByField(count.FieldId) });
                result.Add(new JObject { ["category"] = category, ["supported"] = true, ["available_fields"] = available });
            }
            finally
            {
                if (ownsTransaction) transaction!.RollBack();
                else if (schedule != null) doc.Delete(schedule.Id);
            }
        }
        return result;
    }

    public static void ValidatePlans(Document doc, IEnumerable<JObject> plans, JArray errors)
    {
        var names = new HashSet<string>(
            new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().Select(x => x.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var plan in plans)
        {
            var name = plan.Value<string>("name") ?? string.Empty;
            var category = plan.Value<string>("category") ?? string.Empty;
            var fields = (plan["fields"] as JArray ?? new JArray()).Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList();
            var sortBy = (plan["sort_by"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            var totalFields = (plan["total_fields"] as JArray ?? new JArray()).Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList();
            if (string.IsNullOrWhiteSpace(name)) errors.Add("Schedule name is required.");
            else if (!names.Add(name)) errors.Add("Schedule name already exists or is duplicated in this request: " + name);
            if (!Categories.ContainsKey(category)) errors.Add("Unsupported schedule category: " + category + ". Use pipes, pipe_fittings or pipe_accessories.");
            if (plan.Value<bool?>("learner_requirements_confirmed") != true) errors.Add("Schedule " + name + " requires learner_requirements_confirmed=true after asking the learner which columns, sort/group order and totals are needed.");
            if (plan["sort_by"] is not JArray) errors.Add("Schedule " + name + " requires an explicit sort_by array; use [] only after the learner confirms no sorting/grouping.");
            if (plan["total_fields"] is not JArray) errors.Add("Schedule " + name + " requires an explicit total_fields array; use [] only after the learner confirms no totals.");
            if (fields.Count == 0) errors.Add("Schedule fields are required for " + name + ".");
            if (fields.Count != fields.Distinct(StringComparer.OrdinalIgnoreCase).Count()) errors.Add("Schedule fields must be unique for " + name + ".");
            foreach (var field in fields)
                if (!string.Equals(field, "count", StringComparison.OrdinalIgnoreCase) && !Fields.ContainsKey(field)) errors.Add("Unsupported schedule field " + field + " for " + name + ".");
            foreach (var sorting in sortBy)
            {
                var field = sorting.Value<string>("field") ?? string.Empty;
                var order = sorting.Value<string>("order") ?? string.Empty;
                if (!fields.Contains(field, StringComparer.OrdinalIgnoreCase)) errors.Add("Sort/group field " + field + " must also be selected as a column in " + name + ".");
                if (!string.Equals(order, "ascending", StringComparison.OrdinalIgnoreCase) && !string.Equals(order, "descending", StringComparison.OrdinalIgnoreCase)) errors.Add("Sort order for " + field + " must be ascending or descending.");
            }
            if (sortBy.Select(x => x.Value<string>("field") ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sortBy.Count) errors.Add("Sort/group fields must be unique for " + name + ".");
            foreach (var field in totalFields)
                if (!fields.Contains(field, StringComparer.OrdinalIgnoreCase)) errors.Add("Total field " + field + " must also be selected as a column in " + name + ".");
            if (totalFields.Count != totalFields.Distinct(StringComparer.OrdinalIgnoreCase).Count()) errors.Add("Total fields must be unique for " + name + ".");

            if (Categories.ContainsKey(category))
            {
                var catalog = Discover(doc, new[] { category }).OfType<JObject>().First();
                var capabilities = (catalog["available_fields"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(x => x.Value<string>("key") ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                foreach (var field in fields)
                    if (!capabilities.ContainsKey(field)) errors.Add("Field " + field + " is not available for schedule category " + category + " in the active Revit document.");
                foreach (var sorting in sortBy)
                {
                    var field = sorting.Value<string>("field") ?? string.Empty;
                    if (capabilities.TryGetValue(field, out var capability) && capability.Value<bool?>("can_sort") != true) errors.Add("Field " + field + " cannot be used for sorting/grouping in " + name + ".");
                }
                foreach (var field in totalFields)
                    if (capabilities.TryGetValue(field, out var capability) && capability.Value<bool?>("can_total") != true) errors.Add("Field " + field + " cannot be totaled in " + name + ".");
            }

            var filter = plan["filter"] as JObject;
            if (filter == null || !string.Equals(filter.Value<string>("field"), "comments", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(filter.Value<string>("operator"), "equals", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(filter.Value<string>("value")) || filter.Value<bool?>("hidden") != true)
                errors.Add("Schedule " + name + " requires a hidden comments equals filter with a non-empty value.");
        }
    }

    public static ViewSchedule Create(Document doc, JObject plan)
    {
        var errors = new JArray();
        ValidatePlans(doc, new[] { plan }, errors);
        if (errors.Count > 0) throw new CommandResultException(ErrorCodes.InvalidParam, string.Join(" ", errors.Values<string>()));

        var categoryName = plan.Value<string>("category")!;
        var schedule = ViewSchedule.CreateSchedule(doc, RevitIdCompatibility.Eid((long)(int)Categories[categoryName]));
        schedule.Name = plan.Value<string>("name")!;
        var definition = schedule.Definition;
        var added = new Dictionary<string, ScheduleField>(StringComparer.OrdinalIgnoreCase);
        foreach (var semantic in (plan["fields"] as JArray ?? new JArray()).Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!))
        {
            if (string.Equals(semantic, "count", StringComparison.OrdinalIgnoreCase))
            {
                added[semantic] = definition.AddField(ScheduleFieldType.Count);
                continue;
            }
            added[semantic] = AddBuiltInField(definition, Fields[semantic], semantic, schedule.Name);
        }

        foreach (var sorting in (plan["sort_by"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var semantic = sorting.Value<string>("field")!;
            var order = string.Equals(sorting.Value<string>("order"), "descending", StringComparison.OrdinalIgnoreCase) ? ScheduleSortOrder.Descending : ScheduleSortOrder.Ascending;
            var sort = new ScheduleSortGroupField(added[semantic].FieldId, order)
            {
                ShowHeader = sorting.Value<bool?>("show_header") == true,
                ShowFooter = sorting.Value<bool?>("show_footer") == true,
                ShowFooterCount = sorting.Value<bool?>("show_footer_count") == true,
                ShowBlankLine = sorting.Value<bool?>("blank_line") == true
            };
            definition.AddSortGroupField(sort);
        }
        definition.IsItemized = plan.Value<bool?>("is_itemized") ?? true;
        var totalFields = (plan["total_fields"] as JArray ?? new JArray()).Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList();
        foreach (var semantic in totalFields)
        {
            var field = added[semantic];
            if (!field.CanTotal()) throw new CommandResultException(ErrorCodes.InvalidParam, "Field " + semantic + " cannot be totaled in " + schedule.Name + ".");
            field.DisplayType = ScheduleFieldDisplayType.Totals;
        }
        definition.ShowGrandTotal = totalFields.Count > 0;
        definition.ShowGrandTotalTitle = totalFields.Count > 0;
        definition.ShowGrandTotalCount = plan.Value<bool?>("show_grand_total_count") == true;

        var comments = added.TryGetValue("comments", out var existing)
            ? existing
            : AddBuiltInField(definition, Fields["comments"], "comments", schedule.Name);
        comments.IsHidden = true;
        var filter = plan["filter"] as JObject ?? new JObject();
        definition.AddFilter(new ScheduleFilter(comments.FieldId, ScheduleFilterType.Equal, filter.Value<string>("value") ?? string.Empty));
        return schedule;
    }

    private static ScheduleField AddBuiltInField(ScheduleDefinition definition, IReadOnlyList<BuiltInParameter> parameters, string semantic, string scheduleName)
    {
        var schedulable = FindSchedulable(definition.GetSchedulableFields(), parameters);
        if (schedulable == null)
            throw new CommandResultException(ErrorCodes.InvalidParam, "Field " + semantic + " is not schedulable for " + scheduleName + ".");
        return definition.AddField(schedulable);
    }

    private static SchedulableField? FindSchedulable(IList<SchedulableField> schedulable, IReadOnlyList<BuiltInParameter> parameters)
    {
        foreach (var parameter in parameters)
        {
            var parameterId = RevitIdCompatibility.Eid((long)(int)parameter);
            var match = schedulable.FirstOrDefault(x => x.ParameterId == parameterId);
            if (match != null) return match;
        }
        return null;
    }

    public static JObject ReadBack(ViewSchedule schedule)
    {
        var definition = schedule.Definition;
        var fields = definition.GetFieldOrder().Select(definition.GetField).ToList();
        return new JObject
        {
            ["id"] = schedule.Id.Val(),
            ["name"] = schedule.Name,
            ["category_id"] = definition.CategoryId.Val(),
            ["field_names"] = new JArray(fields.Select(x => x.GetName())),
            ["total_field_names"] = new JArray(fields.Where(x => x.DisplayType == ScheduleFieldDisplayType.Totals).Select(x => x.GetName())),
            ["sort_group_fields"] = new JArray(definition.GetSortGroupFields().Select(x => new JObject { ["field_name"] = definition.GetField(x.FieldId).GetName(), ["order"] = x.SortOrder.ToString(), ["show_header"] = x.ShowHeader, ["show_footer"] = x.ShowFooter, ["show_footer_count"] = x.ShowFooterCount, ["blank_line"] = x.ShowBlankLine })),
            ["is_itemized"] = definition.IsItemized,
            ["show_grand_total"] = definition.ShowGrandTotal,
            ["hidden_field_count"] = fields.Count(x => x.IsHidden),
            ["filter_count"] = definition.GetFilters().Count
        };
    }
}
