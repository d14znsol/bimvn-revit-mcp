using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DSCons.RevitMcp.Contracts;
using DSCons.RevitMcp.Core;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

/// <summary>
/// The deliberately small contract between catalog evidence and Family API.
/// It is an allow-list, not an arbitrary geometry or connector API.
/// </summary>
internal sealed class FamilyAdapterDefinition
{
    public string Kind { get; }
    public BuiltInCategory Category { get; }
    public string CategoryName { get; }
    public string[] RequiredFields { get; }
    public string[] ConnectorRoles { get; }
    public bool GeometryImplemented { get; }
    public string CertificationState { get; }
    public FamilyAdapterDefinition(string kind, BuiltInCategory category, string categoryName, string[] requiredFields, string[] connectorRoles, bool geometryImplemented, string certificationState)
    {
        Kind = kind; Category = category; CategoryName = categoryName; RequiredFields = requiredFields; ConnectorRoles = connectorRoles; GeometryImplemented = geometryImplemented; CertificationState = certificationState;
    }
}

internal static class FamilyAdapterRegistry
{
    private static readonly IReadOnlyDictionary<string, FamilyAdapterDefinition> Adapters =
        new Dictionary<string, FamilyAdapterDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["axial_fan"] = new("axial_fan", BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment",
                new[] { "diameter_mm", "length_mm", "type_code", "connector_mode" }, new[] { "air_inlet", "air_outlet" }, true, "core_geometry_runtime_2023_none_only"),
            ["fcu"] = new("fcu", BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment",
                new[] { "width_mm", "height_mm", "depth_mm", "type_code", "supply_air", "return_air", "chws", "chwr", "condensate_drain" }, new[] { "supply_air", "return_air", "chws", "chwr", "condensate_drain", "power" }, false, "requires_catalog_fixture_and_runtime"),
            ["inline_fan"] = new("inline_fan", BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment",
                new[] { "diameter_mm", "length_mm", "type_code", "air_system_classification" }, new[] { "air_inlet", "air_outlet" }, false, "requires_catalog_fixture_and_runtime"),
            ["centrifugal_fan"] = new("centrifugal_fan", BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment",
                new[] { "width_mm", "height_mm", "depth_mm", "type_code", "air_system_classification" }, new[] { "air_inlet", "air_outlet" }, false, "requires_catalog_fixture_and_runtime"),
            ["ahu"] = new("ahu", BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment",
                new[] { "width_mm", "height_mm", "depth_mm", "type_code", "supply_air", "return_air" }, new[] { "supply_air", "return_air", "chws", "chwr", "power" }, false, "requires_catalog_fixture_and_runtime"),
            ["pump"] = new("pump", BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment",
                new[] { "A", "A1", "A2", "B", "C", "D1", "D2", "DN1", "DN2", "K1", "K2", "P1", "P2", "H", "H1", "H2", "H3", "M", "N1", "N2", "R", "S1", "S2", "T", "type_code" }, new[] { "suction", "discharge", "power" }, true, "parametric_envelope_pilot_runtime_pending_lod300_connector_probe"),
            ["air_terminal"] = new("air_terminal", BuiltInCategory.OST_DuctTerminal, "Air Terminals",
                new[] { "width_mm", "height_mm", "depth_mm", "type_code", "air_system_classification" }, new[] { "air" }, false, "requires_category_template_catalog_fixture_and_runtime"),
            ["duct_accessory"] = new("duct_accessory", BuiltInCategory.OST_DuctAccessory, "Duct Accessories",
                new[] { "width_mm", "height_mm", "depth_mm", "type_code", "air_system_classification" }, new[] { "air_inlet", "air_outlet" }, false, "requires_category_template_catalog_fixture_and_runtime"),
            ["pipe_accessory"] = new("pipe_accessory", BuiltInCategory.OST_PipeAccessory, "Pipe Accessories",
                new[] { "width_mm", "height_mm", "depth_mm", "type_code", "pipe_system_classification" }, new[] { "pipe_inlet", "pipe_outlet" }, false, "requires_category_template_catalog_fixture_and_runtime"),
            ["plumbing_fixture"] = new("plumbing_fixture", BuiltInCategory.OST_PlumbingFixtures, "Plumbing Fixtures",
                new[] { "width_mm", "height_mm", "depth_mm", "type_code" }, new[] { "dcw", "dhw", "sanitary", "vent" }, false, "requires_catalog_fixture_and_runtime"),
            ["sprinkler"] = new("sprinkler", BuiltInCategory.OST_Sprinklers, "Sprinklers",
                new[] { "diameter_mm", "height_mm", "type_code", "fire_protection_classification" }, new[] { "fire_protection" }, false, "requires_catalog_fixture_and_runtime"),
            ["panel"] = new("panel", BuiltInCategory.OST_ElectricalEquipment, "Electrical Equipment",
                new[] { "width_mm", "height_mm", "depth_mm", "type_code", "voltage", "phase", "poles", "rating_ampere" }, new[] { "power" }, true, "geometry_runtime_pending_connector_probe"),
            ["lighting_fixture"] = new("lighting_fixture", BuiltInCategory.OST_LightingFixtures, "Lighting Fixtures",
                new[] { "width_mm", "height_mm", "depth_mm", "type_code", "voltage", "load" }, new[] { "power" }, false, "requires_catalog_fixture_and_runtime"),
            ["conduit_junction_box"] = new("conduit_junction_box", BuiltInCategory.OST_ElectricalEquipment, "Electrical Equipment",
                new[] { "width_mm", "height_mm", "depth_mm", "type_code", "conduit_profile" }, new[] { "conduit" }, false, "requires_catalog_fixture_and_runtime"),
            ["cable_tray_fitting"] = new("cable_tray_fitting", BuiltInCategory.OST_CableTrayFitting, "Cable Tray Fittings",
                new[] { "width_mm", "height_mm", "depth_mm", "type_code", "tray_profile" }, new[] { "cable_tray" }, false, "requires_catalog_fixture_and_runtime"),
        };

    public static FamilyAdapterDefinition Get(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind) || !Adapters.TryGetValue(kind, out var adapter))
            throw new CommandResultException(ErrorCodes.InvalidParam, "family_kind is not in the DSCons Family adapter allow-list.");
        return adapter;
    }

    public static JArray Catalog() => new(Adapters.Values.OrderBy(x => x.Kind).Select(x => new JObject
    {
        ["family_kind"] = x.Kind, ["category"] = x.CategoryName, ["required_fields"] = new JArray(x.RequiredFields),
        ["connector_roles"] = new JArray(x.ConnectorRoles), ["geometry_implemented"] = x.GeometryImplemented,
        ["certification_state"] = x.CertificationState
    }));
}

