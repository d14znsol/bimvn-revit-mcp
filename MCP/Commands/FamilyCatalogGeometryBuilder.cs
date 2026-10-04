using System.Security.Cryptography;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DSCons.RevitMcp.Contracts;
using DSCons.RevitMcp.Core;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

/// <summary>Catalog-driven parametric-envelope pilots. These are deliberately
/// not certified LOD300 Families: Pump port markers are not pipe connectors,
/// and full role-by-role geometry requires copied-model evidence.</summary>
internal static class FamilyCatalogGeometryBuilder
{
    public static JObject Preview(Autodesk.Revit.ApplicationServices.Application application, string template, bool allowGenericCategoryChange, FamilyQualitySpec spec, string name, string type)
    {
        Document? family = null;
        try
        {
            family = application.NewFamilyDocument(template); var categoryChanged = FamilyTemplateResolver.EnsureTargetCategory(family, spec.Adapter.Category, spec.Adapter.CategoryName, allowGenericCategoryChange); FamilyQualityValidator.ValidateTemplate(family, spec.Adapter);
            using var group = new TransactionGroup(family, "Preview DSCons catalog Family"); group.Start();
            var result = Create(family, spec, name, type); group.RollBack();
            result["model_changed"] = false; result["validation_level"] = "family_document_transactiongroup_rollback"; result["category_assignment"] = new JObject { ["target"] = spec.Adapter.CategoryName, ["changed_from_generic"] = categoryChanged, ["verified"] = true }; return result;
        }
        catch (CommandResultException) { throw; }
        catch (Exception ex) { throw new CommandResultException(ErrorCodes.TransactionFailed, "Catalog Family preview rolled back: " + ex.Message); }
        finally { if (family != null) try { family.Close(false); } catch { } }
    }

    public static JObject Apply(Autodesk.Revit.ApplicationServices.Application application, string template, bool allowGenericCategoryChange, string demo, FamilyQualitySpec spec, string name, string type)
    {
        Document? family = null; string? staging = null;
        var output = Path.Combine(demo, name + ".rfa");
        try
        {
            if (File.Exists(output)) throw new CommandResultException(ErrorCodes.FileConflict, "Output Family exists; overwrite is blocked.");
            family = application.NewFamilyDocument(template); var categoryChanged = FamilyTemplateResolver.EnsureTargetCategory(family, spec.Adapter.Category, spec.Adapter.CategoryName, allowGenericCategoryChange); FamilyQualityValidator.ValidateTemplate(family, spec.Adapter);
            JObject created; using (var group = new TransactionGroup(family, "Create DSCons catalog Family")) { group.Start(); created = Create(family, spec, name, type); group.Assimilate(); }
            staging = Path.Combine(demo, ".dscons-" + Guid.NewGuid().ToString("N") + ".rfa");
            family.SaveAs(staging, new SaveAsOptions { OverwriteExistingFile = false }); family.Close(false); family = null;
            var reopened = application.OpenDocumentFile(staging); JObject readBack;
            try { readBack = FamilyData.Inspect(reopened); } finally { reopened.Close(false); }
            if (!(readBack["is_family_document"]?.Value<bool>() ?? false)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Saved output did not reopen as a Family document.");
            File.Move(staging, output); staging = null;
            return new JObject { ["family_path"] = output, ["sha256"] = Hash(output), ["category_assignment"] = new JObject { ["target"] = spec.Adapter.CategoryName, ["changed_from_generic"] = categoryChanged, ["verified"] = true }, ["created"] = created, ["verification"] = new JObject { ["verified"] = true, ["mode"] = "post_commit_read_back", ["family"] = readBack } };
        }
        catch (CommandResultException) { throw; }
        catch (Exception ex) { throw new CommandResultException(ErrorCodes.TransactionFailed, "Catalog Family apply rolled back: " + ex.Message); }
        finally { if (family != null) try { family.Close(false); } catch { } if (staging != null && File.Exists(staging)) try { File.Delete(staging); } catch { } }
    }

    private static JObject Create(Document family, FamilyQualitySpec spec, string name, string typeName)
    {
        using var transaction = new Transaction(family, "Create catalog LOD300 geometry"); transaction.Start();
        var width = spec.FamilyKind == "pump" ? Mm(spec, "B") : Mm(spec, "width_mm");
        var height = spec.FamilyKind == "pump" ? Mm(spec, "H") : Mm(spec, "height_mm");
        var depth = spec.FamilyKind == "pump" ? Mm(spec, "A") : Mm(spec, "depth_mm");
        var manager = family.FamilyManager; var type = manager.NewType(typeName); manager.CurrentType = type;
        var plane = SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(XYZ.BasisX, XYZ.Zero));
        var result = spec.FamilyKind == "panel"
            ? CreatePanel(family, plane, manager, spec, width, height, depth)
            : CreatePump(family, plane, manager, spec, depth, width, height, Mm(spec, "DN1"), Mm(spec, "DN2"));
        var flex = spec.FamilyKind == "panel"
            ? VerifyFlex(family, manager, result.Width, result.Height, result.Depth, depth, height, width, result.FormCount)
            : VerifyFlex(family, manager, result.Width, result.Height, result.Depth, width, height, depth, result.FormCount);
        if (transaction.Commit() != TransactionStatus.Committed) throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rejected catalog Family geometry.");
        return new JObject { ["family_name"] = name, ["type_name"] = typeName, ["family_kind"] = spec.FamilyKind, ["build_status"] = "parametric_envelope_pilot", ["target_lod"] = "LOD_300", ["detail_profile"] = "dscons_mep_300_v1", ["forms"] = result.FormCount, ["dimensions_mm"] = new JObject { ["width"] = Math.Round(width * 304.8, 1), ["height"] = Math.Round(height * 304.8, 1), ["depth"] = Math.Round(depth * 304.8, 1) }, ["parameter_flex"] = flex, ["connection_status"] = spec.FamilyKind == "pump" ? "physical_port_markers_only_no_pipe_system" : "electrical_connector_pending_runtime_probe" };
    }

