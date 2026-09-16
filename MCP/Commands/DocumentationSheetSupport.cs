using Autodesk.Revit.DB;
using DSCons.RevitMcp.Contracts;
using DSCons.RevitMcp.Core;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

internal static class DocumentationSheetSupport
{
    public static JArray DiscoverTitleBlocks(Document doc, int limit)
    {
        var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .Where(x => x.Category?.Id.IntVal() == (int)BuiltInCategory.OST_TitleBlocks)
            .OrderBy(x => x.FamilyName).ThenBy(x => x.Name).Take(limit).ToList();
        var result = new JArray();
        var ownsTransaction = !doc.IsModifiable;
        using var transaction = ownsTransaction ? new Transaction(doc, "DSCons title block discovery") : null;
        if (ownsTransaction) transaction!.Start();
        try
        {
            foreach (var symbol in symbols)
            {
                var sheet = ViewSheet.Create(doc, symbol.Id);
                doc.Regenerate();
                var outline = sheet.Outline;
                result.Add(new JObject
                {
                    ["id"] = symbol.Id.Val(),
                    ["name"] = symbol.Name,
                    ["family"] = symbol.FamilyName,
                    ["sheet_width_mm"] = Math.Round((outline.Max.U - outline.Min.U) * 304.8, 2),
                    ["sheet_height_mm"] = Math.Round((outline.Max.V - outline.Min.V) * 304.8, 2),
                    ["orientation"] = outline.Max.U - outline.Min.U >= outline.Max.V - outline.Min.V ? "landscape" : "portrait"
                });
                doc.Delete(sheet.Id);
            }
        }
        finally
        {
            if (ownsTransaction) transaction!.RollBack();
        }
        return result;
    }