internal sealed class FamilyQualitySpec
{
    public string SpecId { get; set; } = string.Empty;
    public string FamilyKind { get; set; } = string.Empty;
    public FamilyAdapterDefinition Adapter { get; set; } = null!;
    public JObject Fields { get; set; } = new();
    public string SourceSha256 { get; set; } = string.Empty;
    public string Lod { get; set; } = "LOD_300";
    public string DetailProfile { get; set; } = "dscons_mep_300_v1";

    public static FamilyQualitySpec FromRecord(JObject record)
    {
        var kind = record.Value<string>("family_kind") ?? string.Empty;
        return new FamilyQualitySpec
        {
            SpecId = record.Value<string>("spec_id") ?? string.Empty,
            FamilyKind = kind,
            Adapter = FamilyAdapterRegistry.Get(kind),
            Fields = record["confirmed_fields"] as JObject ?? new JObject(),
            SourceSha256 = record.Value<string>("source_sha256") ?? string.Empty,
            Lod = record.Value<string>("lod") ?? "LOD_300",
            DetailProfile = record.Value<string>("detail_profile") ?? "dscons_mep_300_v1"
        };
    }

    public void RequireBuildable()
    {
        if (!string.Equals(Lod, "LOD_300", StringComparison.Ordinal) || !string.Equals(DetailProfile, "dscons_mep_300_v1", StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.InvalidParam, "Only LOD_300 / dscons_mep_300_v1 is accepted.");
        var missing = Adapter.RequiredFields.Where(key => !HasConfirmedValue(key)).ToArray();
        if (missing.Length != 0)
            throw new CommandResultException(ErrorCodes.InvalidParam, "Catalog fields are missing or uncertain: " + string.Join(", ", missing) + ". Confirm them before building a Family.");
        FamilyQualityValidator.ValidateConnectorFields(this);
    }

    public bool HasConfirmedValue(string key)
    {
        var token = Fields[key];
        if (token == null || token.Type == JTokenType.Null || (token.Type == JTokenType.String && string.IsNullOrWhiteSpace(token.Value<string>()))) return false;
        if (token is JObject state && !string.Equals(state.Value<string>("status"), "confirmed", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    public JToken? Value(string key) => Fields[key] is JObject state && state["value"] != null ? state["value"] : Fields[key];
}

internal static class FamilyQualityValidator
{
    private static readonly HashSet<string> Air = new(StringComparer.OrdinalIgnoreCase) { "SupplyAir", "ReturnAir", "ExhaustAir", "OtherAir", "Global" };
    private static readonly HashSet<string> Pipe = new(StringComparer.OrdinalIgnoreCase) { "SupplyHydronic", "ReturnHydronic", "CondensateDrain", "Sanitary", "Vent", "DomesticColdWater", "DomesticHotWater", "FireProtectWet", "FireProtectDry", "FireProtectPreaction", "OtherPipe" };

    public static void ValidateTemplate(Document family, FamilyAdapterDefinition adapter)
    {
        if (!family.IsFamilyDocument || family.OwnerFamily?.FamilyCategory?.Id.IntVal() != (int)adapter.Category)
            throw new CommandResultException(ErrorCodes.TemplateInvalid, "Template category must be " + adapter.CategoryName + " for adapter " + adapter.Kind + ".");
    }

    public static void ValidateConnectorFields(FamilyQualitySpec spec)
    {
        if (spec.FamilyKind is "axial_fan" or "inline_fan" or "centrifugal_fan")
        {
            var mode = spec.Value("connector_mode")?.Value<string>() ?? "round_hvac";
            if (mode != "none" && mode != "round_hvac") throw new CommandResultException(ErrorCodes.InvalidParam, "connector_mode must be none or round_hvac.");
            if (mode == "round_hvac")
            {
                var classification = spec.Value("air_system_classification")?.Value<string>();
                if (string.IsNullOrWhiteSpace(classification) || !Air.Contains(classification!))
                    throw new CommandResultException(ErrorCodes.InvalidParam, "round_hvac needs confirmed air_system_classification (SupplyAir, ReturnAir, ExhaustAir, OtherAir or Global).");
            }
        }
        if (spec.FamilyKind is "pump" or "plumbing_fixture" or "sprinkler")
        {
            foreach (var key in new[] { "pipe_system_classification", "fire_protection_classification" })
            {
                var value = spec.Value(key)?.Value<string>();
                if (!string.IsNullOrEmpty(value) && !Pipe.Contains(value!)) throw new CommandResultException(ErrorCodes.InvalidParam, "Unsupported confirmed pipe classification: " + value);
            }
        }
    }

    public static JObject QualityContract(FamilyQualitySpec spec) => new()
    {
        ["lod"] = spec.Lod, ["detail_profile"] = spec.DetailProfile, ["origin"] = "geometric_center", ["axis"] = "adapter_defined",
        ["geometry_rules"] = new JArray("reference_framework_before_geometry", "role_subcategory_per_form", "envelope_and_each_role_checked_pm_1mm"),
        ["display_rules"] = new JObject { ["coarse"] = "envelope_and_light_symbol", ["medium"] = "casing_ports_major_blocks", ["fine"] = "visible_major_parts_only", ["maintenance_clearance"] = "separate_non_quantity_visibility_default_off" },
        ["connector_rules"] = new JArray("dedicated_port_face", "outward_normal", "size_associated_to_family_parameter", "one_primary_per_discipline", "linked_inline_pair"),
        ["runtime_gate"] = "connector_probe_and_copied_model_record_required"
    };
}

/// <summary>Stable DSCons schedule-data identity. Built-in Manufacturer,
/// Model, Description, Type Mark and Mark are intentionally not duplicated.
/// The temporary SharedParametersFilename scope prevents changing a learner's
/// global Revit setting when a future adapter materializes these definitions.</summary>
internal static class DsconsParameterRegistry
{
    internal sealed class Definition { public string InternalKey { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; public Guid Guid { get; set; } }
    public static readonly Definition[] Definitions =
    {
        new() { InternalKey = "DSCons_Family_Kind", DisplayName = "DSCons - Loại Family", Guid = new Guid("2a48a2a5-1d5b-4b70-ae0e-2b11d3314201") },
        new() { InternalKey = "DSCons_LOD_Profile", DisplayName = "DSCons - Hồ sơ LOD", Guid = new Guid("d337b004-a76f-4ad9-997a-621ae73fd002") },
        new() { InternalKey = "DSCons_Connector_Roles", DisplayName = "DSCons - Vai trò connector", Guid = new Guid("4c3e5824-78c7-49dd-9256-a925057c3003") },
        new() { InternalKey = "DSCons_Source_SHA256", DisplayName = "DSCons - Mã nguồn SHA256", Guid = new Guid("be8ea78a-8e53-4796-83cf-0a3cfbbbe004") },
    };

    public static void WithTemporarySharedParameterFile(Autodesk.Revit.ApplicationServices.Application application, string temporaryFile, Action action)
    {
        var previous = application.SharedParametersFilename;
        try { application.SharedParametersFilename = temporaryFile; action(); }
        finally { application.SharedParametersFilename = previous; }
    }
}

internal static class FamilyQualityPipeline
{
    public static JObject Preview(UIApplication uiapp, FamilyTemplateSelection selection, FamilyQualitySpec spec, FanSpec? axialSpec, string familyName, string typeName)
    {
        Document? family = null;
        try
        {
            family = uiapp.Application.NewFamilyDocument(selection.Path);
            var categoryChanged = FamilyTemplateResolver.EnsureTargetCategory(family, spec.Adapter.Category, spec.Adapter.CategoryName, selection.RequiresCategoryChange);
            FamilyQualityValidator.ValidateTemplate(family, spec.Adapter);
            if (spec.Adapter.GeometryImplemented && axialSpec != null)
            {
                family.Close(false); family = null;
                var built = FamilyBuilder.Preview(uiapp.Application, axialSpec);
                built["quality_contract"] = FamilyQualityValidator.QualityContract(spec);
                built["adapter"] = spec.Adapter.Kind;
                return built;
            }
            if (spec.Adapter.GeometryImplemented && spec.FamilyKind is "pump" or "panel")
            {
                family.Close(false); family = null;
                var built = FamilyCatalogGeometryBuilder.Preview(uiapp.Application, selection.Path, selection.RequiresCategoryChange, spec, familyName, typeName);
                built["quality_contract"] = FamilyQualityValidator.QualityContract(spec);
                built["adapter"] = spec.Adapter.Kind;
                return built;
            }
            using var group = new TransactionGroup(family, "Validate DSCons Family quality contract"); group.Start();
            using (var transaction = new Transaction(family, "Validate adapter template")) { transaction.Start(); transaction.Commit(); }
            group.RollBack();
            return new JObject { ["status"] = "adapter_registered_geometry_not_enabled", ["model_changed"] = false, ["adapter"] = spec.Adapter.Kind, ["category_assignment"] = new JObject { ["target"] = spec.Adapter.CategoryName, ["changed_from_generic"] = categoryChanged, ["verified"] = true }, ["quality_contract"] = FamilyQualityValidator.QualityContract(spec), ["certification_state"] = spec.Adapter.CertificationState };
        }
        finally { if (family != null) try { family.Close(false); } catch { } }
    }

    public static FanSpec AxialSpec(FamilyQualitySpec quality, FamilyTemplateSelection selection, string demoDirectory, string familyName, string typeName)
    {
        var diameter = quality.Value("diameter_mm")?.Value<double?>() ?? 0;
        var length = quality.Value("length_mm")?.Value<double?>() ?? 0;
        var typeCode = quality.Value("type_code")?.Value<string>() ?? string.Empty;
        var connectorMode = quality.Value("connector_mode")?.Value<string>() ?? "none";
        if (diameter <= 0 || length <= 0 || string.IsNullOrWhiteSpace(typeCode)) throw new CommandResultException(ErrorCodes.InvalidParam, "Axial fan confirmed dimensions/type_code are invalid.");
        var output = Path.Combine(demoDirectory, familyName + ".rfa");
        if (File.Exists(output)) throw new CommandResultException(ErrorCodes.FileConflict, "Output Family exists; overwrite is blocked.");
        return new FanSpec { TemplatePath = selection.Path, TemplateCategory = selection.CategoryName, TemplateOriginalCategory = selection.TemplateCategoryName, TemplateSource = selection.Source, TemplateFallbackReason = selection.FallbackReason, RequiresCategoryChange = selection.RequiresCategoryChange, RevitVersion = selection.RevitVersion, DemoDirectory = demoDirectory, OutputPath = output, FamilyName = familyName, TypeName = typeName, TypeCode = typeCode, DiameterFt = diameter / 304.8, LengthFt = length / 304.8, ConnectorMode = connectorMode, ConnectorSystemClassification = quality.Value("air_system_classification")?.Value<string>() ?? string.Empty };
    }
}

internal static class FamilyConnectorReadBack
{
    public static JObject Describe(ConnectorElement connector)
    {
        object? Get(string name) => connector.GetType().GetProperty(name)?.GetValue(connector);
        JObject Point(object? value, bool millimetres = true)
        {
            var xyz = value as XYZ;
            return xyz == null ? new JObject() : millimetres ? new JObject { ["x_mm"] = Math.Round(xyz.X * 304.8, 3), ["y_mm"] = Math.Round(xyz.Y * 304.8, 3), ["z_mm"] = Math.Round(xyz.Z * 304.8, 3) } : new JObject { ["x"] = Math.Round(xyz.X, 6), ["y"] = Math.Round(xyz.Y, 6), ["z"] = Math.Round(xyz.Z, 6) };
        }
        var linked = connector.GetType().GetMethod("GetLinkedConnectorElement")?.Invoke(connector, null) as ConnectorElement;
        var radius = Get("Radius") as double?;
        var width = Get("Width") as double?;
        var height = Get("Height") as double?;
        var role = connector.get_Parameter(BuiltInParameter.RBS_CONNECTOR_DESCRIPTION)?.AsString();
        return new JObject
        {
            ["id"] = connector.Id.Val(), ["role"] = role ?? connector.Name, ["domain"] = Get("Domain")?.ToString(), ["shape"] = Get("Shape")?.ToString(),
            ["origin_mm"] = Point(Get("Origin")), ["normal"] = Point(Get("Direction"), false), ["system_classification"] = Get("SystemClassification")?.ToString(),
            ["is_primary"] = Get("IsPrimary") as bool? ?? false, ["linked_connector_id"] = linked?.Id.Val(),
            ["size_mm"] = new JObject { ["radius"] = radius.HasValue && radius.Value > 0 ? Math.Round(radius.Value * 304.8, 3) : null, ["width"] = width.HasValue && width.Value > 0 ? Math.Round(width.Value * 304.8, 3) : null, ["height"] = height.HasValue && height.Value > 0 ? Math.Round(height.Value * 304.8, 3) : null },
            ["parameter_association"] = connector.get_Parameter(BuiltInParameter.CONNECTOR_RADIUS) != null ? "family_diameter_or_connector_radius" : null,
            ["runtime_probe"] = "not_certified_until_1m_stub_and_network_record_pass"
        };
    }
}
