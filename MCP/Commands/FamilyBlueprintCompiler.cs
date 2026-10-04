using System.Security.Cryptography;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Lighting;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Visual;
using DSCons.RevitMcp.Contracts;
using DSCons.RevitMcp.Core;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

/// <summary>
/// Bounded declarative Family compiler. It accepts only Blueprint v3 data
/// validated by the Node boundary; it never evaluates C# or arbitrary Revit
/// API calls. The compiler accepts orthogonal or explicitly directed solid
/// extrusions plus bounded literal revolution/sweep/blend/swept-blend forms
/// and native MEP connectors.
/// </summary>
internal sealed class FamilyBlueprintSpec
{
    public string SpecId { get; private set; } = string.Empty;
    public string FamilyKind { get; private set; } = string.Empty;
    public string SourceSha256 { get; private set; } = string.Empty;
    public string Lod { get; private set; } = string.Empty;
    public string DetailProfile { get; private set; } = string.Empty;
    public JObject Blueprint { get; private set; } = new();
    public JObject ConfirmedFields { get; private set; } = new();
    public JObject TypeConfirmedFields { get; private set; } = new();
    public JObject FieldProvenance { get; private set; } = new();
    public JObject CatalogRevisionProvenance { get; private set; } = new();
    public JObject Assessment { get; private set; } = new();
    public string CategoryKey => Blueprint["family"]?.Value<string>("category") ?? string.Empty;
    public string TemplateBehavior => Blueprint["family"]?.Value<string>("template_behavior") ?? string.Empty;
    public string PartType => Blueprint["family"]?.Value<string>("part_type") ?? string.Empty;

    public static FamilyBlueprintSpec FromRecord(JObject record)
    {
        if (!string.Equals(record.Value<string>("record_kind"), "family_spec_v3", StringComparison.Ordinal) || record["blueprint"] is not JObject blueprint)
            throw new CommandResultException(ErrorCodes.EvidenceInvalid, "FamilySpec v3 with a declarative blueprint is required.");
        var spec = new FamilyBlueprintSpec
        {
            SpecId = record.Value<string>("spec_id") ?? string.Empty,
            FamilyKind = record.Value<string>("family_kind") ?? string.Empty,
            SourceSha256 = record.Value<string>("source_sha256") ?? string.Empty,
            Lod = record.Value<string>("lod") ?? string.Empty,
            DetailProfile = record.Value<string>("detail_profile") ?? string.Empty,
            Blueprint = (JObject)blueprint.DeepClone(),
            ConfirmedFields = record["confirmed_fields"] as JObject ?? new JObject(),
            TypeConfirmedFields = record["type_confirmed_fields"] as JObject ?? new JObject(),
            FieldProvenance = record["field_provenance"] as JObject ?? new JObject(),
            CatalogRevisionProvenance = record["catalog_revision_provenance"] as JObject ?? new JObject(),
            Assessment = record["blueprint_assessment"] as JObject ?? new JObject()
        };
        spec.RequireBuildable();
        return spec;
    }

    public void RequireBuildable()
    {
        if (!string.Equals(Blueprint.Value<string>("schema_version"), "3.0", StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Only Family Blueprint schema 3.0 is accepted.");
        if (Lod is not ("LOD_300" or "LOD_350") || DetailProfile is not ("dscons_mep_300_v1" or "dscons_mep_350_v1"))
            throw new CommandResultException(ErrorCodes.InvalidParam, "Blueprint v3 accepts only the declared LOD_300/LOD_350 profiles.");
        if (Assessment.Value<bool?>("buildable_by_api") != true)
            throw new CommandResultException(ErrorCodes.Unsupported, "Blueprint needs unsupported compiler or controlled UI capabilities: " + string.Join(", ", Assessment["unsupported_features"]?.Values<string>() ?? Array.Empty<string>()) + ".");
        if (Blueprint["performance_budget"] is not JObject || Blueprint["complexity_assessment"]?["metrics"] is not JObject || Blueprint["resolved_complexity"] is not JObject)
            throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint is missing recomputed performance-budget or nested-complexity evidence.");
        if (Blueprint["source_traceability"] is not JObject)
            throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint is missing recomputed source-field traceability evidence.");
        if (FieldProvenance.Count == 0)
            throw new CommandResultException(ErrorCodes.EvidenceInvalid, "FamilySpec is missing field-level source provenance evidence.");
        var family = Blueprint["family"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "blueprint.family is missing.");
        if (!string.Equals(family.Value<string>("family_key"), FamilyKind, StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint family_key does not match FamilySpec family_kind.");
        if (!string.Equals(family.Value<string>("primary_axis"), "x", StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.Unsupported, "The current Blueprint compiler supports primary_axis=x only.");
#if REVIT2023
        // Revit 2023 exposes RBS_DUCT_FLOW_PARAM with the correct AirFlow data
        // type and accepts Preset as the Flow Configuration, but its Family API
        // still reports CanElementParameterBeAssociated=false. Do not construct
        // a static-looking connector or claim flexable preset airflow support.
        if ((Blueprint["connectors"] as JArray ?? new JArray()).OfType<JObject>().Any(connector =>
                string.Equals(connector.Value<string>("discipline"), "duct", StringComparison.Ordinal)
                && connector["flow_parameter"] != null))
            throw new CommandResultException(ErrorCodes.Unsupported, "Revit 2023 duct_connector_flow_parameter_revit2023_fail_closed: direct connector Flow association is not available through the verified Family API path. The declared Airflow Family Parameter remains in the Blueprint; complete this association only through the controlled Family Editor UI workflow, then reopen and inspect before any load/place claim.");
#endif
        var parts = Blueprint["parts"] as JArray ?? new JArray();
        var coordinationZones = Blueprint["coordination_zones"] as JArray ?? new JArray();
        var isSymbolic2d = TemplateBehavior is "detail_item" or "annotation";
        var isProfile = TemplateBehavior == "profile";
        var modelLineCount = (Blueprint["model_lines"] as JArray)?.Count ?? 0;
        var symbolicLineCount = (Blueprint["symbolic_lines"] as JArray)?.Count ?? 0;
        var lightSource = Blueprint["light_source"] as JObject;
        if (string.Equals(CategoryKey, "lighting_fixture", StringComparison.Ordinal) && lightSource == null)
            throw new CommandResultException(ErrorCodes.InvalidParam, "Lighting Fixture Blueprint requires an explicit light_source contract.");
        if (!string.Equals(CategoryKey, "lighting_fixture", StringComparison.Ordinal) && lightSource != null)
            throw new CommandResultException(ErrorCodes.InvalidParam, "light_source is valid only for Lighting Fixture Blueprint category.");
        if (parts.Count == 0 && !isSymbolic2d && !isProfile && modelLineCount + symbolicLineCount == 0)
            throw new CommandResultException(ErrorCodes.InvalidParam, "Blueprint must contain at least one solid part or supported Model/Symbolic line declaration.");
        if (coordinationZones.Count > 0 && Lod != "LOD_350") throw new CommandResultException(ErrorCodes.InvalidParam, "Coordination zones require LOD_350.");
        if (coordinationZones.Count > 0 && new[] { "detail_item", "annotation", "profile", "tag", "adaptive" }.Contains(TemplateBehavior, StringComparer.Ordinal))
            throw new CommandResultException(ErrorCodes.Unsupported, "Coordination zones require a model Family template.");
        var twoDimensionalCount = (Blueprint["symbolic_lines"] as JArray)?.Count ?? 0;
        twoDimensionalCount += (Blueprint["detail_lines"] as JArray)?.Count ?? 0;
        twoDimensionalCount += (Blueprint["filled_regions"] as JArray)?.Count ?? 0;
        if (isSymbolic2d && (parts.Count != 0 || twoDimensionalCount < 1)) throw new CommandResultException(ErrorCodes.Unsupported, "Detail Item/Annotation Blueprint requires symbolic lines, detail lines or filled regions without model forms.");
        if (isProfile && (parts.Count != 0 || (Blueprint["profile_loops"] as JArray)?.Count < 1)) throw new CommandResultException(ErrorCodes.Unsupported, "Profile Blueprint currently accepts closed profile loops without model forms.");
        foreach (var part in parts.OfType<JObject>())
        {
            var primitive = part.Value<string>("primitive") ?? string.Empty;
            var operation = part.Value<string>("operation") ?? "solid";
            if (primitive is not ("extrusion" or "revolution" or "sweep" or "blend" or "swept_blend") || operation is not ("solid" or "void"))
                throw new CommandResultException(ErrorCodes.Unsupported, "The current Blueprint compiler accepts the declared solid Family primitives only.");
            var shape = part["profile"]?.Value<string>("shape");
            var endShape = part["end_profile"]?.Value<string>("shape");
            if (shape is not ("rectangle" or "circle" or "ring" or "oval")) throw new CommandResultException(ErrorCodes.Unsupported, "Unsupported Blueprint profile: " + shape + ".");
            if (primitive is "blend" or "swept_blend")
            {
                if (shape == "ring" || endShape is not ("rectangle" or "circle" or "oval")) throw new CommandResultException(ErrorCodes.Unsupported, "Blend and swept-blend profiles must each contain one supported loop.");
            }
            if (primitive is not ("extrusion" or "blend"))
            {
                var profiles = new[] { part["profile"] as JObject, part["end_profile"] as JObject }.Where(item => item != null).Cast<JObject>();
                var parameterized = profiles.Any(profile => profile.Properties().Any(property => property.Name.EndsWith("_parameter", StringComparison.Ordinal)));
                if (parameterized)
                {
                    var pathKind = part["path"]?.Value<string>("kind") ?? string.Empty;
                    var boundedParameterizedProfile = primitive switch
                    {
                        "revolution" => shape is "rectangle" or "circle" or "ring" or "oval",
                        "sweep" when pathKind == "arc" => shape is "rectangle" or "circle" or "oval",
                        "sweep" when pathKind is "line" or "polyline" => shape is "rectangle" or "circle" or "ring" or "oval",
                        "sweep" when pathKind == "offset" => shape == "rectangle",
                        "swept_blend" => shape is "rectangle" or "circle" or "oval" && endShape is "rectangle" or "circle" or "oval",
                        _ => false
                    };
                    if (!boundedParameterizedProfile)
                        throw new CommandResultException(ErrorCodes.Unsupported, "Parameterized profile is outside the bounded Revolution/Sweep/Swept Blend compiler contract.");
                }
            }
        }
        foreach (var zone in coordinationZones.OfType<JObject>())
        {
            if (zone.Value<string>("shape") is not ("box" or "cylinder") || zone.Value<string>("axis") is not ("x" or "y" or "z"))
                throw new CommandResultException(ErrorCodes.Unsupported, "Coordination zones accept bounded box/cylinder extrusions on X/Y/Z only.");
            if (zone.Value<string>("role") != "non_physical_coordination_zone" || string.IsNullOrWhiteSpace(zone.Value<string>("subcategory")))
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Coordination-zone semantic role/subcategory is missing.");
        }
    }

    public double ConfirmedNumber(string key)
    {
        var token = ConfirmedFields[key];
        if (token is JObject state) token = state["value"];
        var value = token?.Value<double?>();
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) throw new CommandResultException(ErrorCodes.InvalidParam, "Confirmed source field " + key + " must be numeric.");
        return value.Value;
    }

    public JToken ConfirmedValue(string key)
    {
        var token = ConfirmedFields[key];
        if (token is JObject state) token = state["value"];
        return token?.DeepClone() ?? throw new CommandResultException(ErrorCodes.InvalidParam, "Confirmed source field " + key + " has no value.");
    }
}

internal static class FamilyBlueprintCompiler
{
    private sealed class PartResult
    {
        public string Key { get; set; } = string.Empty;
        public GenericForm Form { get; set; } = null!;
        public string Primitive { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public JObject Contract { get; set; } = new();
        public JObject? VisibilityParameter { get; set; }
    }

    private sealed class CoordinationZoneResult
    {
        public string Key { get; set; } = string.Empty;
        public GenericForm Form { get; set; } = null!;
        public JObject Contract { get; set; } = new();
        public JObject? VisibilityParameter { get; set; }
    }

    private sealed class ArcPathFrame
    {
        public string Plane { get; set; } = string.Empty;
        public double Radius { get; set; }
        public double Angle { get; set; }
        public XYZ Intersection { get; set; } = XYZ.Zero;
        public XYZ Start { get; set; } = XYZ.Zero;
        public XYZ End { get; set; } = XYZ.Zero;
        public XYZ Center { get; set; } = XYZ.Zero;
        public XYZ Midpoint { get; set; } = XYZ.Zero;
        public XYZ StartTangent { get; set; } = XYZ.BasisX;
        public XYZ EndTangent { get; set; } = XYZ.BasisX;
    }

    private sealed class OffsetPathFrame
    {
        public string Plane { get; set; } = string.Empty;
        public double LeadIn { get; set; }
        public double LeadOut { get; set; }
        public double LateralOffset { get; set; }
        public double Angle { get; set; }
        public XYZ LateralAxis { get; set; } = XYZ.BasisY;
        public XYZ[] Points { get; set; } = Array.Empty<XYZ>();
    }

    private sealed class ExtrusionFrameInfo
    {
        public XYZ Axis { get; set; } = XYZ.BasisX;
        public XYZ Width { get; set; } = XYZ.BasisY;
        public XYZ Height { get; set; } = XYZ.BasisZ;
        public string? ProfilePlane { get; set; }
        public string? WidthViewPlane { get; set; }
        public string? HeightViewPlane { get; set; }
    }

    private sealed class DeclaredReference
    {
        public string Key { get; set; } = string.Empty;
        public Element Element { get; set; } = null!;
        public Reference Reference { get; set; } = null!;
    }

    private sealed class ArrayResult
    {
        public string Key { get; set; } = string.Empty;
        public LinearArray Array { get; set; } = null!;
        public JObject Contract { get; set; } = new();
    }

    private sealed class NestedResult
    {
        public string Key { get; set; } = string.Empty;
        public FamilyInstance Instance { get; set; } = null!;
        public JObject Contract { get; set; } = new();
        public Dictionary<string, Parameter> MappedParameters { get; set; } = new(StringComparer.Ordinal);
        public JObject MappedParameterInterfaces { get; set; } = new();
        public FamilyParameter? FamilyTypeParameter { get; set; }
        public JArray TypeSelections { get; set; } = new();
        public JObject? VisibilityParameter { get; set; }
        public JObject Placement { get; set; } = new();
    }

    private sealed class SavePackage
    {
        public SaveAsOptions Options { get; set; } = null!;
        public JObject Snapshot { get; set; } = new();
    }

    private sealed class PurgePackage
    {
        public JObject Snapshot { get; set; } = new();
        public HashSet<long> InitialTemplateElementIds { get; set; } = new();
    }

    private sealed class AppearanceImageFiles
    {
        public string? TexturePath { get; set; }
        public string? BumpPath { get; set; }
        public double? BumpAmount { get; set; }
    }

    public static JObject Preview(Autodesk.Revit.ApplicationServices.Application application, FamilyTemplateSelection selection, string demoDirectory, FamilyBlueprintSpec spec, string familyName, string typeName)
    {
        Document? family = null;
        try
        {
            RequireSelectionBehavior(selection, spec);
            var sourceTraceability = VerifySourceTraceability(spec);
            RequireNestedArtifacts(spec, demoDirectory);
            var photometricAssets = RequirePhotometricAssets(spec, demoDirectory);
            var appearanceAssets = RequireAppearanceAssets(spec, demoDirectory);
            family = application.NewFamilyDocument(selection.Path);
            var templateHosting = VerifyRootFamilyPlacement(family, spec, "new_from_autodesk_template");
            var initialTemplateElementIds = CaptureElementIds(family);
            var category = FamilyTemplateResolver.BlueprintCategory(spec.CategoryKey);
            var categoryName = FamilyTemplateResolver.BlueprintCategoryName(spec.CategoryKey);
            var categoryChanged = FamilyTemplateResolver.EnsureTargetCategory(family, category, categoryName, selection.RequiresCategoryChange);
            templateHosting = VerifyRootFamilyPlacement(family, spec, categoryChanged ? "after_category_assignment" : "template_category_already_matches");
            using var group = new TransactionGroup(family, "Preview DSCons Family Blueprint v3"); group.Start();
            var result = Create(family, spec, familyName, typeName, photometricAssets, appearanceAssets);
            result["purge_unused"] = ApplyPurgePolicy(family, spec, initialTemplateElementIds).Snapshot;
            group.RollBack();
            result["model_changed"] = false; result["validation_level"] = "family_document_transactiongroup_rollback";
            result["category_assignment"] = new JObject { ["target"] = categoryName, ["changed_from_generic"] = categoryChanged, ["verified"] = true };
            result["template_hosting"] = templateHosting;
            result["source_traceability"] = sourceTraceability;
            return result;
        }
        catch (CommandResultException) { throw; }
        catch (Exception ex) { throw new CommandResultException(ErrorCodes.TransactionFailed, "Blueprint Family preview rolled back: " + ex.Message); }
        finally { if (family != null) try { family.Close(false); } catch { } }
    }

    public static JObject Apply(Autodesk.Revit.ApplicationServices.Application application, FamilyTemplateSelection selection, string demoDirectory, FamilyBlueprintSpec spec, string familyName, string typeName)
    {
        Document? family = null; string? staging = null; string? stagingCatalog = null; string? finalizedCatalog = null;
        var stage = "initialize";
        var output = Path.Combine(demoDirectory, familyName + ".rfa");
        var catalogOutput = Path.Combine(demoDirectory, familyName + ".txt");
        var wantsCatalog = (spec.Blueprint["publication"]?["type_catalog"] as JObject) != null;
        try
        {
            stage = "validate_blueprint_and_output";
            RequireSelectionBehavior(selection, spec);
            var sourceTraceability = VerifySourceTraceability(spec);
            if (File.Exists(output)) throw new CommandResultException(ErrorCodes.FileConflict, "Output Family exists; overwrite is blocked.");
            if (wantsCatalog && File.Exists(catalogOutput)) throw new CommandResultException(ErrorCodes.FileConflict, "Output Family Type Catalog exists; overwrite is blocked.");
            RequireNestedArtifacts(spec, demoDirectory);
            var photometricAssets = RequirePhotometricAssets(spec, demoDirectory);
            var appearanceAssets = RequireAppearanceAssets(spec, demoDirectory);
            stage = "open_autodesk_template";
            family = application.NewFamilyDocument(selection.Path);
            var templateHosting = VerifyRootFamilyPlacement(family, spec, "new_from_autodesk_template");
            var initialTemplateElementIds = CaptureElementIds(family);
            var category = FamilyTemplateResolver.BlueprintCategory(spec.CategoryKey);
            var categoryName = FamilyTemplateResolver.BlueprintCategoryName(spec.CategoryKey);
            var categoryChanged = FamilyTemplateResolver.EnsureTargetCategory(family, category, categoryName, selection.RequiresCategoryChange);
            templateHosting = VerifyRootFamilyPlacement(family, spec, categoryChanged ? "after_category_assignment" : "template_category_already_matches");
            JObject created; PurgePackage purge;
            using (var group = new TransactionGroup(family, "Compile DSCons Family Blueprint v3"))
            {
                stage = "compile_blueprint";
                group.Start(); created = Create(family, spec, familyName, typeName, photometricAssets, appearanceAssets);
                stage = "apply_purge_policy";
                purge = ApplyPurgePolicy(family, spec, initialTemplateElementIds); created["purge_unused"] = purge.Snapshot;
                group.Assimilate();
            }
            created["template_hosting"] = templateHosting;
            created["source_traceability"] = sourceTraceability;
            stage = "save_staging_rfa";
            staging = Path.Combine(demoDirectory, ".dscons-blueprint-" + Guid.NewGuid().ToString("N") + ".rfa");
            var save = CreateSaveOptions(family, spec); family.SaveAs(staging, save.Options); family.Close(false); family = null;
            var rfaBytes = new FileInfo(staging).Length;
            stage = "open_staging_rfa";
            var reopened = application.OpenDocumentFile(staging); JObject readBack; JObject reopenedComplexity;
            JObject reopenedPurge;
            try
            {
                stage = "reopened_family_inspection";
                readBack = FamilyData.Inspect(reopened); AttachAppearanceDependencyEvidence(readBack, spec, appearanceAssets); reopenedComplexity = EvaluateComplexityBudget(reopened, spec, rfaBytes);
                stage = "reopened_purge_verification";
                reopenedPurge = VerifyReopenedPurgePolicy(reopened, spec, purge);
            }
            finally { reopened.Close(false); }
            // Category.Name is localized by Revit (and can differ between a
            // template and its reopened RFA). Verify the native category id;
            // retain the display name solely for human-facing output.
            stage = "reopened_category_verification";
            if (!(readBack["is_family_document"]?.Value<bool>() ?? false) || readBack.Value<long?>("category_id") != (long)category)
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Saved Blueprint output did not reopen with the expected Revit Family category id.");
            stage = "reopened_family_name_verification";
            if (!string.Equals(readBack.Value<string>("family_name"), familyName, StringComparison.Ordinal))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Saved Blueprint output did not reopen with the requested internal Family name.");
            stage = "reopened_hosting_verification";
            var reopenedHosting = VerifyReopenedRootFamilyPlacement(spec, readBack);
            stage = "reopened_type_set_verification";
            var reopenedTypeSet = VerifyReopenedExactFamilyTypeSet(spec, typeName, readBack);
            stage = "reopened_behavior_verification";
            VerifyReopenedFamilyBehavior(spec, readBack);
            stage = "reopened_identity_verification";
            VerifyReopenedIdentityData(spec, readBack);
            stage = "reopened_material_verification";
            VerifyReopenedMaterials(spec, readBack);
            stage = "reopened_part_material_verification";
            VerifyReopenedPartMaterials(spec, readBack);
            stage = "reopened_presentation_verification";
            VerifyReopenedPresentationSubcategories(spec, readBack);
            stage = "reopened_detail_level_verification";
            VerifyReopenedDetailLevelRepresentations(spec, readBack);
            stage = "reopened_coordination_zone_verification";
            VerifyReopenedCoordinationZones(spec, created["coordination_zones"] as JObject, readBack);
            stage = "reopened_parameter_order_verification";
            VerifyReopenedParameterOrder(spec, readBack);
            stage = "reopened_lookup_table_verification";
            VerifyReopenedLookupTables(spec, readBack);
            stage = "reopened_reference_framework_verification";
            VerifyReopenedReferenceFramework(spec, readBack);
            stage = "reopened_model_line_binding_verification";
            VerifyReopenedModelLineEndpointBindings(spec, readBack);
            stage = "reopened_detail_line_binding_verification";
            VerifyReopenedDetailLineEndpointBindings(spec, readBack);
            stage = "reopened_nested_placement_verification";
            VerifyReopenedNestedPlacements(created, readBack);
            stage = "reopened_arc_sweep_verification";
            VerifyReopenedArcSweeps(spec, readBack);
            stage = "reopened_angular_sweep_verification";
            VerifyReopenedAngularSweeps(spec, readBack);
            stage = "reopened_inline_fitting_verification";
            VerifyReopenedTwoPortInlineFitting(spec, readBack);
            stage = "reopened_break_into_verification";
            VerifyReopenedBreakIntoAccessory(spec, readBack);
            stage = "reopened_junction_verification";
            VerifyReopenedJunction(spec, readBack);
            stage = "reopened_profile_shape_verification";
            VerifyReopenedProfileShapes(spec, readBack);
            stage = "reopened_electrical_connector_verification";
            VerifyReopenedElectricalConnectors(spec, readBack);
            stage = "reopened_light_source_verification";
            VerifyReopenedLightSource(spec, created["light_source"] as JObject, readBack);
            stage = "reopened_complexity_verification";
            VerifyReopenedComplexity(created["complexity"] as JObject, reopenedComplexity);
            JObject? catalog = null;
            if (wantsCatalog)
            {
                var content = BuildTypeCatalog(spec); var catalogBytes = TypeCatalogBytes(content); var catalogVerification = VerifyTypeCatalogContent(spec, content, catalogBytes); stagingCatalog = Path.Combine(demoDirectory, ".dscons-blueprint-" + Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllBytes(stagingCatalog, catalogBytes);
                catalog = new JObject { ["path"] = catalogOutput, ["sha256"] = Hash(stagingCatalog), ["bytes"] = new FileInfo(stagingCatalog).Length, ["type_count"] = (spec.Blueprint["types"] as JArray ?? new JArray()).Count, ["parameter_count"] = (spec.Blueprint["publication"]?["type_catalog"]?["parameter_keys"] as JArray ?? new JArray()).Count, ["same_basename_as_rfa"] = true, ["content_verification"] = catalogVerification };
                File.Move(stagingCatalog, catalogOutput); stagingCatalog = null; finalizedCatalog = catalogOutput;
            }
            File.Move(staging, output); staging = null; finalizedCatalog = null;
            return new JObject
            {
                ["family_path"] = output, ["sha256"] = Hash(output), ["schema_version"] = "3.0", ["spec_id"] = spec.SpecId, ["blueprint_hash"] = spec.Blueprint.Value<string>("blueprint_hash"),
                ["category_assignment"] = new JObject { ["target"] = categoryName, ["changed_from_generic"] = categoryChanged, ["verified"] = true },
                ["publication"] = new JObject { ["rfa_bytes"] = new FileInfo(output).Length, ["compact_requested"] = save.Snapshot.Value<bool>("compact_requested"), ["preview"] = save.Snapshot["preview"], ["purge_unused"] = reopenedPurge, ["type_catalog"] = catalog, ["external_dependencies"] = ExternalDependencySnapshot(spec, photometricAssets, appearanceAssets) },
                ["complexity"] = reopenedComplexity, ["created"] = created, ["verification"] = new JObject { ["verified"] = true, ["mode"] = "post_commit_reopen_read_back", ["template_hosting"] = reopenedHosting, ["family_type_set"] = reopenedTypeSet, ["source_traceability"] = sourceTraceability, ["family"] = readBack }
            };
        }
        catch (CommandResultException) { throw; }
        catch (Exception ex) { throw new CommandResultException(ErrorCodes.TransactionFailed, "Blueprint Family apply failed and staging was cleaned at " + stage + " (" + ex.GetType().Name + "): " + ex.Message); }
        finally
        {
            if (family != null) try { family.Close(false); } catch { }
            if (staging != null && File.Exists(staging)) try { File.Delete(staging); } catch { }
            if (stagingCatalog != null && File.Exists(stagingCatalog)) try { File.Delete(stagingCatalog); } catch { }
            if (finalizedCatalog != null && File.Exists(finalizedCatalog)) try { File.Delete(finalizedCatalog); } catch { }
        }
    }

    private static HashSet<long> CaptureElementIds(Document family) =>
        new(new FilteredElementCollector(family).WhereElementIsNotElementType().ToElementIds().Select(id => id.Val()));

    private static PurgePackage ApplyPurgePolicy(Document family, FamilyBlueprintSpec spec, HashSet<long> initialTemplateElementIds)
    {
        var contract = spec.Blueprint["publication"]?["purge_unused"] as JObject;
        var package = new PurgePackage { InitialTemplateElementIds = new HashSet<long>(initialTemplateElementIds) };
        if (contract == null)
        {
            package.Snapshot = new JObject
            {
                ["requested"] = false, ["status"] = "not_requested", ["api_minimum_revit_version"] = 2024,
                ["verification_boundary"] = "No purge was requested; Compact save and complexity/RFA budgets remain separate controls."
            };
            return package;
        }
        var scope = contract.Value<string>("scope") ?? string.Empty;
        var maxPasses = contract.Value<int?>("max_passes") ?? 5;
        var unsupportedBehavior = contract.Value<string>("unsupported_behavior") ?? "fail";
        if (scope != "template_residue" || maxPasses < 1 || maxPasses > 10 || unsupportedBehavior is not ("fail" or "skip_with_evidence"))
            throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Purge policy is outside the validated template_residue contract.");
#if REVIT2024 || REVIT2025 || REVIT2026 || REVIT2027
        List<ElementId> RemainingUnusedTemplateElements() => family.GetUnusedElements(new HashSet<ElementId>())
            .Where(id => initialTemplateElementIds.Contains(id.Val())).ToList();
        var deletedTemplateIds = new HashSet<long>(); var deletedWithDependents = 0; var passCount = 0;
        using var transaction = new Transaction(family, "Purge unused Autodesk template residue"); transaction.Start();
        while (passCount < maxPasses)
        {
            var candidates = RemainingUnusedTemplateElements();
            if (candidates.Count == 0) break;
            passCount++;
            foreach (var id in candidates) deletedTemplateIds.Add(id.Val());
            var deleted = family.Delete(candidates); deletedWithDependents += deleted.Count;
            if (deleted.Count == 0)
            {
                transaction.RollBack();
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit reported purgeable template residue but deleted no elements.");
            }
            family.Regenerate();
        }
        var remaining = RemainingUnusedTemplateElements();
        if (remaining.Count != 0)
        {
            transaction.RollBack();
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Purge did not converge within publication.purge_unused.max_passes; remaining template elements: " + remaining.Count + ".");
        }
        if (transaction.Commit() != TransactionStatus.Committed)
            throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rejected the bounded template-residue purge transaction.");
        package.Snapshot = new JObject
        {
            ["requested"] = true, ["status"] = "applied", ["scope"] = scope, ["api_supported"] = true,
            ["api_minimum_revit_version"] = 2024, ["max_passes"] = maxPasses, ["pass_count"] = passCount,
            ["initial_template_element_count"] = initialTemplateElementIds.Count, ["deleted_template_element_count"] = deletedTemplateIds.Count,
            ["deleted_element_count_with_dependents"] = deletedWithDependents, ["remaining_unused_template_element_count"] = 0,
            ["blueprint_created_elements_protected"] = true,
            ["verification_boundary"] = "Only elements present in the new Autodesk template before Blueprint compilation are purge candidates; Blueprint-created elements are excluded and the saved RFA is verified again after reopen."
        };
#else
        if (unsupportedBehavior == "fail")
            throw new CommandResultException(ErrorCodes.Unsupported, "publication.purge_unused requires Autodesk Revit 2024 or newer; use skip_with_evidence only when an explicitly unpurged older-version output is acceptable.");
        package.Snapshot = new JObject
        {
            ["requested"] = true, ["status"] = "skipped_api_unavailable", ["scope"] = scope, ["api_supported"] = false,
            ["api_minimum_revit_version"] = 2024, ["max_passes"] = maxPasses, ["unsupported_behavior"] = unsupportedBehavior,
            ["initial_template_element_count"] = initialTemplateElementIds.Count, ["blueprint_created_elements_protected"] = true,
            ["verification_boundary"] = "Revit 2019-2023 exposes no public Document.GetUnusedElements API; the RFA remains unpurged and no optimization claim is permitted."
        };
#endif
        return package;
    }

    private static JObject VerifyReopenedPurgePolicy(Document family, FamilyBlueprintSpec spec, PurgePackage package)
    {
        var snapshot = (JObject)package.Snapshot.DeepClone();
        if (spec.Blueprint["publication"]?["purge_unused"] is not JObject)
        {
            snapshot["reopen_verified"] = true;
            return snapshot;
        }
        if (snapshot.Value<string>("status") != "applied")
        {
            snapshot["reopen_verified"] = snapshot.Value<string>("status") == "skipped_api_unavailable";
            return snapshot;
        }
#if REVIT2024 || REVIT2025 || REVIT2026 || REVIT2027
        var remaining = family.GetUnusedElements(new HashSet<ElementId>())
            .Count(id => package.InitialTemplateElementIds.Contains(id.Val()));
        if (remaining != 0)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Saved Family reopened with purgeable Autodesk template residue: " + remaining + ".");
        snapshot["reopen_remaining_unused_template_element_count"] = remaining;
        snapshot["reopen_verified"] = true;
        return snapshot;
#else
        throw new CommandResultException(ErrorCodes.VerificationFailed, "An applied purge record cannot be verified by this pre-2024 Revit runtime.");
#endif
    }

    private static SavePackage CreateSaveOptions(Document family, FamilyBlueprintSpec spec)
    {
        var publication = spec.Blueprint["publication"] as JObject ?? new JObject();
        var compact = publication.Value<bool?>("compact_rfa") ?? true;
        var requestedPreview = publication.Value<string>("preview_view") ?? "auto";
        var options = new SaveAsOptions { OverwriteExistingFile = false, Compact = compact };
        var previewSettings = family.GetDocumentPreviewSettings();
        View? selected = null;
        if (!string.Equals(requestedPreview, "none", StringComparison.Ordinal))
        {
            var candidates = new FilteredElementCollector(family).OfClass(typeof(View)).Cast<View>()
                .Where(view => !view.IsTemplate && previewSettings.IsViewIdValidForPreview(view.Id))
                .OrderBy(view => view is View3D ? 0 : 1)
                .ThenBy(view => view.Name, StringComparer.Ordinal)
                .ToList();
            selected = string.Equals(requestedPreview, "three_dimensional", StringComparison.Ordinal)
                ? candidates.FirstOrDefault(view => view is View3D)
                : candidates.FirstOrDefault();
            if (string.Equals(requestedPreview, "three_dimensional", StringComparison.Ordinal) && selected == null)
                throw new CommandResultException(ErrorCodes.TemplateInvalid, "The selected Family template has no valid 3D preview view.");
            if (selected != null) options.PreviewViewId = selected.Id;
        }
        return new SavePackage
        {
            Options = options,
            Snapshot = new JObject
            {
                ["compact_requested"] = compact,
                ["preview"] = new JObject
                {
                    ["requested"] = requestedPreview,
                    ["selected"] = selected != null,
                    ["view_id"] = selected?.Id.Val(),
                    ["view_name"] = selected?.Name,
                    ["view_type"] = selected?.ViewType.ToString(),
                    ["valid_for_preview"] = selected == null ? null : previewSettings.IsViewIdValidForPreview(selected.Id)
                }
            }
        };
    }

    private static JObject VerifySourceTraceability(FamilyBlueprintSpec spec)
    {
        var requiredFields = (spec.Blueprint["required_source_fields"] as JArray ?? new JArray()).Values<string>()
            .Where(field => !string.IsNullOrWhiteSpace(field)).Select(field => field!).ToList();
        var traceability = spec.Blueprint["source_traceability"] as JObject
            ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint source_traceability is missing.");
        if (!string.Equals(traceability.Value<string>("policy"), "required_source_fields_to_critical_parameter_usage_v1", StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint source_traceability policy is not recognized.");
        if (traceability.Value<int?>("required_field_count") != requiredFields.Count || traceability.Value<bool?>("source_evidence_required") != (requiredFields.Count > 0))
            throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint source_traceability field counts are inconsistent.");
        var parameters = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>()
            .ToDictionary(parameter => parameter.Value<string>("key") ?? string.Empty, StringComparer.Ordinal);
        var bindings = (traceability["bindings"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        if (bindings.Count != requiredFields.Count) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint source_traceability bindings do not cover every required source field.");
        var fieldProvenance = spec.FieldProvenance;
        if (!string.Equals(fieldProvenance.Value<string>("policy"), "field_level_source_provenance_v1", StringComparison.Ordinal) ||
            !string.Equals(fieldProvenance.Value<string>("source_evidence_sha256"), spec.SourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new CommandResultException(ErrorCodes.EvidenceInvalid, "FamilySpec field provenance does not match its immutable SourceEvidence.");
        Dictionary<string, JObject> ProvenanceByField(JArray entries, string scope)
        {
            var result = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (var entry in entries.OfType<JObject>())
            {
                var field = entry.Value<string>("source_field") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(field) || result.ContainsKey(field))
                    throw new CommandResultException(ErrorCodes.EvidenceInvalid, "FamilySpec " + scope + " provenance contains an empty or duplicate source field.");
                result.Add(field, entry);
            }
            return result;
        }
        var provenanceByField = ProvenanceByField(fieldProvenance["fields"] as JArray ?? new JArray(), "default");
        var typeDefinitions = (spec.Blueprint["types"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var typeSpecificRequired = requiredFields.Count > 0 && typeDefinitions.Count > 1;
        if (fieldProvenance.Value<bool?>("type_specific_required") != typeSpecificRequired)
            throw new CommandResultException(ErrorCodes.EvidenceInvalid, "FamilySpec field provenance has an inconsistent type-specific evidence policy.");
        var typeProvenanceByName = new Dictionary<string, JObject>(StringComparer.Ordinal);
        foreach (var entry in (fieldProvenance["type_fields"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var typeName = entry.Value<string>("type_name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(typeName) || typeProvenanceByName.ContainsKey(typeName))
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "FamilySpec type field provenance contains an empty or duplicate Family Type name.");
            typeProvenanceByName.Add(typeName, entry);
        }
        var declaredTypeNames = new HashSet<string>(typeDefinitions.Select(type => type.Value<string>("name") ?? string.Empty), StringComparer.Ordinal);
        if (declaredTypeNames.Contains(string.Empty) || typeProvenanceByName.Keys.Any(name => !declaredTypeNames.Contains(name)) || spec.TypeConfirmedFields.Properties().Any(property => !declaredTypeNames.Contains(property.Name)))
            throw new CommandResultException(ErrorCodes.EvidenceInvalid, "FamilySpec type field provenance references an unknown or unnamed Family Type.");
        static bool IsSha256(string value) => value.Length == 64 && value.All(character => (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f') || (character >= 'A' && character <= 'F'));
        static string CatalogIdentityHash(string sourceSha256, string manufacturer, string catalogId, string revision, string issuedOn, string productSeries)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\u001f", new[] { sourceSha256, manufacturer, catalogId, revision, issuedOn, productSeries })))).Replace("-", string.Empty).ToLowerInvariant();
        }
        static string CatalogIdentityHashWithRevisionProvenance(string sourceSha256, string manufacturer, string catalogId, string revision, string issuedOn, string productSeries, JObject revisionProvenance)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\u001f", new[]
            {
                sourceSha256, manufacturer, catalogId, revision, issuedOn, productSeries,
                revisionProvenance.Value<string>("kind") ?? string.Empty, revisionProvenance.Value<string>("confirmation_origin") ?? string.Empty,
                revisionProvenance.Value<int?>("page")?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                revisionProvenance.Value<int?>("block_index")?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                revisionProvenance.Value<string>("block_sha256") ?? string.Empty
            })))).Replace("-", string.Empty).ToLowerInvariant();
        }
        static string CatalogText(JObject identity, string property, int maximum, string scope)
        {
            var value = identity.Value<string>(property)?.Trim() ?? string.Empty;
            if (value.Length == 0 || value.Length > maximum || value.Any(char.IsControl))
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Catalog revision " + scope + "." + property + " must be a visible string with 1.." + maximum + " characters.");
            return value;
        }
        JObject VerifyCatalogRevisionProvenance()
        {
            var provenance = spec.CatalogRevisionProvenance;
            if (provenance.Count == 0)
                return new JObject { ["policy"] = "catalog_revision_identity_v1", ["declared"] = false, ["scope"] = "legacy_absent", ["boundary"] = "This legacy FamilySpec contains no declared catalog revision identity; no catalog revision claim is made." };
            var allowed = new HashSet<string>(StringComparer.Ordinal) { "policy", "source_evidence_sha256", "declared", "scope", "family", "types", "boundary" };
            var catalogRevisionPolicy = provenance.Value<string>("policy") ?? string.Empty;
            if (provenance.Properties().Any(property => !allowed.Contains(property.Name)) ||
                catalogRevisionPolicy is not ("catalog_revision_identity_v1" or "catalog_revision_identity_v2") ||
                !string.Equals(provenance.Value<string>("source_evidence_sha256"), spec.SourceSha256, StringComparison.OrdinalIgnoreCase) ||
                provenance["declared"]?.Type != JTokenType.Boolean)
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "FamilySpec catalog revision provenance does not match immutable SourceEvidence.");
            var revisionProvenanceRequired = catalogRevisionPolicy == "catalog_revision_identity_v2";
            var declared = provenance.Value<bool>("declared"); var scope = provenance.Value<string>("scope") ?? string.Empty;
            JObject ValidateIdentity(JObject identity, string identityScope)
            {
                var identityAllowed = new HashSet<string>(StringComparer.Ordinal) { "manufacturer", "catalog_id", "revision", "issued_on", "product_series", "source_evidence_sha256", "catalog_identity_sha256" };
                if (revisionProvenanceRequired) identityAllowed.Add("revision_provenance");
                if (identity.Properties().Any(property => !identityAllowed.Contains(property.Name)) ||
                    !string.Equals(identity.Value<string>("source_evidence_sha256"), spec.SourceSha256, StringComparison.OrdinalIgnoreCase))
                    throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Catalog revision identity " + identityScope + " contains unsupported data or a mismatched SourceEvidence hash.");
                var manufacturer = CatalogText(identity, "manufacturer", 120, identityScope); var catalogId = CatalogText(identity, "catalog_id", 120, identityScope); var revision = CatalogText(identity, "revision", 80, identityScope);
                var issuedOn = identity["issued_on"] == null ? string.Empty : CatalogText(identity, "issued_on", 10, identityScope);
                if (!string.IsNullOrEmpty(issuedOn) && (!DateTime.TryParseExact(issuedOn, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate) || parsedDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) != issuedOn))
                    throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Catalog revision " + identityScope + ".issued_on must be a calendar date YYYY-MM-DD.");
                var productSeries = identity["product_series"] == null ? string.Empty : CatalogText(identity, "product_series", 120, identityScope);
                JObject revisionProvenance;
                if (!revisionProvenanceRequired) revisionProvenance = new JObject { ["kind"] = "legacy_absent" };
                else
                {
                    revisionProvenance = identity["revision_provenance"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Catalog revision identity " + identityScope + " requires revision_provenance.");
                    var kind = revisionProvenance.Value<string>("kind") ?? string.Empty;
                    if (kind == "source_document")
                    {
                        var allowedRevisionProvenance = new HashSet<string>(StringComparer.Ordinal) { "kind", "source_evidence_sha256", "page", "block_index", "block_sha256" };
                        var page = revisionProvenance.Value<int?>("page"); var blockIndex = revisionProvenance.Value<int?>("block_index"); var blockSha = revisionProvenance.Value<string>("block_sha256") ?? string.Empty;
                        if (revisionProvenance.Properties().Any(property => !allowedRevisionProvenance.Contains(property.Name)) ||
                            !string.Equals(revisionProvenance.Value<string>("source_evidence_sha256"), spec.SourceSha256, StringComparison.OrdinalIgnoreCase) ||
                            !page.HasValue || page.Value < 1 || !blockIndex.HasValue || blockIndex.Value < 0 || !IsSha256(blockSha))
                            throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Catalog revision identity " + identityScope + " source-document revision_provenance is malformed.");
                        revisionProvenance = new JObject { ["kind"] = kind, ["source_evidence_sha256"] = spec.SourceSha256, ["page"] = page.Value, ["block_index"] = blockIndex.Value, ["block_sha256"] = blockSha.ToLowerInvariant() };
                    }
                    else if (kind == "user_confirmed")
                    {
                        if (revisionProvenance.Properties().Count() != 2 || !string.Equals(revisionProvenance.Value<string>("confirmation_origin"), "explicit", StringComparison.Ordinal))
                            throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Catalog revision identity " + identityScope + " user-confirmed revision_provenance must be explicit.");
                        revisionProvenance = new JObject { ["kind"] = kind, ["confirmation_origin"] = "explicit" };
                    }
                    else throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Catalog revision identity " + identityScope + " revision_provenance kind is unsupported.");
                }
                var declaredHash = identity.Value<string>("catalog_identity_sha256") ?? string.Empty;
                var actualHash = revisionProvenanceRequired
                    ? CatalogIdentityHashWithRevisionProvenance(spec.SourceSha256, manufacturer, catalogId, revision, issuedOn, productSeries, revisionProvenance)
                    : CatalogIdentityHash(spec.SourceSha256, manufacturer, catalogId, revision, issuedOn, productSeries);
                if (!IsSha256(declaredHash) || !string.Equals(declaredHash, actualHash, StringComparison.OrdinalIgnoreCase))
                    throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Catalog revision identity " + identityScope + " hash does not match its immutable source/revision fields.");
                return new JObject { ["catalog_identity_sha256"] = actualHash, ["revision_provenance"] = revisionProvenance, ["issued_on_declared"] = !string.IsNullOrEmpty(issuedOn), ["product_series_declared"] = !string.IsNullOrEmpty(productSeries) };
            }
            if (!declared)
            {
                if (scope != "none" || provenance["family"] != null || provenance["types"] != null)
                    throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Undeclared catalog revision provenance must use scope=none with no catalog identity.");
                return new JObject { ["policy"] = catalogRevisionPolicy, ["source_evidence_sha256"] = spec.SourceSha256, ["declared"] = false, ["scope"] = "none", ["boundary"] = provenance.Value<string>("boundary") ?? string.Empty };
            }
            if (scope == "family")
            {
                if (typeSpecificRequired || provenance["family"] is not JObject identity || provenance["types"] != null)
                    throw new CommandResultException(ErrorCodes.EvidenceInvalid, "A Family-level catalog revision is valid only when per-Type source revision is not required.");
                return new JObject { ["policy"] = catalogRevisionPolicy, ["source_evidence_sha256"] = spec.SourceSha256, ["declared"] = true, ["scope"] = "family", ["family"] = ValidateIdentity(identity, "family"), ["boundary"] = provenance.Value<string>("boundary") ?? string.Empty };
            }
            if (scope != "per_type" || !typeSpecificRequired || provenance["family"] != null || provenance["types"] is not JArray identities)
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Per-Type catalog revision provenance is malformed or not required by this FamilySpec.");
            var byType = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (var entry in identities.OfType<JObject>())
            {
                if (entry.Properties().Any(property => property.Name is not ("type_name" or "catalog")) || entry.Value<string>("type_name") is not string typeName || string.IsNullOrWhiteSpace(typeName) || entry["catalog"] is not JObject identity || byType.ContainsKey(typeName))
                    throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Per-Type catalog revision provenance contains an invalid or duplicate Family Type identity.");
                byType.Add(typeName, identity);
            }
            if (byType.Count != typeDefinitions.Count || byType.Keys.Any(typeName => !declaredTypeNames.Contains(typeName)))
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Per-Type catalog revision provenance must cover every exact declared Family Type once.");
            return new JObject
            {
                ["policy"] = catalogRevisionPolicy, ["source_evidence_sha256"] = spec.SourceSha256, ["declared"] = true, ["scope"] = "per_type",
                ["types"] = new JArray(typeDefinitions.Select(type => new JObject { ["type_name"] = type.Value<string>("name"), ["catalog"] = ValidateIdentity(byType[type.Value<string>("name")!], "Family Type " + type.Value<string>("name")) })),
                ["boundary"] = provenance.Value<string>("boundary") ?? string.Empty
            };
        }
        var sourceDocumentFields = 0; var explicitUserConfirmedFields = 0; var legacyUserConfirmedFields = 0;
        var redactedProvenance = new JArray();
        var redactedTypeProvenance = new Dictionary<string, JArray>(StringComparer.Ordinal);
        JObject ValidateFieldProvenance(JObject confirmedFields, Dictionary<string, JObject> provenanceBySourceField, string sourceField, string scope)
        {
            if (!provenanceBySourceField.TryGetValue(sourceField, out var provenance) || !string.Equals(provenance.Value<string>("status"), "confirmed", StringComparison.Ordinal))
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Confirmed source field " + scope + "." + sourceField + " is missing field-level provenance.");
            var confirmedState = confirmedFields[sourceField] as JObject;
            if (confirmedState == null || !string.Equals(confirmedState.Value<string>("status"), "confirmed", StringComparison.Ordinal) || confirmedState["value"] == null)
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Confirmed source field " + scope + "." + sourceField + " is not normalized as an immutable field state.");
            var provenanceKind = provenance.Value<string>("provenance_kind") ?? string.Empty;
            var stateProvenance = confirmedState["provenance"] as JObject;
            if (stateProvenance == null || !string.Equals(stateProvenance.Value<string>("kind"), provenanceKind, StringComparison.Ordinal))
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Confirmed source field " + scope + "." + sourceField + " provenance differs from the redacted FamilySpec evidence.");
            var redactedField = new JObject { ["source_field"] = sourceField, ["provenance_kind"] = provenanceKind };
            if (provenanceKind == "source_document")
            {
                var locator = provenance["document_locator"] as JObject;
                var stateLocator = stateProvenance["page"] != null ? stateProvenance : null;
                var page = locator?.Value<int?>("page"); var blockIndex = locator?.Value<int?>("block_index"); var blockSha = locator?.Value<string>("block_sha256") ?? string.Empty;
                if (locator == null || !page.HasValue || page.Value < 1 || !blockIndex.HasValue || blockIndex.Value < 0 || !IsSha256(blockSha) ||
                    stateLocator == null || stateLocator.Value<int?>("page") != page || stateLocator.Value<int?>("block_index") != blockIndex ||
                    !string.Equals(stateLocator.Value<string>("block_sha256"), blockSha, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(stateLocator.Value<string>("source_evidence_sha256"), spec.SourceSha256, StringComparison.OrdinalIgnoreCase))
                    throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Source-document provenance locator is malformed or does not match immutable SourceEvidence for field " + scope + "." + sourceField + ".");
                sourceDocumentFields++;
                redactedField["document_locator"] = new JObject { ["page"] = page.Value, ["block_index"] = blockIndex.Value, ["block_sha256"] = blockSha.ToLowerInvariant() };
            }
            else if (provenanceKind == "user_confirmed")
            {
                var origin = provenance.Value<string>("confirmation_origin") ?? string.Empty;
                if (origin is not ("explicit" or "legacy_scalar" or "legacy_field_state") || !string.Equals(stateProvenance.Value<string>("confirmation_origin"), origin, StringComparison.Ordinal))
                    throw new CommandResultException(ErrorCodes.EvidenceInvalid, "User-confirmed provenance is malformed for field " + scope + "." + sourceField + ".");
                if (origin == "explicit") explicitUserConfirmedFields++; else legacyUserConfirmedFields++;
                redactedField["confirmation_origin"] = origin;
            }
            else throw new CommandResultException(ErrorCodes.EvidenceInvalid, "FamilySpec field provenance kind is unsupported for field " + scope + "." + sourceField + ".");
            return redactedField;
        }
        var boundFields = new HashSet<string>(StringComparer.Ordinal); var geometryPaths = 0; var connectorPaths = 0;
        foreach (var binding in bindings)
        {
            var sourceField = binding.Value<string>("source_field") ?? string.Empty;
            if (!requiredFields.Contains(sourceField, StringComparer.Ordinal) || !boundFields.Add(sourceField))
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint source_traceability has an unknown or duplicate source field binding.");
            var parameterKeys = (binding["parameter_keys"] as JArray ?? new JArray()).Values<string>().Where(key => !string.IsNullOrWhiteSpace(key)).Select(key => key!).ToList();
            if (parameterKeys.Count == 0) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Source field " + sourceField + " has no bound Family Parameter.");
            foreach (var parameterKey in parameterKeys)
            {
                if (!parameters.TryGetValue(parameterKey, out var parameter) || !string.Equals(parameter.Value<string>("source_field"), sourceField, StringComparison.Ordinal) || parameter["formula"] != null || parameter["lookup"] != null)
                    throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Source field " + sourceField + " has an invalid direct Family Parameter binding.");
            }
            if (!typeSpecificRequired)
            {
                _ = spec.ConfirmedValue(sourceField);
                redactedProvenance.Add(ValidateFieldProvenance(spec.ConfirmedFields, provenanceByField, sourceField, "default"));
            }
            else foreach (var type in typeDefinitions)
            {
                var typeName = type.Value<string>("name")!;
                if (spec.TypeConfirmedFields[typeName] is not JObject typeFields || !typeProvenanceByName.TryGetValue(typeName, out var typeProvenance))
                    throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Family Type " + typeName + " is missing type-specific source confirmation/provenance.");
                var typeFieldEntries = ProvenanceByField(typeProvenance["fields"] as JArray ?? new JArray(), "Family Type " + typeName);
                var redactedFields = redactedTypeProvenance.TryGetValue(typeName, out var existing) ? existing : redactedTypeProvenance[typeName] = new JArray();
                var redactedField = ValidateFieldProvenance(typeFields, typeFieldEntries, sourceField, "Family Type " + typeName);
                redactedFields.Add(redactedField);
                var sourceValue = typeFields[sourceField]?["value"];
                var typeValues = type["values"] as JObject;
                foreach (var parameterKey in parameterKeys)
                {
                    var typeValue = typeValues?[parameterKey];
                    if (sourceValue == null || typeValue == null)
                        throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Family Type " + typeName + " must explicitly declare source-bound parameter " + parameterKey + "; shared source fallback is blocked for multi-Type Family.");
                    if (!JToken.DeepEquals(typeValue, sourceValue))
                        throw new CommandResultException(ErrorCodes.EvidenceConflict, "Family Type " + typeName + " value for " + parameterKey + " does not match confirmed source field " + sourceField + ".");
                }
            }
            geometryPaths += (binding["geometry_target_paths"] as JArray)?.Count ?? 0;
            connectorPaths += (binding["connector_target_paths"] as JArray)?.Count ?? 0;
        }
        if (requiredFields.Any(field => !boundFields.Contains(field))) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint source_traceability is missing a required source-field binding.");
        var untraced = (traceability["direct_untraced_critical_parameter_keys"] as JArray ?? new JArray()).Values<string>().Where(key => !string.IsNullOrWhiteSpace(key)).ToList();
        // Direct critical parameters need source-field bindings only when the
        // Blueprint actually declares catalog/source fields.  This mirrors the
        // Node validator: an explicitly source-free parametric fixture is
        // allowed, while a catalog-backed Family still fails closed.
        if (requiredFields.Count > 0 && untraced.Count > 0) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint declares direct critical geometry/connector parameters without source traceability: " + string.Join(", ", untraced) + ".");
        var catalogRevision = VerifyCatalogRevisionProvenance();
        return new JObject
        {
            ["verified"] = true, ["mode"] = "immutable_family_spec_source_field_binding_read_back", ["source_evidence_sha256"] = spec.SourceSha256,
            ["required_field_count"] = requiredFields.Count, ["confirmed_field_count"] = boundFields.Count, ["binding_count"] = bindings.Count,
            ["geometry_target_path_count"] = geometryPaths, ["connector_target_path_count"] = connectorPaths,
            ["critical_parameter_reference_count"] = traceability.Value<int?>("critical_parameter_reference_count") ?? 0,
            ["formula_or_lookup_derived_parameter_count"] = (traceability["formula_or_lookup_derived_critical_parameter_keys"] as JArray)?.Count ?? 0,
            ["catalog_revision"] = catalogRevision,
            ["field_provenance"] = new JObject
            {
                ["policy"] = "field_level_source_provenance_v1", ["source_evidence_sha256"] = spec.SourceSha256,
                ["required_field_count"] = requiredFields.Count, ["source_document_field_count"] = sourceDocumentFields,
                ["explicit_user_confirmed_field_count"] = explicitUserConfirmedFields, ["legacy_user_confirmed_field_count"] = legacyUserConfirmedFields,
                ["fields"] = redactedProvenance,
                ["type_specific_required"] = typeSpecificRequired,
                ["type_fields"] = new JArray(typeDefinitions.Select(type => new JObject
                {
                    ["type_name"] = type.Value<string>("name"), ["fields"] = redactedTypeProvenance.TryGetValue(type.Value<string>("name")!, out var fields) ? fields : new JArray()
                })),
                ["boundary"] = "Locator hashes identify reviewed SourceEvidence page/blocks without returning source text. Multi-Type source-bound values are checked against each exact Family Type. This does not prove extraction accuracy, catalog revision applicability, per-type catalog completeness, or Revit runtime behavior."
            },
            ["boundary"] = traceability.Value<string>("boundary") ?? string.Empty
        };
    }

    private static JObject TypeCatalogPreview(FamilyBlueprintSpec spec)
    {
        var catalog = spec.Blueprint["publication"]?["type_catalog"] as JObject;
        if (catalog == null) return new JObject { ["enabled"] = false };
        var content = BuildTypeCatalog(spec);
        var bytes = TypeCatalogBytes(content);
        var contentVerification = VerifyTypeCatalogContent(spec, content, bytes);
        var definitions = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>()
            .ToDictionary(item => item.Value<string>("key") ?? string.Empty, StringComparer.Ordinal);
        var keys = (catalog["parameter_keys"] as JArray ?? new JArray()).Values<string>()
            .Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToList();
        return new JObject
        {
            ["enabled"] = true,
            ["type_count"] = (spec.Blueprint["types"] as JArray ?? new JArray()).Count,
            ["parameter_count"] = keys.Count,
            ["parameter_names"] = new JArray(keys.Select(key => definitions[key].Value<string>("name") ?? key)),
            ["utf8_bom"] = true,
            ["same_basename_as_rfa_required"] = true,
            ["content_sha256"] = HashBytes(bytes),
            ["bytes"] = bytes.Length,
            ["content_verification"] = contentVerification
        };
    }

    private static string BuildTypeCatalog(FamilyBlueprintSpec spec)
    {
        var catalog = spec.Blueprint["publication"]?["type_catalog"] as JObject
            ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint publication.type_catalog is missing.");
        var definitions = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>()
            .ToDictionary(item => item.Value<string>("key") ?? string.Empty, StringComparer.Ordinal);
        var materials = (spec.Blueprint["materials"] as JArray ?? new JArray()).OfType<JObject>()
            .ToDictionary(item => item.Value<string>("key") ?? string.Empty, item => item.Value<string>("name") ?? string.Empty, StringComparer.Ordinal);
        var keys = (catalog["parameter_keys"] as JArray ?? new JArray()).Values<string>()
            .Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToList();
        if (keys.Count == 0) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Type Catalog requires at least one parameter key.");
        foreach (var key in keys)
            if (!definitions.ContainsKey(key)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Type Catalog references unknown parameter " + key + ".");

        string Header(JObject definition)
        {
            var name = definition.Value<string>("name") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Type Catalog parameter name is missing.");
            return Csv(RequireTypeCatalogHeaderName(name) + "##" + TypeCatalogDeclaration(definition.Value<string>("data_type") ?? string.Empty));
        }

        JToken Value(JObject type, string key)
        {
            var definition = definitions[key];
            var value = (type["values"] as JObject)?[key] ?? definition["default"];
            if (value == null && definition.Value<string>("source_field") is string sourceField) value = spec.ConfirmedValue(sourceField);
            return value ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Type Catalog has no value for parameter " + key + " in type " + type.Value<string>("name") + ".");
        }

        string Format(JToken value, JObject definition)
        {
            var dataType = definition.Value<string>("data_type") ?? string.Empty;
            if (dataType == "yesno") return value.Value<bool>() ? "1" : "0";
            if (dataType == "material")
            {
                var materialKey = value.Value<string>() ?? string.Empty;
                if (!materials.TryGetValue(materialKey, out var materialName)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Type Catalog references unknown material " + materialKey + ".");
                return Csv(RequireTypeCatalogCell(materialName, "Type Catalog material name"));
            }
            if (dataType is "text" or "url") return Csv(RequireTypeCatalogCell(value.Value<string>() ?? string.Empty, "Type Catalog text value"));
            if (dataType == "integer") return value.Value<int>().ToString(System.Globalization.CultureInfo.InvariantCulture);
            return value.Value<double>().ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        var types = (spec.Blueprint["types"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        if (types.Count < 2) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Type Catalog requires at least two Family types.");
        var lines = new List<string> { "," + string.Join(",", keys.Select(key => Header(definitions[key]))) };
        foreach (var type in types)
        {
            var name = type.Value<string>("name") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Type Catalog Family type name is missing.");
            lines.Add(Csv(RequireTypeCatalogCell(name, "Type Catalog Family type name")) + "," + string.Join(",", keys.Select(key => Format(Value(type, key), definitions[key]))));
        }
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static string TypeCatalogDeclaration(string dataType) => dataType switch
    {
        "length" => "LENGTH##MILLIMETERS",
        "area" => "AREA##SQUARE_METERS",
        "volume" => "VOLUME##CUBIC_METERS",
        "angle" => "ANGLE##DEGREES",
        "currency" => "CURRENCY##",
        "number" or "integer" or "text" or "url" or "yesno" or "material" => "OTHER##",
        var unsupported => throw new CommandResultException(ErrorCodes.Unsupported, "No documented Type Catalog declaration is defined for parameter data type " + unsupported + "; a real Revit load/select-type certification is required before emitting it.")
    };

    private static string RequireTypeCatalogCell(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, label + " must not be blank.");
        if (value.IndexOfAny(new[] { '\r', '\n' }) >= 0) throw new CommandResultException(ErrorCodes.EvidenceInvalid, label + " cannot contain a line break because a Type Catalog row must remain one physical line.");
        return value;
    }

    private static string RequireTypeCatalogHeaderName(string value)
    {
        var name = RequireTypeCatalogCell(value, "Type Catalog parameter name");
        if (name.IndexOf("##", StringComparison.Ordinal) >= 0) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Type Catalog parameter name cannot contain ## because it is reserved by the Revit Type Catalog header syntax.");
        return name;
    }

    private static JObject VerifyTypeCatalogContent(FamilyBlueprintSpec spec, string content, byte[] bytes)
    {
        if (!content.EndsWith("\r\n", StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Type Catalog content must end with CRLF.");
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] == '\r' && (index + 1 >= content.Length || content[index + 1] != '\n')) throw new CommandResultException(ErrorCodes.VerificationFailed, "Type Catalog content contains a bare carriage return.");
            if (content[index] == '\n' && (index == 0 || content[index - 1] != '\r')) throw new CommandResultException(ErrorCodes.VerificationFailed, "Type Catalog content contains a bare line feed.");
        }
        var rows = content.Substring(0, content.Length - 2).Split(new[] { "\r\n" }, StringSplitOptions.None).Select(ParseTypeCatalogCsvRow).ToList();
        var catalog = spec.Blueprint["publication"]?["type_catalog"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint publication.type_catalog is missing.");
        var keys = (catalog["parameter_keys"] as JArray ?? new JArray()).Values<string>().Where(key => !string.IsNullOrWhiteSpace(key)).Select(key => key!).ToList();
        var definitions = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("key") ?? string.Empty, StringComparer.Ordinal);
        var types = (spec.Blueprint["types"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        if (rows.Count != types.Count + 1) throw new CommandResultException(ErrorCodes.VerificationFailed, "Type Catalog row count does not match the declared Family types.");
        if (rows.Count == 0 || rows[0].Count != keys.Count + 1 || rows[0][0].Length != 0) throw new CommandResultException(ErrorCodes.VerificationFailed, "Type Catalog header does not begin with the declared comma delimiter.");
        for (var index = 0; index < keys.Count; index++)
        {
            if (!definitions.TryGetValue(keys[index], out var definition)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Type Catalog verification cannot find parameter " + keys[index] + ".");
            var expected = RequireTypeCatalogHeaderName(definition.Value<string>("name") ?? string.Empty) + "##" + TypeCatalogDeclaration(definition.Value<string>("data_type") ?? string.Empty);
            if (!string.Equals(rows[0][index + 1], expected, StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Type Catalog header does not match its declared parameter name/type/unit at column " + (index + 1) + ".");
        }
        var typeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row.Count != keys.Count + 1) throw new CommandResultException(ErrorCodes.VerificationFailed, "Type Catalog row " + rowIndex + " has a different number of columns from its header.");
            var expectedName = RequireTypeCatalogCell(types[rowIndex - 1].Value<string>("name") ?? string.Empty, "Type Catalog Family type name");
            if (!string.Equals(row[0], expectedName, StringComparison.Ordinal) || !typeNames.Add(row[0])) throw new CommandResultException(ErrorCodes.VerificationFailed, "Type Catalog row " + rowIndex + " has an invalid or duplicate Family type name.");
        }
        var bom = new System.Text.UTF8Encoding(true).GetPreamble();
        if (bytes.Length < bom.Length || !bytes.Take(bom.Length).SequenceEqual(bom)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Type Catalog bytes are missing the required UTF-8 BOM.");
        string decoded;
        try { decoded = new System.Text.UTF8Encoding(false, true).GetString(bytes, bom.Length, bytes.Length - bom.Length); }
        catch (System.Text.DecoderFallbackException) { throw new CommandResultException(ErrorCodes.VerificationFailed, "Type Catalog bytes are not valid UTF-8."); }
        if (!string.Equals(decoded, content, StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Type Catalog UTF-8 round-trip changed the generated content.");
        return new JObject
        {
            ["format"] = "revit_documented_generic_type_catalog_v1", ["delimiter"] = ",", ["header_columns"] = keys.Count,
            ["type_rows"] = types.Count, ["utf8_bom"] = true, ["csv_round_trip_verified"] = true,
            ["mep_header_tokens_runtime_certification_required"] = true
        };
    }

    private static IReadOnlyList<string> ParseTypeCatalogCsvRow(string row)
    {
        var fields = new List<string>(); var field = new System.Text.StringBuilder(); var quoted = false; var quoteClosed = false;
        for (var index = 0; index < row.Length; index++)
        {
            var current = row[index];
            if (quoted)
            {
                if (current != '"') { field.Append(current); continue; }
                if (index + 1 < row.Length && row[index + 1] == '"') { field.Append('"'); index++; continue; }
                quoted = false; quoteClosed = true; continue;
            }
            if (quoteClosed)
            {
                if (current != ',') throw new CommandResultException(ErrorCodes.VerificationFailed, "Type Catalog CSV contains characters after a closing quote.");
                fields.Add(field.ToString()); field.Clear(); quoteClosed = false; continue;
            }
            if (current == ',') { fields.Add(field.ToString()); field.Clear(); continue; }
            if (current == '"')
            {
                if (field.Length != 0) throw new CommandResultException(ErrorCodes.VerificationFailed, "Type Catalog CSV contains an unescaped quote.");
                quoted = true; continue;
            }
            field.Append(current);
        }
        if (quoted) throw new CommandResultException(ErrorCodes.VerificationFailed, "Type Catalog CSV contains an unclosed quoted field.");
        fields.Add(field.ToString()); return fields;
    }

    private static string Csv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n')) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static byte[] TypeCatalogBytes(string content)
    {
        var encoding = new System.Text.UTF8Encoding(true);
        var preamble = encoding.GetPreamble(); var body = encoding.GetBytes(content); var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length); Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length); return result;
    }

    private static string HashBytes(byte[] bytes) { using var sha = SHA256.Create(); return Convert.ToBase64String(sha.ComputeHash(bytes)); }

    private static JObject Create(Document family, FamilyBlueprintSpec spec, string familyName, string typeName, IReadOnlyDictionary<string, string> photometricAssets, IReadOnlyDictionary<string, AppearanceImageFiles> appearanceAssets)
    {
        using var transaction = new Transaction(family, "Compile declarative Family Blueprint"); transaction.Start();
        var ownerFamily = family.OwnerFamily ?? throw new CommandResultException(ErrorCodes.DocumentTypeInvalid, "A Blueprint build requires a loadable Revit Family document.");
        ownerFamily.Name = familyName;
        if (!string.Equals(ownerFamily.Name, familyName, StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit did not retain the requested internal Family name.");
        var failures = new FamilyCompileFailuresPreprocessor(family);
        var failureOptions = transaction.GetFailureHandlingOptions();
        failureOptions.SetFailuresPreprocessor(failures);
        failureOptions.SetClearAfterRollback(true);
        transaction.SetFailureHandlingOptions(failureOptions);
        var manager = family.FamilyManager;
        var buildStage = "apply_family_behavior";
        try
        {
        var familyBehavior = ApplyFamilyBehavior(family, spec);
        buildStage = "create_parameters";
        var parameters = CreateParameters(family, spec);
        buildStage = "create_materials";
        var materials = CreateMaterials(family, spec, appearanceAssets);
        var presentationSubcategories = CreatePresentationSubcategories(family, spec);
        var electricalLoadClassifications = CreateElectricalLoadClassifications(family, spec);
        var lookupTables = CreateLookupTables(family, spec);
        var types = CreateTypes(manager, spec, parameters, materials, electricalLoadClassifications, typeName);
        var identityData = ApplyIdentityData(family, manager, spec, types);
        manager.CurrentType = types.First();
        ApplyParameterFormulas(manager, spec, parameters, lookupTables);
        // A Lighting template keeps a native light-type object for its initial
        // Family Type.  Configure the declared light types before removing that
        // template residue; otherwise Revit can sever the light-type mapping.
        ApplyLightSource(family, spec, photometricAssets);
        CleanupUndeclaredFamilyTypes(manager, ExpectedFamilyTypeNames(spec, typeName));
        var typeSet = VerifyExactFamilyTypeSet(manager, ExpectedFamilyTypeNames(spec, typeName), "post_template_type_cleanup");
        var lightSource = FinalizeLightSourceReadBack(family, spec);
        var parts = new Dictionary<string, PartResult>(StringComparer.Ordinal);
        var precreatedReferenceLines = new Dictionary<string, ModelCurve>(StringComparer.Ordinal);
        foreach (var part in (spec.Blueprint["parts"] as JArray ?? new JArray()).OfType<JObject>())
        {
            buildStage = "create_part:" + (part.Value<string>("key") ?? "unnamed");
            var profile = part["profile"] as JObject ?? new JObject();
            var primitive = part.Value<string>("primitive") ?? string.Empty;
            GenericForm form;
            if (primitive == "sweep" && part["path"]?.Value<string>("kind") == "reference_line")
            {
                var path = part["path"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Angular sweep path is missing.");
                var referenceLineKey = path.Value<string>("reference_line_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Angular sweep reference_line_key is missing.");
                var lineContract = (spec.Blueprint["reference_lines"] as JArray ?? new JArray()).OfType<JObject>().SingleOrDefault(item => item.Value<string>("key") == referenceLineKey)
                    ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Angular sweep references unknown Reference Line " + referenceLineKey + ".");
                if (!precreatedReferenceLines.TryGetValue(referenceLineKey, out var referenceLine))
                {
                    referenceLine = CreateReferenceLine(family, lineContract);
                    precreatedReferenceLines.Add(referenceLineKey, referenceLine);
                }
                var references = new ReferenceArray(); references.Append(referenceLine.GeometryCurve.Reference ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Angular sweep Reference Line has no stable geometry reference."));
                var sweepCurves = ProfileHasParameters(profile) ? ResolvedProfileAt(profile, "xy", XYZ.Zero, parameters, manager) : LiteralProfileAt(profile, "xy", XYZ.Zero);
                var sweepProfile = family.Application.Create.NewCurveLoopsProfile(sweepCurves);
                form = family.FamilyCreate.NewSweep(true, references, sweepProfile, 0, ParseProfileLocation(part.Value<string>("profile_location")));
                AssociateSweepParameters(family, (Sweep)form, part, profile, parameters, manager, XYZ.Zero, null);
            }
            else if (primitive == "extrusion")
            {
                var start = Mm(part.Value<double>("start_mm")); var end = Mm(part.Value<double>("end_mm"));
                var axisKey = part.Value<string>("axis") ?? spec.Blueprint["family"]?.Value<string>("primary_axis") ?? "x";
                var frame = ExtrusionFrame(part, axisKey);
                var plane = SketchPlane.Create(family, Plane.CreateByOriginAndBasis(XYZ.Zero, frame.Width, frame.Height));
                var extrusionProfile = ResolvedProfileAtFrame(profile, XYZ.Zero, frame.Width, frame.Height, parameters, manager);
                var extrusion = family.FamilyCreate.NewExtrusion(!string.Equals(part.Value<string>("operation"), "void", StringComparison.Ordinal), extrusionProfile, plane, end - start); extrusion.StartOffset = start; extrusion.EndOffset = end;
                AssociateProfileParameters(manager, extrusion, profile, parameters, frame);
                if (part.Value<string>("depth_parameter") is string depthKey)
                {
                    var depth = RequireParameter(parameters, depthKey); var centered = Math.Abs(start + end) < 1e-8;
                    var startParameter = extrusion.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM); var endParameter = extrusion.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM);
                    if (centered)
                    {
                        var half = AddInternalLength(manager, "_" + part.Value<string>("key") + "_half_depth"); var negative = AddInternalLength(manager, "_" + part.Value<string>("key") + "_negative_half_depth");
                        manager.SetFormula(half, depth.Definition.Name + " / 2"); manager.SetFormula(negative, "-" + half.Definition.Name); Associate(manager, startParameter, negative); Associate(manager, endParameter, half);
                    }
                    else Associate(manager, endParameter, depth);
                }
                form = extrusion;
            }
            else form = CreateLiteralForm(family, part, profile, parameters, manager);
            var role = part.Value<string>("role") ?? part.Value<string>("key") ?? "Part";
            var visibility = part["visibility"] as JObject ?? new JObject(); SetRole(family, spec, form, part, role, visibility);
            var visibilityParameter = AssociateVisibility(manager, form, part, parameters, "part " + (part.Value<string>("key") ?? role));
            ApplyPartMaterial(manager, form, part, parameters, materials);
            parts.Add(part.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint part key is missing."), new PartResult { Key = part.Value<string>("key")!, Primitive = primitive, Operation = part.Value<string>("operation") ?? "solid", Form = form, Contract = part, VisibilityParameter = visibilityParameter });
        }
        buildStage = "create_coordination_zones";
        var coordinationZones = CreateCoordinationZones(family, spec, manager, parameters, materials);
        buildStage = "create_reference_graph";
        family.Regenerate();
        var referenceGraph = CreateReferenceGraph(family, spec, manager, parameters, parts, precreatedReferenceLines);
        family.Regenerate();
        var geometryOperations = ApplyGeometryOperations(family, parts);
        var nested = CreateNestedComponents(family, spec, manager, parameters, parts);
        var arrays = CreateArrays(family, spec, manager, parameters, parts, nested);
        var parameterOrder = ApplyParameterOrder(manager, spec, parameters);
        var mirrors = CreateMirrors(family, spec, parts, nested);
        var symbolicLines = CreateSymbolicLines(family, spec, manager, parameters);
        buildStage = "create_model_lines";
        var modelLines = CreateModelLines(family, spec, manager, parameters, referenceGraph);
        var detailLevelRepresentations = VerifyDetailLevelRepresentations(spec, parts, symbolicLines, modelLines);
        var detailLines = CreateDetailLines(family, spec, referenceGraph);
        var filledRegions = CreateFilledRegions(family, spec);
        var profileCurves = CreateProfileCurves(family, spec);
        var controls = CreateControls(family, spec);
        buildStage = "create_connectors";
        family.Regenerate();
        var connectors = CreateConnectors(family, spec, parts, parameters);
        buildStage = "parameter_flex";
        var flex = VerifyParameterFlex(family, manager, spec, parameters, parts, coordinationZones, connectors, arrays, nested, modelLines, detailLines);
        buildStage = "verify_parts";
        var readBack = VerifyParts(family, spec, parts, coordinationZones, connectors);
        buildStage = "commit";
        TransactionStatus commitStatus;
        try { commitStatus = transaction.Commit(); }
        catch (Exception exception)
        {
            throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit threw while committing Blueprint compilation. failure_diagnostic=" + failures.Summary + "; exception=" + exception.Message);
        }
        if (commitStatus != TransactionStatus.Committed) throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rejected Blueprint compilation. status=" + commitStatus + "; failure_diagnostic=" + failures.Summary);
        // Revit can reject FamilyLabel read-back while the authoring transaction
        // is still open even though the committed Family resolves the label.
        // Inspect/count only after a successful commit; Preview's enclosing
        // TransactionGroup still rolls that temporary commit back.
        buildStage = "verify_declared_parameters_post_commit";
        var parameterReadBack = VerifyCommittedParameterReadBack(spec, family);
        buildStage = "evaluate_complexity_post_commit";
        var complexity = EvaluateComplexityBudget(family, spec, null);
        return new JObject
        {
            ["family_name"] = familyName, ["type_name"] = manager.CurrentType.Name, ["family_kind"] = spec.FamilyKind,
            ["schema_version"] = "3.0", ["blueprint_hash"] = spec.Blueprint.Value<string>("blueprint_hash"), ["target_lod"] = spec.Lod,
            ["detail_profile"] = spec.DetailProfile, ["template_behavior"] = spec.TemplateBehavior, ["family_behavior"] = familyBehavior, ["family_type_set"] = typeSet, ["part_count"] = parts.Count,
            ["connector_count"] = connectors.Count, ["material_count"] = materials.Count, ["coordination_zone_count"] = coordinationZones.Count, ["array_count"] = arrays.Count, ["nested_component_count"] = nested.Count, ["symbolic_curve_count"] = symbolicLines.Count, ["model_curve_count"] = modelLines.Count, ["detail_curve_count"] = detailLines.Count, ["filled_region_count"] = filledRegions.Count, ["profile_curve_count"] = profileCurves.Count, ["control_count"] = controls.Count, ["lookup_tables"] = new JArray(lookupTables.Values.Select(item => item.Value<string>("name"))), ["types"] = new JArray(types.Select(item => item.Name)), ["identity_data"] = identityData, ["parameter_order"] = parameterOrder, ["parameter_read_back"] = parameterReadBack, ["type_catalog"] = TypeCatalogPreview(spec), ["parameter_flex"] = flex,
            ["materials"] = MaterialSnapshot(family, spec, appearanceAssets), ["presentation_subcategories"] = presentationSubcategories, ["light_source"] = lightSource, ["detail_level_representations"] = detailLevelRepresentations, ["coordination_zones"] = CoordinationZoneSnapshot(coordinationZones), ["electrical_load_classifications"] = ElectricalLoadClassificationSnapshot(electricalLoadClassifications), ["geometry_operations"] = geometryOperations, ["reference_graph"] = referenceGraph, ["arrays"] = ArraySnapshot(arrays), ["nested_components"] = NestedSnapshot(nested), ["mirrors"] = mirrors, ["symbolic_lines"] = symbolicLines, ["model_lines"] = modelLines, ["detail_lines"] = detailLines, ["filled_regions"] = filledRegions, ["profile_curves"] = profileCurves, ["controls"] = controls, ["complexity"] = complexity, ["component_verification"] = readBack, ["certification_boundary"] = "File-level Blueprint build/read-back proves declared presentation-subcategory styles, coarse/medium 2D and fine 3D visibility settings, coordination-zone and lighting-source data only; rendered output, hosting, circuit/network connection, measured regeneration performance and Project/BEP LOD350 coordination still require copied-Project acceptance."
        };
        }
        catch (CommandResultException) { throw; }
        catch (Exception exception)
        {
            throw new CommandResultException(ErrorCodes.TransactionFailed, "Blueprint compilation failed during " + buildStage + ". failure_diagnostic=" + failures.Summary + "; exception=" + exception.Message);
        }
    }

    private static JObject EvaluateComplexityBudget(Document family, FamilyBlueprintSpec spec, long? rfaBytes)
    {
        var inspected = FamilyData.Inspect(family);
        var observed = inspected["capability_evidence"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Family complexity inspection returned no capability evidence.");
        var declared = spec.Blueprint["complexity_assessment"]?["metrics"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint complexity metrics are missing.");
        var budget = spec.Blueprint["performance_budget"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint performance budget is missing.");
        var resolved = spec.Blueprint["resolved_complexity"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint resolved nested complexity is missing.");
        var metrics = new JObject
        {
            ["forms"] = observed.Value<int>("form_count"), ["void_forms"] = observed.Value<int>("void_form_count"),
            ["parameters"] = observed.Value<int>("parameter_count"), ["formula_parameters"] = observed.Value<int>("formula_parameter_count"),
            ["types"] = observed.Value<int>("family_type_count"), ["nested_instances"] = observed.Value<int>("nested_instance_count"),
            ["array_member_capacity"] = declared.Value<int>("array_member_capacity"), ["mirrored_copies"] = declared.Value<int>("mirrored_copies"),
            ["connectors"] = observed.Value<int>("connector_count"), ["reference_datums"] = Math.Max(declared.Value<int>("reference_datums"), observed.Value<int>("reference_plane_count")),
            ["dimensions"] = observed.Value<int>("dimension_count"), ["two_dimensional_curves"] = observed.Value<int>("two_dimensional_curve_count"),
            ["materials"] = observed.Value<int>("material_count"), ["presentation_subcategories"] = ManagedPresentationSubcategoryCount(inspected, spec), ["light_sources"] = observed.Value<int>("light_source_count"), ["lookup_rows"] = declared.Value<int>("lookup_rows"),
            ["document_elements"] = observed.Value<int>("document_element_count"), ["import_instances"] = observed.Value<int>("import_instance_count")
        };
        var actualScore = metrics.Value<int>("forms") * 10 + metrics.Value<int>("void_forms") * 5 + metrics.Value<int>("parameters") * 2 + metrics.Value<int>("formula_parameters") * 4 + metrics.Value<int>("types") * 3 + metrics.Value<int>("nested_instances") * 15 + metrics.Value<int>("array_member_capacity") * 4 + metrics.Value<int>("mirrored_copies") * 6 + metrics.Value<int>("connectors") * 8 + metrics.Value<int>("reference_datums") + metrics.Value<int>("dimensions") * 3 + metrics.Value<int>("two_dimensional_curves") + metrics.Value<int>("materials") * 2 + metrics.Value<int>("presentation_subcategories") + metrics.Value<int>("light_sources") * 20 + (int)Math.Ceiling(metrics.Value<int>("lookup_rows") / 10.0);
        metrics["complexity_score"] = actualScore;
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["forms"] = "max_forms", ["void_forms"] = "max_void_forms", ["parameters"] = "max_parameters", ["formula_parameters"] = "max_formula_parameters", ["types"] = "max_types", ["nested_instances"] = "max_nested_instances", ["array_member_capacity"] = "max_array_members", ["mirrored_copies"] = "max_mirrored_copies", ["connectors"] = "max_connectors", ["reference_datums"] = "max_reference_datums", ["dimensions"] = "max_dimensions", ["two_dimensional_curves"] = "max_2d_curves", ["materials"] = "max_materials", ["presentation_subcategories"] = "max_presentation_subcategories", ["light_sources"] = "max_light_sources", ["lookup_rows"] = "max_lookup_rows", ["complexity_score"] = "max_complexity_score"
        };
        var violations = new JArray();
        foreach (var pair in pairs)
        {
            var actual = metrics.Value<long>(pair.Key); var maximum = budget.Value<long>(pair.Value);
            if (actual > maximum) violations.Add(new JObject { ["metric"] = pair.Key, ["actual"] = actual, ["budget"] = pair.Value, ["maximum"] = maximum });
        }
        var nestedDepth = resolved.Value<int>("nested_depth");
        if (nestedDepth > budget.Value<int>("max_nested_depth")) violations.Add(new JObject { ["metric"] = "nested_depth", ["actual"] = nestedDepth, ["budget"] = "max_nested_depth", ["maximum"] = budget.Value<int>("max_nested_depth") });
        if (rfaBytes.HasValue && rfaBytes.Value > budget.Value<long>("max_rfa_bytes")) violations.Add(new JObject { ["metric"] = "rfa_bytes", ["actual"] = rfaBytes.Value, ["budget"] = "max_rfa_bytes", ["maximum"] = budget.Value<long>("max_rfa_bytes") });
        if (metrics.Value<int>("import_instances") != 0) violations.Add(new JObject { ["metric"] = "import_instances", ["actual"] = metrics.Value<int>("import_instances"), ["maximum"] = 0, ["reason"] = "Blueprint-built Family cannot hide imported CAD/geometry." });
        if (violations.Count != 0) throw new CommandResultException(ErrorCodes.VerificationFailed, "Family exceeds its declared performance budget: " + violations.ToString(Newtonsoft.Json.Formatting.None));
        return new JObject
        {
            ["within_budget"] = true, ["metrics"] = metrics, ["budget"] = budget.DeepClone(), ["violations"] = violations,
            ["rfa_bytes"] = rfaBytes.HasValue ? rfaBytes.Value : null, ["rfa_size_measured"] = rfaBytes.HasValue,
            ["nested_depth"] = nestedDepth, ["local_declared_complexity_score"] = declared.Value<int>("complexity_score"), ["aggregate_complexity_score"] = resolved.Value<int>("aggregate_complexity_score"),
            ["verification_boundary"] = "Counts and compact RFA bytes are measured. The score is deterministic triage; Project-scale load/regeneration time still requires a benchmark fixture."
        };
    }

    // Autodesk templates contain category subcategories of their own.  They are
    // outside the Blueprint contract and must neither consume nor inflate the
    // Family's presentation budget.  Count only the exact names declared or
    // created by this compiler for physical/2D presentation content.
    private static int ManagedPresentationSubcategoryCount(JObject inspected, FamilyBlueprintSpec spec)
    {
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var contract in (spec.Blueprint["presentation_subcategories"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var name = contract.Value<string>("name");
            if (!string.IsNullOrWhiteSpace(name)) expected.Add(name);
        }
        void AddFallbacks(string property, string prefix)
        {
            foreach (var declaration in (spec.Blueprint[property] as JArray ?? new JArray()).OfType<JObject>())
            {
                if (declaration.Value<string>("subcategory_key") != null) continue;
                var role = declaration.Value<string>("role") ?? declaration.Value<string>("key");
                if (!string.IsNullOrWhiteSpace(role)) expected.Add(prefix + role);
            }
        }
        AddFallbacks("parts", "DSCons " + spec.Lod.Replace("_", string.Empty) + " ");
        AddFallbacks("symbolic_lines", "DSCons Symbol ");
        AddFallbacks("model_lines", "DSCons Model Line ");
        AddFallbacks("detail_lines", "DSCons Detail ");
        AddFallbacks("profile_loops", "DSCons Profile ");
        foreach (var zone in (spec.Blueprint["coordination_zones"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var name = zone.Value<string>("subcategory");
            if (!string.IsNullOrWhiteSpace(name)) expected.Add(name);
        }
        var actual = (inspected["presentation_subcategories"] as JArray ?? new JArray()).OfType<JObject>()
            .Select(item => item.Value<string>("name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = expected.Where(name => !actual.Contains(name)).ToList();
        if (missing.Count != 0) throw new CommandResultException(ErrorCodes.VerificationFailed, "Family inspection is missing compiler-managed presentation subcategory: " + string.Join(", ", missing) + ".");
        return actual.Count(name => expected.Contains(name));
    }

    private static void VerifyReopenedComplexity(JObject? created, JObject reopened)
    {
        if (created == null || created.Value<bool?>("within_budget") != true || reopened.Value<bool?>("within_budget") != true)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Family complexity evidence is missing before or after reopen.");
        var before = created["metrics"] as JObject ?? new JObject(); var after = reopened["metrics"] as JObject ?? new JObject();
        foreach (var key in new[] { "forms", "void_forms", "parameters", "formula_parameters", "types", "nested_instances", "connectors", "reference_datums", "dimensions", "two_dimensional_curves", "materials", "presentation_subcategories", "light_sources", "import_instances" })
            if (before.Value<long>(key) != after.Value<long>(key)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Family complexity metric changed after reopen: " + key + ".");
    }

    private static void RequireSelectionBehavior(FamilyTemplateSelection selection, FamilyBlueprintSpec spec)
    {
        if (!string.Equals(selection.TemplateBehavior, spec.TemplateBehavior, StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.TemplateInvalid, "Resolved Family template behavior does not match the immutable Blueprint behavior.");
    }

    /// <summary>
    /// A behavior-first filename match is not evidence that Revit actually
    /// created the intended hosting Family.  Verify the native placement type
    /// while the Autodesk template is open, once more after an allowed Generic
    /// Model recategorization, and after the staged RFA is reopened.
    /// </summary>
    private static JObject VerifyRootFamilyPlacement(Document family, FamilyBlueprintSpec spec, string stage)
    {
        var expected = ExpectedRootFamilyPlacementType(spec.TemplateBehavior);
        var actual = family.OwnerFamily?.FamilyPlacementType
            ?? throw new CommandResultException(ErrorCodes.TemplateInvalid, "Family template did not create an OwnerFamily placement type.");
        if (expected.HasValue && actual != expected.Value)
            throw new CommandResultException(ErrorCodes.TemplateInvalid, "Autodesk template placement type " + actual + " does not satisfy Blueprint template_behavior " + spec.TemplateBehavior + "; expected " + expected.Value + ".");
        return new JObject
        {
            ["template_behavior"] = spec.TemplateBehavior,
            ["expected_family_placement_type"] = expected?.ToString(),
            ["actual_family_placement_type"] = actual.ToString(),
            ["stage"] = stage,
            ["verified"] = expected.HasValue,
            ["verification_boundary"] = "This verifies the Family document placement type only. Loading into a copied Project and testing host cut, placement, rotate or mirror remains a separate runtime gate."
        };
    }

    private static JObject VerifyReopenedRootFamilyPlacement(FamilyBlueprintSpec spec, JObject readBack)
    {
        var expected = ExpectedRootFamilyPlacementType(spec.TemplateBehavior);
        var hosting = readBack["hosting"] as JObject
            ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family has no hosting read-back.");
        var actual = hosting.Value<string>("family_placement_type")
            ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family has no FamilyPlacementType read-back.");
        if (expected.HasValue && !string.Equals(actual, expected.Value.ToString(), StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family placement type " + actual + " does not satisfy Blueprint template_behavior " + spec.TemplateBehavior + "; expected " + expected.Value + ".");
        return new JObject
        {
            ["template_behavior"] = spec.TemplateBehavior,
            ["expected_family_placement_type"] = expected?.ToString(),
            ["actual_family_placement_type"] = actual,
            ["stage"] = "reopened_rfa",
            ["verified"] = expected.HasValue,
            ["verification_boundary"] = "This verifies the saved Family document placement type only. Project host behavior remains a separate runtime gate."
        };
    }

    private static FamilyPlacementType? ExpectedRootFamilyPlacementType(string templateBehavior) => templateBehavior switch
    {
        "level_based" => FamilyPlacementType.OneLevelBased,
        "face_based" or "work_plane_based" => FamilyPlacementType.WorkPlaneBased,
        "wall_based" or "ceiling_based" or "floor_based" or "roof_based" => FamilyPlacementType.OneLevelBasedHosted,
        "line_based" => FamilyPlacementType.CurveBased,
        "two_level_based" => FamilyPlacementType.TwoLevelsBased,
        // Detail/annotation/profile templates are intentionally observed in
        // family_inspect but have no public behavior claim in this compiler.
        _ => null
    };

    private static JObject ApplyFamilyBehavior(Document family, FamilyBlueprintSpec spec)
    {
        var owner = family.OwnerFamily ?? throw new CommandResultException(ErrorCodes.TemplateInvalid, "Family document has no OwnerFamily for behavior settings.");
        var contract = spec.Blueprint["family"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "blueprint.family is missing.");
        void SetInteger(string property, BuiltInParameter builtIn, int value)
        {
            if (contract[property] == null) return;
            var parameter = owner.get_Parameter(builtIn);
            if (parameter == null || parameter.IsReadOnly || !parameter.Set(value)) throw new CommandResultException(ErrorCodes.Unsupported, "Family behavior " + property + " is unavailable or read-only in the selected template/category.");
            family.Regenerate();
            if (parameter.AsInteger() != value) throw new CommandResultException(ErrorCodes.VerificationFailed, "Family behavior " + property + " did not read back its requested value.");
        }
        void SetElementId(string property, BuiltInParameter builtIn, ElementId value)
        {
            if (contract[property] == null) return;
            var parameter = owner.get_Parameter(builtIn);
            if (parameter == null || parameter.IsReadOnly || value == ElementId.InvalidElementId || !parameter.Set(value))
                throw new CommandResultException(ErrorCodes.Unsupported, "Family behavior " + property + " is unavailable or read-only in the selected template/category.");
            family.Regenerate();
            if (parameter.AsElementId() != value) throw new CommandResultException(ErrorCodes.VerificationFailed, "Family behavior " + property + " did not read back its requested value.");
        }
        if (contract.Value<string>("part_type") is string partTypeKey)
        {
            var partType = ParsePartType(partTypeKey);
            SetInteger("part_type", BuiltInParameter.FAMILY_CONTENT_PART_TYPE, (int)partType);
        }
        SetInteger("shared", BuiltInParameter.FAMILY_SHARED, contract.Value<bool>("shared") ? 1 : 0);
        SetInteger("work_plane_based", BuiltInParameter.FAMILY_WORK_PLANE_BASED, contract.Value<bool>("work_plane_based") ? 1 : 0);
        SetInteger("always_vertical", BuiltInParameter.FAMILY_ALWAYS_VERTICAL, contract.Value<bool>("always_vertical") ? 1 : 0);
        SetInteger("cut_with_voids_when_loaded", BuiltInParameter.FAMILY_ALLOW_CUT_WITH_VOIDS, contract.Value<bool>("cut_with_voids_when_loaded") ? 1 : 0);
        SetInteger("maintain_annotation_orientation", BuiltInParameter.FAMILY_ELECTRICAL_MAINTAIN_ANNOTATION_ORIENTATION, contract.Value<bool>("maintain_annotation_orientation") ? 1 : 0);
        if (contract.Value<string>("round_connector_dimension") is string roundDimension)
        {
            var expectedStyle = roundDimension == "diameter" ? DimensionStyleType.Diameter : DimensionStyleType.Radial;
            var dimensionType = new FilteredElementCollector(family).OfClass(typeof(DimensionType)).Cast<DimensionType>()
                .Where(candidate => candidate.StyleType == expectedStyle)
                .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (dimensionType == null || dimensionType.Id == ElementId.InvalidElementId)
                throw new CommandResultException(ErrorCodes.TemplateInvalid, "The Autodesk template has no " + roundDimension + " DimensionType required for round connectors.");
            SetElementId("round_connector_dimension", BuiltInParameter.FAMILY_ROUNDCONNECTOR_DIMENSIONTYPE, dimensionType.Id);
        }
        return FamilyBehaviorSnapshot(family);
    }

    internal static JObject FamilyBehaviorSnapshot(Document family)
    {
        var owner = family.OwnerFamily;
        int? Integer(BuiltInParameter builtIn) => owner?.get_Parameter(builtIn)?.AsInteger();
        var partValue = Integer(BuiltInParameter.FAMILY_CONTENT_PART_TYPE);
        var roundDimensionType = owner?.get_Parameter(BuiltInParameter.FAMILY_ROUNDCONNECTOR_DIMENSIONTYPE)?.AsElementId();
        var roundDimensionStyle = roundDimensionType == null || roundDimensionType == ElementId.InvalidElementId
            ? (DimensionStyleType?)null
            : (family.GetElement(roundDimensionType) as DimensionType)?.StyleType;
        return new JObject
        {
            ["part_type"] = partValue.HasValue && Enum.IsDefined(typeof(PartType), partValue.Value) ? PartTypeKey((PartType)partValue.Value) : null,
            ["shared"] = BooleanToken(Integer(BuiltInParameter.FAMILY_SHARED)),
            ["work_plane_based"] = BooleanToken(Integer(BuiltInParameter.FAMILY_WORK_PLANE_BASED)),
            ["always_vertical"] = BooleanToken(Integer(BuiltInParameter.FAMILY_ALWAYS_VERTICAL)),
            ["cut_with_voids_when_loaded"] = BooleanToken(Integer(BuiltInParameter.FAMILY_ALLOW_CUT_WITH_VOIDS)),
            ["maintain_annotation_orientation"] = BooleanToken(Integer(BuiltInParameter.FAMILY_ELECTRICAL_MAINTAIN_ANNOTATION_ORIENTATION)),
            ["round_connector_dimension"] = roundDimensionStyle == DimensionStyleType.Diameter ? "diameter" : roundDimensionStyle == DimensionStyleType.Radial ? "radius" : null,
            ["verified_from_revit"] = family.IsFamilyDocument
        };
    }

    private static JToken BooleanToken(int? value) => value.HasValue ? JToken.FromObject(value.Value != 0) : JValue.CreateNull();
    private static PartType ParsePartType(string key)
    {
        var normalized = NormalizeEnumKey(key);
        foreach (PartType value in Enum.GetValues(typeof(PartType))) if (NormalizeEnumKey(value.ToString()) == normalized && value != PartType.Undefined) return value;
        throw new CommandResultException(ErrorCodes.InvalidParam, "Unsupported Family part_type: " + key + ".");
    }
    private static string PartTypeKey(PartType value)
    {
        var name = value.ToString(); var result = new System.Text.StringBuilder();
        for (var index = 0; index < name.Length; index++) { if (index > 0 && char.IsUpper(name[index]) && !char.IsUpper(name[index - 1])) result.Append('_'); result.Append(char.ToLowerInvariant(name[index])); }
        return result.ToString();
    }
    private static string NormalizeEnumKey(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static void RequireNestedArtifacts(FamilyBlueprintSpec spec, string demoDirectory)
    {
        void Require(JObject artifact, string label)
        {
            var path = artifact.Value<string>("family_path") ?? string.Empty; FamilyPlatformSafety.RequireInside(demoDirectory, path);
            if (!File.Exists(path)) throw new CommandResultException(ErrorCodes.SourceUnreadable, "Nested Blueprint artifact is missing for " + label + ".");
            if (!string.Equals(Hash(path), artifact.Value<string>("sha256"), StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested Blueprint artifact checksum changed for " + label + ".");
            if (string.IsNullOrWhiteSpace(artifact.Value<string>("blueprint_hash"))) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested Blueprint artifact hash is missing for " + label + ".");
        }
        foreach (var contract in (spec.Blueprint["nested_components"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var key = contract.Value<string>("key") ?? "nested_component";
            var options = (contract["resolved_options"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            if (options.Count > 0) foreach (var option in options) Require(option, key + " option " + option.Value<string>("option_key"));
            else Require(contract["resolved_artifact"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested component " + key + " has no resolved Blueprint artifact evidence."), key);
        }
    }

    private static IReadOnlyDictionary<string, string> RequirePhotometricAssets(FamilyBlueprintSpec spec, string demoDirectory)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lightSource = spec.Blueprint["light_source"] as JObject;
        if (lightSource?.Value<string>("distribution_style") != "photometric_web") return result;
        if (!Path.IsPathRooted(demoDirectory) || demoDirectory.StartsWith("\\\\", StringComparison.Ordinal) || !Directory.Exists(demoDirectory))
            throw new CommandResultException(ErrorCodes.PathBlocked, "approved_demo_directory must be an existing local absolute directory for photometric-web IES evidence.");
        var root = Path.GetFullPath(demoDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var setting in (lightSource["type_settings"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var web = setting["photometric_web"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Photometric-web type setting is missing its IES declaration.");
            var fileName = web.Value<string>("file_name") ?? string.Empty;
            if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) || !string.Equals(Path.GetExtension(fileName), ".ies", StringComparison.OrdinalIgnoreCase))
                throw new CommandResultException(ErrorCodes.PathBlocked, "Photometric web file must be an .ies basename inside approved_demo_directory.");
            var path = Path.GetFullPath(Path.Combine(root, fileName));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new CommandResultException(ErrorCodes.SourceUnreadable, "Photometric web file is missing inside approved_demo_directory: " + fileName + ".");
            var length = new FileInfo(path).Length;
            if (length < 16 || length > 5L * 1024 * 1024) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Photometric web file must be between 16 bytes and 5 MB: " + fileName + ".");
            if (!string.Equals(HashHex(path), web.Value<string>("sha256"), StringComparison.OrdinalIgnoreCase))
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Photometric web file checksum changed for " + fileName + ".");
            result[fileName] = path;
        }
        return result;
    }

    private static IReadOnlyDictionary<string, AppearanceImageFiles> RequireAppearanceAssets(FamilyBlueprintSpec spec, string demoDirectory)
    {
        var result = new Dictionary<string, AppearanceImageFiles>(StringComparer.Ordinal);
        foreach (var material in (spec.Blueprint["materials"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var appearance = material["appearance"] as JObject;
            if (appearance == null || (appearance["texture"] == null && appearance["bump"] == null)) continue;
            if (material.Value<bool?>("use_render_appearance_for_shading") != true)
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Material texture/bump requires use_render_appearance_for_shading=true.");
            var key = material.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint material key is missing.");
            var files = new AppearanceImageFiles();
            if (appearance["texture"] is JObject texture) files.TexturePath = RequireAppearanceImage(texture, demoDirectory, "texture", key);
            if (appearance["bump"] is JObject bump)
            {
                files.BumpPath = RequireAppearanceImage(bump, demoDirectory, "bump", key);
                var amount = bump.Value<double?>("amount");
                if (!amount.HasValue || amount.Value < -1000 || amount.Value > 1000) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Material " + key + " bump amount must be between -1000 and 1000.");
                files.BumpAmount = amount.Value;
            }
            result.Add(key, files);
        }
        return result;
    }

    private static string RequireAppearanceImage(JObject contract, string demoDirectory, string role, string materialKey)
    {
        if (!Path.IsPathRooted(demoDirectory) || demoDirectory.StartsWith("\\\\", StringComparison.Ordinal) || !Directory.Exists(demoDirectory))
            throw new CommandResultException(ErrorCodes.PathBlocked, "approved_demo_directory must be an existing local absolute directory for material texture/bump evidence.");
        var fileName = contract.Value<string>("file_name") ?? string.Empty; var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) || extension is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif" or ".tiff"))
            throw new CommandResultException(ErrorCodes.PathBlocked, "Material " + materialKey + " " + role + " must be an approved image basename inside approved_demo_directory.");
        var root = Path.GetFullPath(demoDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, fileName));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new CommandResultException(ErrorCodes.SourceUnreadable, "Material " + materialKey + " " + role + " image is missing inside approved_demo_directory: " + fileName + ".");
        var length = new FileInfo(path).Length;
        if (length < 16 || length > 20L * 1024 * 1024) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Material " + materialKey + " " + role + " image must be between 16 bytes and 20 MB: " + fileName + ".");
        if (!string.Equals(HashHex(path), contract.Value<string>("sha256"), StringComparison.OrdinalIgnoreCase))
            throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Material " + materialKey + " " + role + " image checksum changed for " + fileName + ".");
        if (!RecognizedAppearanceImage(path, extension)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Material " + materialKey + " " + role + " image does not match its declared approved image format: " + fileName + ".");
        return path;
    }

    private static bool RecognizedAppearanceImage(string path, string extension)
    {
        var bytes = File.ReadAllBytes(path);
        var png = bytes.Length >= 8 && bytes.Take(8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a });
        var jpeg = bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff;
        var bmp = bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4d;
        var tiff = bytes.Length >= 4 && ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2a && bytes[3] == 0x00) || (bytes[0] == 0x4d && bytes[1] == 0x4d && bytes[2] == 0x00 && bytes[3] == 0x2a));
        return extension == ".png" ? png : extension is ".jpg" or ".jpeg" ? jpeg : extension == ".bmp" ? bmp : tiff;
    }

    private static JArray PhotometricDependencySnapshot(FamilyBlueprintSpec spec, IReadOnlyDictionary<string, string> assets)
    {
        var result = new JArray(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var setting in (spec.Blueprint["light_source"]?["type_settings"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var web = setting["photometric_web"] as JObject; var fileName = web?.Value<string>("file_name");
            if (fileName == null || !assets.TryGetValue(fileName, out var path) || !seen.Add(fileName)) continue;
            result.Add(new JObject { ["kind"] = "ies_photometric_web", ["file_name"] = fileName, ["sha256"] = HashHex(path), ["bytes"] = new FileInfo(path).Length, ["portable_requirement"] = "Keep this IES file beside the RFA or relink it from an approved manufacturer source." });
        }
        return result;
    }

    private static JArray ExternalDependencySnapshot(FamilyBlueprintSpec spec, IReadOnlyDictionary<string, string> photometricAssets, IReadOnlyDictionary<string, AppearanceImageFiles> appearanceAssets)
    {
        var result = PhotometricDependencySnapshot(spec, photometricAssets);
        foreach (var material in (spec.Blueprint["materials"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var key = material.Value<string>("key") ?? string.Empty; var appearance = material["appearance"] as JObject;
            if (appearance == null || !appearanceAssets.TryGetValue(key, out var files)) continue;
            void Add(string role, JObject? contract, string? source)
            {
                if (contract == null || source == null) return;
                result.Add(new JObject { ["kind"] = role == "texture" ? "appearance_texture" : "appearance_bump", ["material_key"] = key, ["material_name"] = material.Value<string>("name"), ["role"] = role, ["file_name"] = contract.Value<string>("file_name"), ["sha256"] = HashHex(source), ["bytes"] = new FileInfo(source).Length, ["portable_requirement"] = "Keep this approved image beside the RFA or relink it from the approved material source." });
            }
            Add("texture", appearance["texture"] as JObject, files.TexturePath); Add("bump", appearance["bump"] as JObject, files.BumpPath);
        }
        return result;
    }

    private static void VerifyReopenedFamilyBehavior(FamilyBlueprintSpec spec, JObject readBack)
    {
        var requested = spec.Blueprint["family"] as JObject ?? new JObject();
        var observed = readBack["family_behavior"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family has no behavior read-back.");
        foreach (var property in new[] { "part_type", "shared", "work_plane_based", "always_vertical", "cut_with_voids_when_loaded", "maintain_annotation_orientation", "round_connector_dimension" })
            if (requested[property] != null && !JToken.DeepEquals(requested[property], observed[property])) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family behavior mismatch for " + property + ".");
    }

    private static void VerifyReopenedMaterials(FamilyBlueprintSpec spec, JObject readBack)
    {
        var declaredMaterials = (spec.Blueprint["materials"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        // Do not index unrelated Autodesk-template materials.  Revit can
        // surface duplicate display names there, while this Blueprint makes no
        // material claim at all.  Declared materials still go through the full
        // reopen verification below.
        if (declaredMaterials.Count == 0) return;
        var observed = (readBack["materials"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("name") ?? string.Empty, StringComparer.Ordinal);
        foreach (var material in declaredMaterials)
        {
            var name = material.Value<string>("name") ?? string.Empty;
            if (!observed.TryGetValue(name, out var actual)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family is missing declared material " + name + ".");
            var expectedColor = material["color_rgb"] as JObject ?? new JObject(); var actualColor = actual["color_rgb"] as JObject ?? new JObject();
            var useRenderAppearance = material.Value<bool?>("use_render_appearance_for_shading") == true;
            if (actual.Value<bool?>("use_render_appearance_for_shading") != useRenderAppearance)
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family material render-appearance setting mismatch for " + name + ".");
            // Revit derives Material.Color from the Appearance Asset whenever
            // UseRenderAppearanceForShading is enabled. In that mode the base
            // Material.Color is not a persisted color contract; verify the
            // declared Appearance values below instead.
            if ((!useRenderAppearance && !JToken.DeepEquals(expectedColor, actualColor)) || material.Value<int?>("transparency") is int transparency && actual.Value<int?>("transparency") != transparency)
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family material graphics mismatch for " + name + ". expected_color=" + expectedColor.ToString(Newtonsoft.Json.Formatting.None) + "; actual_color=" + actualColor.ToString(Newtonsoft.Json.Formatting.None) + "; expected_transparency=" + (material.Value<int?>("transparency")?.ToString() ?? "not_declared") + "; actual_transparency=" + (actual.Value<int?>("transparency")?.ToString() ?? "null") + ".");
            if (material["appearance"] != null && !(actual.Value<bool?>("has_appearance_asset") ?? false)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family material appearance asset is missing for " + name + ".");
            if (material["appearance"] is JObject expectedAppearance)
            {
                var actualAppearance = actual["appearance"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family material appearance data is missing for " + name + ".");
                var expectedAppearanceColor = expectedAppearance["color_rgb"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Material appearance color is missing for " + name + ".");
                var actualAppearanceColor = actualAppearance["color_rgb"] as JObject ?? new JObject();
                if (!JToken.DeepEquals(expectedAppearanceColor, actualAppearanceColor))
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family material appearance color mismatch for " + name + ". expected=" + expectedAppearanceColor.ToString(Newtonsoft.Json.Formatting.None) + "; actual=" + actualAppearanceColor.ToString(Newtonsoft.Json.Formatting.None) + ".");
                foreach (var property in new[] { "transparency", "glossiness" })
                {
                    if (expectedAppearance[property] == null) continue;
                    var requested = expectedAppearance.Value<double>(property); var observedValue = actualAppearance.Value<double?>(property); var tolerance = Math.Max(1e-6, Math.Abs(requested) * 1e-6);
                    if (!observedValue.HasValue || Math.Abs(requested - observedValue.Value) > tolerance)
                        throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family material appearance " + property + " mismatch for " + name + ". expected=" + requested.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "; actual=" + (observedValue?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "null") + ".");
                }
                if (expectedAppearance.Value<bool?>("is_metal") is bool isMetal && actualAppearance.Value<bool?>("is_metal") != isMetal)
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family material appearance is_metal mismatch for " + name + ".");
                void VerifyImage(string role)
                {
                    if (expectedAppearance[role] is not JObject expectedImage) return;
                    var observedImage = actualAppearance[role] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family material " + name + " is missing appearance " + role + " mapping.");
                    foreach (var property in new[] { "file_name", "sha256", "size_bytes", "external_path_redacted" })
                    {
                        if (observedImage[property] == null) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family material " + name + " has incomplete appearance " + role + " evidence.");
                    }
                    if (!string.Equals(expectedImage.Value<string>("file_name"), observedImage.Value<string>("file_name"), StringComparison.Ordinal) || !string.Equals(expectedImage.Value<string>("sha256"), observedImage.Value<string>("sha256"), StringComparison.OrdinalIgnoreCase) || observedImage.Value<bool?>("external_path_redacted") != true)
                        throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family material " + name + " appearance " + role + " evidence mismatch.");
                }
                VerifyImage("texture"); VerifyImage("bump");
                if (expectedAppearance["bump"] is JObject expectedBump)
                {
                    var observedAmount = actualAppearance.Value<double?>("bump_amount"); var requestedAmount = expectedBump.Value<double>("amount");
                    if (!observedAmount.HasValue || Math.Abs(observedAmount.Value - requestedAmount) > 1e-6) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family material " + name + " bump amount mismatch.");
                }
            }
            VerifyAsset(material["physical_asset"] as JObject, actual["physical_asset"] as JObject, name, "Physical", new[] { "density_kg_per_m3", "young_modulus_mpa", "shear_modulus_mpa", "poisson_ratio" }, new[] { "asset_class", "behavior", "name" });
            VerifyAsset(material["thermal_asset"] as JObject, actual["thermal_asset"] as JObject, name, "Thermal", new[] { "density_kg_per_m3", "thermal_conductivity_w_per_mk", "specific_heat_j_per_kgk", "emissivity", "porosity", "reflectivity" }, new[] { "material_type", "behavior", "name", "transmits_light" });
        }

        static void VerifyAsset(JObject? expected, JObject? actual, string materialName, string label, IEnumerable<string> numericProperties, IEnumerable<string> exactProperties)
        {
            if (expected == null) return;
            if (actual == null) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family material " + materialName + " is missing its " + label + " Asset.");
            foreach (var property in exactProperties)
            {
                if (expected[property] == null) continue;
                if (!JToken.DeepEquals(expected[property], actual[property])) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family material " + materialName + " " + label + " Asset mismatch for " + property + ".");
            }
            foreach (var property in numericProperties)
            {
                if (expected[property] == null) continue;
                var requested = expected.Value<double>(property); var observed = actual.Value<double?>(property);
                var tolerance = Math.Max(1e-6, Math.Abs(requested) * 1e-6);
                if (!observed.HasValue || Math.Abs(requested - observed.Value) > tolerance) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family material " + materialName + " " + label + " Asset numeric mismatch for " + property + ".");
            }
        }
    }

    private static void VerifyReopenedPartMaterials(FamilyBlueprintSpec spec, JObject readBack)
    {
        var materialBearingParts = (spec.Blueprint["parts"] as JArray ?? new JArray()).OfType<JObject>()
            .Where(item => item.Value<string>("material_key") != null || item.Value<string>("material_parameter") != null).ToList();
        // As above, skip only when no declared part refers to a material. This
        // preserves strict checks for every fixed or parameterized assignment.
        if (materialBearingParts.Count == 0) return;
        var materialNames = (spec.Blueprint["materials"] as JArray ?? new JArray()).OfType<JObject>()
            .ToDictionary(item => item.Value<string>("key") ?? string.Empty, item => item.Value<string>("name") ?? string.Empty, StringComparer.Ordinal);
        var materialIds = (readBack["materials"] as JArray ?? new JArray()).OfType<JObject>()
            .ToDictionary(item => item.Value<string>("name") ?? string.Empty, item => item.Value<long?>("id"), StringComparer.Ordinal);
        var parameterNames = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>()
            .ToDictionary(item => item.Value<string>("key") ?? string.Empty, item => item.Value<string>("name") ?? string.Empty, StringComparer.Ordinal);
        var forms = (readBack["forms"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        foreach (var part in materialBearingParts)
        {
            var materialKey = part.Value<string>("material_key"); var materialParameterKey = part.Value<string>("material_parameter");
            if (materialKey == null && materialParameterKey == null) continue;
            var partKey = part.Value<string>("key") ?? "Part";
            var role = PartPresentationSubcategoryName(spec, part, partKey);
            var form = forms.SingleOrDefault(item => string.Equals(item.Value<string>("role_or_subcategory"), role, StringComparison.Ordinal))
                ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family is missing material-bearing part " + partKey + ".");
            if (materialKey != null)
            {
                if (!materialNames.TryGetValue(materialKey, out var materialName) || !materialIds.TryGetValue(materialName, out var materialId) || !materialId.HasValue || form.Value<long?>("material_id") != materialId.Value || form.Value<bool?>("material_associated") == true)
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family fixed material assignment mismatch for part " + partKey + ".");
            }
            else
            {
                if (!parameterNames.TryGetValue(materialParameterKey!, out var parameterName)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Part material parameter is missing from the Blueprint: " + materialParameterKey + ".");
                var associated = form["material_associated_parameter"] as JObject;
                if (form.Value<bool?>("material_associated") != true || !string.Equals(associated?.Value<string>("name"), parameterName, StringComparison.Ordinal))
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family material parameter association mismatch for part " + partKey + ".");
            }
        }
    }

    private static void VerifyReopenedPresentationSubcategories(FamilyBlueprintSpec spec, JObject readBack)
    {
        var contracts = (spec.Blueprint["presentation_subcategories"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        if (contracts.Count == 0) return;
        // Autodesk templates can retain unrelated subcategories whose display
        // names repeat. Reopen verification must only index Blueprint-declared
        // names, but must still reject ambiguity for one of those declarations.
        var declaredNames = new HashSet<string>(contracts.Select(item => item.Value<string>("name") ?? string.Empty), StringComparer.OrdinalIgnoreCase);
        var observed = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in (readBack["presentation_subcategories"] as JArray ?? new JArray()).OfType<JObject>()
            .Where(item => declaredNames.Contains(item.Value<string>("name") ?? string.Empty)))
        {
            var name = item.Value<string>("name") ?? string.Empty;
            if (observed.ContainsKey(name))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family contains duplicate declared presentation subcategory " + name + ".");
            observed.Add(name, item);
        }
        foreach (var contract in contracts)
        {
            var key = contract.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Presentation subcategory key is missing during reopen verification.");
            var name = contract.Value<string>("name") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Presentation subcategory name is missing during reopen verification.");
            if (!observed.TryGetValue(name, out var actual)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family is missing presentation subcategory " + key + ".");
            VerifyPresentationSubcategorySnapshot(contract, actual, "Reopened presentation subcategory " + key);
        }
    }

    private static void RequireDetailLevelRepresentationVisibility(JObject? visibility, bool coarse, bool medium, bool fine, string label)
    {
        if (visibility == null || visibility.Value<bool?>("coarse") != coarse || visibility.Value<bool?>("medium") != medium || visibility.Value<bool?>("fine") != fine)
            throw new CommandResultException(ErrorCodes.VerificationFailed, label + " does not match the declared Coarse/Medium/Fine detail-level representation policy.");
    }

    private static JArray VerifyDetailLevelRepresentations(FamilyBlueprintSpec spec, IReadOnlyDictionary<string, PartResult> parts, JArray symbolicLines, JArray modelLines)
    {
        var contracts = (spec.Blueprint["detail_level_representations"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var result = new JArray();
        foreach (var contract in contracts)
        {
            var key = contract.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail-level representation key is missing.");
            var physicalParts = new JArray();
            foreach (var declaredPartKey in (contract["physical_part_keys"] as JArray ?? new JArray()).Values<string>())
            {
                if (string.IsNullOrWhiteSpace(declaredPartKey)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail-level representation " + key + " has an empty physical part key.");
                var partKey = declaredPartKey;
                if (!parts.TryGetValue(partKey, out var part)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail-level representation " + key + " references an unknown physical part " + partKey + ".");
                if (!part.Form.IsSolid) throw new CommandResultException(ErrorCodes.VerificationFailed, "Detail-level representation " + key + " physical part " + partKey + " is not solid.");
                var visibility = VisibilitySnapshot(part.Form.GetVisibility());
                RequireDetailLevelRepresentationVisibility(visibility, false, false, true, "Detail-level representation " + key + " physical part " + partKey);
                physicalParts.Add(new JObject { ["part_key"] = partKey, ["form_id"] = part.Form.Id.Val(), ["visibility"] = visibility, ["verified"] = true });
            }

            JArray Lines(string kind, JArray createdLines, string declarationProperty)
            {
                var resultLines = new JArray();
                foreach (var lineKey in (contract[declarationProperty] as JArray ?? new JArray()).Values<string>())
                {
                    var lineContract = (spec.Blueprint[kind + "_lines"] as JArray ?? new JArray()).OfType<JObject>().SingleOrDefault(item => string.Equals(item.Value<string>("key"), lineKey, StringComparison.Ordinal))
                        ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail-level representation " + key + " references unknown " + kind + " line " + lineKey + ".");
                    var expectedSegments = (lineContract["points_mm"] as JArray ?? new JArray()).Count - 1;
                    var created = createdLines.OfType<JObject>().Where(item => string.Equals(item.Value<string>("declaration_key"), lineKey, StringComparison.Ordinal)).ToList();
                    if (created.Count != expectedSegments) throw new CommandResultException(ErrorCodes.VerificationFailed, "Detail-level representation " + key + " " + kind + " line " + lineKey + " segment count did not read back.");
                    foreach (var curve in created) RequireDetailLevelRepresentationVisibility(curve["visibility"] as JObject, true, true, false, "Detail-level representation " + key + " " + kind + " line " + lineKey);
                    resultLines.Add(new JObject { ["line_key"] = lineKey, ["curve_ids"] = new JArray(created.Select(item => item.Value<long>("curve_id"))), ["visibility"] = new JObject { ["coarse"] = true, ["medium"] = true, ["fine"] = false }, ["verified"] = true });
                }
                return resultLines;
            }

            result.Add(new JObject
            {
                ["key"] = key, ["policy"] = contract.Value<string>("policy"),
                ["physical_parts"] = physicalParts,
                ["symbolic_lines"] = Lines("symbolic", symbolicLines, "symbolic_line_keys"),
                ["model_lines"] = Lines("model", modelLines, "model_line_keys"),
                ["verified"] = true
            });
        }
        return result;
    }

    private static void VerifyReopenedDetailLevelRepresentations(FamilyBlueprintSpec spec, JObject readBack)
    {
        var contracts = (spec.Blueprint["detail_level_representations"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        if (contracts.Count == 0) return;
        var forms = (readBack["forms"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var symbolicCurves = (readBack["symbolic_curves"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var modelCurves = (readBack["model_curves"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        foreach (var contract in contracts)
        {
            var key = contract.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail-level representation key is missing after reopen.");
            foreach (var declaredPartKey in (contract["physical_part_keys"] as JArray ?? new JArray()).Values<string>())
            {
                var partKey = declaredPartKey ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail-level representation " + key + " has an empty physical part key after reopen.");
                var part = (spec.Blueprint["parts"] as JArray ?? new JArray()).OfType<JObject>().SingleOrDefault(item => string.Equals(item.Value<string>("key"), partKey, StringComparison.Ordinal))
                    ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail-level representation " + key + " references unknown physical part " + partKey + " after reopen.");
                var role = PartPresentationSubcategoryName(spec, part, partKey);
                var form = forms.SingleOrDefault(item => string.Equals(item.Value<string>("role_or_subcategory"), role, StringComparison.Ordinal))
                    ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family is missing detail-level physical part " + partKey + ".");
                RequireDetailLevelRepresentationVisibility(form["visibility"] as JObject, false, false, true, "Reopened detail-level representation " + key + " physical part " + partKey);
            }

            void VerifyLines(string kind, List<JObject> curves, string declarationProperty, string stylePrefix)
            {
                foreach (var lineKey in (contract[declarationProperty] as JArray ?? new JArray()).Values<string>())
                {
                    var lineContract = (spec.Blueprint[kind + "_lines"] as JArray ?? new JArray()).OfType<JObject>().SingleOrDefault(item => string.Equals(item.Value<string>("key"), lineKey, StringComparison.Ordinal))
                        ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail-level representation " + key + " references unknown " + kind + " line " + lineKey + " after reopen.");
                    var expectedStyle = PresentationSubcategoryName(spec, lineContract, stylePrefix + (lineContract.Value<string>("role") ?? lineKey));
                    var expectedSegments = (lineContract["points_mm"] as JArray ?? new JArray()).Count - 1;
                    var observed = curves.Where(item => string.Equals(item.Value<string>("line_style"), expectedStyle, StringComparison.Ordinal)).ToList();
                    if (observed.Count != expectedSegments) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened detail-level representation " + key + " " + kind + " line " + lineKey + " segment/style count mismatch.");
                    var expectedPoints = (lineContract["points_mm"] as JArray ?? new JArray()).Select((point, index) => Point(point, "detail-level representation " + key + " " + kind + " line " + lineKey + " point " + index)).ToList();
                    var unmatchedSegments = expectedPoints.Zip(expectedPoints.Skip(1), (start, end) => (Start: start, End: end)).ToList();
                    foreach (var curve in observed)
                    {
                        RequireDetailLevelRepresentationVisibility(curve["visibility"] as JObject, true, true, false, "Reopened detail-level representation " + key + " " + kind + " line " + lineKey);
                        var geometry = curve["curve_geometry"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened detail-level representation " + key + " " + kind + " line " + lineKey + " has no curve geometry.");
                        if (!string.Equals(geometry.Value<string>("kind"), "line", StringComparison.Ordinal))
                            throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened detail-level representation " + key + " " + kind + " line " + lineKey + " is no longer a line segment.");
                        var actualStart = PointFromMm(geometry["start_mm"] as JObject, "reopened detail-level line start");
                        var actualEnd = PointFromMm(geometry["end_mm"] as JObject, "reopened detail-level line end");
                        var tolerance = Mm(.01);
                        var match = unmatchedSegments.FindIndex(segment =>
                            (actualStart.DistanceTo(segment.Start) <= tolerance && actualEnd.DistanceTo(segment.End) <= tolerance) ||
                            (actualStart.DistanceTo(segment.End) <= tolerance && actualEnd.DistanceTo(segment.Start) <= tolerance));
                        if (match < 0) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened detail-level representation " + key + " " + kind + " line " + lineKey + " changed segment geometry.");
                        unmatchedSegments.RemoveAt(match);
                    }
                    if (unmatchedSegments.Count != 0) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened detail-level representation " + key + " " + kind + " line " + lineKey + " is missing declared segment geometry.");
                }
            }

            VerifyLines("symbolic", symbolicCurves, "symbolic_line_keys", "DSCons Symbol ");
            VerifyLines("model", modelCurves, "model_line_keys", "DSCons Model ");
        }
    }

    private static void VerifyReopenedCoordinationZones(FamilyBlueprintSpec spec, JObject? created, JObject readBack)
    {
        var contracts = (spec.Blueprint["coordination_zones"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        if (contracts.Count == 0) return;
        var expected = created ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Created coordination-zone evidence is missing.");
        var forms = (readBack["forms"] as JArray ?? new JArray()).OfType<JObject>()
            .Where(item => (item.Value<string>("role_or_subcategory") ?? string.Empty).StartsWith("DSCons Coordination ", StringComparison.Ordinal)).ToList();
        if (forms.Count != contracts.Count || expected.Count != contracts.Count) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened coordination-zone count does not match the Blueprint declaration.");
        var unmatchedForms = forms.ToList();
        var materials = (readBack["materials"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("name") ?? string.Empty, item => item.Value<long>("id"), StringComparer.Ordinal);
        var materialContracts = (spec.Blueprint["materials"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("key") ?? string.Empty, item => item.Value<string>("name") ?? string.Empty, StringComparer.Ordinal);
        var parameterNames = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("key") ?? string.Empty, item => item.Value<string>("name") ?? string.Empty, StringComparer.Ordinal);
        bool BoundsMatch(JObject expectedBounds, JObject observedBounds)
        {
            var observedMin = observedBounds["min"] as JObject ?? new JObject(); var observedMax = observedBounds["max"] as JObject ?? new JObject();
            var pairs = new[] { ("min_x", observedMin.Value<double>("x")), ("min_y", observedMin.Value<double>("y")), ("min_z", observedMin.Value<double>("z")), ("max_x", observedMax.Value<double>("x")), ("max_y", observedMax.Value<double>("y")), ("max_z", observedMax.Value<double>("z")) };
            return pairs.All(pair => Math.Abs(expectedBounds.Value<double>(pair.Item1) - pair.Item2) <= .01);
        }
        foreach (var contract in contracts)
        {
            var key = contract.Value<string>("key") ?? string.Empty; var subcategory = contract.Value<string>("subcategory") ?? string.Empty;
            var expectedZone = expected[key] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Created coordination-zone snapshot is missing " + key + ".");
            var expectedBounds = expectedZone["bounds_mm"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Created coordination-zone bounds are missing for " + key + ".");
            var form = unmatchedForms.FirstOrDefault(item => string.Equals(item.Value<string>("role_or_subcategory"), subcategory, StringComparison.Ordinal) && item["bounds_mm"] is JObject bounds && BoundsMatch(expectedBounds, bounds))
                ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family is missing coordination zone " + key + " with matching subcategory/bounds.");
            unmatchedForms.Remove(form);
            if (form.Value<bool?>("is_solid") != true) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened coordination zone " + key + " is not a solid coordination form.");
            if (!string.Equals(form.Value<string>("coordination_purpose"), contract.Value<string>("purpose"), StringComparison.Ordinal) || form.Value<bool?>("quantity_exclusion_certified") != false)
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened coordination zone semantic read-back mismatch for " + key + ".");
            if (!JToken.DeepEquals(form["visibility"], expectedZone["visibility"]))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened coordination zone visibility/bounds mismatch for " + key + ".");
            var materialKey = contract.Value<string>("material_key") ?? string.Empty;
            if (!materialContracts.TryGetValue(materialKey, out var materialName) || !materials.TryGetValue(materialName, out var materialId) || form.Value<long?>("material_id") != materialId)
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened coordination zone material mismatch for " + key + ".");
            if (contract.Value<string>("visibility_parameter") is string visibilityKey)
            {
                var observedBinding = form["visibility_parameter"] as JObject;
                if (!parameterNames.TryGetValue(visibilityKey, out var parameterName) || !string.Equals(observedBinding?.Value<string>("name"), parameterName, StringComparison.Ordinal))
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened coordination zone lost its visibility-parameter binding for " + key + ".");
            }
        }
    }

    private static void VerifyReopenedArcSweeps(FamilyBlueprintSpec spec, JObject readBack)
    {
        var arcParts = (spec.Blueprint["parts"] as JArray ?? new JArray()).OfType<JObject>()
            .Where(part => part.Value<string>("primitive") == "sweep" && part["path"]?.Value<string>("kind") == "arc").ToList();
        if (arcParts.Count == 0) return;
        var reopenedForms = (readBack["forms"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var reopenedConnectors = (readBack["connectors"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        foreach (var part in arcParts)
        {
            var role = PartPresentationSubcategoryName(spec, part, part.Value<string>("key") ?? "Part");
            var form = reopenedForms.SingleOrDefault(item => item.Value<string>("class") == nameof(Sweep) && string.Equals(item.Value<string>("role_or_subcategory"), role, StringComparison.Ordinal))
                ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family is missing declared arc sweep " + (part.Value<string>("key") ?? role) + ".");
            var observedPath = form["path"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened arc sweep has no path read-back for " + role + ".");
            var path = part["path"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Arc sweep path declaration is missing.");
            var expectedRadius = path.Value<double?>("radius_mm");
            if (!expectedRadius.HasValue && path.Value<string>("radius_parameter") is string radiusKey)
                expectedRadius = (spec.Blueprint["verification"]?["parameter_flex_cases"] as JArray ?? new JArray()).OfType<JObject>().Single(item => item.Value<string>("parameter_key") == radiusKey).Value<double>("nominal");
            if (!expectedRadius.HasValue || Math.Abs((observedPath.Value<double?>("radius_mm") ?? double.NaN) - expectedRadius.Value) > 1e-3)
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened arc sweep radius does not match the approved nominal value for " + role + ".");
            var declaredIntersection = Point(path["tangent_intersection_mm"], "arc tangent_intersection_mm");
            var observedIntersection = PointFromMm(observedPath["tangent_intersection_mm"] as JObject, "reopened arc tangent intersection");
            if (declaredIntersection.DistanceTo(observedIntersection) > Mm(.5)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened arc sweep connector tangents no longer intersect at the declared Family origin for " + role + ".");
            if (path.Value<string>("radius_parameter") is string parameterKey)
            {
                var parameterName = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>().Single(item => item.Value<string>("key") == parameterKey).Value<string>("name");
                if (!(readBack["dimensions"] as JArray ?? new JArray()).OfType<JObject>().Any(item => item.Value<string>("family_label") == parameterName))
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened arc sweep lost the bend-radius dimension label for " + role + ".");
            }
            var start = PointFromMm(observedPath["start_mm"] as JObject, "reopened arc start"); var end = PointFromMm(observedPath["end_mm"] as JObject, "reopened arc end");
            var startTangent = VectorFromJson(observedPath["start_tangent"] as JObject, "reopened arc start tangent"); var endTangent = VectorFromJson(observedPath["end_tangent"] as JObject, "reopened arc end tangent");
            foreach (var connectorContract in (spec.Blueprint["connectors"] as JArray ?? new JArray()).OfType<JObject>().Where(item => item.Value<string>("host_part") == part.Value<string>("key") && item.Value<string>("host_face") is "path_start" or "path_end"))
            {
                var connectorRole = connectorContract.Value<string>("role") ?? connectorContract.Value<string>("key") ?? string.Empty;
                var connector = reopenedConnectors.SingleOrDefault(item => string.Equals(item.Value<string>("role"), connectorRole, StringComparison.Ordinal))
                    ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family is missing arc-path connector " + connectorRole + ".");
                var isStart = connectorContract.Value<string>("host_face") == "path_start"; var expectedOrigin = isStart ? start : end; var expectedNormal = isStart ? startTangent.Negate() : endTangent;
                var actualOrigin = PointFromMm(connector["origin_mm"] as JObject, "reopened connector origin"); var actualNormal = VectorFromJson(connector["normal"] as JObject, "reopened connector normal");
                if (actualOrigin.DistanceTo(expectedOrigin) > Mm(.5) || actualNormal.Normalize().DotProduct(expectedNormal.Normalize()) < .999)
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened arc-path connector origin/orientation mismatch for " + connectorRole + ".");
            }
        }
    }

    private static void VerifyReopenedAngularSweeps(FamilyBlueprintSpec spec, JObject readBack)
    {
        var angularParts = (spec.Blueprint["parts"] as JArray ?? new JArray()).OfType<JObject>()
            .Where(part => part.Value<string>("primitive") == "sweep" && part["path"]?.Value<string>("kind") == "reference_line").ToList();
        if (angularParts.Count == 0) return;
        var reopenedForms = (readBack["forms"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var reopenedDimensions = (readBack["dimensions"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var dimensions = (spec.Blueprint["dimensions"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("key") ?? string.Empty, StringComparer.Ordinal);
        var referenceLines = (spec.Blueprint["reference_lines"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("key") ?? string.Empty, StringComparer.Ordinal);
        var parameters = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("key") ?? string.Empty, StringComparer.Ordinal);
        var flexCases = (spec.Blueprint["verification"]?["parameter_flex_cases"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("parameter_key") ?? string.Empty, StringComparer.Ordinal);
        foreach (var part in angularParts)
        {
            var path = part["path"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Angular sweep path declaration is missing.");
            var dimensionKey = path.Value<string>("angular_dimension_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Angular sweep dimension key is missing.");
            var referenceLineKey = path.Value<string>("reference_line_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Angular sweep Reference Line key is missing.");
            if (!dimensions.TryGetValue(dimensionKey, out var dimension) || !referenceLines.TryGetValue(referenceLineKey, out var referenceLine))
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Angular sweep declaration references a missing Dimension or Reference Line.");
            var parameterKey = dimension.Value<string>("parameter_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Angular sweep Dimension parameter key is missing.");
            if (!parameters.TryGetValue(parameterKey, out var parameter) || !flexCases.TryGetValue(parameterKey, out var flexCase))
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Angular sweep has no parameter/flex evidence for " + parameterKey + ".");
            var nominal = RequiredFlexValue(flexCase, "nominal", parameterKey);
            var role = PartPresentationSubcategoryName(spec, part, part.Value<string>("key") ?? "Part");
            var form = reopenedForms.SingleOrDefault(item => item.Value<string>("class") == nameof(Sweep) && string.Equals(item.Value<string>("role_or_subcategory"), role, StringComparison.Ordinal))
                ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family is missing declared angular sweep " + (part.Value<string>("key") ?? role) + ".");
            VerifyAngularSweepPathSnapshot(form["path"] as JObject, dimension, referenceLine, nominal, "Reopened angular sweep " + role + ".");
            if (!reopenedDimensions.Any(item => string.Equals(item.Value<string>("family_label"), parameter.Value<string>("name"), StringComparison.Ordinal)))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened angular sweep lost its Angle Family Label for " + role + ".");
        }
    }

    /// <summary>
    /// File-level acceptance for bounded two-port inline fittings. The same
    /// explicit geometry/connector read-back is required for pathway fittings
    /// and the supported Pipe Fitting coupling/flange/union forms. This is an
    /// RFA gate only; Routing Preference and Project-network behavior remain
    /// independent runtime gates.
    /// </summary>
    private static void VerifyReopenedTwoPortInlineFitting(FamilyBlueprintSpec spec, JObject readBack)
    {
        var family = spec.Blueprint["family"] as JObject ?? new JObject();
        var category = family.Value<string>("category") ?? string.Empty;
        var partType = NormalizeFittingBehavior(family.Value<string>("part_type") ?? string.Empty);
        var pathwayInline = category is "conduit_fitting" or "cable_tray_fitting"
            && partType is "transition" or "union" or "offset";
        var pipeInline = category == "pipe_fitting"
            && partType is "union" or "pipe_flange" or "pipe_mechanical_coupling";
        if (!pathwayInline && !pipeInline) return;
        var body = (spec.Blueprint["parts"] as JArray ?? new JArray()).OfType<JObject>().Single(item => (item.Value<string>("operation") ?? "solid") == "solid");
        var role = PartPresentationSubcategoryName(spec, body, body.Value<string>("key") ?? "Part");
        var form = (readBack["forms"] as JArray ?? new JArray()).OfType<JObject>().SingleOrDefault(item => string.Equals(item.Value<string>("role_or_subcategory"), role, StringComparison.Ordinal))
            ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " is missing its declared fitting body.");
        var contracts = (spec.Blueprint["connectors"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var connectors = (readBack["connectors"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        if (contracts.Count != 2 || connectors.Count != 2) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " must contain exactly two connectors.");
        var parameterNames = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("key") ?? string.Empty, item => item.Value<string>("name") ?? string.Empty, StringComparer.Ordinal);
        JObject? observedPath = null;
        if (partType == "offset")
        {
            observedPath = form["path"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened cable-tray offset has no sweep-path read-back.");
            var path = body["path"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Cable-tray offset body has no path declaration.");
            var observedPoints = (observedPath["points_mm"] as JArray ?? new JArray()).OfType<JObject>().Select((item, index) => PointFromMm(item, "reopened offset point " + index)).ToList();
            if (path.Value<string>("kind") == "offset")
            {
                var expected = DeclaredNominalOffsetFrame(spec, path);
                if (observedPath.Value<bool?>("verified") != true || observedPath.Value<string>("kind") != "polyline" || observedPath.Value<string>("plane") != expected.Plane || observedPath.Value<int?>("curve_count") != 3)
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened parameterized cable-tray offset lost its three-segment planar path read-back.");
                if (observedPoints.Count != expected.Points.Length || expected.Points.Where((point, index) => point.DistanceTo(observedPoints[index]) > Mm(.5)).Any())
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened parameterized cable-tray offset no longer matches its approved nominal path geometry.");
                var startTangent = VectorFromJson(observedPath["start_tangent"] as JObject, "reopened offset start tangent");
                var endTangent = VectorFromJson(observedPath["end_tangent"] as JObject, "reopened offset end tangent");
                if (startTangent.Normalize().DotProduct(XYZ.BasisX) < .999 || endTangent.Normalize().DotProduct(XYZ.BasisX) < .999)
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened parameterized cable-tray offset no longer has +X endpoint tangents.");
                var dimensions = (readBack["dimensions"] as JArray ?? new JArray()).OfType<JObject>().ToList();
                foreach (var property in new[] { "lead_in_parameter", "lead_out_parameter", "lateral_offset_parameter", "offset_angle_parameter" })
                {
                    var parameterKey = path.Value<string>(property) ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameterized offset path is missing " + property + ".");
                    if (!parameterNames.TryGetValue(parameterKey, out var parameterName) || !dimensions.Any(item => string.Equals(item.Value<string>("family_label"), parameterName, StringComparison.Ordinal)))
                        throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened parameterized cable-tray offset lost dimension label " + parameterKey + ".");
                }
            }
            else
            {
                var declaredPoints = (path["points_mm"] as JArray ?? new JArray()).Select((item, index) => Point(item, "declared offset point " + index)).ToList();
                if (declaredPoints.Count != 4 || observedPoints.Count != declaredPoints.Count || declaredPoints.Where((point, index) => point.DistanceTo(observedPoints[index]) > Mm(.5)).Any())
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened cable-tray offset path no longer matches its declared four-point path.");
            }
        }
        var primaryCount = 0;
        var observedByRole = new Dictionary<string, JObject>(StringComparer.Ordinal);
        foreach (var contract in contracts)
        {
            var connectorRole = contract.Value<string>("role") ?? contract.Value<string>("key") ?? string.Empty;
            var connector = connectors.SingleOrDefault(item => string.Equals(item.Value<string>("role"), connectorRole, StringComparison.Ordinal))
                ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " is missing connector " + connectorRole + ".");
            observedByRole.Add(connectorRole, connector);
            var hostFace = contract.Value<string>("host_face") ?? string.Empty;
            var expectedNormal = ConnectorFaceNormal(body, hostFace);
            var expectedOrigin = hostFace switch
            {
                "start" => XYZ.BasisX.Multiply(Mm(body.Value<double>("start_mm"))),
                "end" => XYZ.BasisX.Multiply(Mm(body.Value<double>("end_mm"))),
                "path_start" => PointFromMm(observedPath?["start_mm"] as JObject, "reopened offset start"),
                "path_end" => PointFromMm(observedPath?["end_mm"] as JObject, "reopened offset end"),
                _ => throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unsupported inline fitting connector face " + hostFace + ".")
            };
            var actualNormal = VectorFromJson(connector["normal"] as JObject, "reopened inline connector normal");
            var actualOrigin = PointFromMm(connector["origin_mm"] as JObject, "reopened inline connector origin");
            if (actualNormal.Normalize().DotProduct(expectedNormal.Normalize()) < .999 || actualOrigin.DistanceTo(expectedOrigin) > Mm(.5))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " connector origin/orientation mismatch for " + connectorRole + ".");
            var bindings = connector["size_parameter_bindings"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " connector has no size-parameter binding read-back for " + connectorRole + ".");
            foreach (var property in new[] { "diameter_parameter", "width_parameter", "height_parameter" })
            {
                if (contract.Value<string>(property) is not string parameterKey) continue;
                var bindingKey = property.Replace("_parameter", string.Empty);
                if (!parameterNames.TryGetValue(parameterKey, out var parameterName) || !string.Equals(bindings.Value<string>(bindingKey), parameterName, StringComparison.Ordinal))
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " connector lost its " + bindingKey + " binding for " + connectorRole + ".");
            }
            var isPrimary = connector.Value<bool?>("is_primary") == true;
            if (isPrimary) primaryCount++;
            if (contract.Value<bool?>("primary") == true && !isPrimary) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " lost primary connector " + connectorRole + ".");
        }
        if (primaryCount != 1) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " must contain exactly one primary connector.");
        foreach (var contract in contracts)
        {
            var connectorRole = contract.Value<string>("role") ?? contract.Value<string>("key") ?? string.Empty;
            var linkedKey = contract.Value<string>("linked_to") ?? string.Empty;
            var linkedContract = contracts.Single(item => item.Value<string>("key") == linkedKey);
            var linkedRole = linkedContract.Value<string>("role") ?? linkedKey;
            if (observedByRole[connectorRole].Value<long?>("linked_connector_id") != observedByRole[linkedRole].Value<long?>("id"))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " lost reciprocal connector linkage for " + connectorRole + ".");
        }
    }

    /// <summary>
    /// File-level acceptance for a two-port Duct/Pipe Accessory whose Part Type
    /// is Break Into.  The Project must still prove that a live route is split
    /// and connected; this prevents an RFA with only two decorative ports from
    /// being presented as that prerequisite.
    /// </summary>
    private static void VerifyReopenedBreakIntoAccessory(FamilyBlueprintSpec spec, JObject readBack)
    {
        var family = spec.Blueprint["family"] as JObject ?? new JObject();
        var category = family.Value<string>("category") ?? string.Empty;
        var partType = NormalizeFittingBehavior(family.Value<string>("part_type") ?? string.Empty);
        if (category is not ("duct_accessory" or "pipe_accessory") || partType is not ("breaks_into" or "valve_breaks_into")) return;
        var contracts = (spec.Blueprint["connectors"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        if (contracts.Count != 2) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " must contain exactly two connector contracts.");
        var routingBodyKey = contracts[0].Value<string>("host_part") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(routingBodyKey) || contracts.Any(item => !string.Equals(item.Value<string>("host_part"), routingBodyKey, StringComparison.Ordinal)))
            throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Break Into connector contracts must share one routing body.");
        var body = (spec.Blueprint["parts"] as JArray ?? new JArray()).OfType<JObject>().SingleOrDefault(item => string.Equals(item.Value<string>("key"), routingBodyKey, StringComparison.Ordinal))
            ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Break Into routing body is missing.");
        var bodyRole = PartPresentationSubcategoryName(spec, body, routingBodyKey);
        if (!(readBack["forms"] as JArray ?? new JArray()).OfType<JObject>().Any(item => string.Equals(item.Value<string>("role_or_subcategory"), bodyRole, StringComparison.Ordinal)))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " is missing its routing body.");
        var connectors = (readBack["connectors"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        if (connectors.Count != 2) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " must contain exactly two connectors.");
        var parameterNames = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("key") ?? string.Empty, item => item.Value<string>("name") ?? string.Empty, StringComparer.Ordinal);
        var observedByRole = new Dictionary<string, JObject>(StringComparer.Ordinal); var primaryCount = 0;
        foreach (var contract in contracts)
        {
            var role = contract.Value<string>("role") ?? contract.Value<string>("key") ?? string.Empty;
            var connector = connectors.SingleOrDefault(item => string.Equals(item.Value<string>("role"), role, StringComparison.Ordinal))
                ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " is missing connector " + role + ".");
            observedByRole.Add(role, connector);
            var hostFace = contract.Value<string>("host_face") ?? string.Empty;
            if (hostFace is not ("start" or "end")) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Break Into connector must use a start or end routing-body face.");
            var expectedNormal = ConnectorFaceNormal(body, hostFace);
            var expectedOrigin = XYZ.BasisX.Multiply(Mm(body.Value<double>(hostFace == "start" ? "start_mm" : "end_mm")));
            var actualNormal = VectorFromJson(connector["normal"] as JObject, "reopened Break Into connector normal");
            var actualOrigin = PointFromMm(connector["origin_mm"] as JObject, "reopened Break Into connector origin");
            if (actualNormal.Normalize().DotProduct(expectedNormal.Normalize()) < .999 || actualOrigin.DistanceTo(expectedOrigin) > Mm(.5))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " connector origin/orientation mismatch for " + role + ".");
            foreach (var property in new[] { "diameter_parameter", "width_parameter", "height_parameter" })
            {
                if (contract.Value<string>(property) is not string parameterKey) continue;
                var bindingKey = property.Replace("_parameter", string.Empty);
                var bindings = connector["size_parameter_bindings"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " connector has no size-parameter binding read-back for " + role + ".");
                if (!parameterNames.TryGetValue(parameterKey, out var parameterName) || !string.Equals(bindings.Value<string>(bindingKey), parameterName, StringComparison.Ordinal))
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " connector lost its " + bindingKey + " binding for " + role + ".");
            }
            foreach (var property in new[] { "system_classification", "flow_direction", "flow_configuration" })
                if (contract.Value<string>(property) is string expected && !string.Equals(connector.Value<string>(property), expected, StringComparison.Ordinal))
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " connector " + role + " lost " + property + ".");
            var primary = connector.Value<bool?>("is_primary") == true;
            if (primary) primaryCount++;
            if (contract.Value<bool?>("primary") == true && !primary) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " lost its declared start-face primary connector.");
        }
        if (primaryCount != 1) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " must contain exactly one primary connector.");
        foreach (var contract in contracts)
        {
            var role = contract.Value<string>("role") ?? contract.Value<string>("key") ?? string.Empty;
            var linkedKey = contract.Value<string>("linked_to") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Break Into connector contract is missing linked_to.");
            var linkedContract = contracts.SingleOrDefault(item => string.Equals(item.Value<string>("key"), linkedKey, StringComparison.Ordinal))
                ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Break Into connector linked_to does not reference its peer.");
            var linkedRole = linkedContract.Value<string>("role") ?? linkedKey;
            if (observedByRole[role].Value<long?>("linked_connector_id") != observedByRole[linkedRole].Value<long?>("id"))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " lost reciprocal connector linkage for " + role + ".");
        }
    }

    private static void VerifyReopenedJunction(FamilyBlueprintSpec spec, JObject readBack)
    {
        var declaredPartType = spec.Blueprint["family"]?.Value<string>("part_type") ?? string.Empty;
        var partType = NormalizeFittingBehavior(declaredPartType);
        if (partType is not ("tee" or "wye" or "lateral_tee" or "tap_perpendicular" or "tap_adjustable" or "pants" or "cross" or "lateral_cross")) return;
        var partContracts = (spec.Blueprint["parts"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("key") ?? string.Empty, StringComparer.Ordinal);
        var connectorContracts = (spec.Blueprint["connectors"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var connectors = (readBack["connectors"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var expectedCount = partType is "cross" or "lateral_cross" ? 4 : 3;
        if (connectors.Count != expectedCount) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " must contain exactly " + expectedCount + " connectors.");
        XYZ ExpectedNormal(JObject contract)
        {
            var hostPartKey = contract.Value<string>("host_part") ?? string.Empty;
            if (!partContracts.TryGetValue(hostPartKey, out var part)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, partType + " connector references an unknown part during reopen verification.");
            return ConnectorFaceNormal(part, contract.Value<string>("host_face") ?? string.Empty);
        }
        var primaryCount = 0;
        foreach (var contract in connectorContracts)
        {
            var role = contract.Value<string>("role") ?? contract.Value<string>("key") ?? string.Empty;
            var connector = connectors.SingleOrDefault(item => string.Equals(item.Value<string>("role"), role, StringComparison.Ordinal))
                ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " is missing connector " + role + ".");
            var normal = VectorFromJson(connector["normal"] as JObject, "reopened junction connector normal"); var expectedNormal = ExpectedNormal(contract);
            if (normal.Normalize().DotProduct(expectedNormal.Normalize()) < .999) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " connector orientation mismatch for " + role + ".");
            var hostPart = partContracts[contract.Value<string>("host_part") ?? string.Empty]; var face = contract.Value<string>("host_face") ?? string.Empty;
            var expectedOffset = Mm(hostPart.Value<double>(face == "start" ? "start_mm" : "end_mm"));
            var expectedOrigin = AxisVector(hostPart).Multiply(expectedOffset);
            var actualOrigin = PointFromMm(connector["origin_mm"] as JObject, "reopened junction connector origin");
            if (actualOrigin.DistanceTo(expectedOrigin) > Mm(.5)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " connector origin mismatch for " + role + ".");
            var bindings = connector["size_parameter_bindings"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " connector has no size-parameter binding read-back for " + role + ".");
            foreach (var property in new[] { "diameter_parameter", "width_parameter", "height_parameter" })
            {
                if (contract.Value<string>(property) is not string parameterKey) continue;
                var parameterName = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>().Single(item => item.Value<string>("key") == parameterKey).Value<string>("name");
                var bindingKey = property.Replace("_parameter", string.Empty);
                if (!string.Equals(bindings.Value<string>(bindingKey), parameterName, StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " connector lost its " + bindingKey + " binding for " + role + ".");
            }
            var isPrimary = connector.Value<bool?>("is_primary") == true;
            if (isPrimary) primaryCount++;
            if (contract.Value<bool?>("primary") == true && !isPrimary) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " lost its declared primary connector " + role + ".");
        }
        if (primaryCount != 1) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + partType + " must contain exactly one primary connector.");
    }

    private static void VerifyReopenedProfileShapes(FamilyBlueprintSpec spec, JObject readBack)
    {
        var forms = (readBack["forms"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        foreach (var part in (spec.Blueprint["parts"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var role = PartPresentationSubcategoryName(spec, part, part.Value<string>("key") ?? "Part");
            var form = forms.SingleOrDefault(item => string.Equals(item.Value<string>("role_or_subcategory"), role, StringComparison.Ordinal))
                ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family is missing declared form " + role + ".");
            var signature = form["profile_signature"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened form has no profile signature for " + role + ".");
            void RequireShape(string slot, JObject profile)
            {
                var expected = profile.Value<string>("shape") ?? string.Empty;
                var observed = signature[slot]?.Value<string>("shape") ?? string.Empty;
                if (!string.Equals(expected, observed, StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened " + role + " " + slot + " profile shape mismatch; expected " + expected + ", observed " + observed + ".");
            }
            RequireShape("primary", part["profile"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Part profile is missing for " + role + "."));
            if (part["end_profile"] is JObject endProfile) RequireShape("secondary", endProfile);
            var parameterizedProfiles = new[] { part["profile"] as JObject, part["end_profile"] as JObject }.Where(item => item != null && ProfileHasParameters(item)).Cast<JObject>().ToList();
            if (parameterizedProfiles.Count > 0)
            {
                var parameterContracts = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("key") ?? string.Empty, StringComparer.Ordinal);
                var reopenedParameters = (readBack["parameters"] as JArray ?? new JArray()).OfType<JObject>().ToList();
                var dimensionLabels = new HashSet<string>((readBack["dimensions"] as JArray ?? new JArray()).OfType<JObject>().Select(item => item.Value<string>("family_label") ?? string.Empty), StringComparer.Ordinal);
                foreach (var property in parameterizedProfiles.SelectMany(profile => profile.Properties()).Where(item => item.Name.EndsWith("_parameter", StringComparison.Ordinal)))
                {
                    var parameterKey = property.Value.Value<string>() ?? string.Empty;
                    if (!parameterContracts.TryGetValue(parameterKey, out var contract)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameterized profile references unknown parameter " + parameterKey + ".");
                    var parameterName = contract.Value<string>("name") ?? string.Empty;
                    var directlyLabeled = dimensionLabels.Contains(parameterName);
                    var formulaLabeled = reopenedParameters.Any(item => dimensionLabels.Contains(item.Value<string>("name") ?? string.Empty) && (item.Value<string>("formula") ?? string.Empty).Contains(parameterName));
                    if (!directlyLabeled && !formulaLabeled) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened form " + role + " lost its profile dimension/formula binding for " + parameterKey + ".");
                }
            }
        }
    }

    private static string NormalizeFittingBehavior(string partType)
    {
        foreach (var prefix in new[] { "channel_cable_tray_", "ladder_cable_tray_" })
            if (partType.StartsWith(prefix, StringComparison.Ordinal)) partType = partType.Substring(prefix.Length);
        if (partType == "vertical_elbow") return "elbow";
        if (partType.StartsWith("junction_box_", StringComparison.Ordinal)) return partType.Substring("junction_box_".Length);
        return partType;
    }

    private static void VerifyReopenedElectricalConnectors(FamilyBlueprintSpec spec, JObject readBack)
    {
        var contracts = (spec.Blueprint["connectors"] as JArray ?? new JArray()).OfType<JObject>().Where(item => item.Value<string>("discipline") == "electrical").ToList();
        if (contracts.Count == 0) return;
        var connectors = (readBack["connectors"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var parameterNames = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("key") ?? string.Empty, item => item.Value<string>("name") ?? string.Empty, StringComparer.Ordinal);
        var bindingProperties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["voltage_parameter"] = "voltage", ["apparent_load_parameter"] = "apparent_load", ["number_of_poles_parameter"] = "number_of_poles",
            ["power_factor_parameter"] = "power_factor", ["balanced_load_parameter"] = "balanced_load", ["load_classification_parameter"] = "load_classification"
        };
        foreach (var contract in contracts)
        {
            var role = contract.Value<string>("role") ?? contract.Value<string>("key") ?? string.Empty;
            var connector = connectors.SingleOrDefault(item => string.Equals(item.Value<string>("role"), role, StringComparison.Ordinal))
                ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family is missing electrical connector " + role + ".");
            // A logical Data connector has no load/circuit properties to read
            // back. Require this payload only where the Blueprint declares an
            // electrical load/circuit binding or a power-factor state.
            var requiresElectricalReadBack = bindingProperties.Keys.Any(key => contract.Value<string>(key) is string)
                || contract.Value<string>("power_factor_state") is string;
            var electrical = connector["electrical_data"] as JObject;
            if (requiresElectricalReadBack && electrical == null)
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened electrical connector has no load/circuit read-back for " + role + ".");
            var bindings = electrical?["parameter_bindings"] as JObject;
            foreach (var pair in bindingProperties)
            {
                if (contract.Value<string>(pair.Key) is not string parameterKey) continue;
                if (bindings == null || !parameterNames.TryGetValue(parameterKey, out var expectedName) || !string.Equals(bindings.Value<string>(pair.Value), expectedName, StringComparison.Ordinal))
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened electrical connector lost its " + pair.Value + " Family Parameter binding for " + role + ".");
            }
            if (contract.Value<string>("power_factor_state") is string state && !string.Equals(electrical?.Value<string>("power_factor_state"), state, StringComparison.Ordinal))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened electrical connector power-factor state mismatch for " + role + ".");
            if (contract.Value<bool?>("primary") == true && connector.Value<bool?>("is_primary") != true)
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened electrical connector lost primary status for " + role + ".");
        }
    }

    private static void VerifyReopenedLightSource(FamilyBlueprintSpec spec, JObject? created, JObject readBack)
    {
        if (spec.Blueprint["light_source"] == null) return;
        var reopened = readBack["light_source"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Lighting Fixture has no light-source read-back.");
        if (created == null || !JToken.DeepEquals(created, reopened))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Lighting Fixture light-source shape, distribution or photometric data changed after save.");
    }

    private static XYZ PointFromMm(JObject? point, string label) => point == null
        ? throw new CommandResultException(ErrorCodes.VerificationFailed, label + " is missing.")
        : new XYZ(Mm(point.Value<double>("x_mm")), Mm(point.Value<double>("y_mm")), Mm(point.Value<double>("z_mm")));

    private static JObject PointMmSnapshot(XYZ point) => new() { ["x_mm"] = Math.Round(point.X * 304.8, 3), ["y_mm"] = Math.Round(point.Y * 304.8, 3), ["z_mm"] = Math.Round(point.Z * 304.8, 3) };

    private static XYZ VectorFromJson(JObject? vector, string label) => vector == null
        ? throw new CommandResultException(ErrorCodes.VerificationFailed, label + " is missing.")
        : new XYZ(vector.Value<double>("x"), vector.Value<double>("y"), vector.Value<double>("z"));

    private static void VerifyReopenedReferenceFramework(FamilyBlueprintSpec spec, JObject readBack)
    {
        var expected = (spec.Blueprint["reference_planes"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        if (expected.Count == 0) return;
        // Native templates can have repeated unrelated plane labels. Index only
        // declared reference-plane names and never silently select a duplicate
        // of a declared Blueprint plane.
        var declaredNames = new HashSet<string>(expected.Select(item => item.Value<string>("name") ?? string.Empty), StringComparer.OrdinalIgnoreCase);
        var actual = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in (readBack["reference_planes"] as JArray ?? new JArray()).OfType<JObject>()
            .Where(item => declaredNames.Contains(item.Value<string>("name") ?? string.Empty)))
        {
            var name = item.Value<string>("name") ?? string.Empty;
            if (actual.ContainsKey(name))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family contains duplicate declared reference plane " + name + ".");
            actual.Add(name, item);
        }
        var subcategories = (spec.Blueprint["reference_plane_subcategories"] as JArray ?? new JArray()).OfType<JObject>()
            .ToDictionary(item => item.Value<string>("key") ?? string.Empty, item => item, StringComparer.Ordinal);
        foreach (var contract in expected)
        {
            var name = contract.Value<string>("name") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Reference plane name is missing during reopen verification.");
            if (!actual.TryGetValue(name, out var observed)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family is missing reference plane " + name + ".");
            var referenceType = contract.Value<string>("reference_type") ?? LegacyReferenceType(contract.Value<string>("strength"));
            if (!string.Equals(observed.Value<string>("reference_type"), referenceType, StringComparison.Ordinal) || observed.Value<bool?>("defines_origin") != (contract.Value<bool?>("defines_origin") == true))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened reference semantics do not match for " + name + ".");
            if (contract.Value<string>("subcategory_key") is not string subcategoryKey) continue;
            if (!subcategories.TryGetValue(subcategoryKey, out var subcategoryContract)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown reference-plane subcategory " + subcategoryKey + ".");
            var graphics = observed["subcategory"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened reference plane " + name + " lost its subcategory.");
            if (!string.Equals(graphics.Value<string>("name"), subcategoryContract.Value<string>("name"), StringComparison.Ordinal)
                || graphics.Value<int?>("projection_line_weight") != subcategoryContract.Value<int?>("projection_line_weight")
                || !JToken.DeepEquals(graphics["color_rgb"], subcategoryContract["color_rgb"])
                || subcategoryContract.Value<string>("line_pattern_name") is string patternName && !string.Equals(graphics.Value<string>("line_pattern_name"), patternName, StringComparison.Ordinal))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened reference-plane graphics do not match for " + name + ".");
        }
    }

    private static void VerifyReopenedModelLineEndpointBindings(FamilyBlueprintSpec spec, JObject readBack)
    {
        var contracts = (spec.Blueprint["model_lines"] as JArray ?? new JArray()).OfType<JObject>().Where(item => (item["endpoint_bindings"] as JArray ?? new JArray()).Count != 0).ToList();
        if (contracts.Count == 0) return;
        var referenceContracts = (spec.Blueprint["reference_planes"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("key") ?? string.Empty, StringComparer.Ordinal);
        var observedPlanes = (readBack["reference_planes"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("name") ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var modelCurves = (readBack["model_curves"] as JArray ?? new JArray()).OfType<JObject>().ToList(); var tolerance = Mm(.01);
        foreach (var contract in contracts)
        {
            var declarationKey = contract.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Model Line key is missing during reopen verification.");
            var role = contract.Value<string>("role") ?? declarationKey; var expectedStyle = PresentationSubcategoryName(spec, contract, "DSCons Model Line " + role);
            var expectedPoints = (contract["points_mm"] as JArray ?? new JArray()).Select((item, index) => Point(item, "reopened Model Line " + declarationKey + " point " + index)).ToList();
            if (expectedPoints.Count < 2) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Model Line " + declarationKey + " has no segments during reopen verification.");
            foreach (var binding in (contract["endpoint_bindings"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var endpoint = binding.Value<string>("endpoint") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Model Line " + declarationKey + " endpoint binding is missing endpoint during reopen verification.");
                var referencePlaneKey = binding.Value<string>("reference_plane_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Model Line " + declarationKey + " endpoint binding is missing Reference Plane during reopen verification.");
                if (!referenceContracts.TryGetValue(referencePlaneKey, out var referenceContract)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Model Line " + declarationKey + " references unknown Reference Plane " + referencePlaneKey + " during reopen verification.");
                var referenceName = referenceContract.Value<string>("name") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Reference Plane " + referencePlaneKey + " has no name during reopen verification.");
                if (!observedPlanes.TryGetValue(referenceName, out var observedPlane)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family is missing Reference Plane " + referencePlaneKey + " used by Model Line " + declarationKey + ".");
                var planeGeometry = observedPlane["plane_geometry"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Reference Plane " + referencePlaneKey + " has no plane geometry read-back.");
                var planeOrigin = PointFromMm(planeGeometry["origin_mm"] as JObject, "reopened Reference Plane " + referencePlaneKey + " origin");
                var planeNormal = VectorFromJson(planeGeometry["normal"] as JObject, "reopened Reference Plane " + referencePlaneKey + " normal");
                if (planeNormal.GetLength() < 1e-9) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Reference Plane " + referencePlaneKey + " has a zero normal.");
                var segmentStartIndex = endpoint == "start" ? 0 : endpoint == "end" ? expectedPoints.Count - 2 : throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Model Line " + declarationKey + " endpoint binding is invalid during reopen verification.");
                var expectedStart = expectedPoints[segmentStartIndex]; var expectedEnd = expectedPoints[segmentStartIndex + 1];
                var matches = modelCurves.Where(curve => string.Equals(curve.Value<string>("line_style"), expectedStyle, StringComparison.Ordinal))
                    .Select(curve => new { Curve = curve, Geometry = curve["curve_geometry"] as JObject })
                    .Where(item => item.Geometry != null && string.Equals(item.Geometry.Value<string>("kind"), "line", StringComparison.Ordinal))
                    .Select(item => new { item.Curve, Start = PointFromMm(item.Geometry!["start_mm"] as JObject, "reopened Model Line start"), End = PointFromMm(item.Geometry!["end_mm"] as JObject, "reopened Model Line end") })
                    .Where(item => (item.Start.DistanceTo(expectedStart) <= tolerance && item.End.DistanceTo(expectedEnd) <= tolerance) || (item.Start.DistanceTo(expectedEnd) <= tolerance && item.End.DistanceTo(expectedStart) <= tolerance)).ToList();
                if (matches.Count != 1) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Model Line " + declarationKey + " " + endpoint + " endpoint segment cannot be uniquely matched to its declared geometry.");
                var match = matches[0]; var observedEndpoint = endpoint == "start"
                    ? (match.Start.DistanceTo(expectedStart) <= tolerance ? match.Start : match.End)
                    : (match.End.DistanceTo(expectedEnd) <= tolerance ? match.End : match.Start);
                var distance = Math.Abs(observedEndpoint.Subtract(planeOrigin).DotProduct(planeNormal.Normalize()));
                if (distance > tolerance) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Model Line " + declarationKey + " " + endpoint + " endpoint moved off Reference Plane " + referencePlaneKey + ".");
            }
        }
    }

    private static void VerifyReopenedDetailLineEndpointBindings(FamilyBlueprintSpec spec, JObject readBack)
    {
        var contracts = (spec.Blueprint["detail_lines"] as JArray ?? new JArray()).OfType<JObject>().Where(item => (item["endpoint_bindings"] as JArray ?? new JArray()).Count != 0).ToList();
        if (contracts.Count == 0) return;
        var referenceContracts = (spec.Blueprint["reference_planes"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("key") ?? string.Empty, StringComparer.Ordinal);
        var observedPlanes = (readBack["reference_planes"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("name") ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var detailCurves = (readBack["detail_curves"] as JArray ?? new JArray()).OfType<JObject>().ToList(); var tolerance = Mm(.01);
        foreach (var contract in contracts)
        {
            var declarationKey = contract.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail Line key is missing during reopen verification.");
            var role = contract.Value<string>("role") ?? declarationKey; var expectedStyle = PresentationSubcategoryName(spec, contract, "DSCons Detail " + role);
            var expectedPoints = (contract["points_mm"] as JArray ?? new JArray()).Select((item, index) => Point(item, "reopened Detail Line " + declarationKey + " point " + index)).ToList();
            if (expectedPoints.Count < 2) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail Line " + declarationKey + " has no segments during reopen verification.");
            foreach (var binding in (contract["endpoint_bindings"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var endpoint = binding.Value<string>("endpoint") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail Line " + declarationKey + " endpoint binding is missing endpoint during reopen verification.");
                var referencePlaneKey = binding.Value<string>("reference_plane_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail Line " + declarationKey + " endpoint binding is missing Reference Plane during reopen verification.");
                if (!referenceContracts.TryGetValue(referencePlaneKey, out var referenceContract)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail Line " + declarationKey + " references unknown Reference Plane " + referencePlaneKey + " during reopen verification.");
                var referenceName = referenceContract.Value<string>("name") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Reference Plane " + referencePlaneKey + " has no name during reopen verification.");
                if (!observedPlanes.TryGetValue(referenceName, out var observedPlane)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family is missing Reference Plane " + referencePlaneKey + " used by Detail Line " + declarationKey + ".");
                var planeGeometry = observedPlane["plane_geometry"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Reference Plane " + referencePlaneKey + " has no plane geometry read-back.");
                var planeOrigin = PointFromMm(planeGeometry["origin_mm"] as JObject, "reopened Reference Plane " + referencePlaneKey + " origin");
                var planeNormal = VectorFromJson(planeGeometry["normal"] as JObject, "reopened Reference Plane " + referencePlaneKey + " normal");
                if (planeNormal.GetLength() < 1e-9) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Reference Plane " + referencePlaneKey + " has a zero normal.");
                var segmentStartIndex = endpoint == "start" ? 0 : endpoint == "end" ? expectedPoints.Count - 2 : throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail Line " + declarationKey + " endpoint binding is invalid during reopen verification.");
                var expectedStart = expectedPoints[segmentStartIndex]; var expectedEnd = expectedPoints[segmentStartIndex + 1];
                var matches = detailCurves.Where(curve => string.Equals(curve.Value<string>("line_style"), expectedStyle, StringComparison.Ordinal))
                    .Select(curve => new { Curve = curve, Geometry = curve["curve_geometry"] as JObject })
                    .Where(item => item.Geometry != null && string.Equals(item.Geometry.Value<string>("kind"), "line", StringComparison.Ordinal))
                    .Select(item => new { item.Curve, Start = PointFromMm(item.Geometry!["start_mm"] as JObject, "reopened Detail Line start"), End = PointFromMm(item.Geometry!["end_mm"] as JObject, "reopened Detail Line end") })
                    .Where(item => (item.Start.DistanceTo(expectedStart) <= tolerance && item.End.DistanceTo(expectedEnd) <= tolerance) || (item.Start.DistanceTo(expectedEnd) <= tolerance && item.End.DistanceTo(expectedStart) <= tolerance)).ToList();
                if (matches.Count != 1) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Detail Line " + declarationKey + " " + endpoint + " endpoint segment cannot be uniquely matched to its declared geometry.");
                var match = matches[0]; var observedEndpoint = endpoint == "start"
                    ? (match.Start.DistanceTo(expectedStart) <= tolerance ? match.Start : match.End)
                    : (match.End.DistanceTo(expectedEnd) <= tolerance ? match.End : match.Start);
                var distance = Math.Abs(observedEndpoint.Subtract(planeOrigin).DotProduct(planeNormal.Normalize()));
                if (distance > tolerance) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Detail Line " + declarationKey + " " + endpoint + " endpoint moved off Reference Plane " + referencePlaneKey + ".");
            }
        }
    }

    private static void VerifyReopenedNestedPlacements(JObject created, JObject readBack)
    {
        var expected = created["nested_components"] as JObject ?? new JObject();
        var actual = (readBack["nested_instances"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<long>("id"));
        foreach (var property in expected.Properties())
        {
            var nested = property.Value as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Created nested read-back is invalid for " + property.Name + ".");
            var id = nested.Value<long>("element_id");
            if (!actual.TryGetValue(id, out var reopened)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family is missing nested component " + property.Name + ".");
            var placement = nested["placement"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested component " + property.Name + " has no placement evidence.");
            if (!string.Equals(placement.Value<string>("actual_family_placement_type"), reopened.Value<string>("family_placement_type"), StringComparison.Ordinal) || !string.Equals(placement.Value<string>("location_kind"), reopened.Value<string>("location_kind"), StringComparison.Ordinal))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened nested placement type/location mismatch for " + property.Name + ".");
            var expectedLocation = placement.Value<string>("mode") == "level_point" ? placement["point_mm"] : placement["location"];
            if (!JToken.DeepEquals(expectedLocation, reopened["location"])) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened nested location mismatch for " + property.Name + ".");
            if (placement.Value<string>("mode") != "level_point" && (placement.Value<long?>("host_element_id") != reopened.Value<long?>("host_element_id") || placement.Value<long?>("host_face_element_id") != reopened.Value<long?>("host_face_element_id")))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened nested host/host-face mismatch for " + property.Name + ".");
            var expectedInterfaces = nested["mapped_parameter_interfaces"] as JObject ?? new JObject();
            var observedInterfaces = (reopened["parameters"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            foreach (var mapping in expectedInterfaces.Properties())
            {
                var expectedInterface = mapping.Value as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested parameter interface evidence is invalid for " + property.Name + "." + mapping.Name + ".");
                var expectedChild = expectedInterface["child"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested child parameter interface is missing for " + property.Name + "." + mapping.Name + ".");
                var expectedParent = expectedInterface["parent"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested parent parameter interface is missing for " + property.Name + "." + mapping.Name + ".");
                if (expectedInterface.Value<bool?>("shared_identity_required") == true)
                {
                    var identity = observedInterfaces.FirstOrDefault(candidate => candidate.Value<bool?>("shared_guid_identity_lookup") == true
                        && string.Equals(candidate.Value<string>("name"), expectedChild.Value<string>("name"), StringComparison.Ordinal)
                        && string.Equals(candidate.Value<string>("shared_guid"), expectedChild.Value<string>("shared_guid"), StringComparison.OrdinalIgnoreCase));
                    if (identity == null || !string.Equals(expectedChild.Value<string>("name"), expectedParent.Value<string>("name"), StringComparison.Ordinal)
                        || expectedParent.Value<bool?>("is_instance") != true || expectedParent.Value<bool?>("is_shared") != true
                        || !string.Equals(expectedChild.Value<string>("shared_guid"), expectedParent.Value<string>("shared_guid"), StringComparison.OrdinalIgnoreCase))
                        throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened nested Shared GUID identity mismatch for " + property.Name + "." + mapping.Name + ".");
                    continue;
                }
                var observed = observedInterfaces.SingleOrDefault(candidate => string.Equals(candidate.Value<string>("name"), expectedChild.Value<string>("name"), StringComparison.Ordinal));
                if (observed == null || observed.Value<bool?>("is_shared") != expectedChild.Value<bool?>("is_shared") || !string.Equals(observed.Value<string>("shared_guid"), expectedChild.Value<string>("shared_guid"), StringComparison.OrdinalIgnoreCase))
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened nested child parameter identity mismatch for " + property.Name + "." + mapping.Name + ".");
                var observedParent = observed["associated_family_parameter"] as JObject;
                if (observedParent == null || !string.Equals(observedParent.Value<string>("name"), expectedParent.Value<string>("name"), StringComparison.Ordinal)
                    || observedParent.Value<bool?>("is_instance") != expectedParent.Value<bool?>("is_instance") || observedParent.Value<bool?>("is_shared") != expectedParent.Value<bool?>("is_shared")
                    || !string.Equals(observedParent.Value<string>("shared_guid"), expectedParent.Value<string>("shared_guid"), StringComparison.OrdinalIgnoreCase))
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened nested parent association mismatch for " + property.Name + "." + mapping.Name + ".");
                if (expectedInterface.Value<bool?>("shared_identity_required") == true && (expectedInterface.Value<bool?>("same_shared_guid") != true || !string.Equals(observed.Value<string>("shared_guid"), observedParent.Value<string>("shared_guid"), StringComparison.OrdinalIgnoreCase)))
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened nested Shared GUID identity mismatch for " + property.Name + "." + mapping.Name + ".");
            }
        }
        if (actual.Count < expected.Count) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened nested component count is smaller than the Blueprint declaration.");
    }

    private static Dictionary<string, ArrayResult> CreateArrays(Document family, FamilyBlueprintSpec spec, FamilyManager manager, Dictionary<string, FamilyParameter> parameters, Dictionary<string, PartResult> parts, Dictionary<string, NestedResult> nested)
    {
        var results = new Dictionary<string, ArrayResult>(StringComparer.Ordinal);
        foreach (var contract in (spec.Blueprint["arrays"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var key = contract.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Array key is missing.");
            var memberIds = new List<ElementId>();
            var partKeys = (contract["member_part_keys"] as JArray ?? new JArray()).Values<string>().Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToList();
            var nestedKeys = (contract["member_nested_component_keys"] as JArray ?? new JArray()).Values<string>().Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToList();
            if ((partKeys.Count > 0) == (nestedKeys.Count > 0)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Array " + key + " must contain exactly one non-empty part or nested-component member list.");
            foreach (var partKey in partKeys)
            {
                if (!parts.TryGetValue(partKey, out var part)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Array " + key + " references unknown part " + partKey + ".");
                memberIds.Add(part.Form.Id);
            }
            foreach (var nestedKey in nestedKeys)
            {
                if (!nested.TryGetValue(nestedKey, out var nestedComponent)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Array " + key + " references unknown nested component " + nestedKey + ".");
                memberIds.Add(nestedComponent.Instance.Id);
            }
            if (memberIds.Count == 0) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Array " + key + " requires at least one part or nested-component member.");
            var countParameterKey = contract.Value<string>("count_parameter");
            FamilyParameter? countParameter = null;
            var count = contract.Value<int?>("count") ?? 0;
            if (!string.IsNullOrWhiteSpace(countParameterKey))
            {
                countParameter = RequireParameter(parameters, countParameterKey!);
                count = manager.CurrentType.AsInteger(countParameter) ?? throw new CommandResultException(ErrorCodes.InvalidParam, "Array count parameter " + countParameterKey + " has no current value.");
            }
            if (count < 2) throw new CommandResultException(ErrorCodes.InvalidParam, "Array " + key + " requires an initial member count of at least 2.");
            var anchor = contract.Value<string>("anchor") == "last" ? ArrayAnchorMember.Last : ArrayAnchorMember.Second;
            var linearArray = LinearArray.Create(family, ViewForPlane(family, contract.Value<string>("view_plane") ?? string.Empty, "linear array"), memberIds, count, Point(contract["direction_mm"], "array direction_mm"), anchor);
            if (countParameter != null) linearArray.Label = countParameter;
            family.Regenerate();
            if (linearArray.NumMembers != count || countParameter != null && linearArray.Label == null) throw new CommandResultException(ErrorCodes.VerificationFailed, "Array " + key + " did not read back its declared member count/label.");
            results.Add(key, new ArrayResult { Key = key, Array = linearArray, Contract = contract });
        }
        if (results.Count != (spec.Blueprint["arrays"] as JArray ?? new JArray()).Count) throw new CommandResultException(ErrorCodes.VerificationFailed, "Array read-back count does not match the Blueprint declaration.");
        return results;
    }

    private static Dictionary<string, NestedResult> CreateNestedComponents(Document family, FamilyBlueprintSpec spec, FamilyManager manager, Dictionary<string, FamilyParameter> parameters, Dictionary<string, PartResult> parts)
    {
        var results = new Dictionary<string, NestedResult>(StringComparer.Ordinal);
        var loadedFamilies = new Dictionary<string, Family>(StringComparer.OrdinalIgnoreCase);
        foreach (var contract in (spec.Blueprint["nested_components"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var key = contract.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested component key is missing.");
            FamilySymbol symbol; JObject artifact; FamilyParameter? familyTypeParameter = null; var typeSelections = new JArray();
            var resolvedOptions = (contract["resolved_options"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            if (resolvedOptions.Count > 0)
            {
                var optionSymbols = new Dictionary<string, FamilySymbol>(StringComparer.Ordinal);
                foreach (var option in resolvedOptions)
                {
                    var optionKey = option.Value<string>("option_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested option key is missing for " + key + ".");
                    optionSymbols.Add(optionKey, LoadNestedSymbol(family, loadedFamilies, option, key + " option " + optionKey));
                }
                var parameterKey = contract.Value<string>("family_type_parameter_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested component " + key + " has no family_type_parameter_key.");
                var parameterContract = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault(item => string.Equals(item.Value<string>("key"), parameterKey, StringComparison.Ordinal))
                    ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested component " + key + " references unknown Family Type parameter " + parameterKey + ".");
                familyTypeParameter = AddFamilyTypeParameter(family, manager, parameterContract);
                parameters.Add(parameterKey, familyTypeParameter);
                var applicable = family.OwnerFamily?.GetFamilyTypeParameterValues(familyTypeParameter.Id) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Parent Family has no applicable values for " + parameterKey + ".");
                if (optionSymbols.Values.Any(candidate => !applicable.Contains(candidate.Id))) throw new CommandResultException(ErrorCodes.VerificationFailed, "Family Type parameter " + parameterKey + " does not expose every declared nested option.");
                var typeContracts = (spec.Blueprint["types"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("name") ?? string.Empty, StringComparer.Ordinal);
                var familyTypes = manager.Types.Cast<FamilyType>().ToDictionary(item => item.Name, StringComparer.Ordinal);
                var initialType = manager.CurrentType;
                foreach (var typeContract in typeContracts.Values)
                {
                    var typeName = typeContract.Value<string>("name") ?? string.Empty;
                    if (!familyTypes.TryGetValue(typeName, out var familyType)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Parent Family type is missing for nested option assignment: " + typeName + ".");
                    var optionKey = (typeContract["values"] as JObject)?.Value<string>(parameterKey) ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parent Family type " + typeName + " has no nested option for " + parameterKey + ".");
                    if (!optionSymbols.TryGetValue(optionKey, out var optionSymbol)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown nested option " + optionKey + " for " + parameterKey + ".");
                    manager.CurrentType = familyType; manager.Set(familyTypeParameter, optionSymbol.Id);
                    if (familyType.AsElementId(familyTypeParameter) != optionSymbol.Id) throw new CommandResultException(ErrorCodes.VerificationFailed, "Family Type option did not read back for parent type " + typeName + ".");
                    typeSelections.Add(new JObject { ["parent_type"] = typeName, ["option_key"] = optionKey, ["family"] = optionSymbol.FamilyName, ["type"] = optionSymbol.Name, ["symbol_id"] = optionSymbol.Id.Val(), ["verified"] = true });
                }
                manager.CurrentType = initialType;
                var initialOption = typeSelections.OfType<JObject>().First(item => string.Equals(item.Value<string>("parent_type"), initialType.Name, StringComparison.Ordinal)).Value<string>("option_key")!;
                symbol = optionSymbols[initialOption]; artifact = resolvedOptions.First(item => string.Equals(item.Value<string>("option_key"), initialOption, StringComparison.Ordinal));
            }
            else
            {
                artifact = contract["resolved_artifact"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested component " + key + " has no resolved artifact.");
                symbol = LoadNestedSymbol(family, loadedFamilies, artifact, key);
            }
            if (!symbol.IsActive) symbol.Activate(); family.Regenerate();
            var placementMode = contract.Value<string>("placement_mode") ?? "level_point";
            var actualPlacementType = symbol.Family.FamilyPlacementType;
            var expectedPlacementType = ExpectedNestedPlacementType(artifact.Value<string>("template_behavior") ?? string.Empty);
            if (actualPlacementType != expectedPlacementType || !string.Equals(actualPlacementType.ToString(), artifact.Value<string>("family_placement_type"), StringComparison.Ordinal))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested component " + key + " loaded FamilyPlacementType does not match its verified Blueprint artifact.");
            FamilyInstance instance; JObject placement;
            if (placementMode == "level_point")
            {
                if (actualPlacementType != FamilyPlacementType.OneLevelBased) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested component " + key + " level_point requires OneLevelBased placement.");
                var insertion = Point(contract["placement_point_mm"], "nested component placement_point_mm");
                instance = family.FamilyCreate.NewFamilyInstance(insertion, symbol, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                var rotation = contract.Value<double>("rotation_degrees");
                if (Math.Abs(rotation) > 1e-9)
                {
                    var axis = contract.Value<string>("rotation_axis") switch { "x" => XYZ.BasisX, "y" => XYZ.BasisY, _ => XYZ.BasisZ };
                    ElementTransformUtils.RotateElement(family, instance.Id, Line.CreateUnbound(insertion, axis), Radians(rotation));
                }
                family.Regenerate();
                if (instance.Location is not LocationPoint location || !location.Point.IsAlmostEqualTo(insertion)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested component " + key + " level-point location did not read back.");
                placement = new JObject { ["mode"] = placementMode, ["expected_family_placement_type"] = expectedPlacementType.ToString(), ["actual_family_placement_type"] = actualPlacementType.ToString(), ["location_kind"] = "point", ["point_mm"] = PointMm(location.Point), ["verified"] = true };
            }
            else
            {
                var hostPartKey = contract.Value<string>("host_part_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested component " + key + " hosted placement has no host_part_key.");
                if (!parts.TryGetValue(hostPartKey, out var hostPart) || hostPart.Operation != "solid") throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested component " + key + " requires an existing solid host part.");
                var hostFaceKey = contract.Value<string>("host_face") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested component " + key + " hosted placement has no host_face.");
                var hostFace = Face(hostPart.Form, FaceNormal(hostFaceKey), hostPartKey);
                if (placementMode == "host_face_point")
                {
                    if (actualPlacementType != FamilyPlacementType.WorkPlaneBased) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested component " + key + " host_face_point requires WorkPlaneBased placement.");
                    var insertion = Point(contract["placement_point_mm"], "nested component placement_point_mm"); RequirePointOnFace(hostFace, insertion, key);
                    var rawDirection = Point(contract["reference_direction"], "nested component reference_direction");
                    var direction = rawDirection - hostFace.FaceNormal.Multiply(rawDirection.DotProduct(hostFace.FaceNormal));
                    if (direction.GetLength() < 1e-9) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested component " + key + " reference_direction is parallel to its host face normal.");
                    instance = family.FamilyCreate.NewFamilyInstance(hostFace.Reference, insertion, direction.Normalize(), symbol);
                    family.Regenerate();
                    if (instance.Location is not LocationPoint location || !location.Point.IsAlmostEqualTo(insertion)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested component " + key + " hosted point location did not read back.");
                    placement = HostedPlacementSnapshot(instance, placementMode, expectedPlacementType, actualPlacementType, hostPartKey, hostFaceKey, hostPart.Form.Id, "point", PointMm(location.Point));
                    placement["reference_direction"] = VectorSnapshot(direction.Normalize());
                }
                else if (placementMode == "host_face_line")
                {
                    if (actualPlacementType != FamilyPlacementType.CurveBased) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested component " + key + " host_face_line requires CurveBased placement.");
                    var start = Point(contract["placement_line_start_mm"], "nested component placement_line_start_mm"); var end = Point(contract["placement_line_end_mm"], "nested component placement_line_end_mm");
                    var line = Line.CreateBound(start, end); RequireCurveOnFace(hostFace, line, key);
                    instance = family.FamilyCreate.NewFamilyInstance(hostFace.Reference, line, symbol);
                    family.Regenerate();
                    if (instance.Location is not LocationCurve location || location.Curve is not Line actualLine || !actualLine.GetEndPoint(0).IsAlmostEqualTo(start) || !actualLine.GetEndPoint(1).IsAlmostEqualTo(end)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested component " + key + " hosted line location did not read back.");
                    placement = HostedPlacementSnapshot(instance, placementMode, expectedPlacementType, actualPlacementType, hostPartKey, hostFaceKey, hostPart.Form.Id, "curve", new JObject { ["start_mm"] = PointMm(actualLine.GetEndPoint(0)), ["end_mm"] = PointMm(actualLine.GetEndPoint(1)) });
                }
                else throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown nested placement_mode: " + placementMode + ".");
            }
            if (familyTypeParameter != null)
            {
                var typeElementParameter = new[] { BuiltInParameter.ELEM_TYPE_PARAM, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM }
                    .Select(instance.get_Parameter).FirstOrDefault(candidate => candidate != null && manager.CanElementParameterBeAssociated(candidate));
                if (typeElementParameter == null) throw new CommandResultException(ErrorCodes.Unsupported, "Revit exposes no associable nested type selector for " + key + ".");
                manager.AssociateElementParameterToFamilyParameter(typeElementParameter, familyTypeParameter);
                if (manager.GetAssociatedFamilyParameter(typeElementParameter)?.Id != familyTypeParameter.Id) throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested Family Type association did not read back for " + key + ".");
                family.Regenerate();
                var expected = manager.CurrentType.AsElementId(familyTypeParameter);
                if (expected == null || expected == ElementId.InvalidElementId || instance.Symbol.Id != expected) throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested instance did not adopt the current parent type's Family Type option for " + key + ".");
            }
            var mapped = new Dictionary<string, Parameter>(StringComparer.Ordinal);
            var mappedInterfaces = new JObject();
            var parameterNames = artifact["parameter_names"] as JObject ?? new JObject();
            var parameterInterfaces = artifact["parameter_interfaces"] as JObject ?? new JObject();
            foreach (var mapping in (contract["parameter_map"] as JObject ?? new JObject()).Properties())
            {
                var childKey = mapping.Name; var parentKey = mapping.Value.Value<string>() ?? string.Empty;
                var childName = parameterNames.Value<string>(childKey) ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested component " + key + " has no verified child parameter name for " + childKey + ".");
                var parameterInterface = parameterInterfaces[childKey] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested component " + key + "." + childKey + " has no verified parameter interface.");
                var parentParameter = RequireParameter(parameters, parentKey);
                if (parameterInterface.Value<bool?>("shared_identity_required") == true)
                {
                    var mappingMode = contract.Value<string>("shared_parameter_map_mode") ?? "identity_only";
                    if (!string.Equals(mappingMode, "identity_only", StringComparison.Ordinal))
                        throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested Shared parameter mapping supports identity_only only; matching GUIDs are not a parent-to-child value propagation association.");
                    var sharedNestedParameter = FindNestedSharedGuidParameter(instance, childName, parameterInterface);
                    VerifySharedNestedParameterIdentity(key, childKey, sharedNestedParameter, parentParameter, parameterInterface);
                    mapped.Add(childKey, sharedNestedParameter);
                    mappedInterfaces[childKey] = SharedNestedParameterIdentitySnapshot(sharedNestedParameter, parentParameter, parameterInterface);
                    continue;
                }
                var childParameter = FindNestedInstanceParameter(instance, childName, parameterInterface) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested instance parameter not found: " + childName + ".");
                VerifyNestedParameterInterface(key, childKey, childParameter, parentParameter, parameterInterface);
                if (!manager.CanElementParameterBeAssociated(childParameter)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested parameter " + childName + " cannot be associated to parent parameter " + parentParameter.Definition.Name + ".");
                manager.AssociateElementParameterToFamilyParameter(childParameter, parentParameter);
                if (manager.GetAssociatedFamilyParameter(childParameter)?.Id != parentParameter.Id) throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested parameter association did not read back for " + key + "." );
                mapped.Add(childKey, childParameter);
                mappedInterfaces[childKey] = NestedParameterInterfaceSnapshot(childParameter, parentParameter, parameterInterface);
            }
            var visibilityParameter = AssociateVisibility(manager, instance, contract, parameters, "nested component " + key);
            results.Add(key, new NestedResult { Key = key, Instance = instance, Contract = contract, MappedParameters = mapped, MappedParameterInterfaces = mappedInterfaces, FamilyTypeParameter = familyTypeParameter, TypeSelections = typeSelections, VisibilityParameter = visibilityParameter, Placement = placement });
        }
        if (results.Count != (spec.Blueprint["nested_components"] as JArray ?? new JArray()).Count) throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested component read-back count does not match the Blueprint declaration.");
        return results;
    }

    /// <summary>Shared nested parameters are addressable by GUID in Revit and
    /// are not consistently returned by LookupParameter across Family
    /// templates/locales. Prefer the verified GUID, then use name lookup for
    /// non-shared interfaces. No name-only fallback can bypass the interface
    /// verification performed immediately afterwards.</summary>
    private static Parameter? FindNestedInstanceParameter(FamilyInstance instance, string childName, JObject parameterInterface)
    {
        var child = parameterInterface["child"] as JObject;
        var sharedGuid = child?.Value<string>("shared_guid");
        if (Guid.TryParse(sharedGuid, out var guid))
        {
            var byGuid = instance.get_Parameter(guid);
            if (byGuid != null) return byGuid;
        }
        return instance.GetParameters(childName).FirstOrDefault() ?? instance.LookupParameter(childName);
    }

    private static Parameter FindNestedSharedGuidParameter(FamilyInstance instance, string childName, JObject parameterInterface)
    {
        var expectedGuid = (parameterInterface["child"] as JObject)?.Value<string>("shared_guid");
        if (!Guid.TryParse(expectedGuid, out var guid)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested Shared parameter " + childName + " has no verified GUID.");
        return instance.get_Parameter(guid) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested Shared parameter GUID lookup returned no parameter: " + childName + ".");
    }

    /// <summary>For a Shared nested Family, Revit projects the matching
    /// ExternalDefinition through the GUID; it is not an independent nested
    /// element parameter that can safely be associated again.  The child RFA
    /// has already been reopen-verified by its artifact, and this checks the
    /// Parent's matching Instance Shared definition plus the actual nested
    /// GUID lookup. This is definition identity only; it does not prove, or
    /// create, parent-to-child value propagation in a Project.</summary>
    private static void VerifySharedNestedParameterIdentity(string componentKey, string childKey, Parameter nestedGuidParameter, FamilyParameter parentParameter, JObject parameterInterface)
    {
        var child = parameterInterface["child"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested component " + componentKey + "." + childKey + " has an invalid child Shared interface.");
        var parent = parameterInterface["parent"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested component " + componentKey + "." + childKey + " has an invalid parent Shared interface.");
        var childGuid = child.Value<string>("shared_guid"); var parentGuid = parent.Value<string>("shared_guid");
        if (parameterInterface.Value<bool?>("verified") != true || child.Value<bool?>("is_shared") != true || parent.Value<bool?>("is_shared") != true
            || !string.Equals(child.Value<string>("scope"), "instance", StringComparison.Ordinal) || !string.Equals(parent.Value<string>("scope"), "instance", StringComparison.Ordinal)
            || !string.Equals(child.Value<string>("name"), parent.Value<string>("name"), StringComparison.Ordinal)
            || !string.Equals(childGuid, parentGuid, StringComparison.OrdinalIgnoreCase)
            || !parentParameter.IsInstance || !parentParameter.IsShared || !string.Equals(parentParameter.GUID.ToString("D"), parentGuid, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(nestedGuidParameter.Definition.Name, child.Value<string>("name"), StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested Shared parameter identity did not match the verified child artifact and Parent definition for " + componentKey + "." + childKey + ".");
    }

    private static void VerifyNestedParameterInterface(string componentKey, string childKey, Parameter childParameter, FamilyParameter parentParameter, JObject parameterInterface)
    {
        var child = parameterInterface["child"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested component " + componentKey + "." + childKey + " has an invalid child parameter interface.");
        var parent = parameterInterface["parent"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested component " + componentKey + "." + childKey + " has an invalid parent parameter interface.");
        if (parameterInterface.Value<bool?>("verified") != true || !string.Equals(child.Value<string>("name"), childParameter.Definition.Name, StringComparison.Ordinal)
            || !string.Equals(parent.Value<string>("name"), parentParameter.Definition.Name, StringComparison.Ordinal)
            || !string.Equals(parent.Value<string>("scope"), parentParameter.IsInstance ? "instance" : "type", StringComparison.Ordinal)
            || parent.Value<bool?>("is_shared") != parentParameter.IsShared)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested parameter interface read-back does not match the verified artifact for " + componentKey + "." + childKey + ".");
        var childExternal = childParameter.Definition as ExternalDefinition;
        var childIsShared = childExternal != null;
        var childGuid = childExternal?.GUID.ToString("D");
        var parentGuid = parentParameter.IsShared ? parentParameter.GUID.ToString("D") : null;
        var expectedChildGuid = child.Value<string>("shared_guid"); var expectedParentGuid = parent.Value<string>("shared_guid");
        if (child.Value<bool?>("is_shared") != childIsShared || !string.Equals(expectedChildGuid, childGuid, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(expectedParentGuid, parentGuid, StringComparison.OrdinalIgnoreCase))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested parameter Shared GUID interface mismatch for " + componentKey + "." + childKey + ".");
        if (parameterInterface.Value<bool?>("shared_identity_required") == true
            && (!childIsShared || !parentParameter.IsInstance || !parentParameter.IsShared || string.IsNullOrWhiteSpace(childGuid) || !string.Equals(childGuid, parentGuid, StringComparison.OrdinalIgnoreCase)))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested shared parameter map requires matching child/parent Instance Shared GUIDs for " + componentKey + "." + childKey + ".");
    }

    private static JObject NestedParameterInterfaceSnapshot(Parameter childParameter, FamilyParameter parentParameter, JObject parameterInterface)
    {
        var childExternal = childParameter.Definition as ExternalDefinition; var childIsShared = childExternal != null;
        var childGuid = childExternal?.GUID.ToString("D"); var parentGuid = parentParameter.IsShared ? parentParameter.GUID.ToString("D") : null;
        var sharedIdentityRequired = parameterInterface.Value<bool?>("shared_identity_required") == true;
        return new JObject
        {
            ["child"] = new JObject { ["name"] = childParameter.Definition.Name, ["is_shared"] = childIsShared, ["shared_guid"] = childGuid },
            ["parent"] = new JObject { ["name"] = parentParameter.Definition.Name, ["is_instance"] = parentParameter.IsInstance, ["is_shared"] = parentParameter.IsShared, ["shared_guid"] = parentGuid },
            ["shared_identity_required"] = sharedIdentityRequired,
            ["same_shared_guid"] = sharedIdentityRequired ? string.Equals(childGuid, parentGuid, StringComparison.OrdinalIgnoreCase) : null,
            ["verified"] = true
        };
    }

    private static JObject SharedNestedParameterIdentitySnapshot(Parameter nestedGuidParameter, FamilyParameter parentParameter, JObject parameterInterface)
    {
        var child = parameterInterface["child"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested Shared parameter interface is invalid.");
        var parentGuid = parentParameter.GUID.ToString("D");
        return new JObject
        {
            ["child"] = new JObject { ["name"] = child.Value<string>("name"), ["is_shared"] = true, ["shared_guid"] = child.Value<string>("shared_guid") },
            ["parent"] = new JObject { ["name"] = parentParameter.Definition.Name, ["is_instance"] = parentParameter.IsInstance, ["is_shared"] = true, ["shared_guid"] = parentGuid },
            ["shared_identity_required"] = true,
            ["same_shared_guid"] = true,
            ["mapping_mode"] = "shared_guid_identity",
            ["mapping_scope"] = "definition_identity_only",
            ["nested_instance_guid_lookup"] = nestedGuidParameter != null,
            ["association"] = "not_applicable_shared_definition_identity",
            ["value_propagation_verified"] = false,
            ["value_propagation_status"] = "not_proven_by_rfa_identity_or_reopen",
            ["verified"] = true
        };
    }

    private static FamilySymbol LoadNestedSymbol(Document family, Dictionary<string, Family> loadedFamilies, JObject artifact, string label)
    {
        var path = artifact.Value<string>("family_path") ?? string.Empty;
        if (!loadedFamilies.TryGetValue(path, out var childFamily))
        {
            if (!family.LoadFamily(path, new RejectNestedOverwriteLoadOptions(), out childFamily) || childFamily == null) throw new CommandResultException(ErrorCodes.FileConflict, "Nested Blueprint Family could not be loaded safely for " + label + ".");
            loadedFamilies.Add(path, childFamily);
        }
        var expectedShared = artifact.Value<bool?>("shared") ?? false;
        var sharedParameter = childFamily.get_Parameter(BuiltInParameter.FAMILY_SHARED);
        if (sharedParameter == null || (sharedParameter.AsInteger() != 0) != expectedShared) throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested Blueprint Family sharing behavior did not match its verified artifact for " + label + ".");
        var expectedCategory = artifact.Value<string>("category") ?? string.Empty;
        var expectedCategoryElement = family.Settings.Categories.get_Item(FamilyTemplateResolver.BlueprintCategory(expectedCategory));
        if (childFamily.FamilyCategory == null || childFamily.FamilyCategory.Id != expectedCategoryElement.Id)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested Blueprint Family category did not match its verified artifact for " + label + ".");
        var expectedPlacement = ExpectedNestedPlacementType(artifact.Value<string>("template_behavior") ?? string.Empty);
        if (childFamily.FamilyPlacementType != expectedPlacement || !string.Equals(childFamily.FamilyPlacementType.ToString(), artifact.Value<string>("family_placement_type"), StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested Blueprint Family placement behavior did not match its verified artifact for " + label + ".");
        var requestedType = artifact.Value<string>("type_name") ?? string.Empty;
        return new FilteredElementCollector(family).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault(item => item.Family.Id == childFamily.Id && string.Equals(item.Name, requestedType, StringComparison.Ordinal))
            ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Nested Blueprint Family does not contain requested type " + requestedType + ".");
    }

    private static FamilyPlacementType ExpectedNestedPlacementType(string templateBehavior) => templateBehavior switch
    {
        "level_based" => FamilyPlacementType.OneLevelBased,
        "face_based" or "work_plane_based" => FamilyPlacementType.WorkPlaneBased,
        "line_based" => FamilyPlacementType.CurveBased,
        _ => throw new CommandResultException(ErrorCodes.Unsupported, "Nested Blueprint template_behavior " + templateBehavior + " has no bounded placement operation.")
    };

    private static void RequirePointOnFace(PlanarFace face, XYZ point, string key)
    {
        var projection = face.Project(point);
        if (projection == null || projection.Distance > 1e-7 || !face.IsInside(projection.UVPoint)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Nested component " + key + " placement point is outside its declared host face.");
    }

    private static void RequireCurveOnFace(PlanarFace face, Line line, string key)
    {
        for (var index = 0; index <= 8; index++) RequirePointOnFace(face, line.Evaluate(index / 8.0, true), key);
    }

    private static JObject HostedPlacementSnapshot(FamilyInstance instance, string mode, FamilyPlacementType expected, FamilyPlacementType actual, string hostPartKey, string hostFaceKey, ElementId hostId, string locationKind, JToken location)
    {
        var observedHost = instance.Host; var observedFace = instance.HostFace;
        if (observedHost?.Id != hostId || observedFace == null || observedFace.ElementId != hostId) throw new CommandResultException(ErrorCodes.VerificationFailed, "Hosted nested instance did not read back its declared parent solid host.");
        return new JObject { ["mode"] = mode, ["expected_family_placement_type"] = expected.ToString(), ["actual_family_placement_type"] = actual.ToString(), ["host_part_key"] = hostPartKey, ["host_face"] = hostFaceKey, ["host_element_id"] = observedHost.Id.Val(), ["host_face_element_id"] = observedFace.ElementId.Val(), ["location_kind"] = locationKind, ["location"] = location, ["verified"] = true };
    }

    private static JObject VectorSnapshot(XYZ vector) => new() { ["x"] = Math.Round(vector.X, 9), ["y"] = Math.Round(vector.Y, 9), ["z"] = Math.Round(vector.Z, 9) };

    private static FamilyParameter AddFamilyTypeParameter(Document family, FamilyManager manager, JObject item)
    {
        var name = item.Value<string>("name") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Family Type parameter name is missing.");
        var categoryKey = item.Value<string>("family_category") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Family Type parameter category is missing.");
        var category = family.Settings.Categories.get_Item(FamilyTemplateResolver.BlueprintCategory(categoryKey));
        if (category == null) throw new CommandResultException(ErrorCodes.TemplateInvalid, "Family Type parameter category is unavailable: " + categoryKey + ".");
#if REVIT2019 || REVIT2020 || REVIT2021
        return manager.AddParameter(name, ParameterGroup(item.Value<string>("group"), "family_type"), category, false);
#else
        return manager.AddParameter(name, ParameterGroupId(item.Value<string>("group"), "family_type"), category, false);
#endif
    }

    private sealed class RejectNestedOverwriteLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues) { overwriteParameterValues = false; return false; }
        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues) { source = FamilySource.Family; overwriteParameterValues = false; return false; }
    }

    private static JArray CreateMirrors(Document family, FamilyBlueprintSpec spec, Dictionary<string, PartResult> parts, Dictionary<string, NestedResult> nested)
    {
        var results = new JArray();
        foreach (var contract in (spec.Blueprint["mirrors"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var key = contract.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Mirror key is missing.");
            var targetKind = contract.Value<string>("target_kind") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Mirror target_kind is missing.");
            var targetKeys = (contract["target_keys"] as JArray ?? new JArray()).Values<string>().Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToList();
            var elementIds = new List<ElementId>();
            foreach (var targetKey in targetKeys)
            {
                if (targetKind == "part" && parts.TryGetValue(targetKey, out var part)) elementIds.Add(part.Form.Id);
                else if (targetKind == "nested_component" && nested.TryGetValue(targetKey, out var component)) elementIds.Add(component.Instance.Id);
                else throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Mirror " + key + " references unknown " + targetKind + " target " + targetKey + ".");
            }
            if (elementIds.Count == 0 || !ElementTransformUtils.CanMirrorElements(family, elementIds)) throw new CommandResultException(ErrorCodes.Unsupported, "Revit cannot mirror the declared target set for " + key + ".");
            var normal = Point(contract["plane_normal"], "mirror plane_normal");
            if (normal.GetLength() < 1e-9) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Mirror plane_normal must be non-zero.");
            var plane = Plane.CreateByNormalAndOrigin(normal.Normalize(), Point(contract["plane_origin_mm"], "mirror plane_origin_mm"));
            var mirrored = ElementTransformUtils.MirrorElements(family, elementIds, plane, contract.Value<bool>("copy")); family.Regenerate();
            if (mirrored.Count == 0 || mirrored.Any(id => family.GetElement(id) == null)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Mirror result could not be read back for " + key + ".");
            results.Add(new JObject { ["key"] = key, ["target_kind"] = targetKind, ["target_keys"] = new JArray(targetKeys), ["copy"] = contract.Value<bool>("copy"), ["result_element_ids"] = new JArray(mirrored.Select(id => id.Val())), ["verified"] = true });
        }
        if (results.Count != (spec.Blueprint["mirrors"] as JArray ?? new JArray()).Count) throw new CommandResultException(ErrorCodes.VerificationFailed, "Mirror read-back count does not match the Blueprint declaration.");
        return results;
    }

    private static string LegacyReferenceType(string? strength) => strength switch
    {
        "strong" => "strong", "weak" => "weak", "not_reference" => "not_reference",
        _ => throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Reference plane must declare reference_type or legacy strength.")
    };

    private static FamilyInstanceReferenceType ParseReferenceType(string key) => key switch
    {
        "left" => FamilyInstanceReferenceType.Left,
        "center_left_right" => FamilyInstanceReferenceType.CenterLeftRight,
        "right" => FamilyInstanceReferenceType.Right,
        "front" => FamilyInstanceReferenceType.Front,
        "center_front_back" => FamilyInstanceReferenceType.CenterFrontBack,
        "back" => FamilyInstanceReferenceType.Back,
        "bottom" => FamilyInstanceReferenceType.Bottom,
        "center_elevation" => FamilyInstanceReferenceType.CenterElevation,
        "top" => FamilyInstanceReferenceType.Top,
        "strong" => FamilyInstanceReferenceType.StrongReference,
        "weak" => FamilyInstanceReferenceType.WeakReference,
        "not_reference" => FamilyInstanceReferenceType.NotAReference,
        _ => throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown reference_type " + key + ".")
    };

    internal static string ReferenceTypeKey(FamilyInstanceReferenceType type) => type switch
    {
        FamilyInstanceReferenceType.Left => "left",
        FamilyInstanceReferenceType.CenterLeftRight => "center_left_right",
        FamilyInstanceReferenceType.Right => "right",
        FamilyInstanceReferenceType.Front => "front",
        FamilyInstanceReferenceType.CenterFrontBack => "center_front_back",
        FamilyInstanceReferenceType.Back => "back",
        FamilyInstanceReferenceType.Bottom => "bottom",
        FamilyInstanceReferenceType.CenterElevation => "center_elevation",
        FamilyInstanceReferenceType.Top => "top",
        FamilyInstanceReferenceType.StrongReference => "strong",
        FamilyInstanceReferenceType.WeakReference => "weak",
        FamilyInstanceReferenceType.NotAReference => "not_reference",
        _ => "unknown_" + (int)type
    };

    // ELEM_REFERENCE_NAME stores the nine named roles as 0..8, but its three
    // generic choices use Revit's internal 12..14 values rather than the
    // FamilyInstanceReferenceType enum values 11, 9 and 10.  Keep this adapter
    // explicit: casting AsInteger() directly silently misclassifies a freshly
    // created Weak Reference (14) and writes the wrong values for Strong/None.
    private static int ReferenceParameterValue(FamilyInstanceReferenceType type) => type switch
    {
        FamilyInstanceReferenceType.NotAReference => 12,
        FamilyInstanceReferenceType.StrongReference => 13,
        FamilyInstanceReferenceType.WeakReference => 14,
        _ => (int)type
    };

    private static FamilyInstanceReferenceType ReferenceTypeFromParameterValue(int value) => value switch
    {
        12 => FamilyInstanceReferenceType.NotAReference,
        13 => FamilyInstanceReferenceType.StrongReference,
        14 => FamilyInstanceReferenceType.WeakReference,
        >= 0 and <= 8 => (FamilyInstanceReferenceType)value,
        _ => throw new CommandResultException(ErrorCodes.VerificationFailed, "Reference Plane exposes unknown Is Reference value " + value + ".")
    };

    private static Reference ReferencePlaneReference(ReferencePlane plane, string context)
    {
        var reference = plane.GetReference();
        if (reference == null || reference.ElementId == ElementId.InvalidElementId)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Reference Plane has no stable native reference for " + context + ".");
        return reference;
    }

    private static Category ApplyReferencePlaneSubcategory(Document family, JObject contract)
    {
        var parent = family.Settings.Categories.get_Item(BuiltInCategory.OST_CLines)
            ?? throw new CommandResultException(ErrorCodes.Unsupported, "The selected template does not expose the Reference Planes category.");
        var name = contract.Value<string>("name") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Reference-plane subcategory name is missing.");
        Category subcategory;
        if (parent.SubCategories.Contains(name)) subcategory = parent.SubCategories.get_Item(name);
        else
        {
            if (!parent.CanAddSubcategory) throw new CommandResultException(ErrorCodes.Unsupported, "The selected template cannot add Reference Plane subcategories.");
            subcategory = family.Settings.Categories.NewSubcategory(parent, name);
        }
        var color = contract["color_rgb"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Reference-plane subcategory color_rgb is missing.");
        subcategory.LineColor = new Color((byte)color.Value<int>("r"), (byte)color.Value<int>("g"), (byte)color.Value<int>("b"));
        subcategory.SetLineWeight(contract.Value<int>("projection_line_weight"), GraphicsStyleType.Projection);
        if (contract.Value<string>("line_pattern_name") is string requestedPatternName)
        {
            var pattern = LinePatternElement.GetLinePatternElementByName(family, requestedPatternName);
            if (pattern == null || !string.Equals(pattern.Name, requestedPatternName, StringComparison.Ordinal))
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Reference-plane line pattern was not found by exact name: " + requestedPatternName + ".");
            subcategory.SetLinePatternId(pattern.Id, GraphicsStyleType.Projection);
        }
        family.Regenerate();
        var readBack = ReferencePlaneSubcategorySnapshot(family, subcategory);
        if (!string.Equals(readBack.Value<string>("name"), name, StringComparison.Ordinal)
            || readBack.Value<int>("projection_line_weight") != contract.Value<int>("projection_line_weight")
            || !JToken.DeepEquals(readBack["color_rgb"], color)
            || contract.Value<string>("line_pattern_name") is string linePatternName && !string.Equals(readBack.Value<string>("line_pattern_name"), linePatternName, StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Reference-plane subcategory graphics did not read back for " + name + ".");
        return subcategory;
    }

    private static JObject ReferencePlaneSubcategorySnapshot(Document family, Category subcategory)
    {
        var color = subcategory.LineColor;
        var patternId = subcategory.GetLinePatternId(GraphicsStyleType.Projection);
        var patternName = family.GetElement(patternId) is LinePatternElement pattern ? pattern.Name
            : patternId.Val() == LinePatternElement.GetSolidPatternId().Val() ? "<Solid>" : null;
        return new JObject
        {
            ["id"] = subcategory.Id.Val(), ["name"] = subcategory.Name,
            ["color_rgb"] = new JObject { ["r"] = color.Red, ["g"] = color.Green, ["b"] = color.Blue },
            ["projection_line_weight"] = subcategory.GetLineWeight(GraphicsStyleType.Projection),
            ["line_pattern_id"] = patternId.Val(), ["line_pattern_name"] = patternName
        };
    }

    internal static JObject ReferencePlaneSnapshot(Document family, ReferencePlane plane)
    {
        var typeParameter = plane.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME);
        var originParameter = plane.get_Parameter(BuiltInParameter.DATUM_PLANE_DEFINES_ORIGIN);
        var subcategoryParameter = plane.get_Parameter(BuiltInParameter.CLINE_SUBCATEGORY);
        JObject? subcategory = null;
        if (subcategoryParameter?.AsElementId() is ElementId subcategoryId && subcategoryId != ElementId.InvalidElementId)
        {
            var category = Category.GetCategory(family, subcategoryId);
            if (category != null) subcategory = ReferencePlaneSubcategorySnapshot(family, category);
        }
        var geometry = plane.GetPlane();
        return new JObject
        {
            ["id"] = plane.Id.Val(), ["name"] = plane.Name,
            ["reference_type"] = typeParameter == null ? null : ReferenceTypeKey(ReferenceTypeFromParameterValue(typeParameter.AsInteger())),
            ["reference_type_parameter_value"] = typeParameter?.AsInteger(),
            ["reference_type_display"] = typeParameter?.AsValueString(),
            ["defines_origin"] = originParameter?.AsInteger() == 1,
            ["subcategory"] = subcategory,
            ["plane_geometry"] = new JObject { ["origin_mm"] = PointMmSnapshot(geometry.Origin), ["normal"] = new JObject { ["x"] = geometry.Normal.X, ["y"] = geometry.Normal.Y, ["z"] = geometry.Normal.Z } }
        };
    }

    private static void PrepareReferenceFrameworkTemplatePlanes(Document family, IReadOnlyCollection<JObject> planeContracts)
    {
        var stableKeys = new HashSet<string>(new[] { "left", "center_left_right", "right", "front", "center_front_back", "back", "bottom", "center_elevation", "top" }, StringComparer.Ordinal);
        var requestedStableTypes = new HashSet<int>(planeContracts
            .Select(item => item.Value<string>("reference_type") ?? LegacyReferenceType(item.Value<string>("strength")))
            .Where(stableKeys.Contains).Select(item => ReferenceParameterValue(ParseReferenceType(item))));
        var customOrigin = planeContracts.Any(item => item.Value<bool?>("defines_origin") == true);
        if (requestedStableTypes.Count == 0 && !customOrigin) return;
        foreach (var existing in new FilteredElementCollector(family).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>())
        {
            var referenceType = existing.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME);
            if (referenceType != null && requestedStableTypes.Contains(referenceType.AsInteger()))
            {
                if (referenceType.IsReadOnly || !referenceType.Set(ReferenceParameterValue(FamilyInstanceReferenceType.NotAReference)))
                    throw new CommandResultException(ErrorCodes.Unsupported, "The selected template has a named Reference Plane that cannot be released for the Blueprint: " + existing.Name + ".");
            }
            if (!customOrigin) continue;
            var origin = existing.get_Parameter(BuiltInParameter.DATUM_PLANE_DEFINES_ORIGIN);
            if (origin == null || origin.AsInteger() == 0) continue;
            if (origin.IsReadOnly || !origin.Set(0)) throw new CommandResultException(ErrorCodes.Unsupported, "The selected template has an existing Defines Origin plane that cannot be cleared: " + existing.Name + ".");
        }
        family.Regenerate();
        var remainingTemplatePlanes = new FilteredElementCollector(family).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>().ToList();
        if (remainingTemplatePlanes.Any(item => requestedStableTypes.Contains(item.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME)?.AsInteger() ?? int.MinValue)))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "A template Reference Plane still owns a named reference role requested by the Blueprint.");
        if (customOrigin && remainingTemplatePlanes.Any(item => item.get_Parameter(BuiltInParameter.DATUM_PLANE_DEFINES_ORIGIN)?.AsInteger() == 1))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Existing template insertion-origin planes were not cleared before applying the Blueprint origin.");
    }

    private static ModelCurve CreateReferenceLine(Document family, JObject contract)
    {
        var key = contract.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Reference line key is missing.");
        var planeKey = contract.Value<string>("plane") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Reference line plane is missing.");
        var start = Point(contract["start_mm"], "reference line start_mm"); var end = Point(contract["end_mm"], "reference line end_mm");
        var sketch = SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(PlaneNormal(planeKey), start));
        var curve = family.FamilyCreate.NewModelCurve(Line.CreateBound(start, end), sketch); curve.ChangeToReferenceLine(); family.Regenerate();
        if (!curve.IsReferenceLine || curve.GeometryCurve.Reference == null) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reference line " + key + " did not expose a stable Revit reference.");
        return curve;
    }

    private static JArray ConstrainAngularReferenceLines(Document family, View view, string planeKey, XYZ pivot, ModelCurve baseline, ModelCurve driven, string dimensionKey)
    {
        XYZ uAxis; XYZ vAxis;
        switch (planeKey)
        {
            case "xy": uAxis = XYZ.BasisX; vAxis = XYZ.BasisY; break;
            case "xz": uAxis = XYZ.BasisX; vAxis = XYZ.BasisZ; break;
            case "yz": uAxis = XYZ.BasisY; vAxis = XYZ.BasisZ; break;
            default: throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown angular constraint plane " + planeKey + ".");
        }
        var extent = Mm(1000); var normal = PlaneNormal(planeKey); var safe = SafeInternalKey(dimensionKey);
        ReferencePlane Datum(XYZ direction, string suffix)
        {
            var plane = family.FamilyCreate.NewReferencePlane2(pivot.Add(direction.Multiply(-extent)), pivot.Add(direction.Multiply(extent)), normal, view);
            plane.Name = "_DSCons_" + safe + "_" + suffix; plane.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME)?.Set(ReferenceParameterValue(FamilyInstanceReferenceType.NotAReference)); return plane;
        }
        var uZero = Datum(vAxis, "pivot_u_zero"); var vZero = Datum(uAxis, "pivot_v_zero");
        var baselineCurve = baseline.GeometryCurve; var drivenCurve = driven.GeometryCurve;
        var baselineStart = baselineCurve.GetEndPointReference(0) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Angular baseline start has no stable endpoint reference.");
        var drivenStart = drivenCurve.GetEndPointReference(0) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Angular driven line start has no stable endpoint reference.");
        var constraints = new[]
        {
            family.FamilyCreate.NewAlignment(view, new Reference(vZero), baselineCurve.Reference),
            family.FamilyCreate.NewAlignment(view, new Reference(uZero), baselineStart),
            family.FamilyCreate.NewAlignment(view, new Reference(uZero), drivenStart),
            family.FamilyCreate.NewAlignment(view, new Reference(vZero), drivenStart)
        };
        if (constraints.Any(item => item == null)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected an angular Reference Line pivot/baseline constraint for " + dimensionKey + ".");
        family.Regenerate();
        return new JArray(constraints.Select((item, index) => new JObject { ["constraint_index"] = index, ["element_id"] = item.Id.Val(), ["verified"] = true }));
    }

    private static JObject CreateReferenceGraph(Document family, FamilyBlueprintSpec spec, FamilyManager manager, Dictionary<string, FamilyParameter> parameters, Dictionary<string, PartResult> parts, Dictionary<string, ModelCurve> precreatedReferenceLines)
    {
        var declared = new Dictionary<string, DeclaredReference>(StringComparer.Ordinal);
        var planes = new Dictionary<string, ReferencePlane>(StringComparer.Ordinal);
        var planeContracts = (spec.Blueprint["reference_planes"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        PrepareReferenceFrameworkTemplatePlanes(family, planeContracts);
        var subcategoryContracts = (spec.Blueprint["reference_plane_subcategories"] as JArray ?? new JArray()).OfType<JObject>()
            .ToDictionary(item => item.Value<string>("key") ?? string.Empty, item => item, StringComparer.Ordinal);
        var planeReadBack = new JArray(); var lineReadBack = new JArray(); var dimensionReadBack = new JArray(); var alignmentReadBack = new JArray();

        foreach (var contract in planeContracts)
        {
            var key = contract.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Reference plane key is missing.");
            var viewPlane = contract.Value<string>("view_plane") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Reference plane view_plane is missing.");
            var view = ViewForPlane(family, viewPlane, "reference plane");
            var bubbleEnd = Point(contract["bubble_end_mm"], "reference plane bubble_end_mm");
            var freeEnd = Point(contract["free_end_mm"], "reference plane free_end_mm");
            var cutDirection = Direction(contract["cut_vector"], "reference plane cut_vector");
            var thirdPoint = bubbleEnd.Add(freeEnd).Multiply(.5).Add(cutDirection.Multiply(Math.Max(bubbleEnd.DistanceTo(freeEnd), Mm(1))));
            var plane = family.FamilyCreate.NewReferencePlane2(bubbleEnd, freeEnd, thirdPoint, view);
            plane.Name = contract.Value<string>("name") ?? key;
            var referenceTypeKey = contract.Value<string>("reference_type") ?? LegacyReferenceType(contract.Value<string>("strength"));
            var referenceType = ParseReferenceType(referenceTypeKey);
            var strengthParameter = plane.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME);
            if (strengthParameter == null) throw new CommandResultException(ErrorCodes.Unsupported, "Reference type is unavailable for " + key + ".");
            var nativeReferenceValue = strengthParameter.AsInteger();
            var nativeReferenceType = ReferenceTypeFromParameterValue(nativeReferenceValue);
            // Some Autodesk Family templates expose ELEM_REFERENCE_NAME as read-only
            // on a plane created through NewReferencePlane2.  A read-only value is
            // still valid evidence when it already equals the requested contract;
            // never claim a different semantic role merely because the native
            // Reference itself is usable for dimensions or alignments.
            var requestedReferenceValue = ReferenceParameterValue(referenceType);
            if (nativeReferenceValue != requestedReferenceValue)
            {
                if (strengthParameter.IsReadOnly)
                    throw new CommandResultException(ErrorCodes.Unsupported, "Reference plane " + key + " has read-only native reference type " + ReferenceTypeKey(nativeReferenceType) + "; requested " + referenceTypeKey + ".");
                if (!strengthParameter.Set(requestedReferenceValue))
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reference type could not be written for " + key + "; native=" + ReferenceTypeKey(nativeReferenceType) + "; requested=" + referenceTypeKey + "; storage=" + strengthParameter.StorageType + "; read_only=" + strengthParameter.IsReadOnly + ".");
            }
            var definesOrigin = contract.Value<bool?>("defines_origin") == true;
            var originParameter = plane.get_Parameter(BuiltInParameter.DATUM_PLANE_DEFINES_ORIGIN);
            if (originParameter == null) throw new CommandResultException(ErrorCodes.Unsupported, "Defines Origin is unavailable for reference plane " + key + ".");
            var requestedOrigin = definesOrigin ? 1 : 0;
            if (originParameter.AsInteger() != requestedOrigin)
            {
                if (originParameter.IsReadOnly) throw new CommandResultException(ErrorCodes.Unsupported, "Reference plane " + key + " has a read-only Defines Origin value that differs from the Blueprint.");
                if (!originParameter.Set(requestedOrigin)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Defines Origin could not be written for " + key + ".");
            }
            if (contract.Value<string>("subcategory_key") is string subcategoryKey)
            {
                if (!subcategoryContracts.TryGetValue(subcategoryKey, out var subcategoryContract)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Reference plane " + key + " uses unknown subcategory " + subcategoryKey + ".");
                var subcategory = ApplyReferencePlaneSubcategory(family, subcategoryContract);
                var subcategoryParameter = plane.get_Parameter(BuiltInParameter.CLINE_SUBCATEGORY);
                if (subcategoryParameter == null || subcategoryParameter.IsReadOnly) throw new CommandResultException(ErrorCodes.Unsupported, "Reference Plane subcategory is unavailable for " + key + ".");
                if (subcategoryParameter.AsElementId() != subcategory.Id && !subcategoryParameter.Set(subcategory.Id)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reference Plane subcategory could not be written for " + key + ".");
            }
            family.Regenerate();
            if (strengthParameter.AsInteger() != requestedReferenceValue || originParameter.AsInteger() != requestedOrigin) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reference-plane semantics did not read back for " + key + ".");
            // Dimensions and alignments consume the native datum reference.
            // The Reference Plane cut vector is normalized above so this
            // reference remains axis-stable when the transaction commits.
            var dimensionReference = ReferencePlaneReference(plane, key + " dimension witness");
            planes.Add(key, plane); declared.Add(key, new DeclaredReference { Key = key, Element = plane, Reference = dimensionReference });
            var snapshot = ReferencePlaneSnapshot(family, plane); snapshot["key"] = key; snapshot["view_plane"] = viewPlane; snapshot["legacy_strength"] = contract.Value<string>("strength"); snapshot["dimension_reference_type"] = dimensionReference.ElementReferenceType.ToString(); snapshot["verified"] = true;
            if (contract.Value<string>("subcategory_key") is string expectedSubcategoryKey)
            {
                var expectedSubcategory = subcategoryContracts[expectedSubcategoryKey];
                var observedSubcategory = snapshot["subcategory"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reference-plane subcategory did not read back for " + key + ".");
                if (!string.Equals(observedSubcategory.Value<string>("name"), expectedSubcategory.Value<string>("name"), StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reference-plane subcategory name did not read back for " + key + ".");
            }
            planeReadBack.Add(snapshot);
        }
        if (planeContracts.Any(item => item.Value<bool?>("defines_origin") == true))
        {
            var originPlanes = new FilteredElementCollector(family).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>()
                .Where(item => item.get_Parameter(BuiltInParameter.DATUM_PLANE_DEFINES_ORIGIN)?.AsInteger() == 1).ToList();
            if (originPlanes.Count != 2) throw new CommandResultException(ErrorCodes.VerificationFailed, "Custom insertion origin must read back as exactly two Reference Planes; observed " + originPlanes.Count + ".");
            var expectedOriginNames = new HashSet<string>(planeContracts.Where(item => item.Value<bool?>("defines_origin") == true).Select(item => item.Value<string>("name") ?? string.Empty), StringComparer.OrdinalIgnoreCase);
            if (originPlanes.Any(item => !expectedOriginNames.Contains(item.Name))) throw new CommandResultException(ErrorCodes.VerificationFailed, "A template Reference Plane still participates in the custom insertion origin.");
        }

        foreach (var contract in (spec.Blueprint["reference_lines"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var key = contract.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Reference line key is missing.");
            var planeKey = contract.Value<string>("plane") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Reference line plane is missing.");
            var curve = precreatedReferenceLines.TryGetValue(key, out var existing) ? existing : CreateReferenceLine(family, contract);
            var reference = curve.GeometryCurve.Reference ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reference line " + key + " has no stable geometry reference.");
            declared.Add(key, new DeclaredReference { Key = key, Element = curve, Reference = reference });
            lineReadBack.Add(new JObject { ["key"] = key, ["element_id"] = curve.Id.Val(), ["plane"] = planeKey, ["is_reference_line"] = curve.IsReferenceLine, ["verified"] = curve.IsReferenceLine });
        }

        foreach (var contract in (spec.Blueprint["dimensions"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var key = contract.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Dimension key is missing.");
            var viewPlane = contract.Value<string>("view_plane") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Dimension view_plane is missing.");
            var kind = contract.Value<string>("kind") ?? "linear";
            if (kind == "radial")
            {
                var partKey = contract.Value<string>("part_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Radial dimension part_key is missing.");
                if (!parts.TryGetValue(partKey, out var part) || part.Form is not Extrusion extrusion) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Radial dimension " + key + " requires an extrusion part.");
                family.Regenerate();
                var loopIndex = contract.Value<int>("profile_loop_index");
                var loops = extrusion.Sketch.Profile.Cast<CurveArray>().ToList();
                if (loopIndex < 0 || loopIndex >= loops.Count) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Radial dimension " + key + " profile loop is unavailable.");
                var arc = loops[loopIndex].Cast<Curve>().OfType<Arc>().FirstOrDefault() ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Radial dimension " + key + " profile loop has no circular curve.");
                var modelCurve = arc.Reference == null ? null : family.GetElement(arc.Reference.ElementId) as ModelCurve;
                var arcReference = modelCurve?.GeometryCurve.Reference ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Radial dimension " + key + " has no stable circular profile reference.");
                var radialDimension = family.FamilyCreate.NewRadialDimension(ViewForPlane(family, viewPlane, "radial dimension"), arcReference, Point(contract["leader_point_mm"], "radial dimension leader_point_mm"))
                    ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected declared radial dimension " + key + ".");
                var parameterKey = contract.Value<string>("parameter_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Radial dimension parameter_key is missing.");
                radialDimension.FamilyLabel = RequireParameter(parameters, parameterKey); family.Regenerate();
                if (radialDimension.FamilyLabel?.Id != RequireParameter(parameters, parameterKey).Id) throw new CommandResultException(ErrorCodes.VerificationFailed, "Radial dimension " + key + " label did not read back.");
                dimensionReadBack.Add(new JObject { ["key"] = key, ["kind"] = "radial", ["element_id"] = radialDimension.Id.Val(), ["parameter_key"] = parameterKey, ["part_key"] = partKey, ["profile_loop_index"] = loopIndex, ["verified"] = true });
                continue;
            }
            if (kind == "angular")
            {
                var angularReferenceKeys = (contract["reference_keys"] as JArray ?? new JArray()).Values<string>().Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToList();
                if (angularReferenceKeys.Count != 2 || !declared.TryGetValue(angularReferenceKeys[0], out var first) || !declared.TryGetValue(angularReferenceKeys[1], out var second)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Angular dimension " + key + " requires two declared references.");
                XYZ xAxis; XYZ yAxis;
                switch (viewPlane)
                {
                    case "xy": xAxis = XYZ.BasisX; yAxis = XYZ.BasisY; break;
                    case "xz": xAxis = XYZ.BasisX; yAxis = XYZ.BasisZ; break;
                    case "yz": xAxis = XYZ.BasisY; yAxis = XYZ.BasisZ; break;
                    default: throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown angular dimension view plane " + viewPlane + ".");
                }
                var view = ViewForPlane(family, viewPlane, "angular dimension"); var center = Point(contract["arc_center_mm"], "angular dimension arc_center_mm");
                JArray? pivotConstraints = null;
                if (parts.Values.Any(part => part.Contract["path"]?.Value<string>("kind") == "reference_line" && part.Contract["path"]?.Value<string>("angular_dimension_key") == key))
                {
                    if (first.Element is not ModelCurve baseline || second.Element is not ModelCurve driven) throw new CommandResultException(ErrorCodes.VerificationFailed, "Angular sweep requires ModelCurve Reference Lines.");
                    pivotConstraints = ConstrainAngularReferenceLines(family, view, viewPlane, center, baseline, driven, key);
                }
                var arc = Arc.Create(center, Mm(contract.Value<double>("arc_radius_mm")), Radians(contract.Value<double>("start_angle_degrees")), Radians(contract.Value<double>("end_angle_degrees")), xAxis, yAxis);
                var angularDimension = family.FamilyCreate.NewAngularDimension(view, arc, first.Reference, second.Reference)
                    ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected declared angular dimension " + key + ".");
                var parameterKey = contract.Value<string>("parameter_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Angular dimension parameter_key is missing.");
                angularDimension.FamilyLabel = RequireParameter(parameters, parameterKey); family.Regenerate();
                if (angularDimension.FamilyLabel?.Id != RequireParameter(parameters, parameterKey).Id) throw new CommandResultException(ErrorCodes.VerificationFailed, "Angular dimension " + key + " label did not read back.");
                dimensionReadBack.Add(new JObject { ["key"] = key, ["kind"] = "angular", ["element_id"] = angularDimension.Id.Val(), ["parameter_key"] = parameterKey, ["reference_keys"] = new JArray(angularReferenceKeys), ["pivot_constraints"] = pivotConstraints, ["drives_sweep_geometry"] = pivotConstraints != null, ["verified"] = true });
                continue;
            }
            var references = new ReferenceArray(); var referenceKeys = new JArray();
            foreach (var referenceKey in (contract["reference_keys"] as JArray ?? new JArray()).Values<string>().Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!))
            {
                if (!declared.TryGetValue(referenceKey, out var item)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Dimension " + key + " references unknown declaration " + referenceKey + ".");
                references.Append(item.Reference); referenceKeys.Add(referenceKey);
            }
            if (references.Size < 2) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Dimension " + key + " requires at least two references.");
            Dimension dimension;
            try
            {
                dimension = family.FamilyCreate.NewDimension(ViewForPlane(family, viewPlane, "dimension"), Line.CreateBound(Point(contract["line_start_mm"], "dimension line_start_mm"), Point(contract["line_end_mm"], "dimension line_end_mm")), references)
                    ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected declared dimension " + key + ".");
            }
            catch (CommandResultException) { throw; }
            catch (Exception exception)
            {
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected declared dimension " + key + ": " + exception.Message);
            }
            if (contract.Value<bool?>("equality") == true)
            {
                dimension.AreSegmentsEqual = true; family.Regenerate();
                if (!dimension.AreSegmentsEqual) throw new CommandResultException(ErrorCodes.VerificationFailed, "Equal dimension " + key + " did not read back.");
                dimensionReadBack.Add(new JObject { ["key"] = key, ["kind"] = "linear", ["element_id"] = dimension.Id.Val(), ["equality"] = true, ["segment_count"] = dimension.Segments.Size, ["reference_keys"] = referenceKeys, ["verified"] = true });
            }
            else
            {
                var parameterKey = contract.Value<string>("parameter_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Dimension parameter_key is missing.");
                var parameter = RequireParameter(parameters, parameterKey);
                var currentType = manager.CurrentType ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Linear dimension " + key + " has no active Family Type before label assignment.");
                var declaredTypeValue = currentType.AsDouble(parameter) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Linear dimension " + key + " Length parameter has no declared Type value before label assignment.");
                dimension.FamilyLabel = parameter;
                var valueAfterLabel = currentType.AsDouble(parameter) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Linear dimension " + key + " Length parameter lost its Type value after label assignment.");
                // Revit 2023 can replace a newly labelled linear-dimension Type
                // value with the template sentinel (-1 ft). Restore the already
                // validated Blueprint Type value through FamilyManager so the
                // label drives the datums before flex verification. This is not
                // a guessed value or geometry bypass: failure to set/read it is
                // fatal and min/nominal/max still runs immediately afterwards.
                manager.CurrentType = currentType;
                manager.Set(parameter, declaredTypeValue);
                manager.CurrentType = currentType;
                family.Regenerate();
                var restoredTypeValue = currentType.AsDouble(parameter) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Linear dimension " + key + " Length parameter lost its restored Type value.");
                if (Math.Abs(restoredTypeValue - declaredTypeValue) > Mm(.001)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Linear dimension " + key + " did not retain its declared Type value after FamilyLabel assignment.");
                var labelVerified = dimension.FamilyLabel?.Id == parameter.Id;
                if (!labelVerified) throw new CommandResultException(ErrorCodes.VerificationFailed, "Linear dimension " + key + " label did not read back after restoring the declared Type value.");
                dimensionReadBack.Add(new JObject { ["key"] = key, ["kind"] = "linear", ["element_id"] = dimension.Id.Val(), ["parameter_key"] = parameterKey, ["reference_keys"] = referenceKeys, ["value_before_label_mm"] = declaredTypeValue * 304.8, ["value_after_label_mm"] = valueAfterLabel * 304.8, ["value_after_restore_mm"] = restoredTypeValue * 304.8, ["declared_type_value_restored_after_family_label"] = true, ["verified"] = true });
            }
        }

        family.Regenerate();
        foreach (var contract in (spec.Blueprint["alignments"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var key = contract.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Alignment key is missing.");
            var referenceKey = contract.Value<string>("reference_plane_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Alignment reference_plane_key is missing.");
            var partKey = contract.Value<string>("part_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Alignment part_key is missing.");
            if (!planes.TryGetValue(referenceKey, out var plane) || !parts.TryGetValue(partKey, out var part)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Alignment " + key + " references an unknown plane or part.");
            var faceKey = contract.Value<string>("part_face") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Alignment part_face is missing.");
            var faceReference = Face(part.Form, FaceNormal(faceKey), partKey).Reference;
            Dimension alignment;
            try
            {
                alignment = family.FamilyCreate.NewAlignment(ViewForPlane(family, contract.Value<string>("view_plane") ?? string.Empty, "alignment"), ReferencePlaneReference(plane, key), faceReference)
                    ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected declared alignment " + key + ".");
            }
            catch (CommandResultException) { throw; }
            catch (Exception exception)
            {
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected declared alignment " + key + ": " + exception.Message);
            }
            alignmentReadBack.Add(new JObject { ["key"] = key, ["element_id"] = alignment.Id.Val(), ["reference_plane_key"] = referenceKey, ["part_key"] = partKey, ["part_face"] = faceKey, ["verified"] = true });
        }

        if (planeReadBack.Count != (spec.Blueprint["reference_planes"] as JArray ?? new JArray()).Count || lineReadBack.Count != (spec.Blueprint["reference_lines"] as JArray ?? new JArray()).Count || dimensionReadBack.Count != (spec.Blueprint["dimensions"] as JArray ?? new JArray()).Count || alignmentReadBack.Count != (spec.Blueprint["alignments"] as JArray ?? new JArray()).Count)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Reference graph read-back count does not match the Blueprint declaration.");
        return new JObject { ["reference_planes"] = planeReadBack, ["reference_lines"] = lineReadBack, ["dimensions"] = dimensionReadBack, ["alignments"] = alignmentReadBack, ["verified"] = true };
    }

    private static JArray CreateDetailLines(Document family, FamilyBlueprintSpec spec, JObject referenceGraph)
    {
        var referencePlanes = new Dictionary<string, ReferencePlane>(StringComparer.Ordinal);
        foreach (var snapshot in (referenceGraph["reference_planes"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var key = snapshot.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reference graph has a plane without a key.");
            var id = snapshot.Value<long?>("id") ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reference graph plane " + key + " has no element id.");
            referencePlanes[key] = family.GetElement(RevitIdCompatibility.Eid(id)) as ReferencePlane
                ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reference graph plane " + key + " cannot be resolved while creating Detail Lines.");
        }
        var result = new JArray();
        foreach (var contract in (spec.Blueprint["detail_lines"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var declarationKey = contract.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail line key is missing.");
            var planeKey = contract.Value<string>("plane") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail line plane is missing.");
            var view = ViewForPlane(family, planeKey, "detail line");
            var points = (contract["points_mm"] as JArray ?? new JArray()).Select((item, index) => Point(item, "detail line point " + index)).ToList();
            if (points.Count < 2) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail line requires at least two points.");
            var endpointBindings = (contract["endpoint_bindings"] as JArray ?? new JArray()).OfType<JObject>()
                .ToDictionary(item => item.Value<string>("endpoint") ?? string.Empty, item => item, StringComparer.Ordinal);
            var role = contract.Value<string>("role") ?? declarationKey;
            var subcategory = ResolvePresentationSubcategory(family, spec, contract, "DSCons Detail " + role);
            var subcategoryName = subcategory.Name;
            var lineStyle = subcategory.GetGraphicsStyle(GraphicsStyleType.Projection) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Detail-line subcategory has no projection style."); var appliedBindings = 0;

            void BindEndpoint(DetailCurve curve, int endpointIndex, string endpoint, JArray curveBindings)
            {
                if (!endpointBindings.TryGetValue(endpoint, out var binding)) return;
                var referencePlaneKey = binding.Value<string>("reference_plane_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail line " + declarationKey + " " + endpoint + " endpoint binding has no Reference Plane key.");
                if (!referencePlanes.TryGetValue(referencePlaneKey, out var referencePlane)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail line " + declarationKey + " " + endpoint + " endpoint binding references an unavailable Reference Plane " + referencePlaneKey + ".");
                family.Regenerate();
                var endpointReference = curve.GeometryCurve.GetEndPointReference(endpointIndex)
                    ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Detail line " + declarationKey + " " + endpoint + " endpoint has no stable Revit reference.");
                Dimension alignment;
                try
                {
                    alignment = family.FamilyCreate.NewAlignment(view, ReferencePlaneReference(referencePlane, declarationKey + " " + endpoint), endpointReference)
                        ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected Detail Line " + declarationKey + " " + endpoint + " endpoint alignment to Reference Plane " + referencePlaneKey + ".");
                }
                catch (CommandResultException) { throw; }
                catch (Exception exception)
                {
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected Detail Line " + declarationKey + " " + endpoint + " endpoint alignment to Reference Plane " + referencePlaneKey + ": " + exception.Message);
                }
                family.Regenerate();
                VerifyModelLineEndpointOnReferencePlane(curve, endpointIndex, referencePlane, "Detail line " + declarationKey + " " + endpoint + " endpoint");
                curveBindings.Add(new JObject { ["endpoint"] = endpoint, ["reference_plane_key"] = referencePlaneKey, ["reference_plane_id"] = referencePlane.Id.Val(), ["alignment_id"] = alignment.Id.Val(), ["on_reference_plane"] = true, ["tolerance_mm"] = .01 });
                appliedBindings++;
            }
            for (var index = 1; index < points.Count; index++)
            {
                var curve = family.FamilyCreate.NewDetailCurve(view, Line.CreateBound(points[index - 1], points[index])); curve.LineStyle = lineStyle;
                var curveBindings = new JArray();
                if (index == 1) BindEndpoint(curve, 0, "start", curveBindings);
                if (index == points.Count - 1) BindEndpoint(curve, 1, "end", curveBindings);
                result.Add(new JObject { ["declaration_key"] = declarationKey, ["curve_id"] = curve.Id.Val(), ["role"] = subcategoryName, ["plane"] = planeKey, ["view_id"] = view.Id.Val(), ["endpoint_bindings"] = curveBindings, ["verified"] = true });
            }
            if (appliedBindings != endpointBindings.Count) throw new CommandResultException(ErrorCodes.VerificationFailed, "Detail line " + declarationKey + " did not apply every declared endpoint binding.");
        }
        return result;
    }

    private static JArray CreateFilledRegions(Document family, FamilyBlueprintSpec spec)
    {
        var result = new JArray();
        var availableTypes = new FilteredElementCollector(family).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();
        foreach (var contract in (spec.Blueprint["filled_regions"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var planeKey = contract.Value<string>("plane") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Filled region plane is missing.");
            var typeName = contract.Value<string>("type_name") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Filled region type_name is missing.");
            var regionType = availableTypes.FirstOrDefault(item => string.Equals(item.Name, typeName, StringComparison.OrdinalIgnoreCase))
                ?? throw new CommandResultException(ErrorCodes.TemplateInvalid, "Filled Region Type not found in the selected Family template: " + typeName + ".");
            var view = ViewForPlane(family, planeKey, "filled region");
            var loops = new List<CurveLoop>();
            foreach (var loopContract in (contract["boundary_loops"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var points = (loopContract["points_mm"] as JArray ?? new JArray()).Select((item, index) => Point(item, "filled region boundary point " + index)).ToList();
                if (points.Count < 3) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Filled region boundary requires at least three points.");
                var loop = new CurveLoop();
                for (var index = 0; index < points.Count; index++) loop.Append(Line.CreateBound(points[index], points[(index + 1) % points.Count]));
                loops.Add(loop);
            }
            if (loops.Count == 0) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Filled region requires at least one boundary loop.");
            var region = FilledRegion.Create(family, regionType.Id, view.Id, loops);
            result.Add(new JObject { ["declaration_key"] = contract.Value<string>("key"), ["region_id"] = region.Id.Val(), ["role"] = contract.Value<string>("role"), ["plane"] = planeKey, ["view_id"] = view.Id.Val(), ["type_id"] = regionType.Id.Val(), ["type_name"] = regionType.Name, ["boundary_loop_count"] = loops.Count, ["verified"] = true });
        }
        return result;
    }

    private static View ViewForPlane(Document family, string planeKey, string feature)
    {
        var normal = PlaneNormal(planeKey);
        var view = new FilteredElementCollector(family).OfClass(typeof(View)).Cast<View>().FirstOrDefault(item =>
        {
            if (item.IsTemplate || item is ViewSchedule) return false;
            try { return Math.Abs(item.ViewDirection.Normalize().DotProduct(normal)) > .999; } catch { return false; }
        });
        return view ?? throw new CommandResultException(ErrorCodes.TemplateInvalid, "Selected Family template has no view matching the " + planeKey + " plane required by " + feature + ".");
    }

    private static View ViewForNormal(Document family, XYZ normal, XYZ up, string feature)
    {
        normal = normal.Normalize(); up = up.Subtract(normal.Multiply(up.DotProduct(normal)));
        if (up.GetLength() < 1e-9) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Cannot derive an up direction for " + feature + ".");
        up = up.Normalize();
        var existing = new FilteredElementCollector(family).OfClass(typeof(View)).Cast<View>().FirstOrDefault(item =>
        {
            if (item.IsTemplate || item is not (ViewSection or ViewPlan)) return false;
            try { return Math.Abs(item.ViewDirection.Normalize().DotProduct(normal)) > .999; } catch { return false; }
        });
        if (existing != null) return existing;
        var sectionType = new FilteredElementCollector(family).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(item => item.ViewFamily == ViewFamily.Section)
            ?? throw new CommandResultException(ErrorCodes.TemplateInvalid, "Selected Family template has no Section ViewFamilyType required to constrain " + feature + " on an arbitrary extrusion frame.");
        var right = up.CrossProduct(normal).Normalize(); var transform = Transform.Identity;
        transform.Origin = XYZ.Zero; transform.BasisX = right; transform.BasisY = up; transform.BasisZ = normal;
        var extent = Mm(2000); var box = new BoundingBoxXYZ { Transform = transform, Min = new XYZ(-extent, -extent, -extent), Max = new XYZ(extent, extent, extent), Enabled = true };
        try { return ViewSection.CreateSection(family, sectionType.Id, box); }
        catch (Exception ex) { throw new CommandResultException(ErrorCodes.TemplateInvalid, "Selected Family template cannot create the aligned section view required by " + feature + ": " + ex.Message); }
    }

    private static JArray CreateProfileCurves(Document family, FamilyBlueprintSpec spec)
    {
        var result = new JArray();
        foreach (var contract in (spec.Blueprint["profile_loops"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var planeKey = contract.Value<string>("plane") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Profile loop plane is missing.");
            var points = (contract["points_mm"] as JArray ?? new JArray()).Select((item, index) => Point(item, "profile loop point " + index)).ToList();
            if (points.Count < 3) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Profile loop requires at least three points.");
            var sketch = SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(PlaneNormal(planeKey), points[0]));
            var role = contract.Value<string>("role") ?? contract.Value<string>("key") ?? "Profile";
            var subcategory = ResolvePresentationSubcategory(family, spec, contract, "DSCons Profile " + role);
            var subcategoryName = subcategory.Name;
            var lineStyle = subcategory.GetGraphicsStyle(GraphicsStyleType.Projection) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Profile subcategory has no projection style.");
            for (var index = 0; index < points.Count; index++)
            {
                var curve = family.FamilyCreate.NewModelCurve(Line.CreateBound(points[index], points[(index + 1) % points.Count]), sketch); curve.LineStyle = lineStyle;
                result.Add(new JObject { ["loop_key"] = contract.Value<string>("key"), ["curve_id"] = curve.Id.Val(), ["role"] = subcategoryName, ["plane"] = planeKey, ["verified"] = true });
            }
        }
        return result;
    }

    private static JArray CreateControls(Document family, FamilyBlueprintSpec spec)
    {
        var result = new JArray();
        foreach (var contract in (spec.Blueprint["controls"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var key = contract.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Family control key is missing.");
            var plane = contract.Value<string>("view_plane") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Family control view_plane is missing.");
            var view = ViewForPlane(family, plane, "Family control");
            var shape = contract.Value<string>("shape") switch
            {
                "vertical_arrow" => ControlShape.VerticalArrow,
                "horizontal_arrow" => ControlShape.HorizontalArrow,
                "double_vertical_arrow" => ControlShape.DoubleVerticalArrow,
                "double_horizontal_arrow" => ControlShape.DoubleHorizontalArrow,
                _ => throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unsupported Family control shape.")
            };
            var position = Point(contract["position_mm"], "Family control position_mm");
            var control = family.FamilyCreate.NewControl(shape, view, position) ?? throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rejected Family control " + key + ".");
            if (control.Shape != shape || control.View.Id != view.Id || control.Origin.DistanceTo(position) > 1e-8) throw new CommandResultException(ErrorCodes.VerificationFailed, "Family control " + key + " did not read back its declared shape/view/position.");
            result.Add(new JObject { ["key"] = key, ["element_id"] = control.Id.Val(), ["shape"] = contract.Value<string>("shape"), ["view_plane"] = plane, ["origin_mm"] = PointMm(control.Origin), ["verified"] = true });
        }
        return result;
    }

    private static JArray CreateSymbolicLines(Document family, FamilyBlueprintSpec spec, FamilyManager manager, Dictionary<string, FamilyParameter> parameters)
    {
        var result = new JArray();
        foreach (var contract in (spec.Blueprint["symbolic_lines"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var planeKey = contract.Value<string>("plane") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Symbolic line plane is missing.");
            var points = (contract["points_mm"] as JArray ?? new JArray()).Select((item, index) => Point(item, "symbolic line point " + index)).ToList();
            if (points.Count < 2) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Symbolic line requires at least two points.");
            var sketch = SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(PlaneNormal(planeKey), points[0]));
            var role = contract.Value<string>("role") ?? contract.Value<string>("key") ?? "Symbol";
            var subcategory = ResolvePresentationSubcategory(family, spec, contract, "DSCons Symbol " + role);
            var subcategoryName = subcategory.Name;
            var lineStyle = subcategory.GetGraphicsStyle(GraphicsStyleType.Projection) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Symbolic-line subcategory has no projection style.");
            var visibility = contract["visibility"] as JObject ?? new JObject();
            for (var index = 1; index < points.Count; index++)
            {
                var curve = family.FamilyCreate.NewSymbolicCurve(Line.CreateBound(points[index - 1], points[index]), sketch); curve.LineStyle = lineStyle;
                var appliedVisibility = Visibility(visibility, FamilyElementVisibilityType.ViewSpecific); curve.SetVisibility(appliedVisibility);
                var visibilityParameter = AssociateVisibility(manager, curve, contract, parameters, "symbolic line " + (contract.Value<string>("key") ?? role));
                result.Add(new JObject { ["declaration_key"] = contract.Value<string>("key"), ["curve_id"] = curve.Id.Val(), ["role"] = subcategoryName, ["plane"] = planeKey, ["visibility"] = VisibilitySnapshot(appliedVisibility), ["visibility_parameter"] = visibilityParameter, ["verified"] = true });
            }
        }
        return result;
    }

    private static JArray CreateModelLines(Document family, FamilyBlueprintSpec spec, FamilyManager manager, Dictionary<string, FamilyParameter> parameters, JObject referenceGraph)
    {
        var referencePlanes = new Dictionary<string, ReferencePlane>(StringComparer.Ordinal);
        foreach (var snapshot in (referenceGraph["reference_planes"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var key = snapshot.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reference graph has a plane without a key.");
            var id = snapshot.Value<long?>("id") ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reference graph plane " + key + " has no element id.");
            referencePlanes[key] = family.GetElement(RevitIdCompatibility.Eid(id)) as ReferencePlane
                ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reference graph plane " + key + " cannot be resolved while creating Model Lines.");
        }
        var result = new JArray();
        foreach (var contract in (spec.Blueprint["model_lines"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var declarationKey = contract.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Model line key is missing.");
            var planeKey = contract.Value<string>("plane") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Model line plane is missing.");
            var points = (contract["points_mm"] as JArray ?? new JArray()).Select((item, index) => Point(item, "model line point " + index)).ToList();
            if (points.Count < 2) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Model line requires at least two points.");
            var endpointBindings = (contract["endpoint_bindings"] as JArray ?? new JArray()).OfType<JObject>()
                .ToDictionary(item => item.Value<string>("endpoint") ?? string.Empty, item => item, StringComparer.Ordinal);
            var sketch = SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(PlaneNormal(planeKey), points[0]));
            var view = ViewForPlane(family, planeKey, "model-line endpoint binding");
            var role = contract.Value<string>("role") ?? declarationKey;
            var subcategory = ResolvePresentationSubcategory(family, spec, contract, "DSCons Model Line " + role);
            var subcategoryName = subcategory.Name;
            var lineStyle = subcategory.GetGraphicsStyle(GraphicsStyleType.Projection) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Model-line subcategory has no projection style.");
            var visibility = contract["visibility"] as JObject ?? new JObject(); var appliedBindings = 0;

            void BindEndpoint(ModelCurve curve, int endpointIndex, string endpoint, JArray curveBindings)
            {
                if (!endpointBindings.TryGetValue(endpoint, out var binding)) return;
                var referencePlaneKey = binding.Value<string>("reference_plane_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Model line " + declarationKey + " " + endpoint + " endpoint binding has no Reference Plane key.");
                if (!referencePlanes.TryGetValue(referencePlaneKey, out var referencePlane)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Model line " + declarationKey + " " + endpoint + " endpoint binding references an unavailable Reference Plane " + referencePlaneKey + ".");
                family.Regenerate();
                var endpointReference = curve.GeometryCurve.GetEndPointReference(endpointIndex)
                    ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Model line " + declarationKey + " " + endpoint + " endpoint has no stable Revit reference.");
                var endpointPoint = curve.GeometryCurve.GetEndPoint(endpointIndex);
                var targetPlane = referencePlane.GetPlane();
                var preAlignmentDistance = Math.Abs(endpointPoint.Subtract(targetPlane.Origin).DotProduct(targetPlane.Normal));
                if (preAlignmentDistance > Mm(.01))
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Model line " + declarationKey + " " + endpoint + " endpoint is not geometrically aligned to Reference Plane " + referencePlaneKey + " before NewAlignment; distance_mm=" + (preAlignmentDistance * 304.8) + ".");
                Dimension alignment;
                try
                {
                    alignment = family.FamilyCreate.NewAlignment(view, ReferencePlaneReference(referencePlane, declarationKey + " " + endpoint), endpointReference)
                        ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected Model Line " + declarationKey + " " + endpoint + " endpoint alignment to Reference Plane " + referencePlaneKey + ".");
                }
                catch (CommandResultException) { throw; }
                catch (Exception exception)
                {
                    var planeReference = referencePlane.GetReference();
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected Model Line " + declarationKey + " " + endpoint + " endpoint alignment to Reference Plane " + referencePlaneKey
                        + ": " + exception.Message + "; plane_reference_type=" + planeReference.ElementReferenceType + "; plane_reference_id=" + planeReference.ElementId.Val()
                        + "; endpoint_reference_type=" + endpointReference.ElementReferenceType + "; endpoint_reference_id=" + endpointReference.ElementId.Val()
                        + "; pre_alignment_distance_mm=" + (preAlignmentDistance * 304.8) + ".");
                }
                family.Regenerate();
                VerifyModelLineEndpointOnReferencePlane(curve, endpointIndex, referencePlane, "Model line " + declarationKey + " " + endpoint + " endpoint");
                curveBindings.Add(new JObject { ["endpoint"] = endpoint, ["reference_plane_key"] = referencePlaneKey, ["reference_plane_id"] = referencePlane.Id.Val(), ["alignment_id"] = alignment.Id.Val(), ["on_reference_plane"] = true, ["tolerance_mm"] = .01 });
                appliedBindings++;
            }

            for (var index = 1; index < points.Count; index++)
            {
                var curve = family.FamilyCreate.NewModelCurve(Line.CreateBound(points[index - 1], points[index]), sketch); curve.LineStyle = lineStyle;
                var appliedVisibility = Visibility(visibility, FamilyElementVisibilityType.Model); curve.SetVisibility(appliedVisibility);
                var visibilityParameter = AssociateVisibility(manager, curve, contract, parameters, "model line " + declarationKey);
                var curveBindings = new JArray();
                if (index == 1) BindEndpoint(curve, 0, "start", curveBindings);
                if (index == points.Count - 1) BindEndpoint(curve, 1, "end", curveBindings);
                result.Add(new JObject { ["declaration_key"] = declarationKey, ["curve_id"] = curve.Id.Val(), ["role"] = subcategoryName, ["plane"] = planeKey, ["visibility"] = VisibilitySnapshot(appliedVisibility), ["visibility_parameter"] = visibilityParameter, ["endpoint_bindings"] = curveBindings, ["verified"] = true });
            }
            if (appliedBindings != endpointBindings.Count) throw new CommandResultException(ErrorCodes.VerificationFailed, "Model line " + declarationKey + " did not apply every declared endpoint binding.");
        }
        return result;
    }

    private static void VerifyModelLineEndpointOnReferencePlane(CurveElement curve, int endpointIndex, ReferencePlane referencePlane, string label)
    {
        var endpoint = curve.GeometryCurve.GetEndPoint(endpointIndex); var plane = referencePlane.GetPlane();
        var distance = Math.Abs(endpoint.Subtract(plane.Origin).DotProduct(plane.Normal.Normalize()));
        if (distance > Mm(.01)) throw new CommandResultException(ErrorCodes.VerificationFailed, label + " is not on its declared Reference Plane; observed distance=" + (distance * 304.8).ToString("R") + " mm.");
    }

    private static JArray ApplyGeometryOperations(Document family, Dictionary<string, PartResult> parts)
    {
        var results = new JArray(); var joined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in parts.Values.Where(item => item.Operation == "solid"))
        {
            var targets = (part.Contract["join_with"] as JArray ?? new JArray()).Values<string>().Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToList();
            var pending = targets.Where(target => joined.Add(string.CompareOrdinal(part.Key, target) < 0 ? part.Key + "|" + target : target + "|" + part.Key)).ToList();
            if (pending.Count == 0) continue;
            var members = family.Application.Create.NewCombinableElementArray(); members.Append(part.Form);
            foreach (var target in pending) members.Append(parts[target].Form);
            var combination = family.CombineElements(members);
            results.Add(new JObject { ["kind"] = "join", ["source_part"] = part.Key, ["target_parts"] = new JArray(pending), ["combination_id"] = combination.Id.Val(), ["verified"] = true });
        }
        foreach (var part in parts.Values.Where(item => item.Operation == "void"))
        {
            var targets = (part.Contract["cut_targets"] as JArray ?? new JArray()).Values<string>().Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToList();
            if (targets.Count == 0) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Void part " + part.Key + " has no cut target.");
            var members = family.Application.Create.NewCombinableElementArray();
            foreach (var target in targets) members.Append(parts[target].Form);
            members.Append(part.Form);
            var combination = family.CombineElements(members);
            results.Add(new JObject { ["kind"] = "cut", ["void_part"] = part.Key, ["target_parts"] = new JArray(targets), ["combination_id"] = combination.Id.Val(), ["verified"] = true });
        }
        return results;
    }

    private static Dictionary<string, FamilyParameter> CreateParameters(Document family, FamilyBlueprintSpec spec)
    {
        var manager = family.FamilyManager;
        var result = new Dictionary<string, FamilyParameter>(StringComparer.Ordinal);
        var items = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var sharedItems = items.Where(item => item.Value<string>("shared_guid") != null).ToList();
        string? temporaryFile = null; string? previousFile = null; DefinitionGroup? sharedGroup = null;
        try
        {
            if (sharedItems.Count > 0)
            {
                temporaryFile = Path.Combine(Path.GetTempPath(), "dscons-blueprint-shared-" + Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllText(temporaryFile, "# This is a Revit shared parameter file.\r\n# Do not edit manually.\r\n*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n*GROUP\tID\tNAME\r\n*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n", new System.Text.UTF8Encoding(false));
                previousFile = family.Application.SharedParametersFilename; family.Application.SharedParametersFilename = temporaryFile;
                var definitionFile = family.Application.OpenSharedParameterFile() ?? throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit could not open the temporary shared-parameter definition file.");
                sharedGroup = definitionFile.Groups.get_Item("DSCons Blueprint") ?? definitionFile.Groups.Create("DSCons Blueprint");
            }
            foreach (var item in items)
            {
                var key = item.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint parameter key is missing.");
                var name = item.Value<string>("name") ?? key; var dataType = item.Value<string>("data_type") ?? "text"; var instance = string.Equals(item.Value<string>("scope"), "instance", StringComparison.Ordinal);
                if (dataType == "family_type") continue; // Created after its Blueprint-built option Families are loaded.
                FamilyParameter parameter;
                if (item.Value<string>("shared_guid") is string sharedGuid)
                {
                    if (sharedGroup == null) throw new CommandResultException(ErrorCodes.TransactionFailed, "Shared-parameter group was not initialized.");
                    var definition = sharedGroup.Definitions.Create(SharedParameterOptions(item, name, dataType, new Guid(sharedGuid))) as ExternalDefinition ?? throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit could not create shared parameter " + name + ".");
#if REVIT2019 || REVIT2020 || REVIT2021
                    parameter = manager.AddParameter(definition, ParameterGroup(item.Value<string>("group"), dataType), instance);
#else
                    parameter = manager.AddParameter(definition, ParameterGroupId(item.Value<string>("group"), dataType), instance);
#endif
                }
                else parameter = AddParameter(manager, name, dataType, item.Value<string>("group"), instance);
                if (item.Value<string>("description") is string description) manager.SetDescription(parameter, description);
                result.Add(key, parameter);
            }
        }
        finally
        {
            if (temporaryFile != null)
            {
                family.Application.SharedParametersFilename = previousFile ?? string.Empty;
                try { File.Delete(temporaryFile); } catch { }
            }
        }
        return result;
    }

    private static JArray ApplyParameterOrder(FamilyManager manager, FamilyBlueprintSpec spec, IReadOnlyDictionary<string, FamilyParameter> parameters)
    {
        var requestedKeys = (spec.Blueprint["parameter_order"] as JArray ?? new JArray()).Values<string>().Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToList();
        if (requestedKeys.Count != parameters.Count || requestedKeys.Any(key => !parameters.ContainsKey(key))) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint parameter_order must contain every declared parameter exactly once.");
        var requested = requestedKeys.Select(key => parameters[key]).ToList();
        var declaredIds = new HashSet<long>(requested.Select(item => item.Id.Val()));
        var all = manager.GetParameters().ToList();
        var combined = all.Where(item => !declaredIds.Contains(item.Id.Val())).Concat(requested).ToList();
        manager.ReorderParameters(combined);
        var readBack = manager.GetParameters().ToList();
        foreach (var grouping in requested.GroupBy(item => ParameterGroupIdentity(item.Definition)))
        {
            var expected = grouping.Select(item => item.Id.Val()).ToList();
            var actual = readBack.Where(item => expected.Contains(item.Id.Val())).Select(item => item.Id.Val()).ToList();
            if (!expected.SequenceEqual(actual)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Family parameter order did not read back within parameter group " + grouping.Key + ".");
        }
        return new JArray(requestedKeys);
    }

    private static string ParameterGroupIdentity(Definition definition)
    {
#if REVIT2019 || REVIT2020 || REVIT2021
#pragma warning disable CS0618
        return ((int)definition.ParameterGroup).ToString(System.Globalization.CultureInfo.InvariantCulture);
#pragma warning restore CS0618
#else
        return definition.GetGroupTypeId().TypeId;
#endif
    }

    // Preview is rollback-only, but it must still expose the Revit read-back
    // for declared parameter identity. In particular, a successful flex test
    // alone is not evidence that an ExternalDefinition kept its Shared GUID
    // and metadata after Revit created it.
    private static JArray VerifyCommittedParameterReadBack(FamilyBlueprintSpec spec, Document family)
    {
        var inspected = FamilyData.Inspect(family);
        var observed = (inspected["parameters"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var definitions = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>()
            .ToDictionary(item => item.Value<string>("key") ?? string.Empty, item => item, StringComparer.Ordinal);
        var requestedKeys = (spec.Blueprint["parameter_order"] as JArray ?? new JArray()).Values<string>()
            .Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToList();
        // Autodesk templates can retain unrelated native parameters with
        // duplicate display names. They must not make a declared Blueprint
        // parameter ambiguous. A duplicate of a *declared* name still fails
        // closed, because the compiler cannot safely select one.
        var declaredNames = new HashSet<string>(definitions.Values.Select(item => item.Value<string>("name") ?? string.Empty), StringComparer.Ordinal);
        var observedByName = new Dictionary<string, JObject>(StringComparer.Ordinal);
        foreach (var item in observed.Where(item => declaredNames.Contains(item.Value<string>("name") ?? string.Empty)))
        {
            var name = item.Value<string>("name") ?? string.Empty;
            if (observedByName.ContainsKey(name))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Committed preview Family contains duplicate declared parameter name " + name + ".");
            observedByName.Add(name, item);
        }
        var result = new JArray();
        foreach (var key in requestedKeys)
        {
            if (!definitions.TryGetValue(key, out var definition))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Committed preview Family is missing the declared parameter contract " + key + ".");
            var name = definition.Value<string>("name") ?? string.Empty;
            if (!observedByName.TryGetValue(name, out var actual))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Committed preview Family is missing declared parameter " + name + ".");
            var expectedInstance = string.Equals(definition.Value<string>("scope"), "instance", StringComparison.Ordinal);
            if (actual.Value<bool?>("is_instance") != expectedInstance)
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Committed preview Family parameter scope mismatch for " + name + ".");
            if (definition.Value<string>("group") is string expectedGroup && !string.Equals(actual.Value<string>("group"), expectedGroup, StringComparison.Ordinal))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Committed preview Family parameter group mismatch for " + name + ".");
            var expectedFormula = ExpectedParameterFormula(spec, definition);
            if (!string.Equals(actual.Value<string>("formula") ?? string.Empty, expectedFormula ?? string.Empty, StringComparison.Ordinal))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Committed preview Family parameter formula/lookup mismatch for " + name + ".");
            var sharedMetadata = new JObject();
            if (definition.Value<string>("shared_guid") is string expectedGuid)
            {
                if (actual.Value<bool?>("is_shared") != true || !string.Equals(actual.Value<string>("shared_guid"), expectedGuid, StringComparison.OrdinalIgnoreCase))
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Committed preview Family parameter Shared GUID mismatch for " + name + ".");
                foreach (var property in new[] { "description", "visible", "user_modifiable", "hide_when_no_value" })
                {
                    var expectedMetadata = definition[property]; var observedMetadata = actual[property];
                    var available = observedMetadata != null && observedMetadata.Type != JTokenType.Null;
                    // In Revit 2023, FamilyManager.SetDescription persists the
                    // Family UI tooltip but FamilyParameter.Definition may
                    // expose the original ExternalDefinition description
                    // instead. Treat this Family API ambiguity as unavailable;
                    // a controlled Family Editor check is the only acceptance
                    // route for the affected metadata field.
                    var apiReliable = property != "description";
                    if (expectedMetadata != null && available && apiReliable && !JToken.DeepEquals(expectedMetadata, observedMetadata))
                        throw new CommandResultException(ErrorCodes.VerificationFailed, "Committed preview Family Shared Parameter metadata mismatch for " + name + ": " + property + ".");
                    sharedMetadata[property] = expectedMetadata == null ? "not_declared" : available && apiReliable ? "verified" : "unavailable_revit_api";
                }
            }
            else if (actual.Value<bool?>("is_shared") == true)
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Committed preview Family parameter unexpectedly became shared: " + name + ".");
            result.Add(new JObject
            {
                ["key"] = key, ["name"] = name, ["order_index"] = actual.Value<int>("order_index"),
                ["is_instance"] = actual.Value<bool>("is_instance"), ["group"] = actual.Value<string>("group"), ["formula"] = actual.Value<string>("formula"),
                ["is_shared"] = actual.Value<bool>("is_shared"), ["shared_guid"] = actual.Value<string>("shared_guid"),
                ["description"] = actual["description"], ["visible"] = actual["visible"], ["user_modifiable"] = actual["user_modifiable"], ["hide_when_no_value"] = actual["hide_when_no_value"],
                ["shared_metadata_read_back"] = sharedMetadata,
                ["verified"] = true
            });
        }
        foreach (var group in requestedKeys.GroupBy(key => observedByName[definitions[key].Value<string>("name") ?? string.Empty].Value<string>("group") ?? string.Empty))
        {
            var expectedNames = group.Select(key => definitions[key].Value<string>("name") ?? string.Empty).ToList();
            var expectedSet = new HashSet<string>(expectedNames, StringComparer.Ordinal);
            var actualNames = observed.Where(item => expectedSet.Contains(item.Value<string>("name") ?? string.Empty)).OrderBy(item => item.Value<int>("order_index")).Select(item => item.Value<string>("name") ?? string.Empty).ToList();
            if (!expectedNames.SequenceEqual(actualNames))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Committed preview Family parameter order mismatch in group " + group.Key + ".");
        }
        return result;
    }

    private static void VerifyReopenedParameterOrder(FamilyBlueprintSpec spec, JObject readBack)
    {
        var requestedKeys = (spec.Blueprint["parameter_order"] as JArray ?? new JArray()).Values<string>().Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToList();
        var definitions = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("key") ?? string.Empty, item => item, StringComparer.Ordinal);
        var observed = (readBack["parameters"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        // Apply the same scoped duplicate policy after staged RFA reopen.
        // Ignore only irrelevant native/template names; never choose between
        // two parameters that claim the same Blueprint-declared name.
        var declaredNames = new HashSet<string>(definitions.Values.Select(item => item.Value<string>("name") ?? string.Empty), StringComparer.Ordinal);
        var observedByName = new Dictionary<string, JObject>(StringComparer.Ordinal);
        foreach (var item in observed.Where(item => declaredNames.Contains(item.Value<string>("name") ?? string.Empty)))
        {
            var name = item.Value<string>("name") ?? string.Empty;
            if (observedByName.ContainsKey(name))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family contains duplicate declared parameter name " + name + ".");
            observedByName.Add(name, item);
        }
        foreach (var key in requestedKeys)
        {
            if (!definitions.TryGetValue(key, out var definition) || definition == null) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family is missing declared parameter definition for " + key + ".");
            var name = definition.Value<string>("name") ?? string.Empty;
            if (!observedByName.TryGetValue(name, out var actual)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family is missing declared parameter " + name + ".");
            var expectedInstance = string.Equals(definition.Value<string>("scope"), "instance", StringComparison.Ordinal);
            if (actual.Value<bool?>("is_instance") != expectedInstance) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family parameter scope mismatch for " + name + ".");
            if (definition.Value<string>("group") is string expectedGroup && !string.Equals(actual.Value<string>("group"), expectedGroup, StringComparison.Ordinal))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family parameter group mismatch for " + name + ".");
            var expectedFormula = ExpectedParameterFormula(spec, definition);
            if (!string.Equals(actual.Value<string>("formula") ?? string.Empty, expectedFormula ?? string.Empty, StringComparison.Ordinal))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family parameter formula/lookup mismatch for " + name + ".");
            if (definition.Value<string>("shared_guid") is string expectedGuid)
            {
                if (actual.Value<bool?>("is_shared") != true || !string.Equals(actual.Value<string>("shared_guid"), expectedGuid, StringComparison.OrdinalIgnoreCase))
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family parameter Shared GUID mismatch for " + name + ".");
                foreach (var property in new[] { "description", "visible", "user_modifiable", "hide_when_no_value" })
                    if (property != "description" && definition[property] != null && actual[property] != null && actual[property]!.Type != JTokenType.Null && !JToken.DeepEquals(definition[property], actual[property]))
                        throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family Shared Parameter metadata mismatch for " + name + ": " + property + ".");
            }
            else if (actual.Value<bool?>("is_shared") == true) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family parameter unexpectedly became shared: " + name + ".");
        }
        foreach (var group in requestedKeys.GroupBy(key => observedByName[definitions[key].Value<string>("name") ?? string.Empty].Value<string>("group") ?? string.Empty))
        {
            var expectedNames = group.Select(key => definitions[key].Value<string>("name") ?? string.Empty).ToList();
            var expectedSet = new HashSet<string>(expectedNames, StringComparer.Ordinal);
            var actualNames = observed.Where(item => expectedSet.Contains(item.Value<string>("name") ?? string.Empty)).OrderBy(item => item.Value<int>("order_index")).Select(item => item.Value<string>("name") ?? string.Empty).ToList();
            if (!expectedNames.SequenceEqual(actualNames)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family parameter order mismatch in group " + group.Key + ".");
        }
    }

    private static void VerifyReopenedLookupTables(FamilyBlueprintSpec spec, JObject readBack)
    {
        var expected = (spec.Blueprint["lookup_tables"] as JArray ?? new JArray()).OfType<JObject>()
            .Select(item => item.Value<string>("name") ?? string.Empty).OrderBy(name => name, StringComparer.Ordinal).ToList();
        var actual = (readBack["lookup_tables"] as JArray ?? new JArray()).Values<string>()
            .Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!).OrderBy(name => name, StringComparer.Ordinal).ToList();
        if (!expected.SequenceEqual(actual))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family lookup-table inventory mismatch; expected=" + string.Join(",", expected) + "; actual=" + string.Join(",", actual) + ".");
    }

    private static ExternalDefinitionCreationOptions SharedParameterOptions(JObject item, string name, string dataType, Guid guid)
    {
#if REVIT2019 || REVIT2020 || REVIT2021
#pragma warning disable CS0618
        var type = LegacyParameterType(dataType);
        var options = new ExternalDefinitionCreationOptions(name, type) { GUID = guid };
#pragma warning restore CS0618
#else
        var spec = ParameterSpecType(dataType);
        var options = new ExternalDefinitionCreationOptions(name, spec) { GUID = guid };
#endif
        if (item.Value<string>("description") is string description) options.Description = description;
        if (item.Value<bool?>("visible") is bool visible) options.Visible = visible;
        if (item.Value<bool?>("user_modifiable") is bool userModifiable) options.UserModifiable = userModifiable;
        if (item.Value<bool?>("hide_when_no_value") is bool hideWhenNoValue) options.GetType().GetProperty("HideWhenNoValue")?.SetValue(options, hideWhenNoValue);
        return options;
    }

    private static Dictionary<string, ElementId> CreateMaterials(Document family, FamilyBlueprintSpec spec, IReadOnlyDictionary<string, AppearanceImageFiles> appearanceAssets)
    {
        var result = new Dictionary<string, ElementId>(StringComparer.Ordinal);
        foreach (var item in (spec.Blueprint["materials"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var key = item.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint material key is missing.");
            var name = item.Value<string>("name") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint material name is missing.");
            var material = new FilteredElementCollector(family).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal))
                ?? family.GetElement(Material.Create(family, name)) as Material
                ?? throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit could not create material " + name + ".");
            var color = item["color_rgb"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint material color is missing.");
            material.Color = new Color(color.Value<byte>("r"), color.Value<byte>("g"), color.Value<byte>("b"));
            material.Transparency = item.Value<int?>("transparency") ?? 0;
            ApplyFillPattern(family, material, item["surface_foreground_pattern"] as JObject, false);
            ApplyFillPattern(family, material, item["cut_foreground_pattern"] as JObject, true);
            if (item["appearance"] is JObject appearance) ApplyAppearance(family, material, appearance, appearanceAssets.TryGetValue(key, out var images) ? images : null);
            if (item["physical_asset"] is JObject physicalAsset) ApplyPhysicalAsset(family, material, physicalAsset);
            if (item["thermal_asset"] is JObject thermalAsset) ApplyThermalAsset(family, material, thermalAsset);
            if (item.Value<bool?>("use_render_appearance_for_shading") is bool useRender) material.UseRenderAppearanceForShading = useRender;
            result.Add(key, material.Id);
        }
        return result;
    }

    private static Dictionary<string, ElectricalLoadClassification> CreateElectricalLoadClassifications(Document family, FamilyBlueprintSpec spec)
    {
        var result = new Dictionary<string, ElectricalLoadClassification>(StringComparer.Ordinal);
        var existing = new FilteredElementCollector(family).OfClass(typeof(ElectricalLoadClassification)).Cast<ElectricalLoadClassification>()
            .ToDictionary(item => item.Name, item => item, StringComparer.OrdinalIgnoreCase);
        foreach (var item in (spec.Blueprint["electrical_load_classifications"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var key = item.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Electrical load-classification key is missing.");
            var name = item.Value<string>("name") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Electrical load-classification name is missing.");
            var abbreviation = item.Value<string>("abbreviation") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Electrical load-classification abbreviation is missing.");
#if REVIT2019 || REVIT2020
            throw new CommandResultException(ErrorCodes.Unsupported, "Electrical load-classification abbreviation authoring requires Revit 2021 or newer; Revit 2019-2020 expose no public Abbreviation API.");
#else
            if (!existing.TryGetValue(name, out var classification))
            {
                classification = ElectricalLoadClassification.Create(family, name)
                    ?? throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit could not create electrical load classification " + name + ".");
                existing[name] = classification;
            }
            classification.Abbreviation = abbreviation;
            if (!string.Equals(classification.Name, name, StringComparison.Ordinal) || !string.Equals(classification.Abbreviation, abbreviation, StringComparison.Ordinal))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Electrical load classification did not read back: " + name + ".");
            result.Add(key, classification);
#endif
        }
        return result;
    }

    private static JArray ElectricalLoadClassificationSnapshot(IReadOnlyDictionary<string, ElectricalLoadClassification> classifications) => new(classifications.Select(item =>
        new JObject { ["key"] = item.Key, ["element_id"] = item.Value.Id.Val(), ["name"] = item.Value.Name, ["abbreviation"] = ElectricalLoadClassificationAbbreviation(item.Value), ["verified"] = true }));

    private static string ElectricalLoadClassificationAbbreviation(ElectricalLoadClassification classification)
    {
#if REVIT2019 || REVIT2020
        throw new CommandResultException(ErrorCodes.Unsupported, "Electrical load-classification abbreviation read-back requires Revit 2021 or newer.");
#else
        return classification.Abbreviation;
#endif
    }

    private static void ApplyFillPattern(Document family, Material material, JObject? contract, bool cut)
    {
        if (contract == null) return;
        var name = contract.Value<string>("name") ?? string.Empty;
        var target = contract.Value<string>("target") == "model" ? FillPatternTarget.Model : FillPatternTarget.Drafting;
        if (cut && target != FillPatternTarget.Drafting) throw new CommandResultException(ErrorCodes.InvalidParam, "Cut material patterns must be drafting patterns.");
        var pattern = new FilteredElementCollector(family).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal) && item.GetFillPattern().Target == target)
            ?? throw new CommandResultException(ErrorCodes.TemplateInvalid, "Material fill pattern was not found with the requested name/target: " + name + ".");
        var color = contract["color_rgb"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Material fill pattern color is missing.");
        var revitColor = new Color(color.Value<byte>("r"), color.Value<byte>("g"), color.Value<byte>("b"));
        if (cut) { material.CutForegroundPatternId = pattern.Id; material.CutForegroundPatternColor = revitColor; }
        else { material.SurfaceForegroundPatternId = pattern.Id; material.SurfaceForegroundPatternColor = revitColor; }
    }

    private static void ApplyAppearance(Document family, Material material, JObject contract, AppearanceImageFiles? images)
    {
        var assetName = material.Name + " Appearance";
        var element = new FilteredElementCollector(family).OfClass(typeof(AppearanceAssetElement)).Cast<AppearanceAssetElement>().FirstOrDefault(item => string.Equals(item.Name, assetName, StringComparison.Ordinal));
        if (element == null)
        {
            var generic = family.Application.GetAssets(AssetType.Appearance).FirstOrDefault(asset => asset.FindByName(Generic.GenericDiffuse) != null)
                ?? throw new CommandResultException(ErrorCodes.Unsupported, "Revit installation exposes no generic appearance asset for Blueprint material creation.");
            element = AppearanceAssetElement.Create(family, assetName, generic);
        }
        using (var scope = new AppearanceAssetEditScope(family))
        {
            var editable = scope.Start(element.Id);
            var color = contract["color_rgb"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Material appearance color is missing.");
            if (editable.FindByName(Generic.GenericDiffuse) is not AssetPropertyDoubleArray4d diffuse || !diffuse.IsValidValue(new Color(color.Value<byte>("r"), color.Value<byte>("g"), color.Value<byte>("b"))))
                throw new CommandResultException(ErrorCodes.Unsupported, "Generic material appearance does not expose a writable diffuse color.");
            diffuse.SetValueAsColor(new Color(color.Value<byte>("r"), color.Value<byte>("g"), color.Value<byte>("b")));
            SetAppearanceDouble(editable, Generic.GenericTransparency, contract.Value<double?>("transparency") ?? 0, "transparency");
            SetAppearanceDouble(editable, Generic.GenericGlossiness, contract.Value<double?>("glossiness") ?? 0, "glossiness");
            if (contract.Value<bool?>("is_metal") is bool isMetal)
            {
                if (editable.FindByName(Generic.GenericIsMetal) is not AssetPropertyBoolean metal) throw new CommandResultException(ErrorCodes.Unsupported, "Generic material appearance does not expose is_metal.");
                metal.Value = isMetal;
            }
            if (contract["texture"] is JObject)
            {
                if (images?.TexturePath == null) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Validated texture evidence is missing for material " + material.Name + ".");
                SetAppearanceBitmap(editable, Generic.GenericDiffuse, images.TexturePath, "texture");
            }
            else ClearAppearanceBitmap(editable, Generic.GenericDiffuse);
            if (contract["bump"] is JObject)
            {
                if (images?.BumpPath == null || !images.BumpAmount.HasValue) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Validated bump evidence is missing for material " + material.Name + ".");
                SetAppearanceBitmap(editable, Generic.GenericBumpMap, images.BumpPath, "bump");
                SetAppearanceDouble(editable, Generic.GenericBumpAmount, images.BumpAmount.Value, "bump amount");
            }
            else ClearAppearanceBitmap(editable, Generic.GenericBumpMap);
            scope.Commit(true);
        }
        material.AppearanceAssetId = element.Id;
    }

    private static void SetAppearanceBitmap(Asset asset, string propertyName, string imagePath, string label)
    {
        var property = asset.FindByName(propertyName) ?? throw new CommandResultException(ErrorCodes.Unsupported, "Generic material appearance does not expose a writable " + label + " map.");
        var connected = property.GetSingleConnectedAsset();
        if (connected != null && connected.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) is not AssetPropertyString)
        {
            property.RemoveConnectedAsset(); connected = null;
        }
        if (connected == null)
        {
            property.AddConnectedAsset("UnifiedBitmap");
            connected = property.GetSingleConnectedAsset();
        }
        var bitmap = connected?.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) as AssetPropertyString;
        if (bitmap == null || !bitmap.IsValidValue(imagePath)) throw new CommandResultException(ErrorCodes.Unsupported, "Generic material appearance does not accept the validated " + label + " image.");
        bitmap.Value = imagePath;
    }

    private static void ClearAppearanceBitmap(Asset asset, string propertyName)
    {
        // Generic assets copied from Revit's render library can inherit a
        // UnifiedBitmap connection (for example Concrete). A Blueprint that
        // declares only a color must be an untextured solid-color material;
        // retain a bitmap only when it was explicitly supplied and checksum
        // validated by RequireAppearanceAssets.
        var property = asset.FindByName(propertyName);
        if (property?.GetSingleConnectedAsset() != null) property.RemoveConnectedAsset();
    }

    private static void SetAppearanceDouble(Asset asset, string propertyName, double value, string label)
    {
        if (asset.FindByName(propertyName) is not AssetPropertyDouble property || !property.IsValidValue(value)) throw new CommandResultException(ErrorCodes.Unsupported, "Generic material appearance does not accept " + label + "=" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
        property.Value = value;
    }

    private static void ApplyPhysicalAsset(Document family, Material material, JObject contract)
    {
        var name = contract.Value<string>("name") ?? material.Name + " Physical";
        if (!Enum.TryParse(contract.Value<string>("asset_class"), true, out StructuralAssetClass assetClass) || !Enum.IsDefined(typeof(StructuralAssetClass), assetClass))
            throw new CommandResultException(ErrorCodes.Unsupported, "Unsupported physical asset_class for material " + material.Name + ".");
        using (var asset = new StructuralAsset(name, assetClass))
        {
            asset.Behavior = StructuralBehavior.Isotropic;
            asset.Density = DensityToInternal(contract.Value<double>("density_kg_per_m3"));
            if (contract.Value<double?>("young_modulus_mpa") is double young)
            {
                asset.SetYoungModulus(StressToInternal(young));
            }
            if (contract.Value<double?>("shear_modulus_mpa") is double shear)
            {
                asset.SetShearModulus(StressToInternal(shear));
            }
            if (contract.Value<double?>("poisson_ratio") is double poisson) asset.SetPoissonRatio(poisson);
            var propertySet = material.StructuralAssetId != ElementId.InvalidElementId ? family.GetElement(material.StructuralAssetId) as PropertySetElement : null;
            if (propertySet == null) propertySet = PropertySetElement.Create(family, asset) ?? throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit could not create Physical Asset " + name + ".");
            else propertySet.SetStructuralAsset(asset);
            material.StructuralAssetId = propertySet.Id;
        }
        family.Regenerate();
        if (material.StructuralAssetId == ElementId.InvalidElementId || family.GetElement(material.StructuralAssetId) is not PropertySetElement)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Physical Asset did not read back for material " + material.Name + ".");
    }

    private static void ApplyThermalAsset(Document family, Material material, JObject contract)
    {
        var name = contract.Value<string>("name") ?? material.Name + " Thermal";
        if (!Enum.TryParse(contract.Value<string>("material_type"), true, out ThermalMaterialType materialType) || !Enum.IsDefined(typeof(ThermalMaterialType), materialType) || materialType != ThermalMaterialType.Solid)
            throw new CommandResultException(ErrorCodes.Unsupported, "Only solid Thermal Assets are supported for material " + material.Name + ".");
        using (var asset = new ThermalAsset(name, materialType))
        {
            asset.Behavior = StructuralBehavior.Isotropic;
            asset.Density = DensityToInternal(contract.Value<double>("density_kg_per_m3"));
            asset.ThermalConductivity = ThermalConductivityToInternal(contract.Value<double>("thermal_conductivity_w_per_mk"));
            asset.SpecificHeat = SpecificHeatToInternal(contract.Value<double>("specific_heat_j_per_kgk"));
            asset.Emissivity = contract.Value<double>("emissivity");
            if (contract.Value<double?>("porosity") is double porosity) asset.Porosity = porosity;
            if (contract.Value<double?>("reflectivity") is double reflectivity) asset.Reflectivity = reflectivity;
            if (contract.Value<bool?>("transmits_light") is bool transmitsLight) asset.TransmitsLight = transmitsLight;
            var propertySet = material.ThermalAssetId != ElementId.InvalidElementId ? family.GetElement(material.ThermalAssetId) as PropertySetElement : null;
            if (propertySet == null) propertySet = PropertySetElement.Create(family, asset) ?? throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit could not create Thermal Asset " + name + ".");
            else propertySet.SetThermalAsset(asset);
            material.ThermalAssetId = propertySet.Id;
        }
        family.Regenerate();
        if (material.ThermalAssetId == ElementId.InvalidElementId || family.GetElement(material.ThermalAssetId) is not PropertySetElement)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Thermal Asset did not read back for material " + material.Name + ".");
    }

    private static double DensityToInternal(double value)
    {
#if REVIT2019 || REVIT2020
        return UnitUtils.ConvertToInternalUnits(value, DisplayUnitType.DUT_KILOGRAMS_PER_CUBIC_METER);
#else
        return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.KilogramsPerCubicMeter);
#endif
    }

    private static double StressToInternal(double value)
    {
#if REVIT2019 || REVIT2020
        return UnitUtils.ConvertToInternalUnits(value, DisplayUnitType.DUT_MEGAPASCALS);
#else
        return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Megapascals);
#endif
    }

    private static double ThermalConductivityToInternal(double value)
    {
#if REVIT2019 || REVIT2020
        return UnitUtils.ConvertToInternalUnits(value, DisplayUnitType.DUT_WATTS_PER_METER_KELVIN);
#else
        return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.WattsPerMeterKelvin);
#endif
    }

    private static double SpecificHeatToInternal(double value)
    {
#if REVIT2019 || REVIT2020
        return UnitUtils.ConvertToInternalUnits(value, DisplayUnitType.DUT_JOULES_PER_KILOGRAM_CELSIUS);
#else
        return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.JoulesPerKilogramDegreeCelsius);
#endif
    }

    private static Dictionary<string, JObject> CreateLookupTables(Document family, FamilyBlueprintSpec spec)
    {
        var definitions = (spec.Blueprint["lookup_tables"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var result = new Dictionary<string, JObject>(StringComparer.Ordinal);
        if (definitions.Count == 0) return result;
        var owner = family.OwnerFamily ?? throw new CommandResultException(ErrorCodes.TemplateInvalid, "Family document has no OwnerFamily for lookup-table import.");
        var manager = FamilySizeTableManager.GetFamilySizeTableManager(family, owner.Id);
        if (manager == null)
        {
            if (!FamilySizeTableManager.CreateFamilySizeTableManager(family, owner.Id)) throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit could not create a Family size-table manager.");
            manager = FamilySizeTableManager.GetFamilySizeTableManager(family, owner.Id) ?? throw new CommandResultException(ErrorCodes.TransactionFailed, "Family size-table manager was not available after creation.");
        }
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "dscons-blueprint-lookup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporaryDirectory);
        try
        {
            foreach (var definition in definitions)
            {
                var key = definition.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Lookup table key is missing.");
                var name = definition.Value<string>("name") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Lookup table name is missing.");
                var path = Path.Combine(temporaryDirectory, name + ".csv"); File.WriteAllText(path, LookupCsv(definition), new System.Text.UTF8Encoding(false));
                var error = new FamilySizeTableErrorInfo();
                if (!manager.ImportSizeTable(family, path, error) || !manager.HasSizeTable(name))
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Lookup table " + name + " import failed at row " + error.InvalidRowIndex + ", column " + error.InvalidColumnIndex + ": " + error.FamilySizeTableErrorType + ".");
                result.Add(key, (JObject)definition.DeepClone());
            }
        }
        finally { try { Directory.Delete(temporaryDirectory, true); } catch { } }
        return result;
    }

    private static string LookupCsv(JObject definition)
    {
        var columns = (definition["columns"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        string Header(JObject column)
        {
            var dataType = column.Value<string>("data_type");
            var csvType = dataType switch { "length" => "LENGTH", "angle" => "ANGLE", _ => "NUMBER" };
            var unit = dataType switch { "length" => "MILLIMETERS", "angle" => "DEGREES", _ => "GENERAL" };
            return (column.Value<string>("name") ?? string.Empty) + "##" + csvType + "##" + unit;
        }
        string Number(JToken? value) => (value?.Value<double?>() ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Lookup table row contains a missing/non-numeric value.")).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        var lines = new List<string> { "," + string.Join(",", columns.Select(Header)) };
        var rowIndex = 0;
        foreach (var row in (definition["rows"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var values = row["values"] as JObject ?? new JObject();
            var first = columns.Count == 0 ? (++rowIndex).ToString(System.Globalization.CultureInfo.InvariantCulture) : Number(values[columns[0].Value<string>("key")!]);
            lines.Add(first + "," + string.Join(",", columns.Select(column => Number(values[column.Value<string>("key")!]))));
        }
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static string? ExpectedParameterFormula(FamilyBlueprintSpec spec, JObject parameter)
    {
        if (parameter.Value<string>("formula") is string formula) return formula;
        var lookup = parameter["lookup"] as JObject;
        if (lookup == null) return null;
        var tableKey = lookup.Value<string>("table_key") ?? string.Empty;
        var table = (spec.Blueprint["lookup_tables"] as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault(item => string.Equals(item.Value<string>("key"), tableKey, StringComparison.Ordinal))
            ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown lookup table " + tableKey + ".");
        var resultColumnKey = lookup.Value<string>("result_column_key") ?? string.Empty;
        var column = (table["columns"] as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault(item => string.Equals(item.Value<string>("key"), resultColumnKey, StringComparison.Ordinal))
            ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown lookup result column " + resultColumnKey + ".");
        var definitions = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("key") ?? string.Empty, StringComparer.Ordinal);
        string ParameterName(string key) => definitions.TryGetValue(key, out var definition) ? definition.Value<string>("name") ?? string.Empty : throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown lookup Family Parameter " + key + ".");
        var defaultParameter = ParameterName(lookup.Value<string>("default_parameter_key") ?? string.Empty);
        var arguments = (lookup["lookup_parameter_keys"] as JArray ?? new JArray()).Values<string>().Select(key => ParameterName(key ?? string.Empty));
        return "size_lookup(\"" + (table.Value<string>("name") ?? string.Empty) + "\", \"" + (column.Value<string>("name") ?? string.Empty) + "\", " + defaultParameter + ", " + string.Join(", ", arguments) + ")";
    }

    private static void ApplyParameterFormulas(FamilyManager manager, FamilyBlueprintSpec spec, Dictionary<string, FamilyParameter> parameters, Dictionary<string, JObject> lookupTables)
    {
        var formulaContracts = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>()
            .Where(item => item["formula"] != null)
            .ToDictionary(item => item.Value<string>("key") ?? string.Empty, StringComparer.Ordinal);
        var graph = spec.Blueprint["formula_dependency_graph"] as JObject;
        var applicationOrder = graph == null
            ? formulaContracts.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList()
            : (graph["application_order"] as JArray ?? new JArray()).Values<string>().Where(key => !string.IsNullOrWhiteSpace(key)).Select(key => key!).ToList();
        if (applicationOrder.Count != formulaContracts.Count || applicationOrder.Distinct(StringComparer.Ordinal).Count() != applicationOrder.Count || applicationOrder.Any(key => !formulaContracts.ContainsKey(key)))
            throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint formula_dependency_graph.application_order must contain every formula parameter exactly once.");
        foreach (var key in applicationOrder)
        {
            var contract = formulaContracts[key]; var formula = ExpectedParameterFormula(spec, contract) ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Formula contract has no formula.");
            if (!parameters.TryGetValue(key, out var parameter)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Formula contract references unknown Family Parameter " + key + ".");
            manager.SetFormula(parameter, formula);
        }
        foreach (var contract in (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>().Where(item => item["lookup"] != null))
        {
            var key = contract.Value<string>("key") ?? string.Empty;
            var lookup = contract["lookup"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Lookup contract is missing.");
            var tableKey = lookup.Value<string>("table_key") ?? string.Empty;
            if (!lookupTables.ContainsKey(tableKey)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown lookup table " + tableKey + ".");
            if (!parameters.TryGetValue(key, out var parameter)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Lookup contract references unknown Family Parameter " + key + ".");
            manager.SetFormula(parameter, ExpectedParameterFormula(spec, contract) ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Lookup contract has no formula."));
        }
    }

    private static List<JObject> FamilyTypeDefinitions(FamilyBlueprintSpec spec, string requestedTypeName)
    {
        var definitions = (spec.Blueprint["types"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        if (definitions.Count == 0) definitions.Add(new JObject { ["name"] = requestedTypeName, ["values"] = new JObject() });
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            var name = definition.Value<string>("name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint Family Type names must be non-empty and unique (case-insensitive).");
        }
        return definitions;
    }

    private static List<string> ExpectedFamilyTypeNames(FamilyBlueprintSpec spec, string requestedTypeName) =>
        FamilyTypeDefinitions(spec, requestedTypeName).Select(item => item.Value<string>("name")!).ToList();

    /// <summary>
    /// Autodesk RFTs can carry a blank/default Family Type.  A newly generated
    /// RFA must contain the exact Blueprint type set, not an untracked template
    /// residue that changes schedules, Type Catalog expectations or file-size
    /// measurements.  Reuse an exact declared template type if one exists,
    /// create the others, then remove only the remaining undeclared types.
    /// </summary>
    private static List<FamilyType> CreateTypes(FamilyManager manager, FamilyBlueprintSpec spec, Dictionary<string, FamilyParameter> parameters, Dictionary<string, ElementId> materials, Dictionary<string, ElectricalLoadClassification> electricalLoadClassifications, string requestedTypeName)
    {
        var definitions = FamilyTypeDefinitions(spec, requestedTypeName);
        var existing = manager.Types.Cast<FamilyType>().ToList();
        var result = new List<FamilyType>();
        foreach (var definition in definitions)
        {
            var name = definition.Value<string>("name")!;
            var type = existing.SingleOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            if (type == null && existing.Any(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Autodesk template Family Type casing conflicts with the declared Type name " + name + ".");
            if (type == null) type = manager.NewType(name);
            result.Add(type);
        }
        foreach (var definition in definitions)
        {
            var name = definition.Value<string>("name")!;
            var type = result.Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            manager.CurrentType = type;
            foreach (var item in (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>())
            {
                if (string.Equals(item.Value<string>("data_type"), "family_type", StringComparison.Ordinal)) continue;
                if (item["formula"] != null || item["lookup"] != null) continue;
                var key = item.Value<string>("key")!; var value = (definition["values"] as JObject)?[key] ?? item["default"];
                if (value == null && item.Value<string>("source_field") is string sourceField) value = spec.ConfirmedValue(sourceField);
                if (value != null) Set(manager, parameters[key], item.Value<string>("data_type") ?? "text", value, materials, electricalLoadClassifications);
            }
        }
        return result;
    }

    private static void CleanupUndeclaredFamilyTypes(FamilyManager manager, IEnumerable<string> expectedNames)
    {
        var expected = new HashSet<string>(expectedNames, StringComparer.Ordinal);
        foreach (var residue in manager.Types.Cast<FamilyType>().Where(candidate => !expected.Contains(candidate.Name)).ToList())
        {
            manager.CurrentType = residue;
            try { manager.DeleteCurrentType(); }
            catch (Exception ex) { throw new CommandResultException(ErrorCodes.VerificationFailed, "Unable to remove undeclared Autodesk template Family Type " + residue.Name + ": " + ex.Message); }
        }
    }

    private static JObject VerifyExactFamilyTypeSet(FamilyManager manager, IEnumerable<string> expectedNames, string stage)
    {
        var expected = expectedNames.ToList(); var actual = manager.Types.Cast<FamilyType>().Select(item => item.Name).ToList();
        var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal); var actualSet = new HashSet<string>(actual, StringComparer.Ordinal);
        if (expected.Count == 0 || expectedSet.Count != expected.Count || actualSet.Count != actual.Count || expectedSet.Count != actualSet.Count || expectedSet.Any(name => !actualSet.Contains(name)))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Family Type set does not exactly match the Blueprint declaration at " + stage + ". Expected: [" + string.Join(", ", expected) + "]; actual: [" + string.Join(", ", actual) + "].");
        return new JObject
        {
            ["policy"] = "exact_blueprint_family_type_set_v1", ["stage"] = stage,
            ["expected"] = new JArray(expected), ["actual"] = new JArray(actual.OrderBy(item => item, StringComparer.Ordinal)),
            ["template_residue_removed"] = true, ["verified"] = true,
            ["verification_boundary"] = "This verifies the RFA Family Type names only. Type Catalog load/select and Project schedule behavior remain separate runtime gates."
        };
    }

    private static JObject VerifyReopenedExactFamilyTypeSet(FamilyBlueprintSpec spec, string requestedTypeName, JObject readBack)
    {
        var actual = (readBack["types"] as JArray ?? new JArray()).Values<string>().Where(name => name != null).Select(name => name!).ToList();
        var expected = ExpectedFamilyTypeNames(spec, requestedTypeName);
        var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal); var actualSet = new HashSet<string>(actual, StringComparer.Ordinal);
        if (expected.Count == 0 || expectedSet.Count != expected.Count || actualSet.Count != actual.Count || expectedSet.Count != actualSet.Count || expectedSet.Any(name => !actualSet.Contains(name)))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family Type set does not exactly match the Blueprint declaration. Expected: [" + string.Join(", ", expected) + "]; actual: [" + string.Join(", ", actual) + "].");
        return new JObject
        {
            ["policy"] = "exact_blueprint_family_type_set_v1", ["stage"] = "reopened_rfa",
            ["expected"] = new JArray(expected), ["actual"] = new JArray(actual.OrderBy(item => item, StringComparer.Ordinal)),
            ["template_residue_removed"] = true, ["verified"] = true,
            ["verification_boundary"] = "This verifies the reopened RFA Type set only. Type Catalog load/select and Project schedule behavior remain separate runtime gates."
        };
    }

    // Do not create look-alike Family Parameters for the standard Revit
    // Identity Data fields. These bindings are native Category/Family
    // parameters and are what schedules and type properties are expected to
    // expose when the selected template supports them.
    private static IEnumerable<KeyValuePair<string, BuiltInParameter>> NativeIdentityDataParameters()
    {
        yield return new KeyValuePair<string, BuiltInParameter>("manufacturer", BuiltInParameter.ALL_MODEL_MANUFACTURER);
        yield return new KeyValuePair<string, BuiltInParameter>("model", BuiltInParameter.ALL_MODEL_MODEL);
        yield return new KeyValuePair<string, BuiltInParameter>("description", BuiltInParameter.ALL_MODEL_DESCRIPTION);
        yield return new KeyValuePair<string, BuiltInParameter>("url", BuiltInParameter.ALL_MODEL_URL);
        yield return new KeyValuePair<string, BuiltInParameter>("type_comments", BuiltInParameter.ALL_MODEL_TYPE_COMMENTS);
        yield return new KeyValuePair<string, BuiltInParameter>("classification_number", BuiltInParameter.OMNICLASS_CODE);
        yield return new KeyValuePair<string, BuiltInParameter>("classification_title", BuiltInParameter.OMNICLASS_DESCRIPTION);
    }

    internal static JObject BuiltInIdentityDataSnapshot(Document family)
    {
        if (!family.IsFamilyDocument) return new JObject { ["policy"] = "revit_builtin_type_identity_data_v1", ["available"] = false, ["type_values"] = new JArray(), ["inspection_only"] = true };
        var manager = family.FamilyManager;
        var bindings = NativeIdentityDataParameters().ToList();
        var available = new JArray(bindings.Where(binding => manager.get_Parameter(binding.Value) != null).Select(binding => binding.Key));
        var typeValues = new JArray();
        foreach (var type in manager.Types.Cast<FamilyType>())
        {
            var value = new JObject { ["type_name"] = type.Name };
            foreach (var binding in bindings)
            {
                var parameter = manager.get_Parameter(binding.Value);
                value[binding.Key] = parameter == null || parameter.StorageType != StorageType.String ? null : type.AsString(parameter);
            }
            typeValues.Add(value);
        }
        return new JObject { ["policy"] = "revit_builtin_type_identity_data_v1", ["available"] = available.Count == bindings.Count, ["available_fields"] = available, ["type_values"] = typeValues, ["inspection_only"] = true };
    }

    private static JObject ApplyIdentityData(Document family, FamilyManager manager, FamilyBlueprintSpec spec, IReadOnlyCollection<FamilyType> types)
    {
        var contract = spec.Blueprint["identity_data"] as JObject;
        if (contract == null) return new JObject { ["declared"] = false, ["policy"] = "revit_builtin_type_identity_data_v1" };
        var declared = (contract["type_values"] as JArray ?? new JArray()).OfType<JObject>()
            .ToDictionary(item => item.Value<string>("type_name") ?? string.Empty, StringComparer.Ordinal);
        if (declared.Count != types.Count || types.Any(type => !declared.ContainsKey(type.Name)))
            throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Identity Data must declare the exact Family Type set before native Revit fields are written.");
        foreach (var type in types)
        {
            manager.CurrentType = type;
            var values = declared[type.Name];
            foreach (var binding in NativeIdentityDataParameters())
            {
                if (values[binding.Key] == null) continue;
                var expected = values.Value<string>(binding.Key) ?? string.Empty;
                var parameter = manager.get_Parameter(binding.Value);
                if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
                    throw new CommandResultException(ErrorCodes.Unsupported, "Native Revit Identity Data field " + binding.Key + " is unavailable, read-only or not text for Family Type " + type.Name + " in the selected template/category.");
                manager.Set(parameter, expected);
                var observed = type.AsString(parameter);
                if (!string.Equals(observed, expected, StringComparison.Ordinal))
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Native Revit Identity Data field " + binding.Key + " did not read back for Family Type " + type.Name + ".");
            }
        }
        return new JObject { ["declared"] = true, ["policy"] = "revit_builtin_type_identity_data_v1", ["verification"] = "native_type_field_write_read_back", ["type_values"] = (BuiltInIdentityDataSnapshot(family)["type_values"] as JArray ?? new JArray()) };
    }

    private static void VerifyReopenedIdentityData(FamilyBlueprintSpec spec, JObject readBack)
    {
        var contract = spec.Blueprint["identity_data"] as JObject;
        if (contract == null) return;
        var observed = readBack["identity_data"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family inspection did not return native Identity Data.");
        var observedTypes = (observed["type_values"] as JArray ?? new JArray()).OfType<JObject>()
            .ToDictionary(item => item.Value<string>("type_name") ?? string.Empty, StringComparer.Ordinal);
        foreach (var expected in (contract["type_values"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var typeName = expected.Value<string>("type_name") ?? string.Empty;
            if (!observedTypes.TryGetValue(typeName, out var actual)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened Family is missing Identity Data for Family Type " + typeName + ".");
            foreach (var binding in NativeIdentityDataParameters())
            {
                if (expected[binding.Key] == null) continue;
                if (!string.Equals(actual.Value<string>(binding.Key), expected.Value<string>(binding.Key), StringComparison.Ordinal))
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Reopened native Identity Data field " + binding.Key + " does not match the declared value for Family Type " + typeName + ".");
            }
        }
    }

    private static JObject? ApplyLightSource(Document family, FamilyBlueprintSpec spec, IReadOnlyDictionary<string, string> photometricAssets)
    {
        var contract = spec.Blueprint["light_source"] as JObject;
        if (contract == null) return null;
        if (family.OwnerFamily?.FamilyCategory?.Id.IntVal() != (int)BuiltInCategory.OST_LightingFixtures)
            throw new CommandResultException(ErrorCodes.TemplateInvalid, "light_source requires a specialized Autodesk Lighting Fixture template/category.");
        using var lightFamily = LightFamily.GetLightFamily(family);
        if (lightFamily == null || !lightFamily.IsValidObject) throw new CommandResultException(ErrorCodes.TemplateInvalid, "The selected Lighting Fixture template has no API-accessible light source.");
        lightFamily.SetLightShapeStyle(ParseLightShapeStyle(contract.Value<string>("shape_style")));
        lightFamily.SetLightDistributionStyle(ParseLightDistributionStyle(contract.Value<string>("distribution_style")));
        var settings = (contract["type_settings"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("type_name") ?? string.Empty, StringComparer.Ordinal);
        var configured = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < lightFamily.GetNumberOfLightTypes(); index++)
        {
            var typeName = lightFamily.GetLightTypeName(index);
            if (!settings.TryGetValue(typeName, out var setting)) continue;
            using var lightType = lightFamily.GetLightType(index);
            using (var shape = CreateLightShape(contract.Value<string>("shape_style"), setting)) lightType.SetLightShape(shape);
            using (var distribution = CreateLightDistribution(contract.Value<string>("distribution_style"), setting, photometricAssets)) lightType.SetLightDistribution(distribution);
            using (var intensity = CreateInitialIntensity(setting["initial_intensity"] as JObject ?? new JObject())) lightType.SetInitialIntensity(intensity);
            using (var color = CreateInitialColor(setting["initial_color"] as JObject ?? new JObject())) lightType.SetInitialColor(color);
            using (var loss = CreateLossFactor(setting["loss_factor"] as JObject ?? new JObject())) lightType.SetLossFactor(loss);
            if (setting["color_filter_rgb"] is JObject filter) lightType.ColorFilter = new Color((byte)filter.Value<int>("r"), (byte)filter.Value<int>("g"), (byte)filter.Value<int>("b"));
            if (setting.Value<string>("dimming_color") is string dimming) lightType.DimmingColor = dimming == "incandescent" ? LightDimmingColor.Incandescent : LightDimmingColor.None;
            configured.Add(typeName);
        }
        var missing = settings.Keys.Where(name => !configured.Contains(name)).ToList();
        if (missing.Count != 0) throw new CommandResultException(ErrorCodes.VerificationFailed, "Lighting Fixture template did not expose declared light-source types: " + string.Join(", ", missing) + ".");
        family.Regenerate();
        var observed = FamilyLightingReadBack.Describe(family) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Lighting Fixture light source could not be read back after authoring.");
        VerifyLightSourceContract(contract, observed);
        return observed;
    }

    private static JObject? FinalizeLightSourceReadBack(Document family, FamilyBlueprintSpec spec)
    {
        var contract = spec.Blueprint["light_source"] as JObject;
        if (contract == null) return null;
        var observed = FamilyLightingReadBack.Describe(family)
            ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Lighting Fixture light source could not be read back after Family Type cleanup.");
        VerifyLightSourceContract(contract, observed);
        return observed;
    }

    private static LightShapeStyle ParseLightShapeStyle(string? value) => value switch
    {
        "point" => LightShapeStyle.Point, "line" => LightShapeStyle.Line, "rectangle" => LightShapeStyle.Rectangle, "circle" => LightShapeStyle.Circle,
        _ => throw new CommandResultException(ErrorCodes.InvalidParam, "Unsupported light-source shape style: " + value + ".")
    };

    private static LightDistributionStyle ParseLightDistributionStyle(string? value) => value switch
    {
        "spherical" => LightDistributionStyle.Spherical, "hemispherical" => LightDistributionStyle.Hemispherical, "spot" => LightDistributionStyle.Spot, "photometric_web" => LightDistributionStyle.PhotometricWeb,
        _ => throw new CommandResultException(ErrorCodes.InvalidParam, "Unsupported light-source distribution style: " + value + ".")
    };

    private static LightShape CreateLightShape(string? style, JObject setting) => style switch
    {
        "point" => new PointLightShape(),
        "line" => new LineLightShape(Mm(setting.Value<double>("emit_length_mm"))),
        "rectangle" => new RectangleLightShape(Mm(setting.Value<double>("emit_length_mm")), Mm(setting.Value<double>("emit_width_mm"))),
        "circle" => new CircleLightShape(Mm(setting.Value<double>("emit_diameter_mm"))),
        _ => throw new CommandResultException(ErrorCodes.InvalidParam, "Unsupported light-source shape style: " + style + ".")
    };

    private static LightDistribution CreateLightDistribution(string? style, JObject setting, IReadOnlyDictionary<string, string> photometricAssets)
    {
        if (style == "spherical") return new SphericalLightDistribution();
        if (style == "hemispherical") return new HemisphericalLightDistribution();
        if (style == "spot")
        {
            var spot = setting["spot"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Spot light setting is missing.");
            return new SpotLightDistribution(Radians(spot.Value<double>("beam_angle_degrees")), Radians(spot.Value<double>("field_angle_degrees")), Radians(spot.Value<double?>("tilt_angle_degrees") ?? 0));
        }
        if (style == "photometric_web")
        {
            var web = setting["photometric_web"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Photometric-web light setting is missing.");
            var fileName = web.Value<string>("file_name") ?? string.Empty;
            if (!photometricAssets.TryGetValue(fileName, out var path)) throw new CommandResultException(ErrorCodes.SourceUnreadable, "Verified IES asset is missing for " + fileName + ".");
            return new PhotometricWebLightDistribution(path, Radians(web.Value<double?>("tilt_angle_degrees") ?? 0));
        }
        throw new CommandResultException(ErrorCodes.InvalidParam, "Unsupported light-source distribution style: " + style + ".");
    }

    private static InitialIntensity CreateInitialIntensity(JObject contract) => contract.Value<string>("method") switch
    {
        "luminous_flux" => new InitialFluxIntensity(contract.Value<double>("luminous_flux_lm")),
        "luminous_intensity" => new InitialLuminousIntensity(contract.Value<double>("luminous_intensity_cd")),
        "illuminance" => new InitialIlluminanceIntensity(Mm(contract.Value<double>("distance_mm")), contract.Value<double>("illuminance_lux")),
        "wattage" => new InitialWattageIntensity(contract.Value<double>("efficacy_lm_per_w"), contract.Value<double>("wattage_w")),
        _ => throw new CommandResultException(ErrorCodes.InvalidParam, "Unsupported initial light intensity method.")
    };

    private static InitialColor CreateInitialColor(JObject contract)
    {
        if (contract.Value<string>("mode") == "temperature") return new CustomInitialColor(contract.Value<double>("temperature_kelvin"));
        var preset = contract.Value<string>("preset") switch
        {
            "d65" => ColorPreset.D65, "d50" => ColorPreset.D50, "halogen" => ColorPreset.Halogen, "incandescent" => ColorPreset.Incandescent,
            "xenon" => ColorPreset.Xenon, "quartz" => ColorPreset.Quartz, "fluorescent_warm" => ColorPreset.FluorescentWarm,
            "fluorescent_cool" => ColorPreset.FluorescentCool, "fluorescent_white" => ColorPreset.FluorescentWhite,
            "fluorescent_daylight" => ColorPreset.FluorescentDayLight, "fluorescent_light_white" => ColorPreset.FluorescentLightWhite,
            "metal_halide" => ColorPreset.MetalHalide, "high_pressure_sodium" => ColorPreset.HighPressureSodium,
            "low_pressure_sodium" => ColorPreset.LowPressureSodium, "mercury" => ColorPreset.Mercury, "phosphor_mercury" => ColorPreset.PhosphorMercury,
            _ => throw new CommandResultException(ErrorCodes.InvalidParam, "Unsupported initial light color preset.")
        };
        return new PresetInitialColor(preset);
    }

    private static LossFactor CreateLossFactor(JObject contract)
    {
        if (contract.Value<string>("mode") == "basic") return new BasicLossFactor(contract.Value<double>("value"));
        return new AdvancedLossFactor(contract.Value<double>("ballast"), contract.Value<double>("lamp_lumen_depreciation"), contract.Value<double>("lamp_tilt"), contract.Value<double>("luminaire_dirt_depreciation"), contract.Value<double>("surface_depreciation"), contract.Value<double>("temperature"), contract.Value<double>("voltage"));
    }

    private static void VerifyLightSourceContract(JObject contract, JObject observed)
    {
        if (!string.Equals(contract.Value<string>("shape_style"), observed.Value<string>("shape_style"), StringComparison.Ordinal)
            || !string.Equals(contract.Value<string>("distribution_style"), observed.Value<string>("distribution_style"), StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Lighting Fixture source shape/distribution style read-back mismatch.");
        var observedTypes = (observed["type_settings"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("type_name") ?? string.Empty, StringComparer.Ordinal);
        foreach (var expected in (contract["type_settings"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var name = expected.Value<string>("type_name") ?? string.Empty;
            if (!observedTypes.TryGetValue(name, out var actual)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Light-source read-back is missing Family type " + name + ".");
            foreach (var property in new[] { "emit_length_mm", "emit_width_mm", "emit_diameter_mm" }) VerifyNumber(expected, actual, property, name);
            VerifyObject(expected["spot"] as JObject, actual["spot"] as JObject, name, "spot");
            var expectedWeb = expected["photometric_web"] as JObject; var actualWeb = actual["photometric_web"] as JObject;
            if (expectedWeb != null && (actualWeb == null || !string.Equals(expectedWeb.Value<string>("file_name"), actualWeb.Value<string>("file_name"), StringComparison.OrdinalIgnoreCase)))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Photometric web file read-back mismatch for light type " + name + ".");
            if (expectedWeb != null) VerifyNumber(expectedWeb, actualWeb!, "tilt_angle_degrees", name, 0);
            VerifyObject(expected["initial_intensity"] as JObject, actual["initial_intensity"] as JObject, name, "initial_intensity");
            VerifyObject(expected["initial_color"] as JObject, actual["initial_color"] as JObject, name, "initial_color");
            VerifyObject(expected["loss_factor"] as JObject, actual["loss_factor"] as JObject, name, "loss_factor");
            if (expected["color_filter_rgb"] != null && !JToken.DeepEquals(expected["color_filter_rgb"], actual["color_filter_rgb"])) throw new CommandResultException(ErrorCodes.VerificationFailed, "Color-filter read-back mismatch for light type " + name + ".");
            if (expected["dimming_color"] != null && !JToken.DeepEquals(expected["dimming_color"], actual["dimming_color"])) throw new CommandResultException(ErrorCodes.VerificationFailed, "Dimming-color read-back mismatch for light type " + name + ".");
        }

        static void VerifyObject(JObject? expected, JObject? actual, string typeName, string label)
        {
            if (expected == null) return;
            if (actual == null) throw new CommandResultException(ErrorCodes.VerificationFailed, "Missing " + label + " read-back for light type " + typeName + ".");
            foreach (var property in expected.Properties())
            {
                if (property.Name == "sha256") continue;
                if (property.Value.Type is JTokenType.Integer or JTokenType.Float) VerifyNumber(expected, actual, property.Name, typeName);
                else if (!JToken.DeepEquals(property.Value, actual[property.Name])) throw new CommandResultException(ErrorCodes.VerificationFailed, label + " read-back mismatch for light type " + typeName + ": " + property.Name + ".");
            }
        }

        static void VerifyNumber(JObject expected, JObject actual, string property, string typeName, double? defaultValue = null)
        {
            if (expected[property] == null && !defaultValue.HasValue) return;
            var requested = expected.Value<double?>(property) ?? defaultValue!.Value; var value = actual.Value<double?>(property);
            if (!value.HasValue || Math.Abs(requested - value.Value) > Math.Max(.001, Math.Abs(requested) * 1e-6)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Numeric light-source read-back mismatch for type " + typeName + ": " + property + ".");
        }
    }

    private static void ApplyPartMaterial(FamilyManager manager, GenericForm form, JObject part, Dictionary<string, FamilyParameter> parameters, Dictionary<string, ElementId> materials)
    {
        var elementParameter = form.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
        if (part.Value<string>("material_parameter") is string parameterKey)
        {
            if (elementParameter == null || !manager.CanElementParameterBeAssociated(elementParameter)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Form material cannot be associated for part " + part.Value<string>("key") + ".");
            manager.AssociateElementParameterToFamilyParameter(elementParameter, RequireParameter(parameters, parameterKey));
        }
        else if (part.Value<string>("material_key") is string materialKey)
        {
            if (!materials.TryGetValue(materialKey, out var materialId)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown Blueprint material " + materialKey + ".");
            if (elementParameter == null || elementParameter.IsReadOnly || !elementParameter.Set(materialId)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Fixed form material could not be assigned for part " + part.Value<string>("key") + ".");
        }
    }

    private static Dictionary<string, CoordinationZoneResult> CreateCoordinationZones(Document family, FamilyBlueprintSpec spec, FamilyManager manager, Dictionary<string, FamilyParameter> parameters, Dictionary<string, ElementId> materials)
    {
        var results = new Dictionary<string, CoordinationZoneResult>(StringComparer.Ordinal);
        foreach (var contract in (spec.Blueprint["coordination_zones"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var key = contract.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Coordination zone key is missing.");
            var shape = contract.Value<string>("shape") ?? string.Empty; var axis = contract.Value<string>("axis") ?? string.Empty;
            var origin = Point(contract["origin_mm"], "coordination zone " + key + " origin_mm"); var frame = ExtrusionFrame(axis);
            var profile = new JObject { ["shape"] = shape == "box" ? "rectangle" : "circle" };
            void CopyDimension(string property) { if (contract[property] != null) profile[property] = contract[property]!.DeepClone(); }
            if (shape == "box") { CopyDimension("width_mm"); CopyDimension("width_parameter"); CopyDimension("height_mm"); CopyDimension("height_parameter"); }
            else { CopyDimension("diameter_mm"); CopyDimension("diameter_parameter"); }
            var lengthProperty = shape == "box" ? "depth_mm" : "length_mm";
            var lengthParameterProperty = shape == "box" ? "depth_parameter" : "length_parameter";
            var lengthParameterKey = contract.Value<string>(lengthParameterProperty);
            var length = lengthParameterKey == null ? Mm(contract.Value<double>(lengthProperty)) : CurrentLength(manager, RequireParameter(parameters, lengthParameterKey));
            if (length <= 0) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Coordination zone " + key + " axial length must be positive.");
            var plane = SketchPlane.Create(family, Plane.CreateByOriginAndBasis(origin, frame.Width, frame.Height));
            var resolvedProfile = ResolvedProfileAtFrame(profile, origin, frame.Width, frame.Height, parameters, manager);
            var form = family.FamilyCreate.NewExtrusion(true, resolvedProfile, plane, length); form.StartOffset = -length / 2.0; form.EndOffset = length / 2.0;
            AssociateProfileParameters(manager, form, profile, parameters, frame, origin);
            if (lengthParameterKey != null)
            {
                var source = RequireParameter(parameters, lengthParameterKey); var half = AddInternalLength(manager, "_" + key + "_half_length"); var negative = AddInternalLength(manager, "_" + key + "_negative_half_length");
                manager.SetFormula(half, source.Definition.Name + " / 2"); manager.SetFormula(negative, "-" + half.Definition.Name);
                Associate(manager, form.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM), negative); Associate(manager, form.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), half);
            }
            var subcategory = contract.Value<string>("subcategory") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Coordination zone " + key + " subcategory is missing.");
            SetCoordinationZoneRole(family, form, subcategory, contract["visibility"] as JObject ?? new JObject());
            var visibilityParameter = AssociateVisibility(manager, form, contract, parameters, "coordination zone " + key);
            ApplyPartMaterial(manager, form, contract, parameters, materials); family.Regenerate();
            if (!string.Equals(form.Subcategory?.Name, subcategory, StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Coordination zone " + key + " subcategory did not read back.");
            var materialKey = contract.Value<string>("material_key") ?? string.Empty; var materialParameter = form.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
            if (!materials.TryGetValue(materialKey, out var expectedMaterial) || materialParameter?.AsElementId() != expectedMaterial) throw new CommandResultException(ErrorCodes.VerificationFailed, "Coordination zone " + key + " material did not read back.");
            if (!JToken.DeepEquals(VisibilitySnapshot(form.GetVisibility()), VisibilitySnapshot(Visibility(contract["visibility"] as JObject ?? new JObject(), FamilyElementVisibilityType.Model)))) throw new CommandResultException(ErrorCodes.VerificationFailed, "Coordination zone " + key + " visibility did not read back.");
            results.Add(key, new CoordinationZoneResult { Key = key, Form = form, Contract = contract, VisibilityParameter = visibilityParameter });
        }
        if (results.Count != (spec.Blueprint["coordination_zones"] as JArray ?? new JArray()).Count) throw new CommandResultException(ErrorCodes.VerificationFailed, "Coordination-zone read-back count does not match the Blueprint declaration.");
        return results;
    }

    private static CurveArrArray Profile(JObject profile, Dictionary<string, FamilyParameter> parameters, FamilyManager manager, FamilyBlueprintSpec spec)
    {
        double Value(string literal, string parameterKey)
        {
            if (profile.Value<string>(parameterKey) is string key) return CurrentLength(manager, RequireParameter(parameters, key));
            var mm = profile.Value<double?>(literal) ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blueprint profile is missing " + literal + "."); return Mm(mm);
        }
        var shape = profile.Value<string>("shape");
        if (shape == "rectangle") return Rectangle(Value("width_mm", "width_parameter"), Value("height_mm", "height_parameter"));
        if (shape == "circle") return Circle(Value("diameter_mm", "diameter_parameter") / 2);
        if (shape == "oval") return Oval(Value("width_mm", "width_parameter"), Value("height_mm", "height_parameter"), profile.Value<string>("major_axis") ?? string.Empty);
        if (shape == "ring")
        {
            var outer = Value("outer_diameter_mm", "outer_diameter_parameter") / 2; var inner = Value("inner_diameter_mm", "inner_diameter_parameter") / 2;
            if (inner <= 0 || inner >= outer) throw new CommandResultException(ErrorCodes.InvalidParam, "Ring inner diameter must be positive and smaller than outer diameter.");
            var result = Circle(outer); result.Append(CircleLoop(inner)); return result;
        }
        throw new CommandResultException(ErrorCodes.Unsupported, "Unsupported Blueprint profile " + shape + ".");
    }

    private static GenericForm CreateLiteralForm(Document family, JObject part, JObject profile, Dictionary<string, FamilyParameter> parameters, FamilyManager manager)
    {
        var primitive = part.Value<string>("primitive") ?? string.Empty;
        var isSolid = !string.Equals(part.Value<string>("operation"), "void", StringComparison.Ordinal);
        if (primitive == "revolution")
        {
            var planeKey = part.Value<string>("profile_plane") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Revolution profile_plane is missing.");
            var origin = Point(part["profile_origin_mm"], "revolution profile_origin_mm");
            var sketch = SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(PlaneNormal(planeKey), origin));
            var axis = Line.CreateBound(Point(part["axis_start_mm"], "revolution axis_start_mm"), Point(part["axis_end_mm"], "revolution axis_end_mm"));
            var revolutionProfile = ProfileHasParameters(profile) ? ResolvedProfileAt(profile, planeKey, origin, parameters, manager) : LiteralProfileAt(profile, planeKey, origin);
            var revolution = family.FamilyCreate.NewRevolution(isSolid, revolutionProfile, sketch, axis, Radians(part.Value<double>("start_angle_degrees")), Radians(part.Value<double>("end_angle_degrees")));
            if (ProfileHasParameters(profile)) LabelSketchProfile(family, revolution.Sketch, profile, parameters, manager, origin, "revolution " + (part.Value<string>("key") ?? revolution.Id.Val().ToString()) + " profile", planeKey);
            return revolution;
        }
        if (primitive == "sweep")
        {
            var path = PathCurves(part["path"] as JObject, parameters, manager, out var pathPlaneKey, out var pathOrigin, out var arcFrame);
            var pathPlane = SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(PlaneNormal(pathPlaneKey), pathOrigin));
            var resolvedProfile = ProfileHasParameters(profile) ? ResolvedProfileAt(profile, "xy", XYZ.Zero, parameters, manager) : LiteralProfileAt(profile, "xy", XYZ.Zero);
            var sweepProfile = family.Application.Create.NewCurveLoopsProfile(resolvedProfile);
            var sweep = family.FamilyCreate.NewSweep(isSolid, path, pathPlane, sweepProfile, 0, ParseProfileLocation(part.Value<string>("profile_location")));
            AssociateSweepParameters(family, sweep, part, profile, parameters, manager, pathOrigin, arcFrame);
            return sweep;
        }
        if (primitive == "blend")
        {
            var start = Mm(part.Value<double>("start_mm")); var end = Mm(part.Value<double>("end_mm"));
            var baseOrigin = new XYZ(start, 0, 0); var topOrigin = new XYZ(end, 0, 0);
            var sketch = SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(XYZ.BasisX, baseOrigin));
            var endProfile = part["end_profile"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Blend end_profile is missing.");
            var blend = family.FamilyCreate.NewBlend(isSolid, SingleLoop(ResolvedProfileAt(profile, "yz", baseOrigin, parameters, manager), "blend base"), SingleLoop(ResolvedProfileAt(endProfile, "yz", topOrigin, parameters, manager), "blend top"), sketch);
            AssociateBlendProfileParameters(family, blend, profile, endProfile, parameters, manager, baseOrigin, topOrigin, part.Value<string>("key") ?? "blend");
            return blend;
        }
        if (primitive == "swept_blend")
        {
            var path = SinglePathCurve(part["path"] as JObject, parameters, manager, out var pathPlaneKey, out var pathOrigin);
            var pathPlane = SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(PlaneNormal(pathPlaneKey), pathOrigin));
            var endProfile = part["end_profile"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Swept blend end_profile is missing.");
            var bottomCurves = ProfileHasParameters(profile) ? ResolvedProfileAt(profile, "xy", XYZ.Zero, parameters, manager) : LiteralProfileAt(profile, "xy", XYZ.Zero);
            var topCurves = ProfileHasParameters(endProfile) ? ResolvedProfileAt(endProfile, "xy", XYZ.Zero, parameters, manager) : LiteralProfileAt(endProfile, "xy", XYZ.Zero);
            var bottom = family.Application.Create.NewCurveLoopsProfile(bottomCurves); var top = family.Application.Create.NewCurveLoopsProfile(topCurves);
            var sweptBlend = family.FamilyCreate.NewSweptBlend(isSolid, path, pathPlane, bottom, top);
            if (ProfileHasParameters(profile) || ProfileHasParameters(endProfile))
            {
                LabelSketchProfile(family, sweptBlend.BottomSketch ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameterized swept blend has no bottom profile sketch."), profile, parameters, manager, path.GetEndPoint(0), "swept blend " + (part.Value<string>("key") ?? sweptBlend.Id.Val().ToString()) + " bottom", "yz");
                LabelSketchProfile(family, sweptBlend.TopSketch ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameterized swept blend has no top profile sketch."), endProfile, parameters, manager, path.GetEndPoint(1), "swept blend " + (part.Value<string>("key") ?? sweptBlend.Id.Val().ToString()) + " top", "yz");
            }
            return sweptBlend;
        }
        throw new CommandResultException(ErrorCodes.Unsupported, "Unsupported Blueprint primitive " + primitive + ".");
    }

    private static CurveArrArray LiteralProfileAt(JObject profile, string plane, XYZ origin)
    {
        double Required(string name) => Mm(profile.Value<double?>(name) ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Literal profile is missing " + name + "."));
        var shape = profile.Value<string>("shape");
        if (shape == "rectangle") return RectangleAt(Required("width_mm"), Required("height_mm"), plane, origin);
        if (shape == "circle") return CircleAt(Required("diameter_mm") / 2, plane, origin);
        if (shape == "oval") return OvalAt(Required("width_mm"), Required("height_mm"), profile.Value<string>("major_axis") ?? string.Empty, plane, origin);
        if (shape == "ring")
        {
            var outer = Required("outer_diameter_mm") / 2; var inner = Required("inner_diameter_mm") / 2;
            if (inner <= 0 || inner >= outer) throw new CommandResultException(ErrorCodes.InvalidParam, "Ring inner diameter must be positive and smaller than outer diameter.");
            var result = CircleAt(outer, plane, origin); result.Append(CircleLoopAt(inner, plane, origin)); return result;
        }
        throw new CommandResultException(ErrorCodes.Unsupported, "Unsupported literal profile " + shape + ".");
    }

    private static CurveArrArray ResolvedProfileAt(JObject profile, string plane, XYZ origin, Dictionary<string, FamilyParameter> parameters, FamilyManager manager)
    {
        double Value(string literal, string parameterProperty)
        {
            if (profile.Value<string>(parameterProperty) is string parameterKey) return CurrentLength(manager, RequireParameter(parameters, parameterKey));
            return Mm(profile.Value<double?>(literal) ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Resolved profile is missing " + literal + "."));
        }
        var shape = profile.Value<string>("shape");
        if (shape == "rectangle") return RectangleAt(Value("width_mm", "width_parameter"), Value("height_mm", "height_parameter"), plane, origin);
        if (shape == "circle") return CircleAt(Value("diameter_mm", "diameter_parameter") / 2, plane, origin);
        if (shape == "oval") return OvalAt(Value("width_mm", "width_parameter"), Value("height_mm", "height_parameter"), profile.Value<string>("major_axis") ?? string.Empty, plane, origin);
        if (shape == "ring")
        {
            var outer = Value("outer_diameter_mm", "outer_diameter_parameter") / 2; var inner = Value("inner_diameter_mm", "inner_diameter_parameter") / 2;
            if (inner <= 0 || inner >= outer) throw new CommandResultException(ErrorCodes.InvalidParam, "Ring inner diameter must be positive and smaller than outer diameter.");
            var result = CircleAt(outer, plane, origin); result.Append(CircleLoopAt(inner, plane, origin)); return result;
        }
        throw new CommandResultException(ErrorCodes.Unsupported, "Parameterized profile supports rectangle, circle, ring or oval only.");
    }

    private static CurveArrArray ResolvedProfileAtFrame(JObject profile, XYZ origin, XYZ widthAxis, XYZ heightAxis, Dictionary<string, FamilyParameter> parameters, FamilyManager manager)
    {
        double Value(string literal, string parameterProperty)
        {
            if (profile.Value<string>(parameterProperty) is string parameterKey) return CurrentLength(manager, RequireParameter(parameters, parameterKey));
            return Mm(profile.Value<double?>(literal) ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Resolved profile is missing " + literal + "."));
        }
        var shape = profile.Value<string>("shape");
        if (shape == "rectangle") return RectangleAtFrame(Value("width_mm", "width_parameter"), Value("height_mm", "height_parameter"), origin, widthAxis, heightAxis);
        if (shape == "circle") return CircleAtFrame(Value("diameter_mm", "diameter_parameter") / 2, origin, widthAxis, heightAxis);
        if (shape == "oval") return OvalAtFrame(Value("width_mm", "width_parameter"), Value("height_mm", "height_parameter"), profile.Value<string>("major_axis") ?? string.Empty, origin, widthAxis, heightAxis);
        if (shape == "ring")
        {
            var outer = Value("outer_diameter_mm", "outer_diameter_parameter") / 2; var inner = Value("inner_diameter_mm", "inner_diameter_parameter") / 2;
            if (inner <= 0 || inner >= outer) throw new CommandResultException(ErrorCodes.InvalidParam, "Ring inner diameter must be positive and smaller than outer diameter.");
            var result = CircleAtFrame(outer, origin, widthAxis, heightAxis); result.Append(CircleLoopAtFrame(inner, origin, widthAxis, heightAxis)); return result;
        }
        throw new CommandResultException(ErrorCodes.Unsupported, "Parameterized profile supports rectangle, circle, ring or oval only.");
    }

    private static bool ProfileHasParameters(JObject profile) => profile.Properties().Any(property => property.Name.EndsWith("_parameter", StringComparison.Ordinal));

    private static void AssociateSweepParameters(Document family, Sweep sweep, JObject part, JObject profile, Dictionary<string, FamilyParameter> parameters, FamilyManager manager, XYZ profileOrigin, ArcPathFrame? arcFrame)
    {
        family.Regenerate();
        if (ProfileHasParameters(profile))
            throw new CommandResultException(ErrorCodes.Unsupported, "Parameterized Sweep ProfileSketch is a controlled Family Editor UI fallback: Revit rejects its geometric dimension references through the Family API, and SketchEditScope is permitted only for Project documents. Complete and reopen/inspect the profile dimensions through the dedicated Family Editor workflow before claiming API flex support.");
        if (part["path"] is JObject path && path.Value<string>("radius_parameter") is string radiusParameter)
        {
            if (arcFrame == null) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "A sweep radius_parameter requires an arc path.");
            LabelSweepPathRadius(family, sweep, path, radiusParameter, parameters, manager, arcFrame, part.Value<string>("key") ?? "sweep");
        }
        if (part["path"] is JObject offsetPath && offsetPath.Value<string>("kind") == "offset")
            LabelParameterizedOffsetPath(family, sweep, offsetPath, parameters, manager, part.Value<string>("key") ?? "sweep");
    }

    private static void AssociateBlendProfileParameters(Document family, Blend blend, JObject bottomProfile, JObject topProfile, Dictionary<string, FamilyParameter> parameters, FamilyManager manager, XYZ bottomOrigin, XYZ topOrigin, string partKey)
    {
        // Keep parameterized blend constraints explicit; arc sweeps reuse only the bounded sketch-profile labeling primitive below.
        family.Regenerate();
        LabelSketchProfile(family, blend.BottomSketch ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Blend " + partKey + " has no bottom profile sketch."), bottomProfile, parameters, manager, bottomOrigin, partKey + " bottom");
        LabelSketchProfile(family, blend.TopSketch ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Blend " + partKey + " has no top profile sketch."), topProfile, parameters, manager, topOrigin, partKey + " top");
    }

    private static void LabelSketchProfile(Document family, Sketch sketch, JObject profile, Dictionary<string, FamilyParameter> parameters, FamilyManager manager, XYZ origin, string label, string plane = "yz", bool useSketchPlaneOrientation = false)
    {
        var parameterProperties = profile.Properties().Where(property => property.Name.EndsWith("_parameter", StringComparison.Ordinal)).ToList();
        if (parameterProperties.Count == 0) return;
        family.Regenerate();
        var modelCurves = sketch.Profile.Cast<CurveArray>().SelectMany(loop => loop.Cast<Curve>()).Select(curve => curve.Reference == null ? null : family.GetElement(curve.Reference.ElementId) as ModelCurve).Where(item => item != null).Cast<ModelCurve>().ToList();
        View view; XYZ widthAxis; XYZ heightAxis;
        if (useSketchPlaneOrientation)
        {
            var sketchPlane = sketch.SketchPlane?.GetPlane() ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameterized Reference Line sweep profile has no SketchPlane.");
            widthAxis = sketchPlane.XVec.Normalize(); heightAxis = sketchPlane.YVec.Normalize();
            view = ViewForNormal(family, sketchPlane.Normal, heightAxis, "parameterized Reference Line sweep profile");
        }
        else
        {
            view = ViewForPlane(family, plane, "parameterized profile");
            widthAxis = plane switch { "xy" => XYZ.BasisX, "xz" => XYZ.BasisX, "yz" => XYZ.BasisY, _ => throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unsupported parameterized profile plane " + plane + ".") };
            heightAxis = plane switch { "xy" => XYZ.BasisY, "xz" => XYZ.BasisZ, "yz" => XYZ.BasisZ, _ => throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unsupported parameterized profile plane " + plane + ".") };
        }
        double Current(string literal, string parameterProperty)
        {
            if (profile.Value<string>(parameterProperty) is string parameterKey) return CurrentLength(manager, RequireParameter(parameters, parameterKey));
            return Mm(profile.Value<double?>(literal) ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, label + " is missing " + literal + "."));
        }
        var shape = profile.Value<string>("shape");
        if (shape == "rectangle")
        {
            var width = Current("width_mm", "width_parameter"); var height = Current("height_mm", "height_parameter");
            var lines = modelCurves.Where(item => item.GeometryCurve is Line).Select(item => (Curve: item, Line: (Line)item.GeometryCurve, Direction: ((Line)item.GeometryCurve).Direction)).ToList();
            void Dimension(string property, IEnumerable<(ModelCurve Curve, Line Line, XYZ Direction)> candidates, Line dimensionLine)
            {
                if (profile.Value<string>(property) is not string parameterKey) return;
                var sortAxis = property == "width_parameter" ? widthAxis : heightAxis;
                var crossAxis = property == "width_parameter" ? heightAxis : widthAxis;
                var selected = candidates.OrderBy(item => item.Line.Evaluate(.5, true).Subtract(origin).DotProduct(sortAxis)).ToList();
                if (selected.Count != 2) throw ConstraintFailure("profile_" + (property == "width_parameter" ? "width" : "height"), label, parameterKey, Array.Empty<Reference?>(), "The sketch did not expose exactly two dimensionable opposite edges.");
                var firstReference = RequireCurveReference(selected[0].Curve, "profile_" + property + "_first", label, parameterKey);
                var secondReference = RequireCurveReference(selected[1].Curve, "profile_" + property + "_second", label, parameterKey);
                // Sketch-curve references are not a stable dimension contract after a
                // sweep/profile regeneration.  Lock each edge to a datum plane first,
                // then label a plane-to-plane dimension.  This is intentionally kept
                // as a real native constraint; it is not a static-geometry fallback.
                var firstPoint = selected[0].Line.Evaluate(.5, true);
                var secondPoint = selected[1].Line.Evaluate(.5, true);
                var firstDatum = CreateDatumPlane(family, view, firstPoint, widthAxis.CrossProduct(heightAxis).Normalize(), crossAxis, "profile_" + property + "_first", label, parameterKey);
                var secondDatum = CreateDatumPlane(family, view, secondPoint, widthAxis.CrossProduct(heightAxis).Normalize(), crossAxis, "profile_" + property + "_second", label, parameterKey);
                AlignToDatum(family, view, firstReference, firstDatum, "profile_" + property + "_first", label, parameterKey);
                AlignToDatum(family, view, secondReference, secondDatum, "profile_" + property + "_second", label, parameterKey);
                var dimension = CreateStableLinearDimension(family, view, dimensionLine, new Reference(firstDatum), new Reference(secondDatum), "profile_" + (property == "width_parameter" ? "width" : "height"), label, parameterKey);
                dimension.FamilyLabel = RequireParameter(parameters, parameterKey); family.Regenerate();
                if (dimension.FamilyLabel?.Id != RequireParameter(parameters, parameterKey).Id) throw ConstraintFailure("profile_" + (property == "width_parameter" ? "width" : "height"), label, parameterKey, new[] { new Reference(firstDatum), new Reference(secondDatum) }, "The parameter label did not read back after regeneration.");
            }
            Dimension("width_parameter", lines.Where(item => Math.Abs(Math.Abs(item.Direction.DotProduct(heightAxis)) - 1) < 1e-6), Line.CreateBound(origin.Add(widthAxis.Multiply(-width / 2)).Add(heightAxis.Multiply(height / 2 + Mm(50))), origin.Add(widthAxis.Multiply(width / 2)).Add(heightAxis.Multiply(height / 2 + Mm(50)))));
            Dimension("height_parameter", lines.Where(item => Math.Abs(Math.Abs(item.Direction.DotProduct(widthAxis)) - 1) < 1e-6), Line.CreateBound(origin.Add(widthAxis.Multiply(width / 2 + Mm(50))).Add(heightAxis.Multiply(-height / 2)), origin.Add(widthAxis.Multiply(width / 2 + Mm(50))).Add(heightAxis.Multiply(height / 2))));
            return;
        }
        Dimension LabelCircularLoop(int loopIndex, string parameterProperty, string suffix)
        {
            var parameterKey = profile.Value<string>(parameterProperty) ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, label + " is missing " + parameterProperty + ".");
            var loop = sketch.Profile.Cast<CurveArray>().ElementAtOrDefault(loopIndex) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, label + " is missing circular profile loop " + loopIndex + ".");
            var sourceCurve = loop.Cast<Curve>().OfType<Arc>().FirstOrDefault() ?? throw new CommandResultException(ErrorCodes.VerificationFailed, label + " has no circular arc in loop " + loopIndex + ".");
            var modelCurve = (sourceCurve.Reference == null ? null : family.GetElement(sourceCurve.Reference.ElementId) as ModelCurve)
                ?? throw new CommandResultException(ErrorCodes.VerificationFailed, label + " has no dimensionable circular profile arc in loop " + loopIndex + ".");
            var arc = modelCurve.GeometryCurve as Arc ?? throw new CommandResultException(ErrorCodes.VerificationFailed, label + " has no dimensionable circular profile arc in loop " + loopIndex + ".");
            var arcReference = RequireCurveReference(modelCurve, "profile_" + suffix + "_diameter", label, parameterKey);
            var dimension = CreateStableDiameterDimension(family, view, arcReference, arc.Center.Add(arc.Evaluate(.5, true).Subtract(arc.Center).Normalize().Multiply(arc.Radius + Mm(50))), "profile_" + suffix + "_diameter", label, parameterKey);
            var diameter = RequireParameter(parameters, parameterKey); dimension.FamilyLabel = diameter; family.Regenerate();
            if (dimension.FamilyLabel?.Id != diameter.Id) throw ConstraintFailure("profile_" + suffix + "_diameter", label, parameterKey, new[] { arcReference }, "The diameter label did not read back after regeneration.");
            return dimension;
        }
        if (shape == "circle" && profile.Value<string>("diameter_parameter") != null)
        {
            LabelCircularLoop(0, "diameter_parameter", "diameter");
            return;
        }
        if (shape == "ring" && profile.Value<string>("outer_diameter_parameter") != null && profile.Value<string>("inner_diameter_parameter") != null)
        {
            LabelCircularLoop(0, "outer_diameter_parameter", "outer"); LabelCircularLoop(1, "inner_diameter_parameter", "inner");
            return;
        }
        if (shape == "oval")
        {
            LabelOvalSketchProfile(family, sketch, profile, parameters, manager, label);
            return;
        }
        throw new CommandResultException(ErrorCodes.Unsupported, "Parameterized profile supports rectangle, circle or oval only: " + label + ".");
    }

    private static void LabelOvalSketchProfile(Document family, Sketch sketch, JObject profile, Dictionary<string, FamilyParameter> parameters, FamilyManager manager, string label)
    {
        var widthKey = profile.Value<string>("width_parameter") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, label + " oval width_parameter is missing.");
        var heightKey = profile.Value<string>("height_parameter") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, label + " oval height_parameter is missing.");
        var majorAxisKey = profile.Value<string>("major_axis") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, label + " oval major_axis is missing.");
        var width = RequireParameter(parameters, widthKey); var height = RequireParameter(parameters, heightKey);
        var major = majorAxisKey == "width" ? width : majorAxisKey == "height" ? height : throw new CommandResultException(ErrorCodes.EvidenceInvalid, label + " oval major_axis must be width or height.");
        var minor = majorAxisKey == "width" ? height : width;
        if (CurrentLength(manager, major) <= CurrentLength(manager, minor)) throw new CommandResultException(ErrorCodes.InvalidParam, label + " oval major dimension must be greater than its minor dimension.");
        family.Regenerate();
        var curves = sketch.Profile.Cast<CurveArray>().SelectMany(loop => loop.Cast<Curve>())
            .Select(curve => curve.Reference == null ? null : family.GetElement(curve.Reference.ElementId) as ModelCurve)
            .Where(item => item != null).Cast<ModelCurve>().ToList();
        var arcs = curves.Where(item => item.GeometryCurve is Arc).ToList(); var lines = curves.Where(item => item.GeometryCurve is Line).ToList();
        if (arcs.Count != 2 || lines.Count != 2) throw new CommandResultException(ErrorCodes.VerificationFailed, label + " flat-oval sketch must expose exactly two arcs and two tangent lines.");
        var firstArc = (Arc)arcs[0].GeometryCurve; var secondArc = (Arc)arcs[1].GeometryCurve;
        var centerVector = secondArc.Center.Subtract(firstArc.Center);
        if (centerVector.GetLength() < 1e-9) throw new CommandResultException(ErrorCodes.VerificationFailed, label + " oval arc centers are coincident.");
        var majorAxis = centerVector.Normalize(); var normal = firstArc.Normal.Normalize(); var minorAxis = normal.CrossProduct(majorAxis).Normalize();
        var origin = firstArc.Center.Add(secondArc.Center).Multiply(.5);
        var view = ViewForNormal(family, normal, minorAxis, label + " flat-oval profile");
        var safeKey = SafeInternalKey(label) + "_" + sketch.Id.Val();
        var minorRadius = AddInternalLength(manager, "_" + safeKey + "_minor_radius");
        var halfCenterSpacing = AddInternalLength(manager, "_" + safeKey + "_half_center_spacing");
        manager.SetFormula(minorRadius, minor.Definition.Name + " / 2");
        manager.SetFormula(halfCenterSpacing, "(" + major.Definition.Name + " - " + minor.Definition.Name + ") / 2");
        foreach (var modelArc in arcs)
        {
            var arc = (Arc)modelArc.GeometryCurve; var radialDirection = arc.Evaluate(.5, true).Subtract(arc.Center).Normalize();
            var arcReference = RequireCurveReference(modelArc, "flat_oval_radius", label, minor.Definition.Name);
            var radial = CreateStableRadialDimension(family, view, arcReference, arc.Evaluate(.5, true).Add(radialDirection.Multiply(Mm(50))), "flat_oval_radius", label, minor.Definition.Name);
            radial.FamilyLabel = minorRadius;
        }
        var extent = Math.Max(CurrentLength(manager, major), Mm(500));
        var centerPlane = family.FamilyCreate.NewReferencePlane2(origin.Add(minorAxis.Multiply(-extent)), origin.Add(minorAxis.Multiply(extent)), origin.Add(normal.Multiply(extent)), view);
        centerPlane.Name = "_DSCons_" + safeKey + "_major_center";
        centerPlane.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME)?.Set(ReferenceParameterValue(FamilyInstanceReferenceType.NotAReference));
        foreach (var modelArc in arcs)
        {
            var arc = (Arc)modelArc.GeometryCurve; var direction = arc.Center.Subtract(origin).Normalize(); var offset = minorAxis.Multiply(arc.Radius + Mm(50));
            var centerDatum = CreateDatumPlane(family, view, arc.Center, minorAxis, normal, "flat_oval_center", label, halfCenterSpacing.Definition.Name);
            var centerReference = RequireCenterPointReference(modelArc, "flat_oval_center", label, halfCenterSpacing.Definition.Name);
            AlignToDatum(family, view, centerReference, centerDatum, "flat_oval_center", label, halfCenterSpacing.Definition.Name);
            var dimension = CreateStableLinearDimension(family, view, Line.CreateBound(origin.Add(offset), arc.Center.Add(offset)), new Reference(centerPlane), new Reference(centerDatum), "flat_oval_center_spacing", label, halfCenterSpacing.Definition.Name);
            dimension.FamilyLabel = halfCenterSpacing;
            if (Math.Abs(Math.Abs(direction.DotProduct(majorAxis)) - 1) > 1e-6) throw new CommandResultException(ErrorCodes.VerificationFailed, label + " oval arc centers are not aligned to one major axis.");
        }
        family.Regenerate();
        var labels = new HashSet<ElementId>(new[] { minorRadius.Id, halfCenterSpacing.Id });
        var observed = new FilteredElementCollector(family).OfClass(typeof(Dimension)).Cast<Dimension>().Where(item => item.FamilyLabel != null && labels.Contains(item.FamilyLabel.Id)).ToList();
        if (observed.Count < 4 || observed.Count(item => item.FamilyLabel?.Id == minorRadius.Id) != 2 || observed.Count(item => item.FamilyLabel?.Id == halfCenterSpacing.Id) != 2)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Flat-oval radius/center constraints did not read back for " + label + ".");
    }

    private static string SafeInternalKey(string value) => new(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());

    // FamilyCreate.NewDimension is unusually sensitive to transient sketch and
    // arc references.  Keep the defensive checks in one place so a failed
    // constraint names the exact role/part/parameter instead of surfacing the
    // unhelpful Revit message "Invalid number of references".
    private static CommandResultException ConstraintFailure(string role, string part, string parameter, IEnumerable<Reference?>? references, string detail)
    {
        string Describe(Reference? reference)
        {
            if (reference == null) return "null";
            try
            {
                var id = reference.ElementId;
                return id == ElementId.InvalidElementId ? "invalid" : id.Val().ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { return "unreadable"; }
        }
        var observed = references == null ? string.Empty : string.Join(",", references.Select(Describe));
        return new CommandResultException(ErrorCodes.VerificationFailed,
            "constraint_role=" + role + "; part=" + part + "; parameter=" + parameter + "; references=[" + observed + "]; detail=" + detail);
    }

    private static ReferencePlane CreateDatumPlane(Document family, View view, XYZ origin, XYZ alongAxis, XYZ cutAxis, string role, string part, string parameter)
    {
        if (family == null || view == null || origin == null || alongAxis == null || cutAxis == null)
            throw ConstraintFailure(role, part, parameter, Array.Empty<Reference?>(), "The datum plane inputs are null.");
        if (alongAxis.GetLength() < 1e-9 || cutAxis.GetLength() < 1e-9 || alongAxis.CrossProduct(cutAxis).GetLength() < 1e-9)
            throw ConstraintFailure(role, part, parameter, Array.Empty<Reference?>(), "Datum plane axes are zero-length or collinear.");
        try
        {
            var along = alongAxis.Normalize(); var cut = cutAxis.Normalize(); var extent = Mm(500);
            var datum = family.FamilyCreate.NewReferencePlane2(origin.Subtract(along.Multiply(extent)), origin.Add(along.Multiply(extent)), origin.Add(cut.Multiply(extent)), view);
            if (datum == null || datum.Id == ElementId.InvalidElementId)
                throw ConstraintFailure(role, part, parameter, Array.Empty<Reference?>(), "Revit did not create a valid datum Reference Plane.");
            var baseName = "_DSCons_" + SafeInternalKey(part) + "_" + SafeInternalKey(role) + "_" + SafeInternalKey(parameter);
            var ordinal = new FilteredElementCollector(family).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>().Count(item => item.Name.StartsWith(baseName, StringComparison.Ordinal));
            datum.Name = baseName + "_" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var referenceType = datum.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME);
            if (referenceType == null)
                throw ConstraintFailure(role, part, parameter, new[] { new Reference(datum) }, "The datum Reference Plane has no reference-role parameter.");
            // Autodesk templates may assign a valid reference role to a newly
            // created datum that Revit 2023 will not let us replace.  The
            // dimension/alignment contract uses the datum's native Reference,
            // so preserve that allowed role rather than failing or forcing a
            // NotAReference mutation that is unnecessary for constraint
            // stability.
            _ = ReferenceTypeFromParameterValue(referenceType.AsInteger());
            return datum;
        }
        catch (CommandResultException) { throw; }
        catch (Exception exception)
        {
            throw ConstraintFailure(role, part, parameter, Array.Empty<Reference?>(), "Revit rejected datum-plane creation: " + exception.Message);
        }
    }

    private static Reference RequireCurveReference(ModelCurve curve, string role, string part, string parameter)
    {
        var reference = curve?.GeometryCurve?.Reference;
        if (curve == null || curve.Id == ElementId.InvalidElementId || reference == null || reference.ElementId == ElementId.InvalidElementId)
            throw ConstraintFailure(role, part, parameter, new[] { reference }, "The ModelCurve has no valid geometry reference.");
        return reference;
    }

    private static Reference RequireCenterPointReference(ModelCurve curve, string role, string part, string parameter)
    {
        if (curve == null || curve.Id == ElementId.InvalidElementId || curve.GeometryCurve is not Arc)
            throw ConstraintFailure(role, part, parameter, Array.Empty<Reference?>(), "The ModelArc is null, invalid, or does not expose Arc geometry.");
        // The stable reference belongs to CurveElement/ModelCurve in the
        // Revit API, not to the Arc geometry value returned by it.
        var reference = curve.CenterPointReference;
        if (reference == null || reference.ElementId == ElementId.InvalidElementId)
            throw ConstraintFailure(role, part, parameter, new[] { reference }, "The ModelArc has no valid center-point reference.");
        return reference;
    }

    private static void AlignToDatum(Document family, View view, Reference source, ReferencePlane datum, string role, string part, string parameter)
    {
        if (source == null || source.ElementId == ElementId.InvalidElementId || datum == null || datum.Id == ElementId.InvalidElementId)
            throw ConstraintFailure(role, part, parameter, new[] { source, datum == null ? null : new Reference(datum) }, "Alignment has a null or invalid reference.");
        try
        {
            // Materialize the datum before asking Revit to constrain a sketch
            // curve to it.  The datum must be the first witness reference;
            // this is the ordering Revit accepts for profile CurveElement
            // references in the Family Editor.
            family.Regenerate();
            var alignment = family.FamilyCreate.NewAlignment(view, new Reference(datum), source);
            if (alignment == null) throw ConstraintFailure(role, part, parameter, new[] { source, new Reference(datum) }, "Revit rejected the datum alignment.");
            // NewAlignment may be created unlocked in the Family API.  An
            // unlocked alignment is only a visual coincidence and does not
            // propagate a labelled datum dimension into the sketch geometry.
            if (!alignment.IsLocked) alignment.IsLocked = true;
            family.Regenerate();
            if (!alignment.IsLocked) throw ConstraintFailure(role, part, parameter, new[] { source, new Reference(datum) }, "Revit did not retain the datum alignment lock.");
        }
        catch (CommandResultException) { throw; }
        catch (Exception exception)
        {
            throw ConstraintFailure(role, part, parameter, new[] { source, new Reference(datum) }, "Revit rejected the datum alignment: " + exception.Message);
        }
    }

    private static Dimension CreateStableLinearDimension(Document family, View view, Line line, Reference first, Reference second, string role, string part, string parameter)
    {
        var references = new[] { first, second };
        if (view == null || line == null || line.Length < 1e-9 || references.Any(reference => reference == null || reference.ElementId == ElementId.InvalidElementId))
            throw ConstraintFailure(role, part, parameter, references, "A linear dimension needs a view, non-degenerate line and two valid references.");
        if (first.ElementId == second.ElementId)
            throw ConstraintFailure(role, part, parameter, references, "A linear dimension cannot use duplicate witness references.");
        try
        {
            var referenceArray = new ReferenceArray(); referenceArray.Append(first); referenceArray.Append(second);
            if (referenceArray.Size != 2) throw ConstraintFailure(role, part, parameter, references, "The witness reference array did not retain exactly two references.");
            return family.FamilyCreate.NewDimension(view, line, referenceArray)
                ?? throw ConstraintFailure(role, part, parameter, references, "Revit rejected the stable plane-to-plane dimension.");
        }
        catch (CommandResultException) { throw; }
        catch (Exception exception)
        {
            throw ConstraintFailure(role, part, parameter, references, "Revit rejected the stable plane-to-plane dimension: " + exception.Message);
        }
    }

    private static Dimension CreateStableRadialDimension(Document family, View view, Reference arcReference, XYZ leaderPoint, string role, string part, string parameter)
    {
        if (view == null || arcReference == null || arcReference.ElementId == ElementId.InvalidElementId || leaderPoint == null)
            throw ConstraintFailure(role, part, parameter, new[] { arcReference }, "A radial dimension needs one valid arc reference and leader point.");
        try
        {
            var radialType = new FilteredElementCollector(family).OfClass(typeof(DimensionType)).Cast<DimensionType>()
                .Where(candidate => candidate.StyleType == DimensionStyleType.Radial)
                .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (radialType == null || radialType.Id == ElementId.InvalidElementId)
                throw ConstraintFailure(role, part, parameter, new[] { arcReference }, "The Autodesk template has no valid radial DimensionType.");
            var dimension = family.FamilyCreate.NewRadialDimension(view, arcReference, leaderPoint, radialType)
                ?? throw ConstraintFailure(role, part, parameter, new[] { arcReference }, "Revit rejected the stable radial dimension.");
            if (dimension.DimensionType == null || dimension.DimensionType.StyleType != DimensionStyleType.Radial)
                throw ConstraintFailure(role, part, parameter, new[] { arcReference }, "The selected DimensionType did not create a radial dimension.");
            return dimension;
        }
        catch (CommandResultException) { throw; }
        catch (Exception exception)
        {
            throw ConstraintFailure(role, part, parameter, new[] { arcReference }, "Revit rejected the stable radial dimension: " + exception.Message);
        }
    }

    private static Dimension CreateStableDiameterDimension(Document family, View view, Reference arcReference, XYZ leaderPoint, string role, string part, string parameter)
    {
        if (view == null || arcReference == null || arcReference.ElementId == ElementId.InvalidElementId || leaderPoint == null)
            throw ConstraintFailure(role, part, parameter, new[] { arcReference }, "A diameter dimension needs one valid arc reference and leader point.");
        try
        {
            var defaultTypeId = family.GetDefaultElementTypeId(ElementTypeGroup.DiameterDimensionType);
            var defaultType = defaultTypeId == ElementId.InvalidElementId ? null : family.GetElement(defaultTypeId) as DimensionType;
            if (defaultType == null || defaultType.StyleType != DimensionStyleType.Diameter)
                throw ConstraintFailure(role, part, parameter, new[] { arcReference }, "The Autodesk template has no valid default diameter DimensionType.");
            var dimension = family.FamilyCreate.NewDiameterDimension(view, arcReference, leaderPoint)
                ?? throw ConstraintFailure(role, part, parameter, new[] { arcReference }, "Revit rejected the stable diameter dimension.");
            if (dimension.DimensionType == null || dimension.DimensionType.StyleType != DimensionStyleType.Diameter)
                throw ConstraintFailure(role, part, parameter, new[] { arcReference }, "The created dimension is not a diameter DimensionType.");
            return dimension;
        }
        catch (CommandResultException) { throw; }
        catch (Exception exception)
        {
            throw ConstraintFailure(role, part, parameter, new[] { arcReference }, "Revit rejected the stable diameter dimension: " + exception.Message);
        }
    }

    private static CurveArray PathCurves(JObject? path, Dictionary<string, FamilyParameter> parameters, FamilyManager manager, out string plane, out XYZ origin, out ArcPathFrame? arcFrame)
    {
        if (path == null) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Sweep path is missing.");
        plane = path.Value<string>("plane") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Sweep path plane is missing.");
        arcFrame = null;
        if (path.Value<string>("kind") == "arc")
        {
            var radius = path.Value<string>("radius_parameter") is string radiusKey
                ? CurrentLength(manager, RequireParameter(parameters, radiusKey))
                : Mm(path.Value<double?>("radius_mm") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Arc sweep path radius is missing."));
            arcFrame = ArcFrame(path, radius); origin = arcFrame.Start;
            var arcResult = new CurveArray(); arcResult.Append(Arc.Create(arcFrame.Start, arcFrame.End, arcFrame.Midpoint)); return arcResult;
        }
        if (path.Value<string>("kind") == "offset")
        {
            var frame = OffsetFrame(path, parameters, manager); origin = frame.Points[0]; var offsetResult = new CurveArray();
            for (var index = 1; index < frame.Points.Length; index++) offsetResult.Append(Line.CreateBound(frame.Points[index - 1], frame.Points[index]));
            return offsetResult;
        }
        var points = (path["points_mm"] as JArray ?? new JArray()).Select((item, index) => Point(item, "path point " + index)).ToList();
        if (points.Count < 2) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Sweep path needs at least two points.");
        origin = points[0]; var result = new CurveArray();
        for (var index = 1; index < points.Count; index++) result.Append(Line.CreateBound(points[index - 1], points[index]));
        return result;
    }

    private static Curve SinglePathCurve(JObject? path, Dictionary<string, FamilyParameter> parameters, FamilyManager manager, out string plane, out XYZ origin)
    {
        var curves = PathCurves(path, parameters, manager, out plane, out origin, out _).Cast<Curve>().ToList();
        if (curves.Count != 1) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Swept blend requires exactly one path curve.");
        return curves[0];
    }

    private static ArcPathFrame ArcFrame(JObject path, double radius)
    {
        if (radius <= 0) throw new CommandResultException(ErrorCodes.InvalidParam, "Arc sweep radius must be greater than zero.");
        var plane = path.Value<string>("plane") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Arc sweep plane is missing.");
        var intersection = Point(path["tangent_intersection_mm"], "arc tangent_intersection_mm");
        var angle = Radians(path.Value<double?>("sweep_angle_degrees") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Arc sweep angle is missing."));
        if (angle <= 0 || angle >= Math.PI) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Arc sweep angle must be greater than zero and less than 180 degrees.");
        var turn = string.Equals(path.Value<string>("turn_direction"), "clockwise", StringComparison.Ordinal) ? -1.0 : 1.0;
        var left = plane switch { "xy" => XYZ.BasisY, "xz" => XYZ.BasisZ, _ => throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Arc sweep supports xy or xz path planes only.") };
        var tangentDistance = radius * Math.Tan(angle / 2.0);
        var start = intersection.Add(XYZ.BasisX.Multiply(-tangentDistance));
        var endTangent = XYZ.BasisX.Multiply(Math.Cos(angle)).Add(left.Multiply(turn * Math.Sin(angle))).Normalize();
        var end = intersection.Add(endTangent.Multiply(tangentDistance));
        var center = start.Add(left.Multiply(turn * radius));
        var midpoint = start.Add(XYZ.BasisX.Multiply(radius * Math.Sin(angle / 2.0))).Add(left.Multiply(turn * radius * (1.0 - Math.Cos(angle / 2.0))));
        return new ArcPathFrame { Plane = plane, Radius = radius, Angle = angle, Intersection = intersection, Start = start, End = end, Center = center, Midpoint = midpoint, StartTangent = XYZ.BasisX, EndTangent = endTangent };
    }

    private static OffsetPathFrame OffsetFrame(JObject path, Dictionary<string, FamilyParameter> parameters, FamilyManager manager)
    {
        double Length(string property)
        {
            var parameterKey = path.Value<string>(property) ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameterized offset path is missing " + property + ".");
            var value = CurrentLength(manager, RequireParameter(parameters, parameterKey));
            if (value <= 0) throw new CommandResultException(ErrorCodes.InvalidParam, "Parameterized offset path " + property + " must be greater than zero.");
            return value;
        }
        var plane = path.Value<string>("plane") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameterized offset path plane is missing.");
        var unsignedLateral = plane switch { "xy" => XYZ.BasisY, "xz" => XYZ.BasisZ, _ => throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameterized offset path supports xy or xz only.") };
        var sign = path.Value<string>("offset_direction") == "negative" ? -1.0 : path.Value<string>("offset_direction") == "positive" ? 1.0 : throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameterized offset path direction must be positive or negative.");
        var angleKey = path.Value<string>("offset_angle_parameter") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameterized offset path is missing offset_angle_parameter.");
        var angle = manager.CurrentType.AsDouble(RequireParameter(parameters, angleKey)) ?? throw new CommandResultException(ErrorCodes.InvalidParam, "Offset angle parameter " + angleKey + " has no current value.");
        if (angle < Radians(15) - 1e-9 || angle > Radians(75) + 1e-9) throw new CommandResultException(ErrorCodes.InvalidParam, "Parameterized offset angle must stay between 15 and 75 degrees.");
        var leadIn = Length("lead_in_parameter"); var leadOut = Length("lead_out_parameter"); var lateralOffset = Length("lateral_offset_parameter");
        var lateralAxis = unsignedLateral.Multiply(sign); var diagonalRun = lateralOffset / Math.Tan(angle);
        var p0 = XYZ.Zero; var p1 = p0.Add(XYZ.BasisX.Multiply(leadIn));
        var p2 = p1.Add(XYZ.BasisX.Multiply(diagonalRun)).Add(lateralAxis.Multiply(lateralOffset)); var p3 = p2.Add(XYZ.BasisX.Multiply(leadOut));
        return new OffsetPathFrame { Plane = plane, LeadIn = leadIn, LeadOut = leadOut, LateralOffset = lateralOffset, Angle = angle, LateralAxis = lateralAxis, Points = new[] { p0, p1, p2, p3 } };
    }

    private static OffsetPathFrame DeclaredNominalOffsetFrame(FamilyBlueprintSpec spec, JObject path)
    {
        var flexCases = (spec.Blueprint["verification"]?["parameter_flex_cases"] as JArray ?? new JArray()).OfType<JObject>()
            .ToDictionary(item => item.Value<string>("parameter_key") ?? string.Empty, StringComparer.Ordinal);
        double Nominal(string property, bool angle = false)
        {
            var parameterKey = path.Value<string>(property) ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameterized offset path is missing " + property + ".");
            if (!flexCases.TryGetValue(parameterKey, out var flexCase) || flexCase["nominal"] == null)
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameterized offset path has no approved nominal flex value for " + parameterKey + ".");
            var value = flexCase.Value<double>("nominal"); return angle ? Radians(value) : Mm(value);
        }
        var plane = path.Value<string>("plane") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameterized offset path plane is missing.");
        var unsignedLateral = plane switch { "xy" => XYZ.BasisY, "xz" => XYZ.BasisZ, _ => throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameterized offset path supports xy or xz only.") };
        var sign = path.Value<string>("offset_direction") == "negative" ? -1.0 : path.Value<string>("offset_direction") == "positive" ? 1.0 : throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameterized offset path direction must be positive or negative.");
        var leadIn = Nominal("lead_in_parameter"); var leadOut = Nominal("lead_out_parameter"); var lateralOffset = Nominal("lateral_offset_parameter"); var angle = Nominal("offset_angle_parameter", true);
        var lateralAxis = unsignedLateral.Multiply(sign); var diagonalRun = lateralOffset / Math.Tan(angle);
        var p0 = XYZ.Zero; var p1 = p0.Add(XYZ.BasisX.Multiply(leadIn));
        var p2 = p1.Add(XYZ.BasisX.Multiply(diagonalRun)).Add(lateralAxis.Multiply(lateralOffset)); var p3 = p2.Add(XYZ.BasisX.Multiply(leadOut));
        return new OffsetPathFrame { Plane = plane, LeadIn = leadIn, LeadOut = leadOut, LateralOffset = lateralOffset, Angle = angle, LateralAxis = lateralAxis, Points = new[] { p0, p1, p2, p3 } };
    }

    private static void LabelParameterizedOffsetPath(Document family, Sweep sweep, JObject path, Dictionary<string, FamilyParameter> parameters, FamilyManager manager, string partKey)
    {
        family.Regenerate();
        var frame = OffsetFrame(path, parameters, manager); var pathSketch = sweep.PathSketch ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameterized offset sweep " + partKey + " has no path sketch.");
        var modelLines = pathSketch.Profile.Cast<CurveArray>().SelectMany(loop => loop.Cast<Curve>())
            .Select(curve => curve.Reference == null ? null : family.GetElement(curve.Reference.ElementId) as ModelCurve)
            .Where(item => item?.GeometryCurve is Line).Cast<ModelCurve>().ToList();
        if (modelLines.Count != 3) throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameterized offset sweep " + partKey + " must expose exactly three path ModelCurves.");
        bool Same(XYZ first, XYZ second) => first.DistanceTo(second) <= Mm(.1);
        ModelCurve Segment(XYZ first, XYZ second, string label) => modelLines.SingleOrDefault(item =>
        {
            var curve = item.GeometryCurve; return Same(curve.GetEndPoint(0), first) && Same(curve.GetEndPoint(1), second) || Same(curve.GetEndPoint(0), second) && Same(curve.GetEndPoint(1), first);
        }) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameterized offset sweep " + partKey + " is missing its " + label + " path segment.");
        Reference Endpoint(ModelCurve curve, XYZ point, string label)
        {
            var geometry = curve.GeometryCurve; var index = Same(geometry.GetEndPoint(0), point) ? 0 : Same(geometry.GetEndPoint(1), point) ? 1 : -1;
            if (index < 0) throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameterized offset " + label + " endpoint cannot be resolved.");
            return geometry.GetEndPointReference(index) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameterized offset " + label + " endpoint has no stable Revit reference.");
        }
        var first = Segment(frame.Points[0], frame.Points[1], "lead-in"); var diagonal = Segment(frame.Points[1], frame.Points[2], "diagonal"); var last = Segment(frame.Points[2], frame.Points[3], "lead-out");
        var view = ViewForPlane(family, frame.Plane, "parameterized offset path"); var pathNormal = PlaneNormal(frame.Plane);
        var extent = Math.Max(Mm(500), Math.Max(frame.Points[3].X, frame.LateralOffset) * 1.5); var safe = SafeInternalKey(partKey) + "_" + sweep.Id.Val();
        ReferencePlane HorizontalDatum(XYZ center, string suffix)
        {
            var datum = family.FamilyCreate.NewReferencePlane2(center.Add(XYZ.BasisX.Multiply(-extent)), center.Add(XYZ.BasisX.Multiply(extent)), center.Add(pathNormal.Multiply(extent)), view);
            datum.Name = "_DSCons_" + safe + "_" + suffix; datum.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME)?.Set(ReferenceParameterValue(FamilyInstanceReferenceType.NotAReference)); return datum;
        }
        var baseDatum = HorizontalDatum(XYZ.Zero, "offset_base"); var offsetDatum = HorizontalDatum(frame.LateralAxis.Multiply(frame.LateralOffset), "offset_target");
        var originCross = family.FamilyCreate.NewModelCurve(Line.CreateBound(frame.LateralAxis.Multiply(-extent), frame.LateralAxis.Multiply(extent)), pathSketch.SketchPlane);
        originCross.ChangeToReferenceLine(); family.Regenerate();
        if (family.FamilyCreate.NewAlignment(view, new Reference(baseDatum), first.GeometryCurve.Reference) == null
            || family.FamilyCreate.NewAlignment(view, new Reference(offsetDatum), last.GeometryCurve.Reference) == null
            || family.FamilyCreate.NewAlignment(view, originCross.GeometryCurve.Reference, Endpoint(first, frame.Points[0], "origin")) == null)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected a datum alignment for parameterized offset sweep " + partKey + ".");

        Dimension EndpointDimension(ModelCurve segment, XYZ start, XYZ end, XYZ witnessOffset, string parameterProperty, string label)
        {
            var references = new ReferenceArray(); references.Append(Endpoint(segment, start, label + " start")); references.Append(Endpoint(segment, end, label + " end"));
            var dimension = family.FamilyCreate.NewDimension(view, Line.CreateBound(start.Add(witnessOffset), end.Add(witnessOffset)), references)
                ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected the " + label + " dimension for parameterized offset sweep " + partKey + ".");
            dimension.FamilyLabel = RequireParameter(parameters, path.Value<string>(parameterProperty) ?? string.Empty); return dimension;
        }
        var leadInDimension = EndpointDimension(first, frame.Points[0], frame.Points[1], frame.LateralAxis.Multiply(-Mm(50)), "lead_in_parameter", "lead-in");
        var leadOutDimension = EndpointDimension(last, frame.Points[2], frame.Points[3], frame.LateralAxis.Multiply(Mm(50)), "lead_out_parameter", "lead-out");
        var offsetReferences = new ReferenceArray(); offsetReferences.Append(new Reference(baseDatum)); offsetReferences.Append(new Reference(offsetDatum));
        var lateralWitnessStart = XYZ.BasisX.Multiply(frame.Points[3].X + Mm(100));
        var lateralDimension = family.FamilyCreate.NewDimension(view, Line.CreateBound(lateralWitnessStart, lateralWitnessStart.Add(frame.LateralAxis.Multiply(frame.LateralOffset))), offsetReferences)
            ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected the lateral-offset dimension for sweep " + partKey + ".");
        lateralDimension.FamilyLabel = RequireParameter(parameters, path.Value<string>("lateral_offset_parameter") ?? string.Empty);
        var diagonalLength = frame.Points[2].DistanceTo(frame.Points[1]); var angularRadius = Math.Max(Mm(25), Math.Min(frame.LeadIn, diagonalLength) * .5);
        var angularArc = Arc.Create(frame.Points[1], angularRadius, 0, frame.Angle, XYZ.BasisX, frame.LateralAxis);
        var angularDimension = family.FamilyCreate.NewAngularDimension(view, angularArc, first.GeometryCurve.Reference, diagonal.GeometryCurve.Reference)
            ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected the offset-angle dimension for sweep " + partKey + ".");
        angularDimension.FamilyLabel = RequireParameter(parameters, path.Value<string>("offset_angle_parameter") ?? string.Empty); family.Regenerate();
        var labels = new[] { leadInDimension, leadOutDimension, lateralDimension, angularDimension };
        if (labels.Any(item => item.FamilyLabel == null) || labels.Select(item => item.FamilyLabel!.Id).Distinct().Count() != 4)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameterized offset sweep " + partKey + " did not retain four independent dimension labels.");
        var snapshot = SweepPathSnapshot(sweep) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameterized offset sweep " + partKey + " has no path read-back after constraint creation.");
        var observed = (snapshot["points_mm"] as JArray ?? new JArray()).OfType<JObject>().Select((item, index) => PointFromMm(item, "offset path point " + index)).ToList();
        if (observed.Count != 4 || frame.Points.Where((point, index) => point.DistanceTo(observed[index]) > Mm(.5)).Any()) throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameterized offset sweep " + partKey + " moved away from its nominal constrained path.");
    }

    private static void LabelSweepPathRadius(Document family, Sweep sweep, JObject path, string radiusParameterKey, Dictionary<string, FamilyParameter> parameters, FamilyManager manager, ArcPathFrame frame, string partKey)
    {
        family.Regenerate();
        var pathSketch = sweep.PathSketch ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Arc sweep " + partKey + " has no path sketch.");
        var modelArc = pathSketch.Profile.Cast<CurveArray>().SelectMany(loop => loop.Cast<Curve>())
            .Select(curve => curve.Reference == null ? null : family.GetElement(curve.Reference.ElementId) as ModelCurve)
            .FirstOrDefault(item => item?.GeometryCurve is Arc)
            ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Arc sweep " + partKey + " has no dimensionable path arc.");
        var arc = (Arc)modelArc.GeometryCurve; var view = ViewForPlane(family, frame.Plane, "parameterized arc sweep path");
        var radialDirection = arc.Evaluate(.5, true).Subtract(arc.Center).Normalize();
        var arcReference = RequireCurveReference(modelArc, "bend_radial", partKey, radiusParameterKey);
        var radial = CreateStableRadialDimension(family, view, arcReference, arc.Evaluate(.5, true).Add(radialDirection.Multiply(Mm(50))), "bend_radial", partKey, radiusParameterKey);
        var radiusParameter = RequireParameter(parameters, radiusParameterKey); radial.FamilyLabel = radiusParameter; family.Regenerate();
        if (radial.FamilyLabel?.Id != radiusParameter.Id) throw ConstraintFailure("bend_radial", partKey, radiusParameterKey, new[] { arcReference }, "The bend-radius label did not read back after regeneration.");

        var pathNormal = PlaneNormal(frame.Plane); var left = frame.Plane == "xy" ? XYZ.BasisY : XYZ.BasisZ;
        // Use the common datum helper for the tangent-origin witnesses.  It
        // validates the native role that the target template/Revit version
        // assigns, but never rewrites an existing datum to Not a Reference.
        // The stable radial dimension and tangent offset contracts below stay
        // unchanged, while template-specific datum semantics are preserved.
        var xOrigin = CreateDatumPlane(family, view, frame.Intersection, left, pathNormal, "tangent_intersection_x", partKey, radiusParameterKey);
        var leftOrigin = CreateDatumPlane(family, view, frame.Intersection, XYZ.BasisX, pathNormal, "tangent_intersection_left", partKey, radiusParameterKey);
        // Dimensioning a CurveElement center-point reference directly is fragile in a
        // swept sketch and caused Revit's "Invalid number of references"
        // transaction failure.  Two datum planes are constrained to the arc
        // center and become the stable witness references for both dimensions.
        var xCenter = CreateDatumPlane(family, view, arc.Center, left, pathNormal, "tangent_center_x", partKey, radiusParameterKey);
        var leftCenter = CreateDatumPlane(family, view, arc.Center, XYZ.BasisX, pathNormal, "tangent_center_lateral", partKey, radiusParameterKey);
        var centerReference = RequireCenterPointReference(modelArc, "tangent_center", partKey, radiusParameterKey);
        AlignToDatum(family, view, centerReference, xCenter, "tangent_center_x", partKey, radiusParameterKey);
        AlignToDatum(family, view, centerReference, leftCenter, "tangent_center_lateral", partKey, radiusParameterKey);
        var tangentOffset = AddInternalLength(manager, "_" + SafeInternalKey(partKey) + "_tangent_offset_" + sweep.Id.Val());
        var leftOffset = AddInternalLength(manager, "_" + SafeInternalKey(partKey) + "_left_offset_" + sweep.Id.Val());
        manager.SetFormula(tangentOffset, radiusParameter.Definition.Name + " * " + Math.Tan(frame.Angle / 2.0).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        manager.SetFormula(leftOffset, radiusParameter.Definition.Name);
        var xWitness = frame.Intersection.Add(left.Multiply(frame.Radius * 2.0));
        var xDimension = CreateStableLinearDimension(family, view, Line.CreateBound(xWitness, xWitness.Add(XYZ.BasisX.Multiply(-Math.Max(frame.Radius * Math.Tan(frame.Angle / 2.0), Mm(1))))), new Reference(xOrigin), new Reference(xCenter), "tangent_intersection_x", partKey, radiusParameterKey);
        xDimension.FamilyLabel = tangentOffset;
        var leftWitness = frame.Intersection.Add(XYZ.BasisX.Multiply(frame.Radius * 2.0));
        var leftDimension = CreateStableLinearDimension(family, view, Line.CreateBound(leftWitness, leftWitness.Add(left.Multiply((path.Value<string>("turn_direction") == "clockwise" ? -1.0 : 1.0) * Math.Max(frame.Radius, Mm(1))))), new Reference(leftOrigin), new Reference(leftCenter), "tangent_intersection_lateral", partKey, radiusParameterKey);
        leftDimension.FamilyLabel = leftOffset; family.Regenerate();
        if (xDimension.FamilyLabel?.Id != tangentOffset.Id) throw ConstraintFailure("tangent_intersection_x", partKey, radiusParameterKey, new[] { new Reference(xOrigin), new Reference(xCenter) }, "The tangent-intersection X label did not read back after regeneration.");
        if (leftDimension.FamilyLabel?.Id != leftOffset.Id) throw ConstraintFailure("tangent_intersection_lateral", partKey, radiusParameterKey, new[] { new Reference(leftOrigin), new Reference(leftCenter) }, "The tangent-intersection lateral label did not read back after regeneration.");
    }

    internal static JObject? SweepPathSnapshot(Sweep sweep)
    {
        var curves = sweep.PathSketch != null
            ? sweep.PathSketch.Profile.Cast<CurveArray>().SelectMany(loop => loop.Cast<Curve>()).ToList()
            : sweep.Path3d?.AllCurveLoops.Cast<CurveArray>().SelectMany(loop => loop.Cast<Curve>()).ToList();
        if (curves == null) return null;
        if (curves.Count == 1 && curves[0] is Arc arc)
        {
            var start = arc.GetEndPoint(0); var end = arc.GetEndPoint(1);
            var startTangent = arc.ComputeDerivatives(0, true).BasisX.Normalize(); var endTangent = arc.ComputeDerivatives(1, true).BasisX.Normalize();
            var plane = Math.Abs(arc.Normal.Z) > .999 ? "xy" : Math.Abs(arc.Normal.Y) > .999 ? "xz" : "unsupported";
            return new JObject
            {
                ["kind"] = "arc", ["plane"] = plane, ["radius_mm"] = Math.Round(arc.Radius * 304.8, 3), ["center_mm"] = PointMm(arc.Center),
                ["start_mm"] = PointMm(start), ["end_mm"] = PointMm(end), ["start_tangent"] = VectorJson(startTangent), ["end_tangent"] = VectorJson(endTangent),
                ["tangent_intersection_mm"] = PointMm(TangentIntersection(start, startTangent, end, endTangent, plane)), ["verified"] = true
            };
        }
        if (curves.Count > 0 && curves.All(item => item is Line))
        {
            var remaining = curves.Cast<Line>().ToList(); var points = new List<XYZ>();
            bool Same(XYZ first, XYZ second) => first.DistanceTo(second) <= 1e-7;
            var endpoints = remaining.SelectMany(segment => new[] { segment.GetEndPoint(0), segment.GetEndPoint(1) })
                .Where(candidate => remaining.Sum(segment => (Same(candidate, segment.GetEndPoint(0)) ? 1 : 0) + (Same(candidate, segment.GetEndPoint(1)) ? 1 : 0)) == 1).ToList();
            var firstStart = endpoints.FirstOrDefault(candidate =>
            {
                var segment = remaining.Single(item => Same(candidate, item.GetEndPoint(0)) || Same(candidate, item.GetEndPoint(1)));
                var other = Same(candidate, segment.GetEndPoint(0)) ? segment.GetEndPoint(1) : segment.GetEndPoint(0);
                return other.Subtract(candidate).Normalize().DotProduct(XYZ.BasisX) > .999;
            }) ?? endpoints.OrderBy(item => item.GetLength()).FirstOrDefault();
            if (firstStart == null) return new JObject { ["kind"] = curves.Count == 1 ? "line" : "polyline", ["curve_count"] = curves.Count, ["verified"] = false, ["reason"] = "path_has_no_open_endpoint" };
            points.Add(firstStart);
            while (remaining.Count > 0)
            {
                var previous = points[points.Count - 1]; var next = remaining.FirstOrDefault(item => Same(previous, item.GetEndPoint(0)) || Same(previous, item.GetEndPoint(1)));
                if (next == null) return new JObject { ["kind"] = curves.Count == 1 ? "line" : "polyline", ["curve_count"] = curves.Count, ["verified"] = false, ["reason"] = "disconnected_path_curves" };
                points.Add(Same(previous, next.GetEndPoint(0)) ? next.GetEndPoint(1) : next.GetEndPoint(0)); remaining.Remove(next);
            }
            var startTangent = points[1].Subtract(points[0]).Normalize(); var endTangent = points[points.Count - 1].Subtract(points[points.Count - 2]).Normalize();
            bool Constant(Func<XYZ, double> coordinate) => points.All(item => Math.Abs(coordinate(item) - coordinate(points[0])) <= 1e-7);
            var plane = Constant(item => item.Z) ? "xy" : Constant(item => item.Y) ? "xz" : Constant(item => item.X) ? "yz" : "unsupported";
            return new JObject
            {
                ["kind"] = curves.Count == 1 ? "line" : "polyline", ["plane"] = plane, ["curve_count"] = curves.Count,
                ["points_mm"] = new JArray(points.Select(PointMm)), ["start_mm"] = PointMm(points[0]), ["end_mm"] = PointMm(points[points.Count - 1]),
                ["start_tangent"] = VectorJson(startTangent), ["end_tangent"] = VectorJson(endTangent), ["verified"] = plane != "unsupported"
            };
        }
        return new JObject { ["kind"] = "unsupported", ["curve_count"] = curves.Count, ["verified"] = false };
    }

    private static void VerifyAngularSweepPathSnapshot(JObject? observedPath, JObject dimension, JObject referenceLine, double expectedAngleDegrees, string label)
    {
        if (observedPath == null || observedPath.Value<bool?>("verified") != true || observedPath.Value<string>("kind") != "line" || observedPath.Value<int?>("curve_count") != 1)
            throw new CommandResultException(ErrorCodes.VerificationFailed, label + " must reopen/read back as one verified line path.");
        var plane = dimension.Value<string>("view_plane") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, label + " Dimension has no view_plane.");
        if (!string.Equals(observedPath.Value<string>("plane"), plane, StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.VerificationFailed, label + " path plane does not match its angular Dimension.");
        XYZ uAxis; XYZ vAxis;
        switch (plane)
        {
            case "xy": uAxis = XYZ.BasisX; vAxis = XYZ.BasisY; break;
            case "xz": uAxis = XYZ.BasisX; vAxis = XYZ.BasisZ; break;
            case "yz": uAxis = XYZ.BasisY; vAxis = XYZ.BasisZ; break;
            default: throw new CommandResultException(ErrorCodes.EvidenceInvalid, label + " has an unsupported angular plane " + plane + ".");
        }
        var pivot = Point(dimension["arc_center_mm"], label + " pivot");
        var first = PointFromMm(observedPath["start_mm"] as JObject, label + " path start");
        var second = PointFromMm(observedPath["end_mm"] as JObject, label + " path end");
        var tolerance = Mm(.5);
        XYZ endpoint;
        if (first.DistanceTo(pivot) <= tolerance) endpoint = second;
        else if (second.DistanceTo(pivot) <= tolerance) endpoint = first;
        else throw new CommandResultException(ErrorCodes.VerificationFailed, label + " path no longer shares the declared angular pivot.");
        var declaredStart = Point(referenceLine["start_mm"], label + " declared Reference Line start");
        var declaredEnd = Point(referenceLine["end_mm"], label + " declared Reference Line end");
        var declaredLength = declaredEnd.DistanceTo(declaredStart);
        var actualDirection = endpoint.Subtract(pivot); var actualLength = actualDirection.GetLength();
        if (actualLength <= 1e-9 || Math.Abs(actualLength - declaredLength) > tolerance)
            throw new CommandResultException(ErrorCodes.VerificationFailed, label + " Reference Line length changed while the Angle Parameter flexed.");
        var radians = Radians(expectedAngleDegrees);
        var expectedDirection = uAxis.Multiply(Math.Cos(radians)).Add(vAxis.Multiply(Math.Sin(radians))).Normalize();
        if (actualDirection.Normalize().DotProduct(expectedDirection) < .999)
            throw new CommandResultException(ErrorCodes.VerificationFailed, label + " path endpoint does not match the requested absolute angle " + expectedAngleDegrees.ToString(System.Globalization.CultureInfo.InvariantCulture) + " degrees.");
    }

    private static JObject VectorJson(XYZ vector) => new() { ["x"] = Math.Round(vector.X, 6), ["y"] = Math.Round(vector.Y, 6), ["z"] = Math.Round(vector.Z, 6) };

    private static XYZ TangentIntersection(XYZ firstPoint, XYZ firstDirection, XYZ secondPoint, XYZ secondDirection, string plane)
    {
        (double U, double V) Coordinates(XYZ point) => plane == "xy" ? (point.X, point.Y) : plane == "xz" ? (point.X, point.Z) : throw new CommandResultException(ErrorCodes.VerificationFailed, "Arc path plane cannot be projected for tangent verification.");
        var first = Coordinates(firstPoint); var second = Coordinates(secondPoint); var firstVector = Coordinates(firstDirection); var secondVector = Coordinates(secondDirection);
        var determinant = firstVector.U * secondVector.V - firstVector.V * secondVector.U;
        if (Math.Abs(determinant) < 1e-9) throw new CommandResultException(ErrorCodes.VerificationFailed, "Arc path connector tangents do not have one finite intersection.");
        var deltaU = second.U - first.U; var deltaV = second.V - first.V;
        var distance = (deltaU * secondVector.V - deltaV * secondVector.U) / determinant;
        return firstPoint.Add(firstDirection.Multiply(distance));
    }

    private static CurveArray SingleLoop(CurveArrArray profile, string label)
    {
        var loops = profile.Cast<CurveArray>().ToList();
        if (loops.Count != 1) throw new CommandResultException(ErrorCodes.EvidenceInvalid, label + " must contain exactly one profile loop.");
        return loops[0];
    }

    private static ProfilePlaneLocation ParseProfileLocation(string? value) => value switch { "midpoint" => ProfilePlaneLocation.MidPoint, "end" => ProfilePlaneLocation.End, _ => ProfilePlaneLocation.Start };
    private static XYZ Point(JToken? token, string label)
    {
        if (token is not JObject point) throw new CommandResultException(ErrorCodes.EvidenceInvalid, label + " is missing.");
        return new XYZ(Mm(point.Value<double>("x_mm")), Mm(point.Value<double>("y_mm")), Mm(point.Value<double>("z_mm")));
    }
    private static XYZ Direction(JToken? token, string label)
    {
        if (token is not JObject vector) throw new CommandResultException(ErrorCodes.EvidenceInvalid, label + " is missing.");
        var result = new XYZ(vector.Value<double>("x_mm"), vector.Value<double>("y_mm"), vector.Value<double>("z_mm"));
        if (result.GetLength() < 1e-9) throw new CommandResultException(ErrorCodes.EvidenceInvalid, label + " must be a non-zero direction vector.");
        return result.Normalize();
    }
    private static JObject PointMm(XYZ point) => new()
    {
        ["x_mm"] = Math.Round(point.X * 304.8, 3),
        ["y_mm"] = Math.Round(point.Y * 304.8, 3),
        ["z_mm"] = Math.Round(point.Z * 304.8, 3)
    };
    private static XYZ PlaneNormal(string plane) => plane switch { "xy" => XYZ.BasisZ, "xz" => XYZ.BasisY, "yz" => XYZ.BasisX, _ => throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown profile/path plane " + plane + ".") };
    private static double Radians(double degrees) => degrees * Math.PI / 180.0;

    private static void AssociateProfileParameters(FamilyManager manager, Extrusion form, JObject profile, Dictionary<string, FamilyParameter> parameters, ExtrusionFrameInfo frame, XYZ? profileOrigin = null)
    {
        var origin = profileOrigin ?? XYZ.Zero;
        if (profile.Value<string>("shape") == "oval")
        {
            LabelOvalSketchProfile(form.Document, form.Sketch, profile, parameters, manager, "profile " + form.Id.Val());
            return;
        }
        if (profile.Value<string>("width_parameter") is string width && profile.Value<string>("height_parameter") is string height)
            LabelRectangle(form.Document, form, RequireParameter(parameters, width), RequireParameter(parameters, height), CurrentLength(manager, RequireParameter(parameters, width)), CurrentLength(manager, RequireParameter(parameters, height)), "profile " + form.Id.Val(), frame, origin);
        var diameter = profile.Value<string>("diameter_parameter"); var outer = profile.Value<string>("outer_diameter_parameter");
        if (!string.IsNullOrWhiteSpace(diameter) || !string.IsNullOrWhiteSpace(outer))
        {
            var key = diameter ?? outer!;
            LabelDiameter(form.Document, form, 0, RequireParameter(parameters, key), "profile " + key, frame, origin);
        }
        if (profile.Value<string>("inner_diameter_parameter") is string inner)
        {
            LabelDiameter(form.Document, form, 1, RequireParameter(parameters, inner), "ring inner profile", frame, origin);
        }
    }

    private static List<ConnectorElement> CreateConnectors(Document family, FamilyBlueprintSpec spec, Dictionary<string, PartResult> parts, Dictionary<string, FamilyParameter> parameters)
    {
        var result = new List<ConnectorElement>(); var byKey = new Dictionary<string, ConnectorElement>(StringComparer.Ordinal);
        foreach (var contract in (spec.Blueprint["connectors"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var hostKey = contract.Value<string>("host_part") ?? string.Empty;
            if (!parts.TryGetValue(hostKey, out var part)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Connector host_part does not exist: " + hostKey + ".");
            var normal = ConnectorFaceNormal(part.Contract, contract.Value<string>("host_face") ?? string.Empty); var face = Face(part.Form, normal, hostKey);
            var discipline = contract.Value<string>("discipline") ?? string.Empty; var classification = contract.Value<string>("system_classification") ?? string.Empty;
            ConnectorElement connector = discipline switch
            {
                "duct" => ConnectorElement.CreateDuctConnector(family, ParseDuct(classification), ParseProfile(contract.Value<string>("profile")), face.Reference),
                "pipe" => ConnectorElement.CreatePipeConnector(family, ParsePipe(classification), face.Reference),
                "electrical" => ConnectorElement.CreateElectricalConnector(family, ParseElectrical(classification), face.Reference),
                "conduit" => ConnectorElement.CreateConduitConnector(family, face.Reference),
                "cable_tray" => ConnectorElement.CreateCableTrayConnector(family, face.Reference),
                _ => throw new CommandResultException(ErrorCodes.Unsupported, "Unsupported connector discipline: " + discipline + ".")
            };
            SetRole(connector, contract.Value<string>("role") ?? contract.Value<string>("key") ?? discipline); EnsureOutward(connector, normal);
            AssociateConnectorSize(family.FamilyManager, connector, contract, parameters);
            ApplyConnectorTechnicalData(connector, contract, discipline, family.FamilyManager, parameters);
            ApplyElectricalConnectorData(family.FamilyManager, connector, contract, parameters, discipline);
            if (contract.Value<bool?>("primary") == true) connector.AssignAsPrimary();
            result.Add(connector); byKey.Add(contract.Value<string>("key")!, connector);
        }
        foreach (var contract in (spec.Blueprint["connectors"] as JArray ?? new JArray()).OfType<JObject>())
            if (contract.Value<string>("linked_to") is string linked) byKey[contract.Value<string>("key")!].SetLinkedConnectorElement(byKey[linked]);
        return result;
    }

    private static JObject VerifyParameterFlex(Document family, FamilyManager manager, FamilyBlueprintSpec spec, Dictionary<string, FamilyParameter> parameters, Dictionary<string, PartResult> parts, Dictionary<string, CoordinationZoneResult> coordinationZones, List<ConnectorElement> connectors, Dictionary<string, ArrayResult> arrays, Dictionary<string, NestedResult> nested, JArray modelLines, JArray detailLines)
    {
        var results = new JArray(); var current = manager.CurrentType;
        var parameterContracts = (spec.Blueprint["parameters"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("key") ?? string.Empty, StringComparer.Ordinal);
        var flexCases = (spec.Blueprint["verification"]?["parameter_flex_cases"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var flexKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var flexCase in flexCases)
        {
            var key = flexCase.Value<string>("parameter_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameter flex case is missing parameter_key.");
            if (!flexKeys.Add(key)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameter flex case is duplicated for " + key + ".");
        }
        foreach (var item in parameterContracts.Values)
        {
            var key = item.Value<string>("key") ?? string.Empty; var dataType = item.Value<string>("data_type") ?? string.Empty;
            if (dataType is not ("length" or "number" or "integer" or "angle" or "airflow" or "flow") || item["formula"] != null || item["lookup"] != null) continue;
            var sharedNestedInterfaceOnly = IsSharedNestedInstanceParameter(spec, key) && !BlueprintReferencesParameterOutsideNestedInterface(spec, key);
            if (BlueprintReferencesParameter(spec, key) && !flexKeys.Contains(key) && !sharedNestedInterfaceOnly) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Geometry/reference-graph/connector parameter " + key + " has no approved min/nominal/max flex case.");
        }
        var connectorContracts = (spec.Blueprint["connectors"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        if (connectorContracts.Count != connectors.Count) throw new CommandResultException(ErrorCodes.VerificationFailed, "Connector contracts do not match the created connector set before flex verification.");
        foreach (var flexCase in flexCases)
        {
            var key = flexCase.Value<string>("parameter_key")!;
            if (!parameterContracts.TryGetValue(key, out var item) || !parameters.TryGetValue(key, out var parameter)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameter flex case references unknown parameter " + key + ".");
            var dataType = item.Value<string>("data_type") ?? string.Empty;
            if (dataType is not ("length" or "number" or "integer" or "angle" or "airflow" or "flow") || parameter.IsInstance || item["formula"] != null || item["lookup"] != null || parameter.Formula != null)
                throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameter flex case " + key + " must reference a direct numeric Type Parameter.");
            var approved = new[]
            {
                (Name: "min", Value: RequiredFlexValue(flexCase, "min", key)),
                (Name: "nominal", Value: RequiredFlexValue(flexCase, "nominal", key)),
                (Name: "max", Value: RequiredFlexValue(flexCase, "max", key))
            };
            if (!(approved[0].Value < approved[1].Value && approved[1].Value < approved[2].Value)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameter flex case " + key + " must satisfy min < nominal < max.");
            if (dataType == "integer" && approved.Any(step => Math.Abs(step.Value - Math.Round(step.Value)) > 1e-9)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Integer parameter flex values must be whole numbers for " + key + ".");
            var original = ReadFlexValue(current, parameter, dataType, key);
            if (!FlexValueMatches(original, approved[1].Value, dataType)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameter " + key + " current Type value does not match the approved nominal flex value; observed=" + original.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "; expected=" + approved[1].Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ".");
            var affectedParameterKeys = FlexAffectedParameterKeys(parameterContracts, key);
            var referencedParts = parts.Where(pair => PartReferencesParameter(pair.Value.Contract, key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var referencedCoordinationZones = coordinationZones.Where(pair => CoordinationZoneReferencesParameter(pair.Value.Contract, key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach (var partKey in ReferenceGraphPartKeys(spec, key)) if (parts.TryGetValue(partKey, out var part)) referencedParts[partKey] = part;
            var referencedOffsetPaths = referencedParts.Where(pair => pair.Value.Form is Sweep && pair.Value.Contract["path"] is JObject path && path.Value<string>("kind") == "offset" && PathReferencesParameter(path, key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var referencedAngularPaths = parts.Where(pair => pair.Value.Form is Sweep && pair.Value.Contract["path"] is JObject path && path.Value<string>("kind") == "reference_line"
                && (spec.Blueprint["dimensions"] as JArray ?? new JArray()).OfType<JObject>().Any(dimension => string.Equals(dimension.Value<string>("key"), path.Value<string>("angular_dimension_key"), StringComparison.Ordinal) && ParameterReference(dimension, key, "parameter_key")))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var referenceGraphKeys = new HashSet<string>(ReferenceGraphReferenceKeys(spec, key), StringComparer.Ordinal);
            var referencedModelLines = (spec.Blueprint["model_lines"] as JArray ?? new JArray()).OfType<JObject>()
                .Where(line => (line["endpoint_bindings"] as JArray ?? new JArray()).OfType<JObject>().Any(binding => referenceGraphKeys.Contains(binding.Value<string>("reference_plane_key") ?? string.Empty)))
                .ToDictionary(line => line.Value<string>("key") ?? string.Empty, line => line, StringComparer.Ordinal);
            var referencedDetailLines = (spec.Blueprint["detail_lines"] as JArray ?? new JArray()).OfType<JObject>()
                .Where(line => (line["endpoint_bindings"] as JArray ?? new JArray()).OfType<JObject>().Any(binding => referenceGraphKeys.Contains(binding.Value<string>("reference_plane_key") ?? string.Empty)))
                .ToDictionary(line => line.Value<string>("key") ?? string.Empty, line => line, StringComparer.Ordinal);
            var referencedConnectors = new Dictionary<string, ConnectorElement>(StringComparer.Ordinal);
            for (var index = 0; index < connectorContracts.Count; index++)
            {
                var contract = connectorContracts[index];
                var connectorKey = contract.Value<string>("key") ?? "connector_" + index;
                var hostPartKey = contract.Value<string>("host_part") ?? string.Empty;
                var followsArcRadius = parts.TryGetValue(hostPartKey, out var hostPart) && ParameterReference(hostPart.Contract["path"] as JObject ?? new JObject(), key, "radius_parameter") && contract.Value<string>("host_face") is "path_start" or "path_end";
                var followsOffsetEnd = parts.TryGetValue(hostPartKey, out var offsetHost) && offsetHost.Contract["path"] is JObject offsetPath && offsetPath.Value<string>("kind") == "offset" && PathReferencesParameter(offsetPath, key) && contract.Value<string>("host_face") == "path_end";
                if (ConnectorReferencesParameter(contract, key) || followsArcRadius || followsOffsetEnd) referencedConnectors[connectorKey] = connectors[index];
            }
            var referencedArrays = arrays.Where(pair => ParameterReference(pair.Value.Contract, key, "count_parameter")).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var referencedNested = nested.Where(pair => (pair.Value.Contract["parameter_map"] as JObject ?? new JObject()).Properties().Any(property => string.Equals(property.Value.Value<string>(), key, StringComparison.Ordinal))).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var steps = new JArray();
            try
            {
                foreach (var step in approved)
                {
                    SetFlexValue(manager, parameter, dataType, step.Value, key);
                    // Re-select the active type so Revit evaluates dependent
                    // Type formulas before geometry/read-back snapshots. A
                    // single Regenerate alone can leave formula-driven labels
                    // at their previous value inside the authoring transaction.
                    manager.CurrentType = current;
                    family.Regenerate();
                    var readBack = ReadFlexValue(current, parameter, dataType, key);
                    if (!FlexValueMatches(readBack, step.Value, dataType)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameter " + key + " did not read back the approved " + step.Name + " value.");
                    var affectedParameterValues = new JObject();
                    foreach (var affectedKey in affectedParameterKeys.OrderBy(item => item, StringComparer.Ordinal))
                    {
                        if (!parameters.TryGetValue(affectedKey, out var affectedParameter) || !parameterContracts.TryGetValue(affectedKey, out var affectedContract)) continue;
                        var affectedType = affectedContract.Value<string>("data_type") ?? string.Empty;
                        if (affectedType is "length" or "number" or "integer" or "angle" or "airflow" or "flow")
                            affectedParameterValues[affectedKey] = ReadFlexValue(current, affectedParameter, affectedType, affectedKey);
                    }
                    var pathSnapshots = new JObject();
                    foreach (var pathPart in referencedOffsetPaths)
                    {
                        var path = pathPart.Value.Contract["path"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameterized offset flex part has no path declaration.");
                        var expected = OffsetFrame(path, parameters, manager); var snapshot = SweepPathSnapshot((Sweep)pathPart.Value.Form)
                            ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameterized offset path snapshot is missing during flex for " + pathPart.Key + ".");
                        var points = (snapshot["points_mm"] as JArray ?? new JArray()).OfType<JObject>().Select((item, index) => PointFromMm(item, "flexed offset point " + index)).ToList();
                        var startTangent = VectorFromJson(snapshot["start_tangent"] as JObject, "flexed offset start tangent"); var endTangent = VectorFromJson(snapshot["end_tangent"] as JObject, "flexed offset end tangent");
                        if (snapshot.Value<bool?>("verified") != true || snapshot.Value<string>("kind") != "polyline" || snapshot.Value<string>("plane") != expected.Plane || snapshot.Value<int?>("curve_count") != 3
                            || points.Count != expected.Points.Length || expected.Points.Where((point, index) => point.DistanceTo(points[index]) > Mm(.5)).Any()
                            || startTangent.Normalize().DotProduct(XYZ.BasisX) < .999 || endTangent.Normalize().DotProduct(XYZ.BasisX) < .999)
                            throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameterized offset path constraint read-back failed while flexing " + key + " at " + step.Name + " for " + pathPart.Key + ".");
                        pathSnapshots[pathPart.Key] = snapshot;
                    }
                    var angularPathSnapshots = new JObject();
                    foreach (var angularPart in referencedAngularPaths)
                    {
                        var path = angularPart.Value.Contract["path"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Angular sweep flex part has no path declaration.");
                        var dimensionKey = path.Value<string>("angular_dimension_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Angular sweep flex path has no dimension key.");
                        var referenceLineKey = path.Value<string>("reference_line_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Angular sweep flex path has no Reference Line key.");
                        var dimension = (spec.Blueprint["dimensions"] as JArray ?? new JArray()).OfType<JObject>().SingleOrDefault(item => string.Equals(item.Value<string>("key"), dimensionKey, StringComparison.Ordinal))
                            ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Angular sweep flex path references unknown Dimension " + dimensionKey + ".");
                        var referenceLine = (spec.Blueprint["reference_lines"] as JArray ?? new JArray()).OfType<JObject>().SingleOrDefault(item => string.Equals(item.Value<string>("key"), referenceLineKey, StringComparison.Ordinal))
                            ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Angular sweep flex path references unknown Reference Line " + referenceLineKey + ".");
                        var snapshot = SweepPathSnapshot((Sweep)angularPart.Value.Form)
                            ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Angular sweep path snapshot is missing during flex for " + angularPart.Key + ".");
                        VerifyAngularSweepPathSnapshot(snapshot, dimension, referenceLine, step.Value, "Angular sweep flex " + angularPart.Key + " at " + step.Name + ".");
                        angularPathSnapshots[angularPart.Key] = snapshot;
                    }
                    var modelLineSnapshots = new JObject();
                    foreach (var modelLine in referencedModelLines)
                        modelLineSnapshots[modelLine.Key] = ModelLineEndpointBindingSnapshot(family, modelLines, modelLine.Key, modelLine.Value, referenceGraphKeys);
                    var detailLineSnapshots = new JObject();
                    foreach (var detailLine in referencedDetailLines)
                        detailLineSnapshots[detailLine.Key] = DetailLineEndpointBindingSnapshot(family, detailLines, detailLine.Key, detailLine.Value, referenceGraphKeys);
                    steps.Add(new JObject
                    {
                        ["case"] = step.Name, ["requested_value"] = step.Value, ["parameter_read_back"] = readBack, ["affected_parameter_values"] = affectedParameterValues,
                        ["parts_mm"] = PartBounds(referencedParts), ["profile_arc_radii_mm"] = ProfileArcRadii(referencedParts), ["label_dimensions_mm"] = LabelledDimensionSnapshot(family, parameter), ["paths"] = pathSnapshots, ["angular_paths"] = angularPathSnapshots, ["model_lines"] = modelLineSnapshots, ["detail_lines"] = detailLineSnapshots, ["coordination_zones_mm"] = CoordinationZoneBounds(referencedCoordinationZones), ["connectors"] = ConnectorSnapshot(referencedConnectors), ["arrays"] = ArraySnapshot(referencedArrays), ["nested_components"] = NestedSnapshot(referencedNested)
                    });
                }
            }
            finally { SetFlexValue(manager, parameter, dataType, original, key); manager.CurrentType = current; family.Regenerate(); }
            var partChecks = new JArray();
            foreach (var partKey in referencedParts.Keys)
            {
                var changed = SnapshotsVary(steps.OfType<JObject>().Select(step => step["parts_mm"]?[partKey]));
                if (!changed) throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameter " + key + " is referenced by part " + partKey + " but min/nominal/max flex did not change its bounds. part_bounds_mm=" + new JArray(steps.OfType<JObject>().Select(step => new JObject { ["case"] = step.Value<string>("case"), ["bounds"] = step["parts_mm"]?[partKey], ["affected_parameter_values"] = step["affected_parameter_values"], ["label_dimensions_mm"] = step["label_dimensions_mm"] })).ToString(Newtonsoft.Json.Formatting.None) + "; profile_arc_radii_mm=" + new JArray(steps.OfType<JObject>().Select(step => new JObject { ["case"] = step.Value<string>("case"), ["radii"] = step["profile_arc_radii_mm"]?[partKey] })).ToString(Newtonsoft.Json.Formatting.None));
                partChecks.Add(new JObject { ["part_key"] = partKey, ["changed_across_approved_range"] = true });
            }
            var coordinationZoneChecks = new JArray();
            foreach (var zoneKey in referencedCoordinationZones.Keys)
            {
                var changed = SnapshotsVary(steps.OfType<JObject>().Select(step => step["coordination_zones_mm"]?[zoneKey]));
                if (!changed) throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameter " + key + " is referenced by coordination zone " + zoneKey + " but min/nominal/max flex did not change its bounds.");
                coordinationZoneChecks.Add(new JObject { ["coordination_zone_key"] = zoneKey, ["changed_across_approved_range"] = true });
            }
            var connectorChecks = new JArray();
            foreach (var connectorKey in referencedConnectors.Keys)
            {
                var changed = SnapshotsVary(steps.OfType<JObject>().Select(step => step["connectors"]?[connectorKey]));
                if (!changed) throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameter " + key + " is referenced by connector " + connectorKey + " but min/nominal/max flex did not change its size/origin read-back.");
                connectorChecks.Add(new JObject { ["connector_key"] = connectorKey, ["changed_across_approved_range"] = true });
            }
            var pathChecks = new JArray();
            foreach (var pathKey in referencedOffsetPaths.Keys)
            {
                var changed = SnapshotsVary(steps.OfType<JObject>().Select(step => step["paths"]?[pathKey]?["points_mm"]));
                if (!changed) throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameter " + key + " is referenced by parameterized offset path " + pathKey + " but min/nominal/max flex did not change its points.");
                pathChecks.Add(new JObject { ["part_key"] = pathKey, ["changed_across_approved_range"] = true, ["endpoint_tangents_fixed_to_positive_x"] = true, ["verification_mode"] = "parameterized_offset_path_constraint_read_back", ["tolerance_mm"] = .5 });
            }
            var angularPathChecks = new JArray();
            foreach (var pathKey in referencedAngularPaths.Keys)
            {
                var changed = SnapshotsVary(steps.OfType<JObject>().Select(step => step["angular_paths"]?[pathKey]?["end_mm"]));
                if (!changed) throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameter " + key + " drives angular sweep " + pathKey + " but min/nominal/max flex did not rotate its path endpoint.");
                angularPathChecks.Add(new JObject { ["part_key"] = pathKey, ["changed_across_approved_range"] = true, ["verification_mode"] = "reference_line_driven_angular_sweep_path_read_back", ["tolerance_mm"] = .5 });
            }
            var modelLineChecks = new JArray();
            foreach (var modelLineKey in referencedModelLines.Keys)
            {
                var changed = SnapshotsVary(steps.OfType<JObject>().Select(step => step["model_lines"]?[modelLineKey]));
                if (!changed) throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameter " + key + " drives Model Line " + modelLineKey + " through Reference Planes but min/nominal/max flex did not change its bound endpoint.");
                modelLineChecks.Add(new JObject { ["model_line_key"] = modelLineKey, ["changed_across_approved_range"] = true, ["verification_mode"] = "reference_plane_bound_model_line_endpoint_read_back", ["tolerance_mm"] = .01 });
            }
            var detailLineChecks = new JArray();
            foreach (var detailLineKey in referencedDetailLines.Keys)
            {
                var changed = SnapshotsVary(steps.OfType<JObject>().Select(step => step["detail_lines"]?[detailLineKey]));
                if (!changed) throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameter " + key + " drives Detail Line " + detailLineKey + " through Reference Planes but min/nominal/max flex did not change its bound endpoint.");
                detailLineChecks.Add(new JObject { ["detail_line_key"] = detailLineKey, ["changed_across_approved_range"] = true, ["verification_mode"] = "reference_plane_bound_detail_line_endpoint_read_back", ["tolerance_mm"] = .01 });
            }
            var arcIntersectionChecks = new JArray();
            foreach (var arcPart in referencedParts.Values.Where(item => ParameterReference(item.Contract["path"] as JObject ?? new JObject(), key, "radius_parameter")))
            {
                var path = arcPart.Contract["path"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Arc flex part has no path declaration.");
                var endpointContracts = connectorContracts.Where(item => item.Value<string>("host_part") == arcPart.Key && item.Value<string>("host_face") is "path_start" or "path_end").ToList();
                if (endpointContracts.Count != 2) throw new CommandResultException(ErrorCodes.VerificationFailed, "Arc flex verification requires path_start and path_end connectors for " + arcPart.Key + ".");
                var expectedIntersection = Point(path["tangent_intersection_mm"], "arc tangent_intersection_mm");
                foreach (var step in steps.OfType<JObject>())
                {
                    JObject Snapshot(JObject contract) => step["connectors"]?[contract.Value<string>("key") ?? string.Empty] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Arc connector snapshot is missing during flex for " + arcPart.Key + ".");
                    XYZ Origin(JObject snapshot) => snapshot["origin_mm"] is JObject value ? new XYZ(Mm(value.Value<double>("x")), Mm(value.Value<double>("y")), Mm(value.Value<double>("z"))) : throw new CommandResultException(ErrorCodes.VerificationFailed, "Arc connector origin snapshot is missing.");
                    XYZ Normal(JObject snapshot) => snapshot["normal"] is JObject value ? new XYZ(value.Value<double>("x"), value.Value<double>("y"), value.Value<double>("z")) : throw new CommandResultException(ErrorCodes.VerificationFailed, "Arc connector normal snapshot is missing.");
                    var first = Snapshot(endpointContracts[0]); var second = Snapshot(endpointContracts[1]);
                    var observedIntersection = TangentIntersection(Origin(first), Normal(first), Origin(second), Normal(second), path.Value<string>("plane") ?? string.Empty);
                    if (observedIntersection.DistanceTo(expectedIntersection) > Mm(.5)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Arc connector tangents moved away from the declared Family origin while flexing " + key + " at " + step.Value<string>("case") + ".");
                }
                arcIntersectionChecks.Add(new JObject { ["part_key"] = arcPart.Key, ["tangent_intersection_fixed_across_approved_range"] = true, ["tolerance_mm"] = .5 });
            }
            var arrayChecks = new JArray();
            foreach (var arrayKey in referencedArrays.Keys)
            {
                var changed = SnapshotsVary(steps.OfType<JObject>().Select(step => step["arrays"]?[arrayKey]));
                if (!changed) throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameter " + key + " is referenced by array " + arrayKey + " but min/nominal/max flex did not change its member count.");
                arrayChecks.Add(new JObject { ["array_key"] = arrayKey, ["changed_across_approved_range"] = true });
            }
            var nestedChecks = new JArray();
            foreach (var nestedKey in referencedNested.Keys)
            {
                var changed = SnapshotsVary(steps.OfType<JObject>().Select(step => step["nested_components"]?[nestedKey]));
                if (!changed) throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameter " + key + " is linked to nested component " + nestedKey + " but min/nominal/max flex did not change its read-back.");
                nestedChecks.Add(new JObject { ["nested_component_key"] = nestedKey, ["changed_across_approved_range"] = true });
            }
            var dependentParameterChecks = new JArray();
            foreach (var affectedKey in affectedParameterKeys.Where(item => !string.Equals(item, key, StringComparison.Ordinal)).OrderBy(item => item, StringComparer.Ordinal))
            {
                var values = steps.OfType<JObject>().Select(step => step["affected_parameter_values"]?[affectedKey]).ToList();
                if (!SnapshotsVary(values)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameter " + key + " declares dependent formula/lookup parameter " + affectedKey + " but its evaluated value did not change across min/nominal/max.");
                dependentParameterChecks.Add(new JObject { ["parameter_key"] = affectedKey, ["changed_across_approved_range"] = true, ["verification_mode"] = "family_type_evaluated_formula_or_lookup_read_back" });
            }
            if (!FlexValueMatches(ReadFlexValue(current, parameter, dataType, key), original, dataType)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Parameter " + key + " was not restored after flex verification.");
            results.Add(new JObject
            {
                ["parameter_key"] = key, ["data_type"] = dataType, ["original_value"] = original, ["affected_parameter_keys"] = new JArray(affectedParameterKeys.OrderBy(item => item, StringComparer.Ordinal)),
                ["approved"] = new JObject { ["min"] = approved[0].Value, ["nominal"] = approved[1].Value, ["max"] = approved[2].Value },
                ["referenced_parts"] = new JArray(referencedParts.Keys), ["referenced_paths"] = new JArray(referencedOffsetPaths.Keys), ["referenced_angular_paths"] = new JArray(referencedAngularPaths.Keys), ["referenced_model_lines"] = new JArray(referencedModelLines.Keys), ["referenced_detail_lines"] = new JArray(referencedDetailLines.Keys), ["referenced_coordination_zones"] = new JArray(referencedCoordinationZones.Keys), ["referenced_connectors"] = new JArray(referencedConnectors.Keys), ["referenced_arrays"] = new JArray(referencedArrays.Keys), ["referenced_nested_components"] = new JArray(referencedNested.Keys),
                ["steps"] = steps, ["dependent_parameter_checks"] = dependentParameterChecks, ["part_checks"] = partChecks, ["path_checks"] = pathChecks, ["angular_path_checks"] = angularPathChecks, ["model_line_checks"] = modelLineChecks, ["detail_line_checks"] = detailLineChecks, ["coordination_zone_checks"] = coordinationZoneChecks, ["connector_checks"] = connectorChecks, ["arc_tangent_intersection_checks"] = arcIntersectionChecks, ["array_checks"] = arrayChecks, ["nested_checks"] = nestedChecks, ["restored"] = true
            });
        }
        return new JObject { ["verified"] = true, ["mode"] = "approved_min_nominal_max_component_read_back", ["tests"] = results };
    }

    private static HashSet<string> FlexAffectedParameterKeys(IReadOnlyDictionary<string, JObject> parameterContracts, string rootKey)
    {
        var affected = new HashSet<string>(StringComparer.Ordinal) { rootKey };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var contract in parameterContracts.Values)
            {
                var dependentKey = contract.Value<string>("key") ?? string.Empty;
                if (affected.Contains(dependentKey) || (contract["formula"] == null && contract["lookup"] == null)) continue;
                var dependencies = contract["lookup"] is JObject lookup
                    ? (lookup["lookup_parameter_keys"] as JArray ?? new JArray()).Values<string>()
                    : (contract["formula_dependencies"] as JArray ?? new JArray()).Values<string>();
                if (dependencies.Any(dependency => !string.IsNullOrEmpty(dependency) && affected.Contains(dependency))) changed = affected.Add(dependentKey) || changed;
            }
        }
        return affected;
    }

    private static JArray ModelLineEndpointBindingSnapshot(Document family, JArray modelLines, string declarationKey, JObject contract, ISet<string> activeReferencePlaneKeys)
    {
        var result = new JArray();
        foreach (var binding in (contract["endpoint_bindings"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var endpoint = binding.Value<string>("endpoint") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Model Line " + declarationKey + " endpoint binding has no endpoint.");
            var referencePlaneKey = binding.Value<string>("reference_plane_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Model Line " + declarationKey + " endpoint binding has no Reference Plane key.");
            if (!activeReferencePlaneKeys.Contains(referencePlaneKey)) continue;
            var curveSnapshot = modelLines.OfType<JObject>().SingleOrDefault(item => string.Equals(item.Value<string>("declaration_key"), declarationKey, StringComparison.Ordinal)
                && (item["endpoint_bindings"] as JArray ?? new JArray()).OfType<JObject>().Any(itemBinding => string.Equals(itemBinding.Value<string>("endpoint"), endpoint, StringComparison.Ordinal)));
            if (curveSnapshot == null) throw new CommandResultException(ErrorCodes.VerificationFailed, "Model Line " + declarationKey + " " + endpoint + " endpoint binding evidence is missing during flex.");
            var bindingEvidence = (curveSnapshot["endpoint_bindings"] as JArray ?? new JArray()).OfType<JObject>().SingleOrDefault(item => string.Equals(item.Value<string>("endpoint"), endpoint, StringComparison.Ordinal))
                ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Model Line " + declarationKey + " " + endpoint + " binding evidence is ambiguous during flex.");
            if (!string.Equals(bindingEvidence.Value<string>("reference_plane_key"), referencePlaneKey, StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Model Line " + declarationKey + " " + endpoint + " binding changed its declared Reference Plane.");
            var curveId = curveSnapshot.Value<long?>("curve_id") ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Model Line " + declarationKey + " binding has no curve id.");
            var referencePlaneId = bindingEvidence.Value<long?>("reference_plane_id") ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Model Line " + declarationKey + " binding has no Reference Plane id.");
            var curve = family.GetElement(RevitIdCompatibility.Eid(curveId)) as ModelCurve ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Model Line " + declarationKey + " curve cannot be resolved during flex.");
            var referencePlane = family.GetElement(RevitIdCompatibility.Eid(referencePlaneId)) as ReferencePlane ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Model Line " + declarationKey + " Reference Plane cannot be resolved during flex.");
            var endpointIndex = endpoint == "start" ? 0 : endpoint == "end" ? 1 : throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Model Line " + declarationKey + " binding endpoint is invalid.");
            VerifyModelLineEndpointOnReferencePlane(curve, endpointIndex, referencePlane, "Model Line " + declarationKey + " " + endpoint + " endpoint during flex");
            var point = curve.GeometryCurve.GetEndPoint(endpointIndex);
            result.Add(new JObject { ["endpoint"] = endpoint, ["reference_plane_key"] = referencePlaneKey, ["curve_id"] = curveId, ["endpoint_mm"] = PointMmSnapshot(point), ["on_reference_plane"] = true, ["tolerance_mm"] = .01 });
        }
        if (result.Count == 0) throw new CommandResultException(ErrorCodes.VerificationFailed, "Model Line " + declarationKey + " has no endpoint bindings driven by the active reference graph.");
        return result;
    }

    private static JArray DetailLineEndpointBindingSnapshot(Document family, JArray detailLines, string declarationKey, JObject contract, ISet<string> activeReferencePlaneKeys)
    {
        var result = new JArray();
        foreach (var binding in (contract["endpoint_bindings"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var endpoint = binding.Value<string>("endpoint") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail Line " + declarationKey + " endpoint binding has no endpoint.");
            var referencePlaneKey = binding.Value<string>("reference_plane_key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail Line " + declarationKey + " endpoint binding has no Reference Plane key.");
            if (!activeReferencePlaneKeys.Contains(referencePlaneKey)) continue;
            var curveSnapshot = detailLines.OfType<JObject>().SingleOrDefault(item => string.Equals(item.Value<string>("declaration_key"), declarationKey, StringComparison.Ordinal)
                && (item["endpoint_bindings"] as JArray ?? new JArray()).OfType<JObject>().Any(itemBinding => string.Equals(itemBinding.Value<string>("endpoint"), endpoint, StringComparison.Ordinal)));
            if (curveSnapshot == null) throw new CommandResultException(ErrorCodes.VerificationFailed, "Detail Line " + declarationKey + " " + endpoint + " endpoint binding evidence is missing during flex.");
            var bindingEvidence = (curveSnapshot["endpoint_bindings"] as JArray ?? new JArray()).OfType<JObject>().SingleOrDefault(item => string.Equals(item.Value<string>("endpoint"), endpoint, StringComparison.Ordinal))
                ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Detail Line " + declarationKey + " " + endpoint + " binding evidence is ambiguous during flex.");
            if (!string.Equals(bindingEvidence.Value<string>("reference_plane_key"), referencePlaneKey, StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Detail Line " + declarationKey + " " + endpoint + " binding changed its declared Reference Plane.");
            var curveId = curveSnapshot.Value<long?>("curve_id") ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Detail Line " + declarationKey + " binding has no curve id.");
            var referencePlaneId = bindingEvidence.Value<long?>("reference_plane_id") ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Detail Line " + declarationKey + " binding has no Reference Plane id.");
            var curve = family.GetElement(RevitIdCompatibility.Eid(curveId)) as DetailCurve ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Detail Line " + declarationKey + " curve cannot be resolved during flex.");
            var referencePlane = family.GetElement(RevitIdCompatibility.Eid(referencePlaneId)) as ReferencePlane ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Detail Line " + declarationKey + " Reference Plane cannot be resolved during flex.");
            var endpointIndex = endpoint == "start" ? 0 : endpoint == "end" ? 1 : throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Detail Line " + declarationKey + " binding endpoint is invalid.");
            VerifyModelLineEndpointOnReferencePlane(curve, endpointIndex, referencePlane, "Detail Line " + declarationKey + " " + endpoint + " endpoint during flex");
            var point = curve.GeometryCurve.GetEndPoint(endpointIndex);
            result.Add(new JObject { ["endpoint"] = endpoint, ["reference_plane_key"] = referencePlaneKey, ["curve_id"] = curveId, ["endpoint_mm"] = PointMmSnapshot(point), ["on_reference_plane"] = true, ["tolerance_mm"] = .01 });
        }
        if (result.Count == 0) throw new CommandResultException(ErrorCodes.VerificationFailed, "Detail Line " + declarationKey + " has no endpoint bindings driven by the active reference graph.");
        return result;
    }

    private static JObject VerifyParts(Document family, FamilyBlueprintSpec spec, Dictionary<string, PartResult> parts, Dictionary<string, CoordinationZoneResult> coordinationZones, List<ConnectorElement> connectors)
    {
        var forms = new JArray();
        foreach (var part in parts.Values)
        {
            var box = part.Form.get_BoundingBox(null) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Part " + part.Key + " has no bounding box.");
            var expectedSolid = string.Equals(part.Operation, "solid", StringComparison.Ordinal);
            if (part.Form.IsSolid != expectedSolid) throw new CommandResultException(ErrorCodes.VerificationFailed, "Part " + part.Key + " solid/void operation did not read back.");
            var actualVisibility = VisibilitySnapshot(part.Form.GetVisibility());
            var expectedVisibility = VisibilitySnapshot(Visibility(part.Contract["visibility"] as JObject ?? new JObject(), FamilyElementVisibilityType.Model));
            if (!JToken.DeepEquals(actualVisibility, expectedVisibility)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Part " + part.Key + " static visibility did not read back.");
            var material = part.Form.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
            forms.Add(new JObject { ["part_key"] = part.Key, ["primitive"] = part.Primitive, ["operation"] = part.Operation, ["is_solid"] = part.Form.IsSolid, ["form_id"] = part.Form.Id.Val(), ["role"] = part.Form.Subcategory?.Name, ["visibility"] = actualVisibility, ["visibility_parameter"] = part.VisibilityParameter, ["material_id"] = material?.AsElementId().Val(), ["material_associated"] = material != null && family.FamilyManager.GetAssociatedFamilyParameter(material) != null, ["path"] = part.Form is Sweep sweep ? SweepPathSnapshot(sweep) : null, ["bounds_mm"] = BoundsMm(box) });
        }
        var coordinationZoneReadBack = CoordinationZoneSnapshot(coordinationZones);
        var connectorReadBack = new JArray(connectors.Select(FamilyConnectorReadBack.Describe));
        var declaredFormCount = (spec.Blueprint["parts"] as JArray ?? new JArray()).Count + (spec.Blueprint["coordination_zones"] as JArray ?? new JArray()).Count;
        // Connector declarations are optional for 2D symbols, profiles and
        // many ELV/device Families.  Treat an absent array as the explicit
        // empty set, not as a nullable count that makes 0 != null fail a
        // correctly connector-free Family build.
        var declaredConnectorCount = (spec.Blueprint["connectors"] as JArray ?? new JArray()).Count;
        if (forms.Count + coordinationZoneReadBack.Count != declaredFormCount || connectorReadBack.Count != declaredConnectorCount)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Blueprint form/connector count read-back does not match the declaration.");
        return new JObject { ["verified"] = true, ["parts"] = forms, ["coordination_zones"] = coordinationZoneReadBack, ["connectors"] = connectorReadBack, ["not_verified_here"] = new JArray("project_hosting", "network_connection", "rotate_mirror", "project_bep_clearance_clash_access_support_validation", "quantity_exclusion") };
    }

    private static void AssociateConnectorSize(FamilyManager manager, ConnectorElement connector, JObject contract, Dictionary<string, FamilyParameter> parameters)
    {
        void Link(BuiltInParameter id, string property)
        {
            if (contract.Value<string>(property) is not string key) return;
            var elementParameter = connector.get_Parameter(id); var familyParameter = RequireParameter(parameters, key);
            if (elementParameter == null || !manager.CanElementParameterBeAssociated(elementParameter)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Connector " + property + " cannot be associated.");
            manager.AssociateElementParameterToFamilyParameter(elementParameter, familyParameter);
        }
        Link(BuiltInParameter.CONNECTOR_DIAMETER, "diameter_parameter"); Link(BuiltInParameter.CONNECTOR_WIDTH, "width_parameter"); Link(BuiltInParameter.CONNECTOR_HEIGHT, "height_parameter");
    }

    private static void ApplyConnectorTechnicalData(ConnectorElement connector, JObject contract, string discipline, FamilyManager manager, Dictionary<string, FamilyParameter> parameters)
    {
        var connectorStateChanged = false;
        void SetInteger(BuiltInParameter builtIn, int value, string label)
        {
            var parameter = connector.get_Parameter(builtIn);
            if (parameter == null || parameter.IsReadOnly) throw new CommandResultException(ErrorCodes.VerificationFailed, "Connector " + label + " has no writable Revit parameter.");
            if (parameter.AsInteger() != value) parameter.Set(value);
            if (parameter.AsInteger() != value) throw new CommandResultException(ErrorCodes.VerificationFailed, "Connector " + label + " could not be written/read back.");
            connectorStateChanged = true;
        }
        void SetDouble(BuiltInParameter builtIn, double value, string label)
        {
            var parameter = connector.get_Parameter(builtIn);
            if (parameter == null || parameter.IsReadOnly) throw new CommandResultException(ErrorCodes.VerificationFailed, "Connector " + label + " has no writable Revit parameter.");
            if (Math.Abs(parameter.AsDouble() - value) > 1e-9) parameter.Set(value);
            if (Math.Abs(parameter.AsDouble() - value) > 1e-9) throw new CommandResultException(ErrorCodes.VerificationFailed, "Connector " + label + " could not be written/read back.");
        }
        void Associate(BuiltInParameter builtIn, string property)
        {
            if (contract.Value<string>(property) is not string parameterKey) return;
            var connectorParameter = connector.get_Parameter(builtIn); var familyParameter = RequireParameter(parameters, parameterKey);
            var flowConfiguration = discipline == "duct" ? BuiltInParameter.RBS_DUCT_FLOW_CONFIGURATION_PARAM : discipline == "pipe" ? BuiltInParameter.RBS_PIPE_FLOW_CONFIGURATION_PARAM : (BuiltInParameter?)null;
            if (connectorParameter == null || !manager.CanElementParameterBeAssociated(connectorParameter))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Connector " + property + " cannot be associated to " + familyParameter.Definition.Name + ". " + ConnectorAssociationDiagnostic(connector, builtIn, connectorParameter, familyParameter, manager, flowConfiguration));
            manager.AssociateElementParameterToFamilyParameter(connectorParameter, familyParameter);
            if (manager.GetAssociatedFamilyParameter(connectorParameter)?.Id != familyParameter.Id) throw new CommandResultException(ErrorCodes.VerificationFailed, "Connector " + property + " association did not read back.");
        }
        if (contract.Value<string>("flow_configuration") is string configuration)
        {
            var value = configuration switch { "preset" => 1, "system" => 2, "demand" => 3, _ => 0 };
            SetInteger(discipline == "duct" ? BuiltInParameter.RBS_DUCT_FLOW_CONFIGURATION_PARAM : BuiltInParameter.RBS_PIPE_FLOW_CONFIGURATION_PARAM, value, "flow_configuration");
        }
        if (contract.Value<string>("flow_direction") is string direction)
            SetInteger(discipline == "duct" ? BuiltInParameter.RBS_DUCT_FLOW_DIRECTION_PARAM : BuiltInParameter.RBS_PIPE_FLOW_DIRECTION_PARAM, direction switch { "in" => (int)FlowDirectionType.In, "out" => (int)FlowDirectionType.Out, _ => (int)FlowDirectionType.Bidirectional }, "flow_direction");
        // Revit makes the flow/value connector parameter associable only after
        // the declared configuration has been written (for example Preset for
        // a direct airflow/flow parameter, System for flow factor).
        // The connector's parameter graph is only refreshed after regeneration;
        // reading the value immediately after Set is not sufficient evidence of
        // association eligibility.
        if (connectorStateChanged && discipline is "duct" or "pipe") connector.Document.Regenerate();
        if (discipline is "duct" or "pipe")
        {
            Associate(discipline == "duct" ? BuiltInParameter.RBS_DUCT_FLOW_PARAM : BuiltInParameter.RBS_PIPE_FLOW_PARAM, "flow_parameter");
            Associate(BuiltInParameter.RBS_FLOW_FACTOR_PARAM, "flow_factor_parameter");
        }
        if (contract.Value<string>("loss_method") is string lossMethod)
        {
            var value = lossMethod switch { "table" => 1, "specific_loss" => 4, "coefficient" => 6, _ => 0 };
            SetInteger(discipline == "duct" ? BuiltInParameter.RBS_DUCT_FITTING_LOSS_METHOD_PARAM : BuiltInParameter.RBS_PIPE_FITTING_LOSS_METHOD_PARAM, value, "loss_method");
        }
        if (contract.Value<double?>("loss_coefficient") is double coefficient) SetDouble(BuiltInParameter.RBS_LOSS_COEFFICIENT, coefficient, "loss_coefficient");
        if (contract.Value<double?>("pressure_drop_pa") is double pressureDrop) SetDouble(BuiltInParameter.RBS_PRESSURE_DROP, PressureToInternal(pressureDrop), "pressure_drop_pa");
        if (contract.Value<string>("joint_type") is string joint) SetInteger(BuiltInParameter.CONNECTOR_JOINT_TYPE, joint switch { "flanged" => 1, "welded" => 2, "threaded" => 3, "grooved" => 4, "glued" => 5, "soldered" => 6, _ => 0 }, "joint_type");
        if (contract.Value<string>("gender") is string gender) SetInteger(BuiltInParameter.CONNECTOR_GENDER_TYPE, gender switch { "male" => 1, "female" => 2, _ => 0 }, "gender");
        if (contract.Value<double?>("engagement_length_mm") is double engagement) SetDouble(BuiltInParameter.CONNECTOR_ENGAGEMENT_LENGTH, Mm(engagement), "engagement_length_mm");
        if (contract.Value<bool?>("allow_slope_adjustments") is bool allowSlope)
        {
            if (discipline != "pipe") throw new CommandResultException(ErrorCodes.EvidenceInvalid, "allow_slope_adjustments is valid only for pipe connectors.");
            if (!string.Equals(contract.Value<string>("system_classification"), "Global", StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "allow_slope_adjustments requires a Global pipe system classification.");
            SetInteger(BuiltInParameter.RBS_ADJUSTABLE_CONNECTOR, allowSlope ? 1 : 0, "allow_slope_adjustments");
        }
    }

    private static string ConnectorAssociationDiagnostic(ConnectorElement connector, BuiltInParameter builtIn, Parameter? connectorParameter, FamilyParameter familyParameter, FamilyManager manager, BuiltInParameter? flowConfigurationBuiltIn)
    {
        string Describe(Parameter? parameter) => parameter == null
            ? "missing"
            : "id=" + ElementIdDiagnosticValue(parameter.Id) + ",name=" + parameter.Definition.Name + ",storage=" + parameter.StorageType + ",read_only=" + parameter.IsReadOnly + ",value=" + ParameterDiagnosticValue(parameter);
        var canAssociate = connectorParameter != null && manager.CanElementParameterBeAssociated(connectorParameter);
        var flowConfiguration = flowConfigurationBuiltIn.HasValue ? Describe(connector.get_Parameter(flowConfigurationBuiltIn.Value)) : "n/a";
        var targetDataType = connectorParameter?.Definition.GetDataType()?.TypeId ?? "unknown";
        var familyDataType = familyParameter.Definition.GetDataType()?.TypeId ?? "unknown";
        return "association_diagnostic: connector_id=" + ElementIdDiagnosticValue(connector.Id)
            + ", target_built_in=" + builtIn
            + ", target_parameter=[" + Describe(connectorParameter) + ",data_type=" + targetDataType + "]"
            + ", can_associate=" + canAssociate
            + ", family_parameter=[id=" + ElementIdDiagnosticValue(familyParameter.Id) + ",name=" + familyParameter.Definition.Name + ",is_instance=" + familyParameter.IsInstance + ",data_type=" + familyDataType + "]"
            + ", flow_configuration=[" + flowConfiguration + "].";
    }

    private static string ParameterDiagnosticValue(Parameter parameter)
    {
        try
        {
            return parameter.StorageType switch
            {
                StorageType.Integer => parameter.AsInteger().ToString(),
                StorageType.Double => parameter.AsDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                StorageType.String => parameter.AsString() ?? string.Empty,
                StorageType.ElementId => ElementIdDiagnosticValue(parameter.AsElementId()),
                _ => "none"
            };
        }
        catch
        {
            return "unreadable";
        }
    }

    private static string ElementIdDiagnosticValue(ElementId id)
    {
#if REVIT2024 || REVIT2025 || REVIT2026 || REVIT2027
        return id.Value.ToString();
#else
        return id.IntegerValue.ToString();
#endif
    }

    private static void ApplyElectricalConnectorData(FamilyManager manager, ConnectorElement connector, JObject contract, Dictionary<string, FamilyParameter> parameters, string discipline)
    {
        var bindings = new Dictionary<string, BuiltInParameter>(StringComparer.Ordinal)
        {
            ["voltage_parameter"] = BuiltInParameter.RBS_ELEC_VOLTAGE,
            ["apparent_load_parameter"] = BuiltInParameter.RBS_ELEC_APPARENT_LOAD,
            ["number_of_poles_parameter"] = BuiltInParameter.RBS_ELEC_NUMBER_OF_POLES,
            ["power_factor_parameter"] = BuiltInParameter.RBS_ELEC_POWER_FACTOR,
            ["balanced_load_parameter"] = BuiltInParameter.RBS_ELEC_BALANCED_LOAD,
            ["load_classification_parameter"] = BuiltInParameter.RBS_ELEC_LOAD_CLASSIFICATION
        };
        if (discipline != "electrical")
        {
            if (bindings.Keys.Any(key => contract[key] != null) || contract["power_factor_state"] != null) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Electrical connector data was declared on a non-electrical connector.");
            return;
        }
        foreach (var binding in bindings)
        {
            if (contract.Value<string>(binding.Key) is not string parameterKey) continue;
            var elementParameter = connector.get_Parameter(binding.Value);
            var familyParameter = RequireParameter(parameters, parameterKey);
            if (elementParameter == null || !manager.CanElementParameterBeAssociated(elementParameter)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Electrical connector " + binding.Key + " cannot be associated to " + familyParameter.Definition.Name + ".");
            manager.AssociateElementParameterToFamilyParameter(elementParameter, familyParameter);
            if (manager.GetAssociatedFamilyParameter(elementParameter)?.Id != familyParameter.Id) throw new CommandResultException(ErrorCodes.VerificationFailed, "Electrical connector " + binding.Key + " association did not read back.");
        }
        if (contract.Value<string>("power_factor_state") is string state)
        {
            var parameter = connector.get_Parameter(BuiltInParameter.RBS_ELEC_POWER_FACTOR_STATE);
            var value = state == "leading" ? (int)PowerFactorStateType.Leading : (int)PowerFactorStateType.Lagging;
            if (parameter == null || parameter.IsReadOnly || !parameter.Set(value) || parameter.AsInteger() != value) throw new CommandResultException(ErrorCodes.VerificationFailed, "Electrical connector power_factor_state could not be written/read back.");
        }
    }

    private static double PressureToInternal(double pascals)
    {
#if REVIT2019 || REVIT2020 || REVIT2021
#pragma warning disable CS0618
        return UnitUtils.ConvertToInternalUnits(pascals, DisplayUnitType.DUT_PASCALS);
#pragma warning restore CS0618
#else
        return UnitUtils.ConvertToInternalUnits(pascals, UnitTypeId.Pascals);
#endif
    }

    private static FamilyParameter AddParameter(FamilyManager manager, string name, string dataType, string? group, bool instance)
    {
#if REVIT2019 || REVIT2020 || REVIT2021
#pragma warning disable CS0618
        var type = LegacyParameterType(dataType);
        return manager.AddParameter(name, ParameterGroup(group, dataType), type, instance);
#pragma warning restore CS0618
#else
        var spec = ParameterSpecType(dataType);
        return manager.AddParameter(name, ParameterGroupId(group, dataType), spec, instance);
#endif
    }

#if REVIT2019 || REVIT2020 || REVIT2021
#pragma warning disable CS0618
    private static ParameterType LegacyParameterType(string dataType) => dataType switch
    {
        "length" => ParameterType.Length, "area" => ParameterType.Area, "volume" => ParameterType.Volume,
        "number" => ParameterType.Number, "integer" => ParameterType.Integer, "yesno" => ParameterType.YesNo,
        "angle" => ParameterType.Angle, "material" => ParameterType.Material, "url" => ParameterType.URL,
        "currency" => ParameterType.Currency, "airflow" => ParameterType.HVACAirflow, "flow" => ParameterType.Flow,
        "hvac_pressure" => ParameterType.HVACPressure, "piping_pressure" => ParameterType.PipingPressure,
        "electrical_power" => ParameterType.ElectricalPower, "apparent_power" => ParameterType.ElectricalApparentPower,
        "voltage" => ParameterType.ElectricalPotential, "current" => ParameterType.ElectricalCurrent,
        "frequency" => ParameterType.ElectricalFrequency, "hvac_temperature" => ParameterType.HVACTemperature,
        "piping_temperature" => ParameterType.PipingTemperature, "number_of_poles" => ParameterType.NumberOfPoles,
        "load_classification" => ParameterType.LoadClassification, "text" => ParameterType.Text,
        _ => throw new CommandResultException(ErrorCodes.Unsupported, "Unsupported Family parameter data type: " + dataType + ".")
    };
#pragma warning restore CS0618
#else
    private static ForgeTypeId ParameterSpecType(string dataType) => dataType switch
    {
        "length" => SpecTypeId.Length, "area" => SpecTypeId.Area, "volume" => SpecTypeId.Volume,
        "number" => SpecTypeId.Number, "integer" => SpecTypeId.Int.Integer, "yesno" => SpecTypeId.Boolean.YesNo,
        "angle" => SpecTypeId.Angle, "material" => SpecTypeId.Reference.Material, "url" => SpecTypeId.String.Url,
        "currency" => SpecTypeId.Currency, "airflow" => SpecTypeId.AirFlow, "flow" => SpecTypeId.Flow,
        "hvac_pressure" => SpecTypeId.HvacPressure, "piping_pressure" => SpecTypeId.PipingPressure,
        "electrical_power" => SpecTypeId.ElectricalPower, "apparent_power" => SpecTypeId.ApparentPower,
        "voltage" => SpecTypeId.ElectricalPotential, "current" => SpecTypeId.Current,
        "frequency" => SpecTypeId.ElectricalFrequency, "hvac_temperature" => SpecTypeId.HvacTemperature,
        "piping_temperature" => SpecTypeId.PipingTemperature, "number_of_poles" => SpecTypeId.Int.NumberOfPoles,
        "load_classification" => SpecTypeId.Reference.LoadClassification, "text" => SpecTypeId.String.Text,
        _ => throw new CommandResultException(ErrorCodes.Unsupported, "Unsupported Family parameter data type: " + dataType + ".")
    };
#endif

#if REVIT2019 || REVIT2020 || REVIT2021
    private static BuiltInParameterGroup ParameterGroup(string? group, string dataType) => group switch
    {
        "constraints" => BuiltInParameterGroup.PG_CONSTRAINTS,
        "geometry" => BuiltInParameterGroup.PG_GEOMETRY,
        "materials" => BuiltInParameterGroup.PG_MATERIALS,
        "mechanical" => BuiltInParameterGroup.PG_MECHANICAL,
        "mechanical_airflow" => BuiltInParameterGroup.PG_MECHANICAL_AIRFLOW,
        "electrical" => BuiltInParameterGroup.PG_ELECTRICAL,
        "plumbing" => BuiltInParameterGroup.PG_PLUMBING,
        "data" => BuiltInParameterGroup.PG_DATA,
        "graphics" => BuiltInParameterGroup.PG_GRAPHICS,
        "general" => BuiltInParameterGroup.PG_GENERAL,
        _ => dataType is "length" or "area" or "volume" or "angle" ? BuiltInParameterGroup.PG_GEOMETRY : dataType == "material" ? BuiltInParameterGroup.PG_MATERIALS :
            dataType is "airflow" or "hvac_pressure" or "hvac_temperature" ? BuiltInParameterGroup.PG_MECHANICAL :
            dataType is "flow" or "piping_pressure" or "piping_temperature" ? BuiltInParameterGroup.PG_PLUMBING :
            dataType is "electrical_power" or "apparent_power" or "voltage" or "current" or "frequency" or "number_of_poles" or "load_classification" ? BuiltInParameterGroup.PG_ELECTRICAL : BuiltInParameterGroup.PG_IDENTITY_DATA
    };
#else
    private static ForgeTypeId ParameterGroupId(string? group, string dataType) => group switch
    {
        "constraints" => GroupTypeId.Constraints,
        "geometry" => GroupTypeId.Geometry,
        "materials" => GroupTypeId.Materials,
        "mechanical" => GroupTypeId.Mechanical,
        "mechanical_airflow" => GroupTypeId.MechanicalAirflow,
        "electrical" => GroupTypeId.Electrical,
        "plumbing" => GroupTypeId.Plumbing,
        "data" => GroupTypeId.Data,
        "graphics" => GroupTypeId.Graphics,
        "general" => GroupTypeId.General,
        _ => dataType is "length" or "area" or "volume" or "angle" ? GroupTypeId.Geometry : dataType == "material" ? GroupTypeId.Materials :
            dataType is "airflow" or "hvac_pressure" or "hvac_temperature" ? GroupTypeId.Mechanical :
            dataType is "flow" or "piping_pressure" or "piping_temperature" ? GroupTypeId.Plumbing :
            dataType is "electrical_power" or "apparent_power" or "voltage" or "current" or "frequency" or "number_of_poles" or "load_classification" ? GroupTypeId.Electrical : GroupTypeId.IdentityData
    };
#endif

    private static FamilyParameter AddInternalLength(FamilyManager manager, string name, bool isInstance = false)
    {
#if REVIT2019 || REVIT2020 || REVIT2021
#pragma warning disable CS0618
        return manager.AddParameter(name, BuiltInParameterGroup.PG_GEOMETRY, ParameterType.Length, isInstance);
#pragma warning restore CS0618
#else
        return manager.AddParameter(name, GroupTypeId.Geometry, SpecTypeId.Length, isInstance);
#endif
    }

    private static void Set(FamilyManager manager, FamilyParameter parameter, string dataType, JToken value, Dictionary<string, ElementId> materials, Dictionary<string, ElectricalLoadClassification> electricalLoadClassifications)
    {
        switch (dataType)
        {
            case "length": manager.Set(parameter, Mm(value.Value<double>())); break;
            case "area": case "volume": case "airflow": case "flow": case "hvac_pressure": case "piping_pressure":
            case "electrical_power": case "apparent_power": case "voltage": case "current": case "frequency":
            case "hvac_temperature": case "piping_temperature": manager.Set(parameter, ToInternal(dataType, value.Value<double>())); break;
            case "angle": manager.Set(parameter, value.Value<double>() * Math.PI / 180.0); break;
            case "number": case "currency": manager.Set(parameter, value.Value<double>()); break;
            case "integer": case "number_of_poles": manager.Set(parameter, value.Value<int>()); break;
            case "yesno": manager.Set(parameter, value.Value<bool>() ? 1 : 0); break;
            case "text": case "url": manager.Set(parameter, value.Value<string>() ?? string.Empty); break;
            case "material":
                var materialKey = value.Value<string>() ?? string.Empty;
                if (!materials.TryGetValue(materialKey, out var materialId)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown Blueprint material " + materialKey + ".");
                manager.Set(parameter, materialId); break;
            case "load_classification":
                var classificationKey = value.Value<string>() ?? string.Empty;
                if (!electricalLoadClassifications.TryGetValue(classificationKey, out var classification)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown electrical load classification " + classificationKey + ".");
                manager.Set(parameter, classification.Id); break;
            default: throw new CommandResultException(ErrorCodes.Unsupported, "Unsupported parameter data type: " + dataType + ".");
        }
    }

    private static double CurrentLength(FamilyManager manager, FamilyParameter parameter) => manager.CurrentType.AsDouble(parameter) ?? throw new CommandResultException(ErrorCodes.InvalidParam, "Length parameter " + parameter.Definition.Name + " has no current value.");
    private static FamilyParameter RequireParameter(Dictionary<string, FamilyParameter> parameters, string key) => parameters.TryGetValue(key, out var parameter) ? parameter : throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown Blueprint parameter: " + key + ".");
    private static double Mm(double value) => value / 304.8;

    private static double ToInternal(string dataType, double value)
    {
#if REVIT2019 || REVIT2020 || REVIT2021
#pragma warning disable CS0618
        var unit = dataType switch
        {
            "area" => DisplayUnitType.DUT_SQUARE_METERS, "volume" => DisplayUnitType.DUT_CUBIC_METERS,
            "airflow" or "flow" => DisplayUnitType.DUT_LITERS_PER_SECOND,
            "hvac_pressure" or "piping_pressure" => DisplayUnitType.DUT_PASCALS,
            "electrical_power" => DisplayUnitType.DUT_WATTS, "apparent_power" => DisplayUnitType.DUT_VOLT_AMPERES,
            "voltage" => DisplayUnitType.DUT_VOLTS, "current" => DisplayUnitType.DUT_AMPERES,
            "frequency" => DisplayUnitType.DUT_HERTZ,
            "hvac_temperature" or "piping_temperature" => DisplayUnitType.DUT_CELSIUS,
            _ => throw new CommandResultException(ErrorCodes.Unsupported, "No SI input conversion is defined for " + dataType + ".")
        };
        return UnitUtils.ConvertToInternalUnits(value, unit);
#pragma warning restore CS0618
#else
        var unit = dataType switch
        {
            "area" => UnitTypeId.SquareMeters, "volume" => UnitTypeId.CubicMeters,
            "airflow" or "flow" => UnitTypeId.LitersPerSecond,
            "hvac_pressure" or "piping_pressure" => UnitTypeId.Pascals,
            "electrical_power" => UnitTypeId.Watts, "apparent_power" => UnitTypeId.VoltAmperes,
            "voltage" => UnitTypeId.Volts, "current" => UnitTypeId.Amperes,
            "frequency" => UnitTypeId.Hertz,
            "hvac_temperature" or "piping_temperature" => UnitTypeId.Celsius,
            _ => throw new CommandResultException(ErrorCodes.Unsupported, "No SI input conversion is defined for " + dataType + ".")
        };
        return UnitUtils.ConvertToInternalUnits(value, unit);
#endif
    }

    private static double FlowFromInternal(double value)
    {
#if REVIT2019 || REVIT2020 || REVIT2021
#pragma warning disable CS0618
        return UnitUtils.ConvertFromInternalUnits(value, DisplayUnitType.DUT_LITERS_PER_SECOND);
#pragma warning restore CS0618
#else
        return UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.LitersPerSecond);
#endif
    }

    private static CurveArrArray Rectangle(double width, double height)
    {
        var y = width / 2; var z = height / 2; var loop = new CurveArray(); var a = new XYZ(0, -y, -z); var b = new XYZ(0, y, -z); var c = new XYZ(0, y, z); var d = new XYZ(0, -y, z);
        loop.Append(Line.CreateBound(a, b)); loop.Append(Line.CreateBound(b, c)); loop.Append(Line.CreateBound(c, d)); loop.Append(Line.CreateBound(d, a)); var result = new CurveArrArray(); result.Append(loop); return result;
    }
    private static CurveArrArray Circle(double radius) { var result = new CurveArrArray(); result.Append(CircleLoop(radius)); return result; }
    private static CurveArray CircleLoop(double radius)
    {
        var loop = new CurveArray(); var top = new XYZ(0, radius, 0); var bottom = new XYZ(0, -radius, 0); var right = new XYZ(0, 0, radius); var left = new XYZ(0, 0, -radius);
        loop.Append(Arc.Create(top, bottom, right)); loop.Append(Arc.Create(bottom, top, left)); return loop;
    }
    private static CurveArrArray Oval(double width, double height, string majorAxis) => OvalAtFrame(width, height, majorAxis, XYZ.Zero, XYZ.BasisY, XYZ.BasisZ);

    private static CurveArrArray RectangleAt(double width, double height, string plane, XYZ origin)
    {
        var u = width / 2; var v = height / 2;
        var loop = new CurveArray(); var a = ProfilePoint(plane, origin, -u, -v); var b = ProfilePoint(plane, origin, u, -v); var c = ProfilePoint(plane, origin, u, v); var d = ProfilePoint(plane, origin, -u, v);
        loop.Append(Line.CreateBound(a, b)); loop.Append(Line.CreateBound(b, c)); loop.Append(Line.CreateBound(c, d)); loop.Append(Line.CreateBound(d, a)); var result = new CurveArrArray(); result.Append(loop); return result;
    }
    private static CurveArrArray CircleAt(double radius, string plane, XYZ origin) { var result = new CurveArrArray(); result.Append(CircleLoopAt(radius, plane, origin)); return result; }
    private static CurveArray CircleLoopAt(double radius, string plane, XYZ origin)
    {
        var loop = new CurveArray(); var top = ProfilePoint(plane, origin, 0, radius); var bottom = ProfilePoint(plane, origin, 0, -radius); var right = ProfilePoint(plane, origin, radius, 0); var left = ProfilePoint(plane, origin, -radius, 0);
        loop.Append(Arc.Create(top, bottom, right)); loop.Append(Arc.Create(bottom, top, left)); return loop;
    }
    private static CurveArrArray OvalAt(double width, double height, string majorAxis, string plane, XYZ origin)
    {
        var widthAxis = plane switch { "xy" or "xz" => XYZ.BasisX, "yz" => XYZ.BasisY, _ => throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown profile plane " + plane + ".") };
        var heightAxis = plane switch { "xy" => XYZ.BasisY, "xz" or "yz" => XYZ.BasisZ, _ => throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown profile plane " + plane + ".") };
        return OvalAtFrame(width, height, majorAxis, origin, widthAxis, heightAxis);
    }
    private static CurveArrArray RectangleAtFrame(double width, double height, XYZ origin, XYZ widthAxis, XYZ heightAxis)
    {
        var u = width / 2; var v = height / 2; XYZ At(double x, double y) => origin.Add(widthAxis.Multiply(x)).Add(heightAxis.Multiply(y));
        var loop = new CurveArray(); var a = At(-u, -v); var b = At(u, -v); var c = At(u, v); var d = At(-u, v);
        loop.Append(Line.CreateBound(a, b)); loop.Append(Line.CreateBound(b, c)); loop.Append(Line.CreateBound(c, d)); loop.Append(Line.CreateBound(d, a)); var result = new CurveArrArray(); result.Append(loop); return result;
    }
    private static CurveArrArray CircleAtFrame(double radius, XYZ origin, XYZ widthAxis, XYZ heightAxis) { var result = new CurveArrArray(); result.Append(CircleLoopAtFrame(radius, origin, widthAxis, heightAxis)); return result; }
    private static CurveArray CircleLoopAtFrame(double radius, XYZ origin, XYZ widthAxis, XYZ heightAxis)
    {
        XYZ At(double x, double y) => origin.Add(widthAxis.Multiply(x)).Add(heightAxis.Multiply(y));
        var loop = new CurveArray(); var top = At(0, radius); var bottom = At(0, -radius); var right = At(radius, 0); var left = At(-radius, 0);
        loop.Append(Arc.Create(top, bottom, right)); loop.Append(Arc.Create(bottom, top, left)); return loop;
    }
    private static CurveArrArray OvalAtFrame(double width, double height, string majorAxis, XYZ origin, XYZ widthAxis, XYZ heightAxis)
    {
        if (width <= 0 || height <= 0) throw new CommandResultException(ErrorCodes.InvalidParam, "Oval width and height must be greater than zero.");
        XYZ At(double widthOffset, double heightOffset) => origin.Add(widthAxis.Multiply(widthOffset)).Add(heightAxis.Multiply(heightOffset));
        var loop = new CurveArray();
        if (majorAxis == "width")
        {
            if (width <= height) throw new CommandResultException(ErrorCodes.InvalidParam, "Oval major_axis=width requires width greater than height.");
            var radius = height / 2; var centerOffset = (width - height) / 2;
            loop.Append(Line.CreateBound(At(-centerOffset, radius), At(centerOffset, radius)));
            loop.Append(Arc.Create(At(centerOffset, radius), At(centerOffset, -radius), At(centerOffset + radius, 0)));
            loop.Append(Line.CreateBound(At(centerOffset, -radius), At(-centerOffset, -radius)));
            loop.Append(Arc.Create(At(-centerOffset, -radius), At(-centerOffset, radius), At(-centerOffset - radius, 0)));
        }
        else if (majorAxis == "height")
        {
            if (height <= width) throw new CommandResultException(ErrorCodes.InvalidParam, "Oval major_axis=height requires height greater than width.");
            var radius = width / 2; var centerOffset = (height - width) / 2;
            loop.Append(Line.CreateBound(At(radius, -centerOffset), At(radius, centerOffset)));
            loop.Append(Arc.Create(At(radius, centerOffset), At(-radius, centerOffset), At(0, centerOffset + radius)));
            loop.Append(Line.CreateBound(At(-radius, centerOffset), At(-radius, -centerOffset)));
            loop.Append(Arc.Create(At(-radius, -centerOffset), At(radius, -centerOffset), At(0, -centerOffset - radius)));
        }
        else throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Oval profile major_axis must be width or height.");
        var result = new CurveArrArray(); result.Append(loop); return result;
    }
    private static XYZ ProfilePoint(string plane, XYZ origin, double u, double v) => plane switch
    {
        "xy" => origin + new XYZ(u, v, 0),
        "xz" => origin + new XYZ(u, 0, v),
        "yz" => origin + new XYZ(0, u, v),
        _ => throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown profile plane " + plane + ".")
    };

    private static JArray CreatePresentationSubcategories(Document family, FamilyBlueprintSpec spec)
    {
        var result = new JArray();
        foreach (var contract in (spec.Blueprint["presentation_subcategories"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var key = contract.Value<string>("key") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Presentation subcategory key is missing.");
            var subcategory = ApplyPresentationSubcategory(family, contract);
            var snapshot = PresentationSubcategorySnapshot(family, subcategory);
            VerifyPresentationSubcategorySnapshot(contract, snapshot, "Created presentation subcategory " + key);
            snapshot["key"] = key; snapshot["verified"] = true; result.Add(snapshot);
        }
        return result;
    }

    private static Category ApplyPresentationSubcategory(Document family, JObject contract)
    {
        var category = family.OwnerFamily?.FamilyCategory ?? throw new CommandResultException(ErrorCodes.TemplateInvalid, "Family category is missing for presentation subcategory.");
        var name = contract.Value<string>("name") ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Presentation subcategory name is missing.");
        if (!name.StartsWith("DSCons ", StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Presentation subcategory name must start with DSCons.");
        Category subcategory;
        if (category.SubCategories.Contains(name)) subcategory = category.SubCategories.get_Item(name);
        else
        {
            if (!category.CanAddSubcategory) throw new CommandResultException(ErrorCodes.Unsupported, "The selected template cannot add Family presentation subcategories.");
            subcategory = family.Settings.Categories.NewSubcategory(category, name);
        }
        var color = contract["color_rgb"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Presentation subcategory color_rgb is missing.");
        subcategory.LineColor = new Color((byte)color.Value<int>("r"), (byte)color.Value<int>("g"), (byte)color.Value<int>("b"));
        subcategory.SetLineWeight(contract.Value<int>("projection_line_weight"), GraphicsStyleType.Projection);
        var supportsCutGraphics = SupportsGraphicsStyle(subcategory, GraphicsStyleType.Cut);
        if (supportsCutGraphics && contract.Value<int?>("cut_line_weight") is int cutLineWeight) subcategory.SetLineWeight(cutLineWeight, GraphicsStyleType.Cut);
        ApplySubcategoryLinePattern(family, subcategory, contract.Value<string>("projection_line_pattern_name"), GraphicsStyleType.Projection, "projection");
        if (supportsCutGraphics) ApplySubcategoryLinePattern(family, subcategory, contract.Value<string>("cut_line_pattern_name"), GraphicsStyleType.Cut, "cut");
        return subcategory;
    }

    private static bool SupportsGraphicsStyle(Category subcategory, GraphicsStyleType styleType)
    {
        try { return subcategory.GetGraphicsStyle(styleType) != null; }
        catch { return false; }
    }

    private static void ApplySubcategoryLinePattern(Document family, Category subcategory, string? patternName, GraphicsStyleType styleType, string label)
    {
        if (string.IsNullOrWhiteSpace(patternName)) return;
        if (string.Equals(patternName, "Solid", StringComparison.OrdinalIgnoreCase) || string.Equals(patternName, "<Solid>", StringComparison.OrdinalIgnoreCase))
        {
            subcategory.SetLinePatternId(LinePatternElement.GetSolidPatternId(), styleType);
            return;
        }
        var pattern = new FilteredElementCollector(family).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>().SingleOrDefault(item => string.Equals(item.Name, patternName, StringComparison.Ordinal))
            ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Presentation subcategory " + label + " line pattern is missing from the selected Autodesk template: " + patternName + ".");
        subcategory.SetLinePatternId(pattern.Id, styleType);
    }

    internal static JObject PresentationSubcategorySnapshot(Document family, Category subcategory)
    {
        string? PatternName(GraphicsStyleType styleType)
        {
            try
            {
                var id = subcategory.GetLinePatternId(styleType);
                if (id == null || id == ElementId.InvalidElementId) return null;
                return id.Val() == LinePatternElement.GetSolidPatternId().Val() ? "Solid" : (family.GetElement(id) as LinePatternElement)?.Name;
            }
            catch { return null; }
        }
        int? LineWeight(GraphicsStyleType styleType)
        {
            try { return subcategory.GetLineWeight(styleType); }
            catch { return null; }
        }
        var color = subcategory.LineColor; var supportsCutGraphics = SupportsGraphicsStyle(subcategory, GraphicsStyleType.Cut);
        return new JObject
        {
            ["id"] = subcategory.Id.Val(), ["name"] = subcategory.Name,
            ["color_rgb"] = new JObject { ["r"] = color.Red, ["g"] = color.Green, ["b"] = color.Blue },
            ["projection_line_weight"] = LineWeight(GraphicsStyleType.Projection), ["cut_graphics_supported"] = supportsCutGraphics,
            ["cut_line_weight"] = supportsCutGraphics ? LineWeight(GraphicsStyleType.Cut) : null,
            ["projection_line_pattern_name"] = PatternName(GraphicsStyleType.Projection), ["cut_line_pattern_name"] = supportsCutGraphics ? PatternName(GraphicsStyleType.Cut) : null
        };
    }

    private static void VerifyPresentationSubcategorySnapshot(JObject contract, JObject observed, string label)
    {
        if (!string.Equals(contract.Value<string>("name"), observed.Value<string>("name"), StringComparison.Ordinal) || !JToken.DeepEquals(contract["color_rgb"], observed["color_rgb"])
            || observed.Value<int?>("projection_line_weight") != contract.Value<int?>("projection_line_weight"))
            throw new CommandResultException(ErrorCodes.VerificationFailed, label + " graphics did not read back.");
        foreach (var property in new[] { "projection_line_pattern_name" })
            if (contract[property] != null && !JToken.DeepEquals(contract[property], observed[property]))
                throw new CommandResultException(ErrorCodes.VerificationFailed, label + " did not read back " + property + ".");
        if (observed.Value<bool?>("cut_graphics_supported") != false)
            foreach (var property in new[] { "cut_line_weight", "cut_line_pattern_name" })
                if (contract[property] != null && !JToken.DeepEquals(contract[property], observed[property]))
                    throw new CommandResultException(ErrorCodes.VerificationFailed, label + " did not read back " + property + ".");
    }

    private static JObject? PresentationSubcategoryContract(FamilyBlueprintSpec spec, JObject declaration)
    {
        if (declaration.Value<string>("subcategory_key") is not string key) return null;
        return (spec.Blueprint["presentation_subcategories"] as JArray ?? new JArray()).OfType<JObject>().SingleOrDefault(item => string.Equals(item.Value<string>("key"), key, StringComparison.Ordinal))
            ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Declaration references unknown presentation subcategory " + key + ".");
    }

    private static string PresentationSubcategoryName(FamilyBlueprintSpec spec, JObject declaration, string fallback) => PresentationSubcategoryContract(spec, declaration)?.Value<string>("name") ?? fallback;

    private static string PartPresentationSubcategoryName(FamilyBlueprintSpec spec, JObject part, string fallbackKey)
    {
        var role = part.Value<string>("role") ?? fallbackKey;
        return PresentationSubcategoryName(spec, part, "DSCons " + spec.Lod.Replace("_", string.Empty) + " " + role);
    }

    private static Category ResolvePresentationSubcategory(Document family, FamilyBlueprintSpec spec, JObject declaration, string fallback)
    {
        var category = family.OwnerFamily?.FamilyCategory ?? throw new CommandResultException(ErrorCodes.TemplateInvalid, "Family category is missing.");
        var contract = PresentationSubcategoryContract(spec, declaration);
        var name = contract?.Value<string>("name") ?? fallback;
        if (category.SubCategories.Contains(name)) return category.SubCategories.get_Item(name);
        if (contract != null) throw new CommandResultException(ErrorCodes.VerificationFailed, "Declared presentation subcategory was not created: " + name + ".");
        if (!category.CanAddSubcategory) throw new CommandResultException(ErrorCodes.Unsupported, "The selected template cannot add the default Family presentation subcategory " + name + ".");
        return family.Settings.Categories.NewSubcategory(category, name);
    }

    private static void SetRole(Document family, FamilyBlueprintSpec spec, GenericForm form, JObject declaration, string role, JObject visibility)
    {
        var fallback = "DSCons " + spec.Lod.Replace("_", string.Empty) + " " + role;
        form.Subcategory = ResolvePresentationSubcategory(family, spec, declaration, fallback);
        try { form.SetVisibility(Visibility(visibility, FamilyElementVisibilityType.Model)); }
        catch (Exception exception) { throw new CommandResultException(ErrorCodes.VerificationFailed, "Part " + role + " model visibility could not be applied: " + exception.Message); }
    }

    private static void SetCoordinationZoneRole(Document family, GenericForm form, string subcategoryName, JObject visibility)
    {
        var category = family.OwnerFamily?.FamilyCategory ?? throw new CommandResultException(ErrorCodes.TemplateInvalid, "Family category is missing.");
        var subcategory = category.SubCategories.Contains(subcategoryName) ? category.SubCategories.get_Item(subcategoryName) : family.Settings.Categories.NewSubcategory(category, subcategoryName);
        form.Subcategory = subcategory; form.SetVisibility(Visibility(visibility, FamilyElementVisibilityType.Model));
    }

    private static FamilyElementVisibility Visibility(JObject contract, FamilyElementVisibilityType type)
    {
        var visibility = new FamilyElementVisibility(type)
        {
            IsShownInCoarse = contract.Value<bool?>("coarse") ?? true,
            IsShownInMedium = contract.Value<bool?>("medium") ?? true,
            IsShownInFine = contract.Value<bool?>("fine") ?? true,
            IsShownInFrontBack = contract.Value<bool?>("front_back") ?? true,
            IsShownInLeftRight = contract.Value<bool?>("left_right") ?? true,
            IsShownInPlanRCPCut = contract.Value<bool?>("plan_rcp") ?? true
        };
        // Revit exposes Only When Cut only for ViewSpecific visibility.  It
        // throws when the property is assigned on a Model form, even when the
        // requested value is false, so do not write an inapplicable property.
        if (type == FamilyElementVisibilityType.ViewSpecific)
            visibility.IsShownOnlyWhenCut = contract.Value<bool?>("only_when_cut") ?? false;
        else if (contract.Value<bool?>("only_when_cut") == true)
            throw new CommandResultException(ErrorCodes.EvidenceInvalid, "only_when_cut is valid only for view-specific Family elements.");
        return visibility;
    }
    private static JObject VisibilitySnapshot(FamilyElementVisibility visibility) => new()
    {
        ["coarse"] = visibility.IsShownInCoarse, ["medium"] = visibility.IsShownInMedium, ["fine"] = visibility.IsShownInFine,
        ["front_back"] = visibility.IsShownInFrontBack, ["left_right"] = visibility.IsShownInLeftRight,
        ["plan_rcp"] = visibility.IsShownInPlanRCPCut, ["only_when_cut"] = visibility.IsShownOnlyWhenCut
    };

    private static JObject? AssociateVisibility(FamilyManager manager, Element element, JObject contract, Dictionary<string, FamilyParameter> parameters, string label)
    {
        if (contract.Value<string>("visibility_parameter") is not string parameterKey) return null;
        var elementParameter = element.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM)
            ?? throw new CommandResultException(ErrorCodes.VerificationFailed, label + " does not expose Revit's Visible parameter.");
        var familyParameter = RequireParameter(parameters, parameterKey);
        if (!manager.CanElementParameterBeAssociated(elementParameter)) throw new CommandResultException(ErrorCodes.VerificationFailed, label + " Visible parameter cannot be associated to " + familyParameter.Definition.Name + ".");
        manager.AssociateElementParameterToFamilyParameter(elementParameter, familyParameter);
        var associated = manager.GetAssociatedFamilyParameter(elementParameter);
        if (associated?.Id != familyParameter.Id) throw new CommandResultException(ErrorCodes.VerificationFailed, label + " visibility association did not read back.");
        return new JObject { ["parameter_key"] = parameterKey, ["name"] = associated.Definition.Name, ["is_instance"] = associated.IsInstance, ["verified"] = true };
    }

    private static PlanarFace Face(GenericForm form, XYZ normal, string key)
    {
        // A just-created extrusion may not expose reference-bearing faces until
        // the Family document regenerates.  Connector creation must bind a
        // real planar face, never a geometric approximation or fallback point.
        form.Document.Regenerate();
        var face = form.get_Geometry(new Options { ComputeReferences = true, IncludeNonVisibleObjects = true })
            .OfType<Solid>().SelectMany(solid => solid.Faces.Cast<Face>()).OfType<PlanarFace>()
            .FirstOrDefault(candidate => candidate.FaceNormal.DotProduct(normal) > .999);
        return face ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Part " + key + " has no stable host face for the declared connector.");
    }
    private static XYZ PathEndpointTangent(JObject part, bool start)
    {
        var path = part["path"] as JObject ?? throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Path connector host has no path declaration.");
        if (part.Value<string>("primitive") != "sweep") throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Path connector requires a sweep host.");
        if (path.Value<string>("kind") == "arc")
        {
            if (start) return XYZ.BasisX;
            var angle = Radians(path.Value<double>("sweep_angle_degrees")); var turn = path.Value<string>("turn_direction") == "clockwise" ? -1.0 : 1.0;
            var left = path.Value<string>("plane") switch { "xy" => XYZ.BasisY, "xz" => XYZ.BasisZ, _ => throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Arc-path connector supports xy or xz paths only.") };
            return XYZ.BasisX.Multiply(Math.Cos(angle)).Add(left.Multiply(turn * Math.Sin(angle))).Normalize();
        }
        if (path.Value<string>("kind") == "offset") return XYZ.BasisX;
        var points = (path["points_mm"] as JArray ?? new JArray()).Select((item, index) => Point(item, "path endpoint point " + index)).ToList();
        if (points.Count < 2) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Line/polyline path connector requires at least two path points.");
        return start ? points[1].Subtract(points[0]).Normalize() : points[points.Count - 1].Subtract(points[points.Count - 2]).Normalize();
    }
    private static XYZ ConnectorFaceNormal(JObject part, string hostFace)
    {
        if (hostFace == "path_start") return -PathEndpointTangent(part, true);
        if (hostFace == "path_end") return PathEndpointTangent(part, false);
        if (hostFace == "start") return -AxisVector(part);
        if (hostFace == "end") return AxisVector(part);
        return FaceNormal(hostFace);
    }
    private static XYZ FaceNormal(string hostFace) => hostFace switch { "start" => -XYZ.BasisX, "end" => XYZ.BasisX, "positive_y" => XYZ.BasisY, "negative_y" => -XYZ.BasisY, "positive_z" => XYZ.BasisZ, "negative_z" => -XYZ.BasisZ, _ => throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown connector host_face: " + hostFace + ".") };
    private static ConnectorProfileType ParseProfile(string? value) => value switch { "round" => ConnectorProfileType.Round, "rectangular" => ConnectorProfileType.Rectangular, "oval" => ConnectorProfileType.Oval, _ => ConnectorProfileType.Invalid };
    private static DuctSystemType ParseDuct(string value) => Enum.TryParse(value, true, out DuctSystemType result) ? result : throw new CommandResultException(ErrorCodes.InvalidParam, "Invalid duct system classification: " + value + ".");
    private static PipeSystemType ParsePipe(string value) => Enum.TryParse(value, true, out PipeSystemType result) ? result : throw new CommandResultException(ErrorCodes.InvalidParam, "Invalid pipe system classification: " + value + ".");
    private static ElectricalSystemType ParseElectrical(string value) => Enum.TryParse(value, true, out ElectricalSystemType result) ? result : throw new CommandResultException(ErrorCodes.InvalidParam, "Invalid electrical system classification: " + value + ".");

    private static void SetRole(ConnectorElement connector, string role)
    {
        var parameter = connector.get_Parameter(BuiltInParameter.RBS_CONNECTOR_DESCRIPTION); if (parameter == null || parameter.IsReadOnly || !parameter.Set(role)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Connector role could not be written.");
    }
    private static void EnsureOutward(ConnectorElement connector, XYZ normal)
    {
        var direction = connector.GetType().GetProperty("Direction")?.GetValue(connector) as XYZ; if (direction != null && direction.DotProduct(normal) < .999) connector.FlipDirection();
        direction = connector.GetType().GetProperty("Direction")?.GetValue(connector) as XYZ; if (direction != null && direction.DotProduct(normal) < .999) throw new CommandResultException(ErrorCodes.VerificationFailed, "Connector direction is not outward.");
    }
    private static void Associate(FamilyManager manager, Parameter? elementParameter, FamilyParameter familyParameter)
    {
        if (elementParameter == null || !manager.CanElementParameterBeAssociated(elementParameter)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected association for " + familyParameter.Definition.Name + "."); manager.AssociateElementParameterToFamilyParameter(elementParameter, familyParameter);
    }
    private sealed class FamilyCompileFailuresPreprocessor : IFailuresPreprocessor
    {
        private readonly Document document;
        private readonly List<string> messages = new();
        public FamilyCompileFailuresPreprocessor(Document document) { this.document = document; }
        public string Summary => messages.Count == 0 ? "none" : string.Join(" | ", messages.Distinct(StringComparer.Ordinal));
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var hasError = false;
            foreach (var failure in failuresAccessor.GetFailureMessages())
            {
                var elementDetails = string.Join(",", failure.GetFailingElementIds().Select(Describe));
                messages.Add("failure_id=" + failure.GetFailureDefinitionId().Guid + "; severity=" + failure.GetSeverity() + "; description=" + failure.GetDescriptionText() + "; elements=" + (string.IsNullOrWhiteSpace(elementDetails) ? "none" : elementDetails));
                if (failure.GetSeverity() == FailureSeverity.Warning) failuresAccessor.DeleteWarning(failure);
                else hasError = true;
            }
            return hasError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
        private string Describe(ElementId id)
        {
            var element = document.GetElement(id);
            if (element is Dimension dimension)
                return id.Val() + ":Dimension(shape=" + dimension.DimensionShape + ",style=" + (dimension.DimensionType?.StyleType.ToString() ?? "missing") + ",label=" + (dimension.FamilyLabel?.Definition.Name ?? "none") + ")";
            return id.Val() + ":" + (element?.GetType().Name ?? "missing");
        }
    }
    private static void LabelDiameter(Document family, Extrusion form, int loopIndex, FamilyParameter parameter, string part, ExtrusionFrameInfo frame, XYZ? profileOrigin = null)
    {
        // Revit 2023 accepts radial/diameter Dimension creation for a sketch
        // arc but rejects its FamilyLabel later during document evaluation.
        // Do not retain that invalid label.  Instead, lock the two circular
        // sketch arcs to centre/end-point datum planes, then label the native
        // top-to-bottom linear dimension with the declared Diameter parameter.
        // Unlike the old plane-only fallback, these datums are constrained to
        // the real arc centres and endpoints, so Diameter flexes the geometry.
        family.Regenerate();
        var loop = form.Sketch.Profile.Cast<CurveArray>().ElementAt(loopIndex);
        var arcs = loop.Cast<Curve>().Select(curve => curve.Reference == null ? null : family.GetElement(curve.Reference.ElementId) as ModelCurve)
            .Where(curve => curve?.GeometryCurve is Arc).Cast<ModelCurve>().ToList();
        if (arcs.Count != 2) throw ConstraintFailure("profile_diameter_plane_alignment", part, parameter.Definition.Name, Array.Empty<Reference?>(), "A circular extrusion profile must expose exactly two ModelArc segments.");
        var firstArc = arcs[0].GeometryCurve as Arc ?? throw ConstraintFailure("profile_diameter_plane_alignment", part, parameter.Definition.Name, Array.Empty<Reference?>(), "The first circular profile curve is not an Arc.");
        var radius = firstArc.Radius; var tolerance = Mm(.5);
        if (radius <= 1e-9 || arcs.Select(curve => (curve.GeometryCurve as Arc)!).Any(arc => Math.Abs(arc.Radius - radius) > tolerance || arc.Center.DistanceTo(firstArc.Center) > tolerance))
            throw ConstraintFailure("profile_diameter_plane_alignment", part, parameter.Definition.Name, Array.Empty<Reference?>(), "The circular profile arcs do not retain one center and radius.");
        var view = ViewForNormal(family, frame.Axis, frame.Height, "linear diameter extrusion profile");
        var center = firstArc.Center;
        var widthCenter = CreateDatumPlane(family, view, center, frame.Height, frame.Axis, "profile_diameter_center_width", part, parameter.Definition.Name);
        var heightCenter = CreateDatumPlane(family, view, center, frame.Width, frame.Axis, "profile_diameter_center_height", part, parameter.Definition.Name);
        foreach (var modelArc in arcs)
        {
            var centerReference = RequireCenterPointReference(modelArc, "profile_diameter_center", part, parameter.Definition.Name);
            AlignToDatum(family, view, centerReference, widthCenter, "profile_diameter_center_width", part, parameter.Definition.Name);
            AlignToDatum(family, view, centerReference, heightCenter, "profile_diameter_center_height", part, parameter.Definition.Name);
        }
        var endpoints = new[] { 0, 1 }.Select(index => new { Index = index, Point = firstArc.GetEndPoint(index) }).OrderBy(item => item.Point.Subtract(center).DotProduct(frame.Height)).ToList();
        if (endpoints.Count != 2 || Math.Abs(endpoints[0].Point.Subtract(center).DotProduct(frame.Height) + radius) > tolerance || Math.Abs(endpoints[1].Point.Subtract(center).DotProduct(frame.Height) - radius) > tolerance)
            throw ConstraintFailure("profile_diameter_plane_alignment", part, parameter.Definition.Name, Array.Empty<Reference?>(), "The circular profile does not expose the expected opposing height-axis endpoints.");
        var bottom = CreateDatumPlane(family, view, endpoints[0].Point, frame.Width, frame.Axis, "profile_diameter_bottom", part, parameter.Definition.Name);
        var top = CreateDatumPlane(family, view, endpoints[1].Point, frame.Width, frame.Axis, "profile_diameter_top", part, parameter.Definition.Name);
        Reference EndpointReference(int index, string role) => firstArc.GetEndPointReference(index) ?? throw ConstraintFailure(role, part, parameter.Definition.Name, Array.Empty<Reference?>(), "The circular profile endpoint has no stable Revit reference.");
        AlignToDatum(family, view, EndpointReference(endpoints[0].Index, "profile_diameter_bottom"), bottom, "profile_diameter_bottom", part, parameter.Definition.Name);
        AlignToDatum(family, view, EndpointReference(endpoints[1].Index, "profile_diameter_top"), top, "profile_diameter_top", part, parameter.Definition.Name);
        var witnessOffset = radius + Mm(50);
        var dimension = CreateStableLinearDimension(family, view,
            Line.CreateBound(center.Add(frame.Width.Multiply(witnessOffset)).Add(frame.Height.Multiply(-radius)), center.Add(frame.Width.Multiply(witnessOffset)).Add(frame.Height.Multiply(radius))),
            new Reference(bottom), new Reference(top), "profile_diameter_plane_alignment", part, parameter.Definition.Name);
        dimension.FamilyLabel = parameter;
        family.Regenerate();
        if (dimension.FamilyLabel?.Id != parameter.Id)
            throw ConstraintFailure("profile_diameter_plane_alignment", part, parameter.Definition.Name, new[] { new Reference(bottom), new Reference(top) }, "The plane-to-plane Diameter FamilyLabel did not read back after regeneration.");
        var expectedRadius = CurrentLength(family.FamilyManager, parameter) / 2.0;
        if (arcs.Select(item => item.GeometryCurve as Arc).Any(arc => arc == null || Math.Abs(arc.Radius - expectedRadius) > tolerance))
            throw ConstraintFailure("profile_diameter_plane_alignment", part, parameter.Definition.Name, Array.Empty<Reference?>(), "The plane-to-plane Diameter label did not drive both circular profile arcs to the declared diameter.");
    }
    private static void LabelRectangle(Document family, Extrusion form, FamilyParameter width, FamilyParameter height, double initialWidth, double initialHeight, string part, ExtrusionFrameInfo frame, XYZ? profileOrigin = null)
    {
        family.Regenerate();
        var origin = profileOrigin ?? XYZ.Zero;
        var widthView = frame.WidthViewPlane == null ? ViewForNormal(family, frame.Height, frame.Axis, "rectangle extrusion width") : ViewForPlane(family, frame.WidthViewPlane, "rectangle extrusion width");
        var heightView = frame.HeightViewPlane == null ? ViewForNormal(family, frame.Width, frame.Axis, "rectangle extrusion height") : ViewForPlane(family, frame.HeightViewPlane, "rectangle extrusion height");
        var extent = Math.Max(Math.Max(initialWidth, initialHeight), Mm(500));
        ReferencePlane PlaneAt(XYZ offsetAxis, double offset, XYZ cutAxis, View view) => family.FamilyCreate.NewReferencePlane2(
            origin.Add(frame.Axis.Multiply(-extent)).Add(offsetAxis.Multiply(offset)), origin.Add(frame.Axis.Multiply(extent)).Add(offsetAxis.Multiply(offset)), origin.Add(cutAxis.Multiply(extent)).Add(offsetAxis.Multiply(offset)), view);
        var left = PlaneAt(frame.Width, -initialWidth / 2, frame.Height, widthView);
        var right = PlaneAt(frame.Width, initialWidth / 2, frame.Height, widthView);
        var bottom = PlaneAt(frame.Height, -initialHeight / 2, frame.Width, heightView);
        var top = PlaneAt(frame.Height, initialHeight / 2, frame.Width, heightView);
        foreach (var plane in new[] { left, right, bottom, top }) plane.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME)?.Set(ReferenceParameterValue(FamilyInstanceReferenceType.StrongReference));
        AlignToDatum(family, widthView, FormFace(form, -frame.Width, part), left, "extrusion_width_left", part, width.Definition.Name); AlignToDatum(family, widthView, FormFace(form, frame.Width, part), right, "extrusion_width_right", part, width.Definition.Name);
        AlignToDatum(family, heightView, FormFace(form, -frame.Height, part), bottom, "extrusion_height_bottom", part, height.Definition.Name); AlignToDatum(family, heightView, FormFace(form, frame.Height, part), top, "extrusion_height_top", part, height.Definition.Name);
        var widthDimension = CreateStableLinearDimension(family, widthView, Line.CreateBound(origin.Add(frame.Width.Multiply(-initialWidth / 2)), origin.Add(frame.Width.Multiply(initialWidth / 2))), new Reference(left), new Reference(right), "extrusion_width", part, width.Definition.Name); widthDimension.FamilyLabel = width;
        var heightDimension = CreateStableLinearDimension(family, heightView, Line.CreateBound(origin.Add(frame.Height.Multiply(-initialHeight / 2)), origin.Add(frame.Height.Multiply(initialHeight / 2))), new Reference(bottom), new Reference(top), "extrusion_height", part, height.Definition.Name); heightDimension.FamilyLabel = height;
    }
    private static ExtrusionFrameInfo ExtrusionFrame(string axisKey) => axisKey switch
    {
        "x" => new ExtrusionFrameInfo { Axis = XYZ.BasisX, Width = XYZ.BasisY, Height = XYZ.BasisZ, ProfilePlane = "yz", WidthViewPlane = "xy", HeightViewPlane = "xz" },
        "y" => new ExtrusionFrameInfo { Axis = XYZ.BasisY, Width = -XYZ.BasisX, Height = XYZ.BasisZ, ProfilePlane = "xz", WidthViewPlane = "xy", HeightViewPlane = "yz" },
        "z" => new ExtrusionFrameInfo { Axis = XYZ.BasisZ, Width = XYZ.BasisX, Height = XYZ.BasisY, ProfilePlane = "xy", WidthViewPlane = "xz", HeightViewPlane = "yz" },
        _ => throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Unknown extrusion axis " + axisKey + ".")
    };
    private static ExtrusionFrameInfo ExtrusionFrame(JObject part, string fallbackAxis)
    {
        if (part["axis_direction"] is not JObject vector) return ExtrusionFrame(part.Value<string>("axis") ?? fallbackAxis);
        var axis = new XYZ(vector.Value<double>("x"), vector.Value<double>("y"), vector.Value<double>("z"));
        if (axis.GetLength() < 1e-9) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Extrusion axis_direction must be non-zero.");
        axis = axis.Normalize(); var guide = Math.Abs(axis.Z) < .9 ? XYZ.BasisZ : XYZ.BasisY;
        var width = guide.CrossProduct(axis).Normalize(); var height = axis.CrossProduct(width).Normalize();
        return new ExtrusionFrameInfo { Axis = axis, Width = width, Height = height };
    }
    private static XYZ AxisVector(JObject part) => ExtrusionFrame(part, part.Value<string>("axis") ?? "x").Axis;
    private static Reference FormFace(Extrusion form, XYZ normal, string part) => form.get_Geometry(new Options { ComputeReferences = true }).OfType<Solid>().SelectMany(solid => solid.Faces.Cast<Face>()).OfType<PlanarFace>().FirstOrDefault(candidate => candidate.FaceNormal.DotProduct(normal) > .999)?.Reference ?? throw new CommandResultException(ErrorCodes.VerificationFailed, part + " has no stable face for alignment.");
    private static bool BlueprintReferencesParameter(FamilyBlueprintSpec spec, string parameterKey) =>
        (spec.Blueprint["parts"] as JArray ?? new JArray()).OfType<JObject>().Any(part => PartReferencesParameter(part, parameterKey)) ||
        (spec.Blueprint["coordination_zones"] as JArray ?? new JArray()).OfType<JObject>().Any(zone => CoordinationZoneReferencesParameter(zone, parameterKey)) ||
        (spec.Blueprint["connectors"] as JArray ?? new JArray()).OfType<JObject>().Any(connector => ConnectorReferencesParameter(connector, parameterKey)) ||
        (spec.Blueprint["dimensions"] as JArray ?? new JArray()).OfType<JObject>().Any(dimension => ParameterReference(dimension, parameterKey, "parameter_key")) ||
        ModelLineEndpointBindingsReferenceParameter(spec, parameterKey) ||
        DetailLineEndpointBindingsReferenceParameter(spec, parameterKey) ||
        (spec.Blueprint["arrays"] as JArray ?? new JArray()).OfType<JObject>().Any(array => ParameterReference(array, parameterKey, "count_parameter")) ||
        (spec.Blueprint["nested_components"] as JArray ?? new JArray()).OfType<JObject>().Any(component => (component["parameter_map"] as JObject ?? new JObject()).Properties().Any(property => string.Equals(property.Value.Value<string>(), parameterKey, StringComparison.Ordinal)));

    private static bool BlueprintReferencesParameterOutsideNestedInterface(FamilyBlueprintSpec spec, string parameterKey) =>
        (spec.Blueprint["parts"] as JArray ?? new JArray()).OfType<JObject>().Any(part => PartReferencesParameter(part, parameterKey)) ||
        (spec.Blueprint["coordination_zones"] as JArray ?? new JArray()).OfType<JObject>().Any(zone => CoordinationZoneReferencesParameter(zone, parameterKey)) ||
        (spec.Blueprint["connectors"] as JArray ?? new JArray()).OfType<JObject>().Any(connector => ConnectorReferencesParameter(connector, parameterKey)) ||
        (spec.Blueprint["dimensions"] as JArray ?? new JArray()).OfType<JObject>().Any(dimension => ParameterReference(dimension, parameterKey, "parameter_key")) ||
        ModelLineEndpointBindingsReferenceParameter(spec, parameterKey) ||
        DetailLineEndpointBindingsReferenceParameter(spec, parameterKey) ||
        (spec.Blueprint["arrays"] as JArray ?? new JArray()).OfType<JObject>().Any(array => ParameterReference(array, parameterKey, "count_parameter"));

    private static bool IsSharedNestedInstanceParameter(FamilyBlueprintSpec spec, string parameterKey) =>
        (spec.Blueprint["nested_components"] as JArray ?? new JArray()).OfType<JObject>().Any(component => string.Equals(component.Value<string>("sharing") ?? "embedded", "shared", StringComparison.Ordinal)
            && (component["parameter_map"] as JObject ?? new JObject()).Properties().Any(property => string.Equals(property.Value.Value<string>(), parameterKey, StringComparison.Ordinal)));

    private static bool ModelLineEndpointBindingsReferenceParameter(FamilyBlueprintSpec spec, string parameterKey)
    {
        var referenceKeys = new HashSet<string>(ReferenceGraphReferenceKeys(spec, parameterKey), StringComparer.Ordinal);
        return referenceKeys.Count != 0 && (spec.Blueprint["model_lines"] as JArray ?? new JArray()).OfType<JObject>()
            .Any(line => (line["endpoint_bindings"] as JArray ?? new JArray()).OfType<JObject>().Any(binding => referenceKeys.Contains(binding.Value<string>("reference_plane_key") ?? string.Empty)));
    }

    private static bool DetailLineEndpointBindingsReferenceParameter(FamilyBlueprintSpec spec, string parameterKey)
    {
        var referenceKeys = new HashSet<string>(ReferenceGraphReferenceKeys(spec, parameterKey), StringComparer.Ordinal);
        return referenceKeys.Count != 0 && (spec.Blueprint["detail_lines"] as JArray ?? new JArray()).OfType<JObject>()
            .Any(line => (line["endpoint_bindings"] as JArray ?? new JArray()).OfType<JObject>().Any(binding => referenceKeys.Contains(binding.Value<string>("reference_plane_key") ?? string.Empty)));
    }

    private static IEnumerable<string> ReferenceGraphReferenceKeys(FamilyBlueprintSpec spec, string parameterKey)
    {
        var dimensions = (spec.Blueprint["dimensions"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var referenceKeys = new HashSet<string>(dimensions
            .Where(dimension => (dimension.Value<string>("kind") ?? "linear") == "linear" && ParameterReference(dimension, parameterKey, "parameter_key"))
            .SelectMany(dimension => (dimension["reference_keys"] as JArray ?? new JArray()).Values<string>().Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!)), StringComparer.Ordinal);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var equality in dimensions.Where(dimension => dimension.Value<bool?>("equality") == true))
            {
                var equalReferences = (equality["reference_keys"] as JArray ?? new JArray()).Values<string>().Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToList();
                if (!equalReferences.Any(referenceKeys.Contains)) continue;
                foreach (var referenceKey in equalReferences) if (referenceKeys.Add(referenceKey)) changed = true;
            }
        }
        return referenceKeys;
    }

    private static IEnumerable<string> ReferenceGraphPartKeys(FamilyBlueprintSpec spec, string parameterKey)
    {
        var dimensions = (spec.Blueprint["dimensions"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        var referenceKeys = new HashSet<string>(ReferenceGraphReferenceKeys(spec, parameterKey), StringComparer.Ordinal);
        var partKeys = new HashSet<string>((spec.Blueprint["alignments"] as JArray ?? new JArray()).OfType<JObject>()
            .Where(alignment => referenceKeys.Contains(alignment.Value<string>("reference_plane_key") ?? string.Empty))
            .Select(alignment => alignment.Value<string>("part_key") ?? string.Empty)
            .Where(partKey => !string.IsNullOrWhiteSpace(partKey)), StringComparer.Ordinal);
        foreach (var radial in dimensions.Where(dimension => (dimension.Value<string>("kind") ?? "linear") == "radial" && ParameterReference(dimension, parameterKey, "parameter_key")))
        {
            var partKey = radial.Value<string>("part_key"); if (!string.IsNullOrWhiteSpace(partKey)) partKeys.Add(partKey!);
        }
        foreach (var angular in dimensions.Where(dimension => (dimension.Value<string>("kind") ?? "linear") == "angular" && ParameterReference(dimension, parameterKey, "parameter_key")))
        {
            var dimensionKey = angular.Value<string>("key") ?? string.Empty;
            var drivenReference = (angular["reference_keys"] as JArray ?? new JArray()).Values<string>().Skip(1).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(dimensionKey) || string.IsNullOrWhiteSpace(drivenReference)) continue;
            foreach (var part in (spec.Blueprint["parts"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var path = part["path"] as JObject;
                if (part.Value<string>("primitive") == "sweep" && path?.Value<string>("kind") == "reference_line"
                    && string.Equals(path.Value<string>("angular_dimension_key"), dimensionKey, StringComparison.Ordinal)
                    && string.Equals(path.Value<string>("reference_line_key"), drivenReference, StringComparison.Ordinal))
                {
                    var partKey = part.Value<string>("key"); if (!string.IsNullOrWhiteSpace(partKey)) partKeys.Add(partKey!);
                }
            }
        }
        return partKeys;
    }
    private static bool PartReferencesParameter(JObject part, string parameterKey) =>
        ParameterReference(part, parameterKey, "depth_parameter") || PathReferencesParameter(part["path"] as JObject, parameterKey) || ProfileReferencesParameter(part["profile"] as JObject, parameterKey) || ProfileReferencesParameter(part["end_profile"] as JObject, parameterKey);
    private static bool PathReferencesParameter(JObject? path, string parameterKey) => path != null &&
        new[] { "radius_parameter", "lead_in_parameter", "lead_out_parameter", "lateral_offset_parameter", "offset_angle_parameter" }.Any(property => ParameterReference(path, parameterKey, property));
    private static bool CoordinationZoneReferencesParameter(JObject zone, string parameterKey) =>
        new[] { "width_parameter", "height_parameter", "depth_parameter", "diameter_parameter", "length_parameter" }.Any(property => ParameterReference(zone, parameterKey, property));
    private static bool ProfileReferencesParameter(JObject? profile, string parameterKey) => profile != null &&
        new[] { "width_parameter", "height_parameter", "diameter_parameter", "outer_diameter_parameter", "inner_diameter_parameter" }.Any(property => ParameterReference(profile, parameterKey, property));
    private static bool ConnectorReferencesParameter(JObject connector, string parameterKey) =>
        new[] { "diameter_parameter", "width_parameter", "height_parameter", "flow_parameter", "flow_factor_parameter", "voltage_parameter", "apparent_load_parameter", "number_of_poles_parameter", "power_factor_parameter", "balanced_load_parameter", "load_classification_parameter" }.Any(property => ParameterReference(connector, parameterKey, property));
    private static bool ParameterReference(JObject contract, string parameterKey, string property) => string.Equals(contract.Value<string>(property), parameterKey, StringComparison.Ordinal);
    private static double RequiredFlexValue(JObject flexCase, string property, string parameterKey)
    {
        var value = flexCase.Value<double?>(property);
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameter flex case " + parameterKey + "." + property + " must be finite.");
        return value.Value;
    }
    private static void SetFlexValue(FamilyManager manager, FamilyParameter parameter, string dataType, double value, string parameterKey)
    {
        switch (dataType)
        {
            case "length": manager.Set(parameter, Mm(value)); break;
            case "airflow": case "flow": manager.Set(parameter, ToInternal(dataType, value)); break;
            case "angle": manager.Set(parameter, Radians(value)); break;
            case "number": manager.Set(parameter, value); break;
            case "integer": manager.Set(parameter, checked((int)Math.Round(value))); break;
            default: throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameter flex case " + parameterKey + " has unsupported data type " + dataType + ".");
        }
    }
    private static double ReadFlexValue(FamilyType type, FamilyParameter parameter, string dataType, string parameterKey) => dataType switch
    {
        "length" => (type.AsDouble(parameter) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Length parameter " + parameterKey + " has no value.")) * 304.8,
        "airflow" or "flow" => FlowFromInternal(type.AsDouble(parameter) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Flow parameter " + parameterKey + " has no value.")),
        "angle" => (type.AsDouble(parameter) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Angle parameter " + parameterKey + " has no value.")) * 180.0 / Math.PI,
        "number" => type.AsDouble(parameter) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Number parameter " + parameterKey + " has no value."),
        "integer" => type.AsInteger(parameter) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Integer parameter " + parameterKey + " has no value."),
        _ => throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Parameter flex case " + parameterKey + " has unsupported data type " + dataType + ".")
    };
    private static bool FlexValueMatches(double actual, double expected, string dataType)
    {
        if (dataType == "integer") return Math.Abs(actual - expected) < .5;
        var tolerance = dataType is "length" or "airflow" or "flow" ? 1e-4 : dataType == "angle" ? 1e-6 : 1e-8;
        return Math.Abs(actual - expected) <= tolerance * Math.Max(1.0, Math.Abs(expected));
    }
    private static bool SnapshotsVary(IEnumerable<JToken?> snapshots)
    {
        var values = snapshots.Where(item => item != null).Cast<JToken>().ToList();
        return values.Count > 1 && values.Skip(1).Any(item => !JToken.DeepEquals(values[0], item));
    }
    private static JObject CoordinationZoneSnapshot(IReadOnlyDictionary<string, CoordinationZoneResult> zones) => new(zones.Select(item =>
    {
        var form = item.Value.Form; var contract = item.Value.Contract; var bounds = form.get_BoundingBox(null) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Coordination zone " + item.Key + " has no bounds.");
        if (!form.IsSolid) throw new CommandResultException(ErrorCodes.VerificationFailed, "Coordination zone " + item.Key + " must remain a solid coordination form.");
        return new JProperty(item.Key, new JObject
        {
            ["zone_key"] = item.Key, ["purpose"] = contract.Value<string>("purpose"), ["shape"] = contract.Value<string>("shape"), ["axis"] = contract.Value<string>("axis"),
            ["origin_mm"] = contract["origin_mm"]?.DeepClone(), ["role"] = contract.Value<string>("role"), ["subcategory"] = form.Subcategory?.Name,
            ["material_key"] = contract.Value<string>("material_key"), ["material_id"] = form.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.AsElementId().Val(),
            ["visibility"] = VisibilitySnapshot(form.GetVisibility()), ["visibility_parameter"] = item.Value.VisibilityParameter, ["bounds_mm"] = BoundsMm(bounds),
            ["semantic_intent"] = "non_physical_coordination_geometry", ["quantity_exclusion_certified"] = false, ["requires_project_bep_validation"] = true, ["verified"] = true
        });
    }));
    private static JObject PartBounds(IReadOnlyDictionary<string, PartResult> parts) => new(parts.Select(item => new JProperty(item.Key, BoundsMm(item.Value.Form.get_BoundingBox(null) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Part " + item.Key + " has no bounds.")))));
    private static JObject ProfileArcRadii(IReadOnlyDictionary<string, PartResult> parts) => new(parts.Where(item => item.Value.Form is Extrusion).Select(item =>
        new JProperty(item.Key, new JArray(((Extrusion)item.Value.Form).Sketch.Profile.Cast<CurveArray>().SelectMany(loop => loop.Cast<Curve>()).OfType<Arc>().Select(arc => Math.Round(arc.Radius * 304.8, 3))))));
    private static JArray LabelledDimensionSnapshot(Document family, FamilyParameter parameter)
    {
        var result = new JArray();
        foreach (var dimension in new FilteredElementCollector(family).OfClass(typeof(Dimension)).Cast<Dimension>())
        {
            FamilyParameter? label;
            try { label = dimension.FamilyLabel; }
            catch { continue; }
            if (label?.Id != parameter.Id) continue;
            double? valueMm = null;
            try { if (dimension.Value is double value) valueMm = Math.Round(value * 304.8, 3); }
            catch { /* retain the label identity even if the template denies a value read-back. */ }
            result.Add(new JObject { ["dimension_id"] = dimension.Id.Val(), ["family_label"] = label.Definition.Name, ["value_mm"] = valueMm, ["shape"] = dimension.DimensionShape.ToString(), ["reference_count"] = dimension.References?.Size ?? 0 });
        }
        return result;
    }
    private static JObject CoordinationZoneBounds(IReadOnlyDictionary<string, CoordinationZoneResult> zones) => new(zones.Select(item => new JProperty(item.Key, BoundsMm(item.Value.Form.get_BoundingBox(null) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Coordination zone " + item.Key + " has no bounds.")))));
    private static JObject ArraySnapshot(IReadOnlyDictionary<string, ArrayResult> arrays) => new(arrays.Select(item => new JProperty(item.Key, new JObject { ["element_id"] = item.Value.Array.Id.Val(), ["member_count"] = item.Value.Array.NumMembers, ["count_parameter"] = item.Value.Contract.Value<string>("count_parameter"), ["verified"] = item.Value.Array.NumMembers >= 2 })));
    private static JObject NestedSnapshot(IReadOnlyDictionary<string, NestedResult> nested) => new(nested.Select(item =>
    {
        var parameters = new JObject(item.Value.MappedParameters.Select(parameter => new JProperty(parameter.Key, ParameterValue(parameter.Value))));
        var bounds = item.Value.Instance.get_BoundingBox(null);
        return new JProperty(item.Key, new JObject { ["element_id"] = item.Value.Instance.Id.Val(), ["family"] = item.Value.Instance.Symbol.FamilyName, ["type"] = item.Value.Instance.Symbol.Name, ["sharing"] = item.Value.Contract.Value<string>("sharing") ?? "embedded", ["placement"] = item.Value.Placement, ["mapped_parameters"] = parameters, ["mapped_parameter_interfaces"] = item.Value.MappedParameterInterfaces, ["family_type_parameter"] = item.Value.FamilyTypeParameter == null ? null : new JObject { ["name"] = item.Value.FamilyTypeParameter.Definition.Name, ["is_instance"] = item.Value.FamilyTypeParameter.IsInstance, ["selections"] = item.Value.TypeSelections, ["verified"] = true }, ["visibility_parameter"] = item.Value.VisibilityParameter, ["bounds_mm"] = bounds == null ? null : BoundsMm(bounds), ["verified"] = true });
    }));
    private static JToken? ParameterValue(Parameter parameter) => parameter.StorageType switch
    {
        StorageType.Double => parameter.AsDouble(),
        StorageType.Integer => parameter.AsInteger(),
        StorageType.String => parameter.AsString(),
        StorageType.ElementId => parameter.AsElementId().Val(),
        _ => null
    };
    private static JObject ConnectorSnapshot(IReadOnlyDictionary<string, ConnectorElement> connectors) => new(connectors.Select(item => new JProperty(item.Key, ConnectorSnapshot(item.Value))));
    private static JObject ConnectorSnapshot(ConnectorElement item)
    {
        object? Get(string name) => item.GetType().GetProperty(name)?.GetValue(item);
        var origin = Get("Origin") as XYZ; var normal = Get("Direction") as XYZ; var radius = Get("Radius") as double?; var width = Get("Width") as double?; var height = Get("Height") as double?;
        return new JObject { ["id"] = item.Id.Val(), ["origin_mm"] = origin == null ? null : new JObject { ["x"] = Math.Round(origin.X * 304.8, 3), ["y"] = Math.Round(origin.Y * 304.8, 3), ["z"] = Math.Round(origin.Z * 304.8, 3) }, ["normal"] = normal == null ? null : VectorJson(normal), ["radius_mm"] = radius.HasValue ? Math.Round(radius.Value * 304.8, 3) : null, ["width_mm"] = width.HasValue ? Math.Round(width.Value * 304.8, 3) : null, ["height_mm"] = height.HasValue ? Math.Round(height.Value * 304.8, 3) : null, ["electrical_data"] = FamilyConnectorReadBack.Describe(item)["electrical_data"] };
    }
    private static JArray ConnectorSnapshot(IEnumerable<ConnectorElement> connectors) => new(connectors.Select(item =>
        ConnectorSnapshot(item)));
    private static JArray MaterialSnapshot(Document family, FamilyBlueprintSpec spec, IReadOnlyDictionary<string, AppearanceImageFiles> appearanceAssets)
    {
        var declaredNames = new HashSet<string>((spec.Blueprint["materials"] as JArray ?? new JArray()).OfType<JObject>().Select(item => item.Value<string>("name") ?? string.Empty), StringComparer.Ordinal);
        var result = new JArray(new FilteredElementCollector(family).OfClass(typeof(Material)).Cast<Material>().Where(item => declaredNames.Contains(item.Name)).Select(item => FamilyMaterialReadBack.Describe(family, item)));
        AttachAppearanceDependencyEvidence(new JObject { ["materials"] = result }, spec, appearanceAssets); return result;
    }

    private static void AttachAppearanceDependencyEvidence(JObject readBack, FamilyBlueprintSpec spec, IReadOnlyDictionary<string, AppearanceImageFiles> appearanceAssets)
    {
        var observed = (readBack["materials"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(item => item.Value<string>("name") ?? string.Empty, StringComparer.Ordinal);
        foreach (var material in (spec.Blueprint["materials"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var key = material.Value<string>("key") ?? string.Empty; var appearance = material["appearance"] as JObject;
            if (appearance == null || !appearanceAssets.TryGetValue(key, out var files)) continue;
            var name = material.Value<string>("name") ?? string.Empty;
            if (!observed.TryGetValue(name, out var actual)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Material read-back is missing declared material " + name + ".");
            var actualAppearance = actual["appearance"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Material read-back is missing appearance data for " + name + ".");
            void Verify(string role, JObject? declared, string? source)
            {
                if (declared == null || source == null) return;
                var actualImage = actualAppearance[role] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Material " + name + " is missing reopened " + role + " mapping.");
                var fileName = declared.Value<string>("file_name") ?? string.Empty;
                if (!string.Equals(actualImage.Value<string>("file_name"), fileName, StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Material " + name + " reopened " + role + " filename mismatch.");
                if (!string.Equals(HashHex(source), declared.Value<string>("sha256"), StringComparison.OrdinalIgnoreCase)) throw new CommandResultException(ErrorCodes.EvidenceInvalid, "Material " + name + " " + role + " source changed before reopened read-back.");
                actualImage["sha256"] = HashHex(source); actualImage["size_bytes"] = new FileInfo(source).Length; actualImage["external_path_redacted"] = true;
            }
            Verify("texture", appearance["texture"] as JObject, files.TexturePath); Verify("bump", appearance["bump"] as JObject, files.BumpPath);
        }
    }
    private static JObject BoundsMm(BoundingBoxXYZ box) => new() { ["min_x"] = Math.Round(box.Min.X * 304.8, 3), ["min_y"] = Math.Round(box.Min.Y * 304.8, 3), ["min_z"] = Math.Round(box.Min.Z * 304.8, 3), ["max_x"] = Math.Round(box.Max.X * 304.8, 3), ["max_y"] = Math.Round(box.Max.Y * 304.8, 3), ["max_z"] = Math.Round(box.Max.Z * 304.8, 3) };
    private static string Hash(string path) { using var sha = SHA256.Create(); using var stream = File.OpenRead(path); return Convert.ToBase64String(sha.ComputeHash(stream)); }
    private static string HashHex(string path) { using var sha = SHA256.Create(); using var stream = File.OpenRead(path); return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant(); }
}

internal static class FamilyMaterialReadBack
{
    public static JObject Describe(Document document, Material material)
    {
        JObject? Pattern(ElementId id, Color color)
        {
            if (id == ElementId.InvalidElementId || document.GetElement(id) is not FillPatternElement element) return null;
            return new JObject { ["id"] = id.Val(), ["name"] = element.Name, ["target"] = element.GetFillPattern().Target == FillPatternTarget.Model ? "model" : "drafting", ["color_rgb"] = ColorJson(color) };
        }
        JObject? appearance = null;
        if (material.AppearanceAssetId != ElementId.InvalidElementId && document.GetElement(material.AppearanceAssetId) is AppearanceAssetElement appearanceElement)
        {
            var asset = appearanceElement.GetRenderingAsset();
            JObject? Bitmap(string propertyName)
            {
                var property = asset.FindByName(propertyName); var connected = property?.GetSingleConnectedAsset();
                var source = connected?.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) as AssetPropertyString;
                if (source == null || string.IsNullOrWhiteSpace(source.Value)) return null;
                // Revit's stock appearance assets can expose a non-file internal
                // bitmap token here (for example a render-library reference).
                // It is not Blueprint texture evidence and Path.GetFileName
                // rightfully rejects it. A declared Blueprint bitmap still has
                // to be a valid local filename and is checked by
                // AttachAppearanceDependencyEvidence after reopen.
                string fileName;
                try { fileName = Path.GetFileName(source.Value); }
                catch (ArgumentException) { return null; }
                catch (NotSupportedException) { return null; }
                if (string.IsNullOrWhiteSpace(fileName)) return null;
                return new JObject { ["file_name"] = fileName, ["connected_asset_schema"] = connected?.Name, ["external_path_redacted"] = true };
            }
            appearance = new JObject { ["id"] = appearanceElement.Id.Val(), ["name"] = appearanceElement.Name, ["schema"] = asset.Name };
            if (asset.FindByName(Generic.GenericDiffuse) is AssetPropertyDoubleArray4d diffuse) appearance["color_rgb"] = ColorJson(diffuse.GetValueAsColor());
            if (asset.FindByName(Generic.GenericTransparency) is AssetPropertyDouble transparency) appearance["transparency"] = transparency.Value;
            if (asset.FindByName(Generic.GenericGlossiness) is AssetPropertyDouble glossiness) appearance["glossiness"] = glossiness.Value;
            if (asset.FindByName(Generic.GenericIsMetal) is AssetPropertyBoolean metal) appearance["is_metal"] = metal.Value;
            appearance["texture"] = Bitmap(Generic.GenericDiffuse);
            appearance["bump"] = Bitmap(Generic.GenericBumpMap);
            if (asset.FindByName(Generic.GenericBumpAmount) is AssetPropertyDouble bumpAmount) appearance["bump_amount"] = bumpAmount.Value;
        }
        JObject? physicalAsset = null;
        if (material.StructuralAssetId != ElementId.InvalidElementId && document.GetElement(material.StructuralAssetId) is PropertySetElement physicalElement)
        {
            using var asset = physicalElement.GetStructuralAsset();
            physicalAsset = new JObject
            {
                ["id"] = physicalElement.Id.Val(), ["name"] = asset.Name, ["asset_class"] = asset.StructuralAssetClass.ToString().ToLowerInvariant(), ["behavior"] = asset.Behavior.ToString().ToLowerInvariant(),
                ["density_kg_per_m3"] = Round(DensityFromInternal(asset.Density)), ["young_modulus_mpa"] = Round(StressFromInternal(asset.YoungModulus.X)),
                ["shear_modulus_mpa"] = Round(StressFromInternal(asset.ShearModulus.X)), ["poisson_ratio"] = Round(asset.PoissonRatio.X)
            };
        }
        JObject? thermalAsset = null;
        if (material.ThermalAssetId != ElementId.InvalidElementId && document.GetElement(material.ThermalAssetId) is PropertySetElement thermalElement)
        {
            using var asset = thermalElement.GetThermalAsset();
            thermalAsset = new JObject
            {
                ["id"] = thermalElement.Id.Val(), ["name"] = asset.Name, ["material_type"] = asset.ThermalMaterialType.ToString().ToLowerInvariant(), ["behavior"] = asset.Behavior.ToString().ToLowerInvariant(),
                ["density_kg_per_m3"] = Round(DensityFromInternal(asset.Density)), ["thermal_conductivity_w_per_mk"] = Round(ThermalConductivityFromInternal(asset.ThermalConductivity)),
                ["specific_heat_j_per_kgk"] = Round(SpecificHeatFromInternal(asset.SpecificHeat)), ["emissivity"] = Round(asset.Emissivity), ["porosity"] = Round(asset.Porosity),
                ["reflectivity"] = Round(asset.Reflectivity), ["transmits_light"] = asset.TransmitsLight
            };
        }
        return new JObject
        {
            ["id"] = material.Id.Val(), ["name"] = material.Name, ["color_rgb"] = ColorJson(material.Color), ["transparency"] = material.Transparency,
            ["use_render_appearance_for_shading"] = material.UseRenderAppearanceForShading,
            ["surface_foreground_pattern"] = Pattern(material.SurfaceForegroundPatternId, material.SurfaceForegroundPatternColor),
            ["cut_foreground_pattern"] = Pattern(material.CutForegroundPatternId, material.CutForegroundPatternColor),
            ["appearance_asset_id"] = material.AppearanceAssetId.Val(), ["has_appearance_asset"] = material.AppearanceAssetId != ElementId.InvalidElementId,
            ["appearance"] = appearance,
            ["structural_asset_id"] = material.StructuralAssetId.Val(), ["has_physical_asset"] = material.StructuralAssetId != ElementId.InvalidElementId, ["physical_asset"] = physicalAsset,
            ["thermal_asset_id"] = material.ThermalAssetId.Val(), ["has_thermal_asset"] = material.ThermalAssetId != ElementId.InvalidElementId, ["thermal_asset"] = thermalAsset
        };
    }

    private static JObject ColorJson(Color color) => new() { ["r"] = color.Red, ["g"] = color.Green, ["b"] = color.Blue };
    private static double Round(double value) => Math.Round(value, 6);
    private static double DensityFromInternal(double value)
    {
#if REVIT2019 || REVIT2020
        return UnitUtils.ConvertFromInternalUnits(value, DisplayUnitType.DUT_KILOGRAMS_PER_CUBIC_METER);
#else
        return UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.KilogramsPerCubicMeter);
#endif
    }
    private static double StressFromInternal(double value)
    {
#if REVIT2019 || REVIT2020
        return UnitUtils.ConvertFromInternalUnits(value, DisplayUnitType.DUT_MEGAPASCALS);
#else
        return UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.Megapascals);
#endif
    }
    private static double ThermalConductivityFromInternal(double value)
    {
#if REVIT2019 || REVIT2020
        return UnitUtils.ConvertFromInternalUnits(value, DisplayUnitType.DUT_WATTS_PER_METER_KELVIN);
#else
        return UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.WattsPerMeterKelvin);
#endif
    }
    private static double SpecificHeatFromInternal(double value)
    {
#if REVIT2019 || REVIT2020
        return UnitUtils.ConvertFromInternalUnits(value, DisplayUnitType.DUT_JOULES_PER_KILOGRAM_CELSIUS);
#else
        return UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.JoulesPerKilogramDegreeCelsius);
#endif
    }
}

internal static class FamilyLightingReadBack
{
    public static JObject? Describe(Document document)
    {
        if (!document.IsFamilyDocument || document.OwnerFamily?.FamilyCategory?.Id.IntVal() != (int)BuiltInCategory.OST_LightingFixtures) return null;
        try
        {
            using var family = LightFamily.GetLightFamily(document);
            if (family == null || !family.IsValidObject) return null;
            var settings = new JArray();
            for (var index = 0; index < family.GetNumberOfLightTypes(); index++)
            {
                using var type = family.GetLightType(index);
                using var shape = type.GetLightShape();
                using var distribution = type.GetLightDistribution();
                using var intensity = type.GetInitialIntensity();
                using var color = type.GetInitialColor();
                using var loss = type.GetLossFactor();
                var setting = new JObject
                {
                    ["type_name"] = family.GetLightTypeName(index),
                    ["initial_intensity"] = Intensity(intensity), ["initial_color"] = InitialColorSnapshot(color), ["loss_factor"] = Loss(loss),
                    ["color_filter_rgb"] = ColorJson(type.ColorFilter), ["dimming_color"] = type.DimmingColor == LightDimmingColor.Incandescent ? "incandescent" : "none"
                };
                switch (shape)
                {
                    case LineLightShape line: setting["emit_length_mm"] = Mm(line.EmitLength); break;
                    case RectangleLightShape rectangle: setting["emit_length_mm"] = Mm(rectangle.EmitLength); setting["emit_width_mm"] = Mm(rectangle.EmitWidth); break;
                    case CircleLightShape circle: setting["emit_diameter_mm"] = Mm(circle.EmitDiameter); break;
                }
                switch (distribution)
                {
                    case SpotLightDistribution spot:
                        setting["spot"] = new JObject { ["beam_angle_degrees"] = Degrees(spot.SpotBeamAngle), ["field_angle_degrees"] = Degrees(spot.SpotFieldAngle), ["tilt_angle_degrees"] = Degrees(spot.TiltAngle) };
                        break;
                    case PhotometricWebLightDistribution web:
                        setting["photometric_web"] = new JObject { ["file_name"] = Path.GetFileName(web.PhotometricWebFile), ["tilt_angle_degrees"] = Degrees(web.TiltAngle), ["source_path_redacted"] = true };
                        break;
                }
                settings.Add(setting);
            }
            var transform = family.GetLightSourceTransform();
            return new JObject
            {
                ["shape_style"] = ShapeStyle(family.GetLightShapeStyle()), ["distribution_style"] = DistributionStyle(family.GetLightDistributionStyle()),
                ["type_settings"] = settings,
                ["transform"] = new JObject { ["origin_mm"] = Point(transform.Origin), ["basis_x"] = Vector(transform.BasisX), ["basis_y"] = Vector(transform.BasisY), ["basis_z"] = Vector(transform.BasisZ), ["read_only_from_template"] = true },
                ["verified_from_revit"] = true
            };
        }
        catch { return null; }
    }

    private static JObject Intensity(InitialIntensity value) => value switch
    {
        InitialFluxIntensity flux => new JObject { ["method"] = "luminous_flux", ["luminous_flux_lm"] = Round(flux.Flux) },
        InitialLuminousIntensity luminous => new JObject { ["method"] = "luminous_intensity", ["luminous_intensity_cd"] = Round(luminous.Luminosity) },
        InitialIlluminanceIntensity illuminance => new JObject { ["method"] = "illuminance", ["illuminance_lux"] = Round(illuminance.Illuminance), ["distance_mm"] = Mm(illuminance.Distance) },
        InitialWattageIntensity wattage => new JObject { ["method"] = "wattage", ["wattage_w"] = Round(wattage.Wattage), ["efficacy_lm_per_w"] = Round(wattage.Efficacy) },
        _ => new JObject { ["method"] = value.GetType().Name }
    };

    private static JObject InitialColorSnapshot(InitialColor value) => value switch
    {
        CustomInitialColor custom => new JObject { ["mode"] = "temperature", ["temperature_kelvin"] = Round(custom.Temperature) },
        PresetInitialColor preset => new JObject { ["mode"] = "preset", ["preset"] = Preset(preset.Preset) },
        _ => new JObject { ["mode"] = value.GetType().Name }
    };

    private static JObject Loss(LossFactor value)
    {
        if (value is BasicLossFactor basic) return new JObject { ["mode"] = "basic", ["value"] = Round(basic.LossFactor) };
        if (value is AdvancedLossFactor advanced) return new JObject
        {
            ["mode"] = "advanced", ["ballast"] = Round(advanced.BallastLossFactor), ["lamp_lumen_depreciation"] = Round(advanced.LampLumenDepreciation),
            ["lamp_tilt"] = Round(advanced.LampTiltLossFactor), ["luminaire_dirt_depreciation"] = Round(advanced.LuminaireDirtDepreciation),
            ["surface_depreciation"] = Round(advanced.SurfaceDepreciationLossFactor), ["temperature"] = Round(advanced.TemperatureLossFactor), ["voltage"] = Round(advanced.VoltageLossFactor)
        };
        return new JObject { ["mode"] = value.GetType().Name };
    }

    private static string ShapeStyle(LightShapeStyle value) => value switch { LightShapeStyle.Point => "point", LightShapeStyle.Line => "line", LightShapeStyle.Rectangle => "rectangle", LightShapeStyle.Circle => "circle", _ => value.ToString().ToLowerInvariant() };
    private static string DistributionStyle(LightDistributionStyle value) => value switch { LightDistributionStyle.Spherical => "spherical", LightDistributionStyle.Hemispherical => "hemispherical", LightDistributionStyle.Spot => "spot", LightDistributionStyle.PhotometricWeb => "photometric_web", _ => value.ToString().ToLowerInvariant() };
    private static string Preset(ColorPreset value) => value switch
    {
        ColorPreset.D65 => "d65", ColorPreset.D50 => "d50", ColorPreset.Halogen => "halogen", ColorPreset.Incandescent => "incandescent", ColorPreset.Xenon => "xenon", ColorPreset.Quartz => "quartz",
        ColorPreset.FluorescentWarm => "fluorescent_warm", ColorPreset.FluorescentCool => "fluorescent_cool", ColorPreset.FluorescentWhite => "fluorescent_white", ColorPreset.FluorescentDayLight => "fluorescent_daylight",
        ColorPreset.FluorescentLightWhite => "fluorescent_light_white", ColorPreset.MetalHalide => "metal_halide", ColorPreset.HighPressureSodium => "high_pressure_sodium", ColorPreset.LowPressureSodium => "low_pressure_sodium",
        ColorPreset.Mercury => "mercury", ColorPreset.PhosphorMercury => "phosphor_mercury", _ => value.ToString().ToLowerInvariant()
    };
    private static JObject ColorJson(Color color) => new() { ["r"] = color.Red, ["g"] = color.Green, ["b"] = color.Blue };
    private static JObject Point(XYZ point) => new() { ["x_mm"] = Mm(point.X), ["y_mm"] = Mm(point.Y), ["z_mm"] = Mm(point.Z) };
    private static JObject Vector(XYZ value) => new() { ["x"] = Round(value.X), ["y"] = Round(value.Y), ["z"] = Round(value.Z) };
    private static double Mm(double feet) => Round(feet * 304.8);
    private static double Degrees(double radians) => Round(radians * 180.0 / Math.PI);
    private static double Round(double value) => Math.Round(value, 6);
}