    private sealed class BuildResult
    {
        public FamilyParameter Width { get; set; } = null!;
        public FamilyParameter Height { get; set; } = null!;
        public FamilyParameter Depth { get; set; } = null!;
        public int FormCount { get; set; }
    }

    private static BuildResult CreatePanel(Document family, SketchPlane plane, FamilyManager manager, FamilyQualitySpec spec, double width, double height, double depth)
    {
        var cabinetWidth = AddLength(manager, "Bề rộng tủ"); var cabinetHeight = AddLength(manager, "Chiều cao tủ"); var cabinetDepth = AddLength(manager, "Chiều sâu tủ");
        var halfDepth = AddLength(manager, "_Nửa chiều sâu tủ"); var negativeHalfDepth = AddLength(manager, "_Âm nửa chiều sâu tủ"); var halfWidth = AddLength(manager, "_Nửa bề rộng tủ"); var negativeHalfWidth = AddLength(manager, "_Âm nửa bề rộng tủ"); var doorBack = AddLength(manager, "_Mặt sau cửa tủ");
        manager.Set(cabinetWidth, width); manager.Set(cabinetHeight, height); manager.Set(cabinetDepth, depth); manager.SetFormula(halfDepth, "Chiều sâu tủ / 2"); manager.SetFormula(negativeHalfDepth, "-_Nửa chiều sâu tủ"); manager.SetFormula(halfWidth, "Bề rộng tủ / 2"); manager.SetFormula(negativeHalfWidth, "-_Nửa bề rộng tủ"); manager.SetFormula(doorBack, "Chiều sâu tủ * 0.47");
        SetText(manager, "Mã tủ", spec.Value("type_code")?.Value<string>() ?? string.Empty); SetText(manager, "Điện áp danh định", (spec.Value("voltage")?.Value<string>() ?? spec.Value("voltage")?.ToString() ?? string.Empty) + " V"); SetText(manager, "Pha", spec.Value("phase")?.Value<string>() ?? string.Empty); SetText(manager, "Số cực", spec.Value("poles")?.ToString() ?? string.Empty); SetText(manager, "Dòng định mức", (spec.Value("rating_ampere")?.ToString() ?? string.Empty) + " A");
        var enclosure = family.FamilyCreate.NewExtrusion(true, Rectangle(-width / 2, width / 2, 0, height), plane, depth); enclosure.StartOffset = -depth / 2; enclosure.EndOffset = depth / 2; SetRole(family, enclosure, "PanelEnclosure", true, true, true);
        Associate(manager, enclosure.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM), negativeHalfDepth); Associate(manager, enclosure.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), halfDepth);
        var widthPlane = SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(XYZ.BasisY, XYZ.Zero)); var widthForm = family.FamilyCreate.NewExtrusion(true, RectangleXZ(-depth / 2, depth / 2, 0, height), widthPlane, width); widthForm.StartOffset = -width / 2; widthForm.EndOffset = width / 2; SetRole(family, widthForm, "PanelWidthFramework", true, true, true); Associate(manager, widthForm.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM), negativeHalfWidth); Associate(manager, widthForm.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), halfWidth);
        var heightPlane = SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero)); var heightForm = family.FamilyCreate.NewExtrusion(true, RectangleXY(-depth / 2, depth / 2, -width / 2, width / 2), heightPlane, height); heightForm.StartOffset = 0; heightForm.EndOffset = height; SetRole(family, heightForm, "PanelHeightFramework", true, true, true); Associate(manager, heightForm.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), cabinetHeight);
        var door = family.FamilyCreate.NewExtrusion(true, Rectangle(-width * .46, width * .46, height * .06, height * .94), plane, depth * .03); door.StartOffset = depth * .47; door.EndOffset = depth / 2; SetRole(family, door, "PanelDoor", false, true, true);
        Associate(manager, door.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM), doorBack); Associate(manager, door.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), halfDepth);
        return new BuildResult { Width = cabinetDepth, Height = cabinetHeight, Depth = cabinetWidth, FormCount = 4 };
    }

    private static BuildResult CreatePump(Document family, SketchPlane plane, FamilyManager manager, FamilyQualitySpec spec, double length, double width, double height, double suctionDn, double dischargeDn)
    {
        AddPumpCatalogParameters(manager, spec);
        var pumpLength = AddLength(manager, "Chiều dài"); var pumpWidth = AddLength(manager, "Chiều rộng"); var pumpHeight = AddLength(manager, "Chiều cao");
        var halfLength = AddLength(manager, "_Nửa chiều dài"); var negativeHalfLength = AddLength(manager, "_Âm nửa chiều dài"); var halfWidth = AddLength(manager, "_Nửa chiều rộng"); var negativeHalfWidth = AddLength(manager, "_Âm nửa chiều rộng"); var baseHeight = AddLength(manager, "_Chiều cao bệ"); var casingRadius = AddLength(manager, "_Bán kính volute"); var motorRadius = AddLength(manager, "_Bán kính motor"); var suctionDiameter = AddLength(manager, "DN hút"); var dischargeDiameter = AddLength(manager, "DN đẩy"); var suctionRadius = AddLength(manager, "_Bán kính hút"); var dischargeRadius = AddLength(manager, "_Bán kính đẩy");
        manager.Set(pumpLength, length); manager.Set(pumpWidth, width); manager.Set(pumpHeight, height); manager.Set(suctionDiameter, suctionDn); manager.Set(dischargeDiameter, dischargeDn); manager.Set(casingRadius, height * .28); manager.Set(motorRadius, height * .20); manager.Set(suctionRadius, suctionDn / 2); manager.Set(dischargeRadius, dischargeDn / 2);
        manager.SetFormula(halfLength, "Chiều dài / 2"); manager.SetFormula(negativeHalfLength, "-_Nửa chiều dài"); manager.SetFormula(halfWidth, "Chiều rộng / 2"); manager.SetFormula(negativeHalfWidth, "-_Nửa chiều rộng"); manager.SetFormula(baseHeight, "Chiều cao * 0.10");
        SetText(manager, "Model bơm", spec.Value("type_code")?.Value<string>() ?? string.Empty); SetText(manager, "Lưu lượng catalog", spec.Value("performance")?["flow_m3h"]?.ToString() + " m³/h"); SetText(manager, "Cột áp catalog", spec.Value("performance")?["head_m"]?.ToString() + " m"); SetText(manager, "Công suất motor", spec.Value("performance")?["motor_kw"]?.ToString() + " kW");
        var baseForm = family.FamilyCreate.NewExtrusion(true, Rectangle(-width / 2, width / 2, 0, height * .10), plane, length); baseForm.StartOffset = -length / 2; baseForm.EndOffset = length / 2; SetRole(family, baseForm, "PumpBase", true, true, true);
        Associate(manager, baseForm.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM), negativeHalfLength); Associate(manager, baseForm.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), halfLength);
        var baseWidthPlane = SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(XYZ.BasisY, XYZ.Zero)); var baseWidthForm = family.FamilyCreate.NewExtrusion(true, RectangleXZ(-length / 2, length / 2, 0, height * .10), baseWidthPlane, width); baseWidthForm.StartOffset = -width / 2; baseWidthForm.EndOffset = width / 2; SetRole(family, baseWidthForm, "PumpBaseWidth", true, true, true); Associate(manager, baseWidthForm.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM), negativeHalfWidth); Associate(manager, baseWidthForm.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), halfWidth);
        var casing = family.FamilyCreate.NewExtrusion(true, Circle(height * .28, height * .38), plane, length * .34); casing.StartOffset = length * .16; casing.EndOffset = length * .50; SetRole(family, casing, "PumpVolute", false, true, true); LabelRadial(family, casing, casingRadius, height * .28, height * .38, "pump volute");
        var motor = family.FamilyCreate.NewExtrusion(true, Circle(height * .20, height * .50), plane, length * .44); motor.StartOffset = -length * .45; motor.EndOffset = -length * .01; SetRole(family, motor, "PumpMotor", false, true, true); LabelRadial(family, motor, motorRadius, height * .20, height * .50, "pump motor");
        var terminalPlane = SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero)); var terminal = family.FamilyCreate.NewExtrusion(true, RectangleXY(-length * .30, -length * .10, -width * .25, width * .25), terminalPlane, height * .28); terminal.StartOffset = height * .72; terminal.EndOffset = height; SetRole(family, terminal, "PumpTerminalBox", false, true, true); Associate(manager, terminal.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), pumpHeight);
        var suction = family.FamilyCreate.NewExtrusion(true, Circle(suctionDn / 2, height * .38), plane, length * .08); suction.StartOffset = -length * .50; suction.EndOffset = -length * .42; SetRole(family, suction, "PortSuctionDN" + Math.Round(suctionDn * 304.8), false, true, true); LabelRadial(family, suction, suctionRadius, suctionDn / 2, height * .38, "suction marker");
        var discharge = family.FamilyCreate.NewExtrusion(true, Circle(dischargeDn / 2, height * .38), plane, length * .08); discharge.StartOffset = length * .42; discharge.EndOffset = length * .50; SetRole(family, discharge, "PortDischargeDN" + Math.Round(dischargeDn * 304.8), false, true, true); LabelRadial(family, discharge, dischargeRadius, dischargeDn / 2, height * .38, "discharge marker");
        return new BuildResult { Width = pumpLength, Height = pumpHeight, Depth = pumpWidth, FormCount = 7 };
    }

    private static void SetRole(Document family, GenericForm form, string role, bool coarse, bool medium, bool fine)
    {
        var category = family.OwnerFamily!.FamilyCategory!; var subName = "DSCons LOD300 " + role;
        var sub = category.SubCategories.Contains(subName) ? category.SubCategories.get_Item(subName) : family.Settings.Categories.NewSubcategory(category, subName);
        form.Subcategory = sub; form.SetVisibility(new FamilyElementVisibility(FamilyElementVisibilityType.Model) { IsShownInCoarse = coarse, IsShownInMedium = medium, IsShownInFine = fine });
    }

    private static void AddPumpCatalogParameters(FamilyManager manager, FamilyQualitySpec spec)
    {
        foreach (var key in new[] { "A", "A1", "A2", "B", "C", "D1", "D2", "DN1", "DN2", "K1", "K2", "P1", "P2", "H", "H1", "H2", "H3", "M", "N1", "N2", "R", "S1", "S2", "T" })
        {
            var parameter = AddLength(manager, "Catalog " + key);
            manager.Set(parameter, Mm(spec, key));
        }
    }

    private static void SetText(FamilyManager manager, string name, string value)
    {
        var parameter = AddText(manager, name); manager.Set(parameter, value ?? string.Empty);
    }

    private static void Associate(FamilyManager manager, Parameter? elementParameter, FamilyParameter familyParameter)
    {
        if (elementParameter == null || !manager.CanElementParameterBeAssociated(elementParameter))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit did not allow Family parameter association for " + familyParameter.Definition.Name + ".");
        manager.AssociateElementParameterToFamilyParameter(elementParameter, familyParameter);
    }

    private static ViewSection DimensionView(Document family)
    {
        var view = new FilteredElementCollector(family).OfClass(typeof(ViewSection)).Cast<ViewSection>()
            .FirstOrDefault(candidate => !candidate.IsTemplate && Math.Abs(candidate.ViewDirection.X) > 0.99);
        return view ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "The Family template does not provide an elevation suitable for labeled dimensions.");
    }

    private static ViewPlan PlanDimensionView(Document family)
    {
        var view = new FilteredElementCollector(family).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(candidate => !candidate.IsTemplate);
        return view ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "The Family template does not provide a plan view suitable for labeled dimensions.");
    }

    private static ViewSection HeightDimensionView(Document family)
    {
        var view = new FilteredElementCollector(family).OfClass(typeof(ViewSection)).Cast<ViewSection>()
            .FirstOrDefault(candidate => !candidate.IsTemplate && Math.Abs(candidate.ViewDirection.Y) > 0.99);
        return view ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "The Family template does not provide an elevation suitable for height dimensions.");
    }

    private static Reference CurveReference(Document family, Curve curve, string part)
    {
        var sketchReference = curve.Reference;
        var modelCurve = sketchReference == null ? null : family.GetElement(sketchReference.ElementId) as ModelCurve;
        return modelCurve?.GeometryCurve.Reference ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "The " + part + " profile has no dimensionable curve reference.");
    }

    private static void LabelRectangle(Document family, Extrusion extrusion, FamilyParameter width, FamilyParameter height, double initialWidth, double initialHeight, string part)
    {
        family.Regenerate();
        var widthView = PlanDimensionView(family); var heightView = HeightDimensionView(family);
        var left = family.FamilyCreate.NewReferencePlane2(new XYZ(-1, -initialWidth / 2, 0), new XYZ(1, -initialWidth / 2, 0), new XYZ(0, -initialWidth / 2, 1), widthView);
        var right = family.FamilyCreate.NewReferencePlane2(new XYZ(-1, initialWidth / 2, 0), new XYZ(1, initialWidth / 2, 0), new XYZ(0, initialWidth / 2, 1), widthView);
        var bottom = family.FamilyCreate.NewReferencePlane2(new XYZ(-1, 0, 0), new XYZ(1, 0, 0), new XYZ(0, 1, 0), heightView);
        var top = family.FamilyCreate.NewReferencePlane2(new XYZ(-1, 0, initialHeight), new XYZ(1, 0, initialHeight), new XYZ(0, 1, initialHeight), heightView);
        foreach (var referencePlane in new[] { left, right, bottom, top }) referencePlane.get_Parameter(BuiltInParameter.ELEM_IS_REFERENCE)?.Set((int)FamilyInstanceReferenceType.StrongReference);
        family.FamilyCreate.NewAlignment(widthView, FaceReference(extrusion, -XYZ.BasisY, part), new Reference(left)); family.FamilyCreate.NewAlignment(widthView, FaceReference(extrusion, XYZ.BasisY, part), new Reference(right));
        family.FamilyCreate.NewAlignment(heightView, FaceReference(extrusion, -XYZ.BasisZ, part), new Reference(bottom)); family.FamilyCreate.NewAlignment(heightView, FaceReference(extrusion, XYZ.BasisZ, part), new Reference(top));
        var widthRefs = new ReferenceArray(); widthRefs.Append(new Reference(left)); widthRefs.Append(new Reference(right));
        var widthDimension = family.FamilyCreate.NewDimension(widthView, Line.CreateBound(new XYZ(0, -initialWidth / 2, 0), new XYZ(0, initialWidth / 2, 0)), widthRefs);
        if (widthDimension == null) throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected the width dimension for " + part + ".");
        widthDimension.FamilyLabel = width;
        var heightRefs = new ReferenceArray(); heightRefs.Append(new Reference(bottom)); heightRefs.Append(new Reference(top));
        var heightDimension = family.FamilyCreate.NewDimension(heightView, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(0, 0, initialHeight)), heightRefs);
        if (heightDimension == null) throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected the height dimension for " + part + ".");
        heightDimension.FamilyLabel = height;
    }

    private static Reference FaceReference(Extrusion extrusion, XYZ normal, string part)
    {
        var face = extrusion.get_Geometry(new Options { ComputeReferences = true }).OfType<Solid>().SelectMany(solid => solid.Faces.Cast<Face>()).OfType<PlanarFace>()
            .FirstOrDefault(candidate => candidate.FaceNormal.DotProduct(normal) > .999);
        return face?.Reference ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "The " + part + " form has no stable face for parameter alignment.");
    }

    private static void LabelRadial(Document family, Extrusion extrusion, FamilyParameter parameter, double radius, double zOffset, string part)
    {
        family.Regenerate(); var curve = extrusion.Sketch.Profile.Cast<CurveArray>().Single().Cast<Curve>().OfType<Arc>().FirstOrDefault();
        if (curve == null) throw new CommandResultException(ErrorCodes.VerificationFailed, "The " + part + " profile has no circular sketch curve.");
        var dimension = family.FamilyCreate.NewRadialDimension(DimensionView(family), CurveReference(family, curve, part), new XYZ(0, radius + .20, zOffset));
        if (dimension == null) throw new CommandResultException(ErrorCodes.VerificationFailed, "Revit rejected the radial dimension for " + part + ".");
        dimension.FamilyLabel = parameter;
    }

    private static JObject VerifyFlex(Document family, FamilyManager manager, FamilyParameter xParameter, FamilyParameter zParameter, FamilyParameter yParameter, double originalX, double originalZ, double originalY, int expectedForms)
    {
        family.Regenerate(); var before = FormBounds(family, expectedForms);
        try
        {
            manager.Set(xParameter, originalX * 1.10); manager.Set(zParameter, originalZ * 1.10); manager.Set(yParameter, originalY * 1.10); family.Regenerate();
            var after = FormBounds(family, expectedForms);
            var xChanged = Math.Abs(after["x_span"]!.Value<double>() - before["x_span"]!.Value<double>()) >= .001; var yChanged = Math.Abs(after["y_span"]!.Value<double>() - before["y_span"]!.Value<double>()) >= .001; var zChanged = Math.Abs(after["z_span"]!.Value<double>() - before["z_span"]!.Value<double>()) >= .001;
            return new JObject { ["verified"] = xChanged && yChanged && zChanged, ["mode"] = "temporary_parameter_flex_read_back", ["changed_axes"] = new JObject { ["x"] = xChanged, ["y"] = yChanged, ["z"] = zChanged }, ["before_ft"] = before, ["after_ft"] = after };
        }
        finally { manager.Set(xParameter, originalX); manager.Set(zParameter, originalZ); manager.Set(yParameter, originalY); family.Regenerate(); }
    }

    private static JObject FormBounds(Document family, int expectedForms)
    {
        var boxes = new FilteredElementCollector(family).OfClass(typeof(Extrusion)).Cast<Extrusion>().Select(form => form.get_BoundingBox(null)).Where(box => box != null).ToList();
        if (boxes.Count != expectedForms) throw new CommandResultException(ErrorCodes.VerificationFailed, "Family flex read-back did not find the expected form count.");
        return new JObject { ["x_span"] = boxes.Max(box => box!.Max.X) - boxes.Min(box => box!.Min.X), ["y_span"] = boxes.Max(box => box!.Max.Y) - boxes.Min(box => box!.Min.Y), ["z_span"] = boxes.Max(box => box!.Max.Z) - boxes.Min(box => box!.Min.Z) };
    }

    private static double Mm(FamilyQualitySpec spec, string key) { var value = spec.Value(key)?.Value<double?>() ?? 0; if (value <= 0) throw new CommandResultException(ErrorCodes.InvalidParam, key + " must be a confirmed positive millimetre value."); return value / 304.8; }
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
    private static CurveArrArray Circle(double radius, double zOffset = 0) { var result = new CurveArrArray(); var top = new XYZ(0, radius, zOffset); var bottom = new XYZ(0, -radius, zOffset); var right = new XYZ(0, 0, zOffset + radius); var left = new XYZ(0, 0, zOffset - radius); var loop = new CurveArray(); loop.Append(Arc.Create(top, bottom, right)); loop.Append(Arc.Create(bottom, top, left)); result.Append(loop); return result; }
    private static CurveArrArray Rectangle(double y0, double y1, double z0, double z1) { var a = new XYZ(0, y0, z0); var b = new XYZ(0, y1, z0); var c = new XYZ(0, y1, z1); var d = new XYZ(0, y0, z1); var loop = new CurveArray(); loop.Append(Line.CreateBound(a, b)); loop.Append(Line.CreateBound(b, c)); loop.Append(Line.CreateBound(c, d)); loop.Append(Line.CreateBound(d, a)); var result = new CurveArrArray(); result.Append(loop); return result; }
    private static CurveArrArray RectangleXZ(double x0, double x1, double z0, double z1) { var a = new XYZ(x0, 0, z0); var b = new XYZ(x1, 0, z0); var c = new XYZ(x1, 0, z1); var d = new XYZ(x0, 0, z1); var loop = new CurveArray(); loop.Append(Line.CreateBound(a, b)); loop.Append(Line.CreateBound(b, c)); loop.Append(Line.CreateBound(c, d)); loop.Append(Line.CreateBound(d, a)); var result = new CurveArrArray(); result.Append(loop); return result; }
    private static CurveArrArray RectangleXY(double x0, double x1, double y0, double y1) { var a = new XYZ(x0, y0, 0); var b = new XYZ(x1, y0, 0); var c = new XYZ(x1, y1, 0); var d = new XYZ(x0, y1, 0); var loop = new CurveArray(); loop.Append(Line.CreateBound(a, b)); loop.Append(Line.CreateBound(b, c)); loop.Append(Line.CreateBound(c, d)); loop.Append(Line.CreateBound(d, a)); var result = new CurveArrArray(); result.Append(loop); return result; }
    private static string Hash(string path) { using var sha = SHA256.Create(); using var stream = File.OpenRead(path); return Convert.ToBase64String(sha.ComputeHash(stream)); }
}
