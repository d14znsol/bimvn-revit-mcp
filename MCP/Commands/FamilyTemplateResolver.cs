using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using DSCons.RevitMcp.Contracts;
using DSCons.RevitMcp.Core;

namespace DSCons.RevitMcp.Commands;

internal sealed class FamilyTemplateSelection
{
    public string Path { get; set; } = string.Empty;
    public string CategoryKey { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string RevitVersion { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string TemplateCategoryName { get; set; } = string.Empty;
    public string TemplateBehavior { get; set; } = "level_based";
    public bool RequiresCategoryChange { get; set; }
    public string FallbackReason { get; set; } = string.Empty;
}

/// <summary>
/// Resolves an exact-year Autodesk Family template so learners do not browse
/// the Revit installation manually. It never searches another Revit year and
/// uses the exact-year Metric Generic Model template only when the specialized
/// template is missing. The Family category is then changed before geometry.
/// </summary>
internal static class FamilyTemplateResolver
{
    private sealed class TemplateDefinition
    {
        public string Key { get; }
        public string Name { get; }
        public string[] FileNames { get; }
        public TemplateDefinition(string key, string name, params string[] fileNames)
        {
            Key = key; Name = name; FileNames = fileNames;
        }
    }

    private static readonly IReadOnlyDictionary<string, TemplateDefinition> Definitions =
        new Dictionary<string, TemplateDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["mechanical_equipment"] = new("mechanical_equipment", "Mechanical Equipment", "Metric Mechanical Equipment.rft", "Mechanical Equipment.rft"),
            ["air_terminal"] = new("air_terminal", "Air Terminal", "Metric Air Terminal.rft", "Air Terminal.rft"),
            ["duct_accessory"] = new("duct_accessory", "Duct Accessory", "Metric Duct Accessory.rft", "Duct Accessory.rft"),
            ["pipe_accessory"] = new("pipe_accessory", "Pipe Accessory", "Metric Pipe Accessory.rft", "Pipe Accessory.rft"),
            ["plumbing_fixture"] = new("plumbing_fixture", "Plumbing Fixture", "Metric Plumbing Fixture.rft", "Plumbing Fixture.rft"),
            ["sprinkler"] = new("sprinkler", "Sprinkler", "Metric Sprinkler.rft", "Sprinkler.rft"),
            ["electrical_equipment"] = new("electrical_equipment", "Electrical Equipment", "Metric Electrical Equipment.rft", "Electrical Equipment.rft"),
            ["lighting_fixture"] = new("lighting_fixture", "Lighting Fixture", "Metric Lighting Fixture.rft", "Lighting Fixture.rft"),
            ["cable_tray_fitting"] = new("cable_tray_fitting", "Cable Tray Fitting", "Metric Cable Tray Fitting.rft", "Cable Tray Fitting.rft"),
            ["duct_fitting"] = new("duct_fitting", "Duct Fitting", "Metric Duct Elbow.rft", "Duct Elbow.rft"),
            ["pipe_fitting"] = new("pipe_fitting", "Pipe Fitting", "Metric Pipe Fitting.rft", "Pipe Fitting.rft"),
            ["electrical_fixture"] = new("electrical_fixture", "Electrical Fixtures", "Metric Electrical Fixture.rft", "Electrical Fixture.rft"),
            ["fire_alarm_device"] = new("fire_alarm_device", "Fire Alarm Devices", "Metric Fire Alarm Device.rft", "Fire Alarm Device.rft"),
            ["data_device"] = new("data_device", "Data Devices", "Metric Data Device.rft", "Data Device.rft"),
            ["communication_device"] = new("communication_device", "Communication Devices", "Metric Communication Device.rft", "Communication Device.rft"),
            ["security_device"] = new("security_device", "Security Devices", "Metric Security Device.rft", "Security Device.rft"),
            ["nurse_call_device"] = new("nurse_call_device", "Nurse Call Devices", "Metric Nurse Call Device.rft", "Nurse Call Device.rft"),
            ["telephone_device"] = new("telephone_device", "Telephone Devices", "Metric Telephone Device.rft", "Telephone Device.rft"),
            ["conduit_fitting"] = new("conduit_fitting", "Conduit Fitting", "Metric Conduit Fitting.rft", "Conduit Fitting.rft"),
            ["detail_item"] = new("detail_item", "Detail Items", "Metric Detail Item.rft", "Detail Item.rft"),
            ["profile"] = new("profile", "Profiles", "Metric Profile.rft", "Profile.rft"),
            ["annotation"] = new("annotation", "Generic Annotations", "Metric Generic Annotation.rft", "Generic Annotation.rft"),
            ["tag"] = new("tag", "Tags", "Metric Generic Tag.rft", "Generic Tag.rft"),
            ["generic_model"] = new("generic_model", "Generic Model", "Metric Generic Model.rft", "Generic Model.rft")
        };

    private static readonly IReadOnlyDictionary<string, string> BuiltInCategoryNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mechanical_equipment"] = "OST_MechanicalEquipment", ["air_terminal"] = "OST_DuctTerminal",
            ["duct_accessory"] = "OST_DuctAccessory", ["duct_fitting"] = "OST_DuctFitting",
            ["pipe_accessory"] = "OST_PipeAccessory", ["pipe_fitting"] = "OST_PipeFitting",
            ["plumbing_fixture"] = "OST_PlumbingFixtures", ["sprinkler"] = "OST_Sprinklers",
            ["electrical_equipment"] = "OST_ElectricalEquipment", ["electrical_fixture"] = "OST_ElectricalFixtures",
            ["lighting_fixture"] = "OST_LightingFixtures", ["fire_alarm_device"] = "OST_FireAlarmDevices",
            ["data_device"] = "OST_DataDevices", ["communication_device"] = "OST_CommunicationDevices",
            ["security_device"] = "OST_SecurityDevices", ["nurse_call_device"] = "OST_NurseCallDevices",
            ["telephone_device"] = "OST_TelephoneDevices", ["conduit_fitting"] = "OST_ConduitFitting",
            ["cable_tray_fitting"] = "OST_CableTrayFitting", ["generic_model"] = "OST_GenericModel",
            ["detail_item"] = "OST_DetailComponents", ["profile"] = "OST_ProfileFamilies", ["annotation"] = "OST_GenericAnnotation"
        };

