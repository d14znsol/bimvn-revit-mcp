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
            ["generic_model"] = new("generic_model", "Generic Model", "Metric Generic Model.rft", "Generic Model.rft")
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

    private static FamilyTemplateSelection Selection(string path, TemplateDefinition definition, string version, string source, string templateCategoryName, bool requiresCategoryChange, string fallbackReason) => new()
    {
        Path = path, CategoryKey = definition.Key, CategoryName = definition.Name,
        RevitVersion = version, Source = source, TemplateCategoryName = templateCategoryName,
        RequiresCategoryChange = requiresCategoryChange, FallbackReason = fallbackReason
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