    public static void ValidatePlans(Document doc, IEnumerable<JObject> plans, JArray errors)
    {
        foreach (var sheet in plans)
        {
            var name = sheet.Value<string>("name") ?? "<unnamed sheet>";
            if (sheet.Value<bool?>("learner_title_block_confirmed") != true)
                errors.Add("Sheet " + name + " requires learner_title_block_confirmed=true after showing the available title blocks and asking the learner which one to use.");
            var titleBlockId = sheet.Value<long?>("title_block_type_id");
            var titleBlock = titleBlockId.HasValue ? doc.GetElement(RevitIdCompatibility.Eid(titleBlockId.Value)) : null;
            if (titleBlock is not ElementType || titleBlock.Category?.Id.IntVal() != (int)BuiltInCategory.OST_TitleBlocks)
                errors.Add("title_block_type_id for " + name + " must resolve to a Title Block type.");

            foreach (var placement in (sheet["placements"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var viewId = placement.Value<long?>("view_id");
                var view = viewId.HasValue ? doc.GetElement(RevitIdCompatibility.Eid(viewId.Value)) as View : null;
                if (view == null || view.IsTemplate) errors.Add("Each placement on " + name + " must reference an existing non-template View.");
                var mode = placement.Value<string>("layout_mode") ?? string.Empty;
                if (placement.Value<bool?>("learner_layout_confirmed") != true)
                    errors.Add("Each placement on " + name + " requires learner_layout_confirmed=true after the learner reviews the crop/scale proposal.");
                if (string.Equals(mode, "manual", StringComparison.OrdinalIgnoreCase))
                {
                    if (placement["x_mm"] == null || placement["y_mm"] == null) errors.Add("Manual placement on " + name + " requires x_mm and y_mm.");
                    continue;
                }
                if (!string.Equals(mode, "fit_to_title_block", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("layout_mode on " + name + " must be fit_to_title_block or manual.");
                    continue;
                }
                var fitIds = (placement["fit_element_ids"] as JArray ?? new JArray()).Values<long>().Distinct().ToList();
                if (fitIds.Count == 0) errors.Add("fit_to_title_block on " + name + " requires fit_element_ids so grids or unrelated annotations do not control the scale.");
                foreach (var id in fitIds) if (doc.GetElement(RevitIdCompatibility.Eid(id)) == null) errors.Add("fit_element_id " + id + " does not exist for " + name + ".");
                var scales = (placement["allowed_scales"] as JArray ?? new JArray()).Values<int>().Distinct().ToList();
                if (scales.Count == 0 || scales.Any(x => x <= 0)) errors.Add("fit_to_title_block on " + name + " requires positive allowed_scales.");
                foreach (var key in new[] { "edge_margin_mm", "reserved_left_mm", "reserved_right_mm", "reserved_bottom_mm", "reserved_top_mm", "crop_margin_mm" })
                    if ((placement.Value<double?>(key) ?? 0) < 0) errors.Add(key + " cannot be negative for " + name + ".");
            }
        }
    }

    public static JObject PlaceView(Document doc, ViewSheet sheet, JObject placement)
    {
        var view = doc.GetElement(RevitIdCompatibility.Eid(placement.Value<long>("view_id"))) as View
            ?? throw new CommandResultException(ErrorCodes.InvalidParam, "view_id must resolve to a View.");
        var mode = placement.Value<string>("layout_mode") ?? string.Empty;
        if (string.Equals(mode, "manual", StringComparison.OrdinalIgnoreCase))
        {
            if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id)) throw new CommandResultException(ErrorCodes.InvalidParam, "The selected view cannot be added to this sheet or is already placed on another sheet.");
            var viewport = Viewport.Create(doc, sheet.Id, view.Id, new XYZ(placement.Value<double>("x_mm") / 304.8, placement.Value<double>("y_mm") / 304.8, 0));
            doc.Regenerate();
            return ReadBack(sheet, viewport, null, view.Scale, false);
        }

        var crop = CropToElements(doc, view, (placement["fit_element_ids"] as JArray ?? new JArray()).Values<long>(), placement.Value<double?>("crop_margin_mm") ?? 500);
        try
        {
            view.CropBoxActive = true;
            view.CropBoxVisible = false;
            view.CropBox = crop;
        }
        catch (Exception ex)
        {
            throw new CommandResultException(ErrorCodes.InvalidParam, "The selected view crop is read-only or controlled by its View Template. Adjust the template/crop choice before previewing the Sheet. " + ex.Message);
        }
        var region = UsableRegion(sheet, placement);
        if (region.MaxX <= region.MinX || region.MaxY <= region.MinY)
            throw new CommandResultException(ErrorCodes.InvalidParam, "The requested sheet margins/reserved title-block area leave no usable drawing region.");
        var center = new XYZ((region.MinX + region.MaxX) / 2, (region.MinY + region.MaxY) / 2, 0);
        if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id)) throw new CommandResultException(ErrorCodes.InvalidParam, "The selected view cannot be added to this sheet or is already placed on another sheet.");
        var viewportToFit = Viewport.Create(doc, sheet.Id, view.Id, center);
        var scales = (placement["allowed_scales"] as JArray ?? new JArray()).Values<int>().Distinct().ToList();
        foreach (var scale in scales)
        {
            try { view.Scale = scale; }
            catch (Exception ex) { throw new CommandResultException(ErrorCodes.InvalidParam, "The selected view scale is read-only or controlled by its View Template. Adjust the template/scale choice before previewing the Sheet. " + ex.Message); }
            doc.Regenerate();
            viewportToFit.SetBoxCenter(center);
            doc.Regenerate();
            var bounds = CombinedOutline(viewportToFit);
            if (!Fits(bounds, region)) continue;
            return ReadBack(sheet, viewportToFit, region, scale, true);
        }
        var last = CombinedOutline(viewportToFit);
        throw new CommandResultException(ErrorCodes.InvalidParam,
            "The view does not fit the selected title block at any allowed scale. Available region is " +
            Mm(region.MaxX - region.MinX) + " x " + Mm(region.MaxY - region.MinY) + " mm; viewport at 1:" + view.Scale +
            " is " + Mm(last.MaximumPoint.X - last.MinimumPoint.X) + " x " + Mm(last.MaximumPoint.Y - last.MinimumPoint.Y) + " mm. Add a smaller drawing scale, reduce crop scope/margins, or choose a larger title block, then preview again.");
    }

    private static BoundingBoxXYZ CropToElements(Document doc, View view, IEnumerable<long> ids, double marginMm)
    {
        var current = view.CropBox;
        var inverse = current.Transform.Inverse;
        var points = new List<XYZ>();
        foreach (var id in ids.Distinct())
        {
            var element = doc.GetElement(RevitIdCompatibility.Eid(id));
            var box = element?.get_BoundingBox(view) ?? element?.get_BoundingBox(null);
            if (box == null) continue;
            foreach (var x in new[] { box.Min.X, box.Max.X })
            foreach (var y in new[] { box.Min.Y, box.Max.Y })
            foreach (var z in new[] { box.Min.Z, box.Max.Z }) points.Add(inverse.OfPoint(new XYZ(x, y, z)));
        }
        if (points.Count == 0) throw new CommandResultException(ErrorCodes.InvalidParam, "fit_element_ids have no bounding-box evidence in the selected view.");
        var margin = marginMm / 304.8;
        current.Min = new XYZ(points.Min(x => x.X) - margin, points.Min(x => x.Y) - margin, current.Min.Z);
        current.Max = new XYZ(points.Max(x => x.X) + margin, points.Max(x => x.Y) + margin, current.Max.Z);
        return current;
    }

    private static SheetRegion UsableRegion(ViewSheet sheet, JObject placement)
    {
        var outline = sheet.Outline;
        var edge = (placement.Value<double?>("edge_margin_mm") ?? 10) / 304.8;
        return new SheetRegion
        {
            MinX = outline.Min.U + edge + (placement.Value<double?>("reserved_left_mm") ?? 0) / 304.8,
            MaxX = outline.Max.U - edge - (placement.Value<double?>("reserved_right_mm") ?? 0) / 304.8,
            MinY = outline.Min.V + edge + (placement.Value<double?>("reserved_bottom_mm") ?? 40) / 304.8,
            MaxY = outline.Max.V - edge - (placement.Value<double?>("reserved_top_mm") ?? 0) / 304.8
        };
    }

    private static Outline CombinedOutline(Viewport viewport)
    {
        var box = viewport.GetBoxOutline();
        try
        {
            var label = viewport.GetLabelOutline();
            return new Outline(
                new XYZ(Math.Min(box.MinimumPoint.X, label.MinimumPoint.X), Math.Min(box.MinimumPoint.Y, label.MinimumPoint.Y), 0),
                new XYZ(Math.Max(box.MaximumPoint.X, label.MaximumPoint.X), Math.Max(box.MaximumPoint.Y, label.MaximumPoint.Y), 0));
        }
        catch { return box; }
    }

    private static bool Fits(Outline bounds, SheetRegion region) =>
        bounds.MinimumPoint.X >= region.MinX && bounds.MaximumPoint.X <= region.MaxX &&
        bounds.MinimumPoint.Y >= region.MinY && bounds.MaximumPoint.Y <= region.MaxY;

    private static JObject ReadBack(ViewSheet sheet, Viewport viewport, SheetRegion? region, int scale, bool autoFit)
    {
        var bounds = CombinedOutline(viewport);
        return new JObject
        {
            ["viewport_id"] = viewport.Id.Val(), ["sheet_id"] = sheet.Id.Val(), ["view_id"] = viewport.ViewId.Val(),
            ["auto_fit"] = autoFit, ["selected_scale"] = scale, ["fits_usable_region"] = region == null || Fits(bounds, region),
            ["viewport_bounds_mm"] = Bounds(bounds),
            ["usable_region_mm"] = region == null ? null : new JObject { ["min_x"] = Mm(region.MinX), ["min_y"] = Mm(region.MinY), ["max_x"] = Mm(region.MaxX), ["max_y"] = Mm(region.MaxY), ["width"] = Mm(region.MaxX - region.MinX), ["height"] = Mm(region.MaxY - region.MinY) }
        };
    }

    private static JObject Bounds(Outline outline) => new()
    {
        ["min_x"] = Mm(outline.MinimumPoint.X), ["min_y"] = Mm(outline.MinimumPoint.Y),
        ["max_x"] = Mm(outline.MaximumPoint.X), ["max_y"] = Mm(outline.MaximumPoint.Y),
        ["width"] = Mm(outline.MaximumPoint.X - outline.MinimumPoint.X), ["height"] = Mm(outline.MaximumPoint.Y - outline.MinimumPoint.Y)
    };

    private static double Mm(double feet) => Math.Round(feet * 304.8, 2);
    private sealed class SheetRegion { public double MinX { get; set; } public double MaxX { get; set; } public double MinY { get; set; } public double MaxY { get; set; } }
}