    private static readonly HashSet<string> NoGenericRecategorization = new(StringComparer.OrdinalIgnoreCase)
    {
        "duct_fitting", "pipe_fitting", "conduit_fitting", "cable_tray_fitting",
        "lighting_fixture", "detail_item", "profile", "annotation", "tag"
    };

    private static readonly IReadOnlyDictionary<string, string> CategoryByFamilyKind =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["axial_fan"] = "mechanical_equipment", ["inline_fan"] = "mechanical_equipment",
            ["centrifugal_fan"] = "mechanical_equipment", ["fcu"] = "mechanical_equipment",
            ["ahu"] = "mechanical_equipment", ["pump"] = "mechanical_equipment",
            ["air_terminal"] = "air_terminal", ["duct_accessory"] = "duct_accessory",
            ["damper"] = "duct_accessory", ["pipe_accessory"] = "pipe_accessory",
            ["plumbing_fixture"] = "plumbing_fixture", ["sprinkler"] = "sprinkler",
            ["panel"] = "electrical_equipment", ["conduit_junction_box"] = "electrical_equipment",
            ["lighting_fixture"] = "lighting_fixture", ["cable_tray_fitting"] = "cable_tray_fitting",
            ["generic_model"] = "generic_model"
        };

    public static FamilyTemplateSelection Resolve(Application application, string familyKind, string? explicitPath = null, string? approvedDemoDirectory = null, bool explicitMustBeInsideDemo = false)
    {
        if (!CategoryByFamilyKind.TryGetValue(familyKind, out var categoryKey) || !Definitions.TryGetValue(categoryKey, out var definition))
            throw new CommandResultException(ErrorCodes.TemplateInvalid, "No automatic Family template mapping exists for family_kind: " + familyKind + ".");

        var version = application.VersionNumber;
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var path = RequireLocalTemplate(explicitPath!);
            if (explicitMustBeInsideDemo)
            {
                if (string.IsNullOrWhiteSpace(approvedDemoDirectory) || !IsInside(approvedDemoDirectory!, path))
                    throw new CommandResultException(ErrorCodes.PathBlocked, "An explicit template_path must remain inside approved_demo_directory. Omit template_path to let MCP use the trusted Autodesk template automatically.");
            }
            return Selection(path, definition, version, "explicit_override", "caller_provided_unverified", false, string.Empty);
        }

        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var root = Path.Combine(common, "Autodesk", "RVT " + version, "Family Templates");
        if (!Directory.Exists(root))
            throw new CommandResultException(ErrorCodes.TemplateInvalid, "Revit " + version + " Family Templates are not installed. Repair/install the matching Revit content pack; the learner should not browse for a template manually.");

        var files = Directory.GetFiles(root, "*.rft", SearchOption.AllDirectories)
            .Where(path => path.IndexOf(Path.DirectorySeparatorChar + "Annotations" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0)
            .ToArray();
        foreach (var candidateName in definition.FileNames)
        {
            var match = files.Where(path => string.Equals(Path.GetFileName(path), candidateName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path.IndexOf(Path.DirectorySeparatorChar + "English" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0 ? 1 : 0)
                .ThenBy(path => path.IndexOf("Imperial", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (match != null) return Selection(Path.GetFullPath(match), definition, version, "autodesk_exact_year_auto_detected", definition.Name, false, string.Empty);
        }

        var generic = Definitions["generic_model"];
        if (!string.Equals(definition.Key, generic.Key, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var candidateName in generic.FileNames.Where(name => name.StartsWith("Metric ", StringComparison.OrdinalIgnoreCase)))
            {
                var match = files.Where(path => string.Equals(Path.GetFileName(path), candidateName, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(path => path.IndexOf(Path.DirectorySeparatorChar + "English" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0 ? 1 : 0)
                    .ThenBy(path => path.IndexOf("Imperial", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0)
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (match != null)
                    return Selection(Path.GetFullPath(match), definition, version,
                        "autodesk_exact_year_generic_metric_fallback", generic.Name, true,
                        definition.Name + " template is missing; MCP will change Family Category from Generic Models to " + definition.Name + " before geometry.");
            }
        }

        throw new CommandResultException(ErrorCodes.TemplateInvalid,
            "No " + definition.Name + " template was found for Revit " + version + ". Expected " +
            string.Join(" or ", definition.FileNames) + ", or an exact-year Metric Generic Model fallback. Install/repair that Revit version's Family Templates content; MCP will not use another Revit year.");
    }

    /// <summary>Resolve by placement behavior before category. Hosted and
    /// category-specialized families never fall back to an unrelated plain
    /// Generic Model template.</summary>
    public static FamilyTemplateSelection ResolveBlueprint(Application application, string categoryKey, string templateBehavior, string? explicitPath = null, string? approvedDemoDirectory = null, bool explicitMustBeInsideDemo = false, string? partType = null)
    {
        if (!Definitions.TryGetValue(categoryKey, out var definition))
            throw new CommandResultException(ErrorCodes.TemplateInvalid, "No Family template mapping exists for blueprint category: " + categoryKey + ".");
        if (templateBehavior is "adaptive" or "tag")
            throw new CommandResultException(ErrorCodes.Unsupported, "Blueprint template behavior " + templateBehavior + " requires a separate compiler or controlled Family Editor workflow.");
        var version = application.VersionNumber;
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var explicitTemplate = RequireLocalTemplate(explicitPath!);
            if (explicitMustBeInsideDemo && (string.IsNullOrWhiteSpace(approvedDemoDirectory) || !IsInside(approvedDemoDirectory!, explicitTemplate)))
                throw new CommandResultException(ErrorCodes.PathBlocked, "An explicit template_path must remain inside approved_demo_directory.");
            return Selection(explicitTemplate, definition, version, "explicit_override", "caller_provided_unverified", false, string.Empty, templateBehavior);
        }

        var files = ExactYearTemplateFiles(version);
        var specializedNames = SpecializedNames(definition, templateBehavior, partType).ToArray();
        foreach (var candidateName in specializedNames)
        {
            var match = Preferred(files, candidateName);
            if (match != null) return Selection(match, definition, version, "autodesk_exact_year_behavior_and_category", definition.Name, false, string.Empty, templateBehavior);
        }

        if (NoGenericRecategorization.Contains(categoryKey))
        {
            var primitive = string.IsNullOrWhiteSpace(partType) ? "unspecified primitive" : "primitive " + partType;
            var checkedNames = specializedNames.Length == 0 ? "no native filename mapping" : string.Join(", ", specializedNames);
            var sourceHint = categoryKey is "pipe_fitting" or "duct_fitting"
                ? "Use a verified native source RFA for this primitive through the Family-source workflow; do not substitute Generic Model or another primitive."
                : "Install/repair the matching Revit Family Templates content.";
            throw new CommandResultException(ErrorCodes.TemplateInvalid, "No native " + definition.Name + " template for " + primitive + " and behavior " + templateBehavior + " was found in Revit " + version + ". Checked: " + checkedNames + ". Generic Model fallback is blocked. " + sourceHint);
        }
        foreach (var candidateName in GenericBehaviorNames(templateBehavior))
        {
            var match = Preferred(files, candidateName);
            if (match != null)
                return Selection(match, definition, version, "autodesk_exact_year_behavior_generic_fallback", "Generic Model", !string.Equals(categoryKey, "generic_model", StringComparison.OrdinalIgnoreCase),
                    definition.Name + " template for behavior " + templateBehavior + " is missing; the matching Autodesk Generic Model behavior template will be recategorized and verified before geometry.", templateBehavior);
        }
        throw new CommandResultException(ErrorCodes.TemplateInvalid, "No exact-year Autodesk template matches behavior " + templateBehavior + " and category " + definition.Name + " for Revit " + version + ".");
    }

    public static BuiltInCategory BlueprintCategory(string categoryKey)
    {
        if (!BuiltInCategoryNames.TryGetValue(categoryKey, out var enumName) || !Enum.TryParse(enumName, out BuiltInCategory category))
            throw new CommandResultException(ErrorCodes.Unsupported, "Blueprint category " + categoryKey + " has no loadable-model category compiler mapping.");
        return category;
    }

    public static string BlueprintCategoryName(string categoryKey) => Definitions.TryGetValue(categoryKey, out var definition) ? definition.Name : categoryKey;

    public static bool EnsureTargetCategory(Document family, BuiltInCategory targetCategory, string targetName, bool allowGenericFallback)
    {
        if (!family.IsFamilyDocument || family.OwnerFamily?.FamilyCategory == null)
            throw new CommandResultException(ErrorCodes.TemplateInvalid, "The selected template did not create a valid Family document.");
        var current = family.OwnerFamily.FamilyCategory;
        if (current.Id.IntVal() == (int)targetCategory) return false;
        if (!allowGenericFallback || current.Id.IntVal() != (int)BuiltInCategory.OST_GenericModel)
            throw new CommandResultException(ErrorCodes.TemplateInvalid, "Template category must be " + targetName + "; only the trusted exact-year Generic Model fallback may be recategorized automatically.");
        Category target;
        try { target = family.Settings.Categories.get_Item(targetCategory); }
        catch (Exception ex) { throw new CommandResultException(ErrorCodes.TemplateInvalid, "Revit cannot resolve target Family Category " + targetName + ": " + ex.Message); }
        using var transaction = new Transaction(family, "Set DSCons Family category");
        transaction.Start();
        family.OwnerFamily.FamilyCategory = target;
        if (transaction.Commit() != TransactionStatus.Committed)
            throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rejected Family Category change to " + targetName + ".");
        if (family.OwnerFamily.FamilyCategory?.Id.IntVal() != (int)targetCategory)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Family Category read-back did not match " + targetName + ".");
        return true;
    }

    private static FamilyTemplateSelection Selection(string path, TemplateDefinition definition, string version, string source, string templateCategoryName, bool requiresCategoryChange, string fallbackReason, string templateBehavior = "level_based") => new()
    {
        Path = path, CategoryKey = definition.Key, CategoryName = definition.Name,
        RevitVersion = version, Source = source, TemplateCategoryName = templateCategoryName,
        RequiresCategoryChange = requiresCategoryChange, FallbackReason = fallbackReason, TemplateBehavior = templateBehavior
    };

    private static string[] ExactYearTemplateFiles(string version)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "RVT " + version, "Family Templates");
        if (!Directory.Exists(root)) throw new CommandResultException(ErrorCodes.TemplateInvalid, "Revit " + version + " Family Templates are not installed.");
        return Directory.GetFiles(root, "*.rft", SearchOption.AllDirectories).ToArray();
    }

    private static string? Preferred(IEnumerable<string> files, string candidateName) => files
        .Where(path => string.Equals(Path.GetFileName(path), candidateName, StringComparison.OrdinalIgnoreCase))
        .OrderBy(path => path.IndexOf(Path.DirectorySeparatorChar + "English" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0 ? 1 : 0)
        .ThenBy(path => path.IndexOf("Imperial", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0)
        .ThenBy(path => path, StringComparer.OrdinalIgnoreCase).Select(Path.GetFullPath).FirstOrDefault();

    private static IEnumerable<string> SpecializedNames(TemplateDefinition definition, string behavior, string? partType)
    {
        if (behavior is "level_based" or "detail_item" or "profile" or "annotation")
        {
            if (string.Equals(definition.Key, "duct_fitting", StringComparison.OrdinalIgnoreCase))
            {
                var ductFile = partType?.ToLowerInvariant() switch
                {
                    "elbow" => "Metric Duct Elbow.rft",
                    "cross" => "Metric Duct Cross.rft",
                    "tee" => "Metric Duct Tee.rft",
                    "transition" => "Metric Duct Transition.rft",
                    _ => string.Empty,
                };
                return string.IsNullOrWhiteSpace(ductFile) ? Array.Empty<string>() : new[] { ductFile };
            }
            if (string.Equals(definition.Key, "pipe_fitting", StringComparison.OrdinalIgnoreCase))
            {
                var pipeFile = partType?.ToLowerInvariant() switch
                {
                    "elbow" => new[] { "Metric Pipe Elbow.rft", "Pipe Elbow.rft" },
                    "cross" => new[] { "Metric Pipe Cross.rft", "Pipe Cross.rft" },
                    "tee" or "lateral_tee" or "wye" => new[] { "Metric Pipe Tee.rft", "Pipe Tee.rft" },
                    "transition" => new[] { "Metric Pipe Transition.rft", "Pipe Transition.rft" },
                    "union" or "pipe_mechanical_coupling" or "pipe_flange" => new[] { "Metric Pipe Union.rft", "Metric Pipe Fitting.rft", "Pipe Union.rft", "Pipe Fitting.rft" },
                    _ => Array.Empty<string>(),
                };
                return pipeFile;
            }
            return definition.FileNames;
        }
        var suffix = behavior switch
        {
            "wall_based" => " wall based.rft", "ceiling_based" => " ceiling based.rft",
            "face_based" => " Hosted.rft", _ => string.Empty
        };
        if (string.IsNullOrEmpty(suffix)) return Array.Empty<string>();
        return new[] { "Metric " + definition.Name.TrimEnd('s') + suffix, definition.Name.TrimEnd('s') + suffix };
    }

    private static IEnumerable<string> GenericBehaviorNames(string behavior) => behavior switch
    {
        "level_based" => new[] { "Metric Generic Model.rft", "Generic Model.rft" },
        "work_plane_based" or "face_based" => new[] { "Metric Generic Model face based.rft", "Generic Model face based.rft" },
        "wall_based" => new[] { "Metric Generic Model wall based.rft", "Generic Model wall based.rft" },
        "ceiling_based" => new[] { "Metric Generic Model ceiling based.rft", "Generic Model ceiling based.rft" },
        "floor_based" => new[] { "Metric Generic Model floor based.rft", "Generic Model floor based.rft" },
        "roof_based" => new[] { "Metric Generic Model roof based.rft", "Generic Model roof based.rft" },
        "line_based" => new[] { "Metric Generic Model line based.rft", "Generic Model line based.rft" },
        "two_level_based" => new[] { "Metric Generic Model two level based.rft", "Generic Model two level based.rft" },
        _ => Array.Empty<string>()
    };

    private static string RequireLocalTemplate(string raw)
    {
        if (!Path.IsPathRooted(raw) || raw.StartsWith("\\\\", StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.TemplateInvalid, "template_path must be a local absolute .rft path.");
        var path = Path.GetFullPath(raw);
        if (!string.Equals(Path.GetExtension(path), ".rft", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new CommandResultException(ErrorCodes.TemplateInvalid, "The requested Family template is missing or is not an .rft file.");
        return path;
    }

    private static bool IsInside(string root, string path)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
