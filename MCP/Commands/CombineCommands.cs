using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DSCons.RevitMcp.Contracts;
using DSCons.RevitMcp.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

/// <summary>In-session clash evidence. Records are deliberately short lived:
/// a changed document, active view, loaded link, or link transform invalidates
/// the result before it can be turned into an issue report or section.</summary>
internal static class CoordinationScanStore
{
    private sealed class Entry
    {
        public string DocumentFingerprint { get; set; } = string.Empty;
        public long ViewId { get; set; }
        public Dictionary<long, string> LinkFingerprints { get; set; } = new();
        public string ResourceFingerprint { get; set; } = string.Empty;
        public JArray Findings { get; set; } = new();
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }

    internal sealed class Scan
    {
        public string ScanId { get; set; } = string.Empty;
        public string ResourceFingerprint { get; set; } = string.Empty;
        public JArray Findings { get; set; } = new();
    }

    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    public static string Create(Document doc, IReadOnlyDictionary<long, string> links, JArray findings)
    {
        var id = "scan-" + Guid.NewGuid().ToString("N");
        var resource = CombineSafety.Fingerprint(string.Join("|", links.OrderBy(x => x.Key).Select(x => x.Key + ":" + x.Value)));
        lock (Gate)
        {
            Entries[id] = new Entry
            {
                DocumentFingerprint = MepSafety.DocumentFingerprint(doc),
                ViewId = doc.ActiveView.Id.Val(),
                LinkFingerprints = links.ToDictionary(pair => pair.Key, pair => pair.Value),
                ResourceFingerprint = resource,
                Findings = (JArray)findings.DeepClone(),
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15)
            };
        }
        return id;
    }

    public static Scan Require(Document doc, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new CommandResultException(ErrorCodes.InvalidParam, "scan_id is required.");
        Entry? entry;
        lock (Gate)
        {
            Entries.TryGetValue(id!, out entry);
            if (entry != null && entry.ExpiresAtUtc < DateTimeOffset.UtcNow) Entries.Remove(id!);
        }
        if (entry == null || entry.ExpiresAtUtc < DateTimeOffset.UtcNow)
            throw new CommandResultException(ErrorCodes.PreviewInvalid, "scan_id is unknown or expired; scan again.");
        if (!string.Equals(entry.DocumentFingerprint, MepSafety.DocumentFingerprint(doc), StringComparison.Ordinal) || entry.ViewId != doc.ActiveView.Id.Val())
            throw new CommandResultException(ErrorCodes.ContextInvalid, "Document or active view changed after the coordination scan. Re-anchor and scan again.");
        foreach (var link in entry.LinkFingerprints)
        {
            var instance = doc.GetElement(RevitIdCompatibility.Eid(link.Key)) as RevitLinkInstance;
            if (instance?.GetLinkDocument() == null || !string.Equals(link.Value, CombineSafety.LinkFingerprint(instance), StringComparison.Ordinal))
                throw new CommandResultException(ErrorCodes.ContextInvalid, "A linked document or transform changed after the coordination scan. Scan again.");
        }
        return new Scan { ScanId = id!, ResourceFingerprint = entry.ResourceFingerprint, Findings = (JArray)entry.Findings.DeepClone() };
    }
}

internal static class CombineSafety
{
    private static readonly Regex ReportName = new("^[A-Za-z0-9][A-Za-z0-9._ -]{0,79}$", RegexOptions.CultureInvariant);

    public static string Fingerprint(params string[] values)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("|", values)))).Replace("-", string.Empty).ToLowerInvariant();
    }

    public static string LocalOutputDirectory(JObject args)
    {
        var raw = args.Value<string>("approved_output_directory");
        if (string.IsNullOrWhiteSpace(raw))
            throw new CommandResultException(ErrorCodes.PathBlocked, "approved_output_directory must be an existing local absolute directory; UNC/network paths are blocked.");
        var safeRaw = raw!;
        if (!Path.IsPathRooted(safeRaw) || IsNetworkPath(safeRaw))
            throw new CommandResultException(ErrorCodes.PathBlocked, "approved_output_directory must be an existing local absolute directory; UNC/network paths are blocked.");
        var canonical = Path.GetFullPath(safeRaw);
        if (!Directory.Exists(canonical)) throw new CommandResultException(ErrorCodes.PathBlocked, "approved_output_directory does not exist.");
        return canonical;
    }

    public static (string Json, string Html) ReportPaths(string directory, string? requestedName)
    {
        var name = requestedName?.Trim() ?? string.Empty;
        if (!ReportName.IsMatch(name) || !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
            throw new CommandResultException(ErrorCodes.InvalidParam, "report_name must be a safe file stem (letters, numbers, spaces, dot, dash or underscore). ");
        var json = Path.GetFullPath(Path.Combine(directory, name + ".json"));
        var html = Path.GetFullPath(Path.Combine(directory, name + ".html"));
        RequireInside(directory, json); RequireInside(directory, html);
        return (json, html);
    }

    public static void RequireNoConflict(params string[] paths)
    {
        if (paths.Any(File.Exists)) throw new CommandResultException(ErrorCodes.FileConflict, "A requested issue report output already exists; overwrite is blocked.");
    }

    public static string Sha256(string path)
    {
        using var sha = SHA256.Create(); using var stream = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }

    public static string LinkFingerprint(RevitLinkInstance link)
    {
        var linked = link.GetLinkDocument();
        var transform = link.GetTransform();
        string Point(XYZ point) => string.Join(",", new[] { point.X, point.Y, point.Z }.Select(value => Math.Round(value, 8).ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        return Fingerprint(link.Id.Val().ToString(), linked?.Title ?? "<unloaded>", linked?.PathName ?? string.Empty, (linked?.IsModified ?? false).ToString(), Point(transform.Origin), Point(transform.BasisX), Point(transform.BasisY), Point(transform.BasisZ));
    }

    private static bool IsNetworkPath(string value) => value.StartsWith("\\\\", StringComparison.Ordinal) || value.StartsWith("//", StringComparison.Ordinal) || value.StartsWith("\\\\?\\", StringComparison.Ordinal);
    private static void RequireInside(string root, string path)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new CommandResultException(ErrorCodes.PathBlocked, "Report path is outside approved_output_directory.");
    }
}

internal sealed class CoordinationSolidScanCommand : ReadCommand
{
    public CoordinationSolidScanCommand() : base("coordination_solid_scan", "Revit Solid-intersection evidence after bounding-box prefilter; clearance remains triage only.") { }

    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); BimContextStore.Require(doc, args.Value<string>("context_id"));
        var scanMode = args.Value<string>("scan_mode") ?? string.Empty;
        if (scanMode != "solid_intersection" && scanMode != "clearance_triage") throw new CommandResultException(ErrorCodes.InvalidParam, "scan_mode must be solid_intersection or clearance_triage.");
        var clearanceFt = Math.Max(0, args.Value<double?>("clearance_mm") ?? 0) / 304.8;
        if (scanMode == "clearance_triage" && clearanceFt <= 0) throw new CommandResultException(ErrorCodes.InvalidParam, "clearance_mm must be positive for clearance_triage.");
        var toleranceFt3 = Math.Max(0.000000001, (args.Value<double?>("tolerance_mm3") ?? 1) / 28316846.592);
        var limit = Math.Min(1000, Math.Max(1, args.Value<int?>("limit") ?? 200));
        var categories = new HashSet<string>((args["linked_categories"] as JArray ?? new JArray()).Values<string>().Where(value => !string.IsNullOrWhiteSpace(value))!, StringComparer.OrdinalIgnoreCase);
        var requestedSources = (args["source_element_ids"] as JArray ?? new JArray()).Values<long>().Distinct().Select(RevitIdCompatibility.Eid).ToList();
        var sources = requestedSources.Count > 0
            ? requestedSources.Select(doc.GetElement).Where(element => element != null).Cast<Element>().Where(MepData.IsMep).ToList()
            : app.ActiveUIDocument!.Selection.GetElementIds().Select(doc.GetElement).Where(element => element != null).Cast<Element>().Where(MepData.IsMep).ToList();
        if (sources.Count == 0) sources = new FilteredElementCollector(doc).WhereElementIsNotElementType().Where(MepData.IsMep).Take(Math.Min(1000, limit * 4)).ToList();
        if (sources.Count == 0) throw new CommandResultException(ErrorCodes.InvalidParam, "No MEP source elements were supplied, selected or found in the active document.");
        if (requestedSources.Count > 0 && sources.Count != requestedSources.Count) throw new CommandResultException(ErrorCodes.InvalidParam, "Every source_element_id must resolve to an MEP element in the active document.");

        var requestedLinks = new HashSet<long>((args["link_instance_ids"] as JArray ?? new JArray()).Values<long>().Distinct());
        var links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()
            .Where(link => requestedLinks.Count == 0 || requestedLinks.Contains(link.Id.Val())).ToList();
        if (requestedLinks.Count > 0 && (links.Count != requestedLinks.Count || links.Any(link => link.GetLinkDocument() == null)))
            throw new CommandResultException(ErrorCodes.InvalidParam, "Every link_instance_id must resolve to a loaded Revit link.");
        links = links.Where(link => link.GetLinkDocument() != null).ToList();
        if (links.Count == 0) throw new CommandResultException(ErrorCodes.InvalidParam, "No loaded Revit links are available for this scan.");

        var findings = new JArray(); var duplicateKeys = new HashSet<string>(StringComparer.Ordinal); var linkFingerprints = new Dictionary<long, string>();
        var sourceSolids = new Dictionary<long, List<Solid>>();
        foreach (var link in links)
        {
            var linked = link.GetLinkDocument()!; var transform = link.GetTransform(); linkFingerprints[link.Id.Val()] = CombineSafety.LinkFingerprint(link);
            var candidates = new FilteredElementCollector(linked).WhereElementIsNotElementType()
                .Where(element => element.Category != null && (categories.Count == 0 || categories.Contains(element.Category.Name))).Take(5000).ToList();
            var linkedSolids = new Dictionary<long, List<Solid>>();
            foreach (var source in sources)
            {
                var sourceBounds = CoordinationGeometry.Bounds(source.get_BoundingBox(null), Transform.Identity); if (sourceBounds == null) continue;
                foreach (var candidate in candidates)
                {
                    var candidateBounds = CoordinationGeometry.Bounds(candidate.get_BoundingBox(null), transform);
                    if (candidateBounds == null || !sourceBounds.Expand(clearanceFt).Intersects(candidateBounds)) continue;
                    var key = source.Id.Val() + ":" + link.Id.Val() + ":" + candidate.Id.Val();
                    if (!duplicateKeys.Add(key)) continue;
                    if (scanMode == "clearance_triage")
                    {
                        findings.Add(Finding("clearance-" + key, source, candidate, link, sourceBounds.IntersectionCenter(candidateBounds), 0, "clearance_triage", "review", clearanceFt));
                    }
                    else
                    {
                        if (!sourceSolids.TryGetValue(source.Id.Val(), out var hostSolids)) sourceSolids[source.Id.Val()] = hostSolids = CoordinationGeometry.Solids(source, Transform.Identity);
                        if (!linkedSolids.TryGetValue(candidate.Id.Val(), out var candidateSolids)) linkedSolids[candidate.Id.Val()] = candidateSolids = CoordinationGeometry.Solids(candidate, transform);
                        var intersection = CoordinationGeometry.IntersectionVolume(hostSolids, candidateSolids, toleranceFt3);
                        if (intersection > toleranceFt3)
                            findings.Add(Finding("solid-" + key, source, candidate, link, sourceBounds.IntersectionCenter(candidateBounds), intersection, "solid_intersection", "high", clearanceFt));
                    }
                    if (findings.Count >= limit) break;
                }
                if (findings.Count >= limit) break;
            }
            if (findings.Count >= limit) break;
        }
        var scanId = CoordinationScanStore.Create(doc, linkFingerprints, findings);
        return new JObject
        {
            ["scan_id"] = scanId, ["expires_at_utc"] = DateTimeOffset.UtcNow.AddMinutes(15), ["source_element_count"] = sources.Count, ["loaded_link_count"] = links.Count,
            ["finding_count"] = findings.Count, ["truncated"] = findings.Count >= limit, ["scan_mode"] = scanMode, ["findings"] = findings,
            ["method"] = scanMode == "solid_intersection" ? "transformed bounding-box prefilter followed by Revit Solid intersection" : "transformed bounding-box clearance_triage",
            ["disclaimer"] = scanMode == "solid_intersection" ? "Only findings with positive Solid intersection volume are exact-clash evidence. They remain unreviewed engineering issues and never trigger rerouting." : "clearance_triage is conservative review evidence only, not a solid-intersection certificate or an automatic clearance rule."
        };
    }

    private static JObject Finding(string id, Element source, Element linked, RevitLinkInstance link, XYZ location, double volumeFt3, string method, string confidence, double clearanceFt) => new()
    {
        ["finding_id"] = id, ["status"] = "unreviewed", ["method"] = method, ["confidence"] = confidence,
        ["source_element_id"] = source.Id.Val(), ["source_category"] = source.Category?.Name, ["source_system"] = MepData.SystemName(source),
        ["linked_element_id"] = linked.Id.Val(), ["linked_category"] = linked.Category?.Name, ["linked_system"] = MepData.SystemName(linked), ["link_instance_id"] = link.Id.Val(), ["link_document"] = link.GetLinkDocument()?.Title,
        ["location"] = MepData.Point(location), ["intersection_volume_mm3"] = Math.Round(volumeFt3 * 28316846.592, 3), ["clearance_mm"] = Math.Round(clearanceFt * 304.8, 2)
    };
}

internal static class CoordinationGeometry
{
    internal sealed class Box
    {
        public XYZ Min { get; set; } = XYZ.Zero;
        public XYZ Max { get; set; } = XYZ.Zero;
        public Box Expand(double amount) => new() { Min = new XYZ(Min.X - amount, Min.Y - amount, Min.Z - amount), Max = new XYZ(Max.X + amount, Max.Y + amount, Max.Z + amount) };
        public bool Intersects(Box other) => Min.X <= other.Max.X && Max.X >= other.Min.X && Min.Y <= other.Max.Y && Max.Y >= other.Min.Y && Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;
        public XYZ IntersectionCenter(Box other) => new((Math.Max(Min.X, other.Min.X) + Math.Min(Max.X, other.Max.X)) / 2, (Math.Max(Min.Y, other.Min.Y) + Math.Min(Max.Y, other.Max.Y)) / 2, (Math.Max(Min.Z, other.Min.Z) + Math.Min(Max.Z, other.Max.Z)) / 2);
    }

    public static Box? Bounds(BoundingBoxXYZ? box, Transform transform)
    {
        if (box == null) return null;
        var corners = new[] { new XYZ(box.Min.X, box.Min.Y, box.Min.Z), new XYZ(box.Min.X, box.Min.Y, box.Max.Z), new XYZ(box.Min.X, box.Max.Y, box.Min.Z), new XYZ(box.Min.X, box.Max.Y, box.Max.Z), new XYZ(box.Max.X, box.Min.Y, box.Min.Z), new XYZ(box.Max.X, box.Min.Y, box.Max.Z), new XYZ(box.Max.X, box.Max.Y, box.Min.Z), new XYZ(box.Max.X, box.Max.Y, box.Max.Z) }.Select(transform.OfPoint).ToList();
        return new Box { Min = new XYZ(corners.Min(point => point.X), corners.Min(point => point.Y), corners.Min(point => point.Z)), Max = new XYZ(corners.Max(point => point.X), corners.Max(point => point.Y), corners.Max(point => point.Z)) };
    }

    public static List<Solid> Solids(Element element, Transform transform)
    {
        var result = new List<Solid>(); var geometry = element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false, ComputeReferences = false });
        if (geometry != null) Collect(geometry, transform, result);
        return result;
    }

    // GeometryInstance traversal avoids silently ignoring nested family solids.
    private static void Collect(GeometryElement geometry, Transform transform, ICollection<Solid> result)
    {
        foreach (var item in geometry)
        {
            if (item is Solid solid && solid.Faces.Size > 0 && solid.Volume > 0)
            {
                try { result.Add(SolidUtils.CreateTransformed(solid, transform)); } catch (Autodesk.Revit.Exceptions.ArgumentException) { }
            }
            else if (item is GeometryInstance instance)
            {
                try { Collect(instance.GetInstanceGeometry(), transform, result); } catch (Autodesk.Revit.Exceptions.InvalidOperationException) { }
            }
        }
    }

    public static double IntersectionVolume(IEnumerable<Solid> first, IEnumerable<Solid> second, double toleranceFt3)
    {
        var volume = 0d;
        foreach (var a in first)
        foreach (var b in second)
        {
            try
            {
                var intersection = BooleanOperationsUtils.ExecuteBooleanOperation(a, b, BooleanOperationsType.Intersect);
                if (intersection != null && intersection.Faces.Size > 0 && intersection.Volume > toleranceFt3) volume += intersection.Volume;
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException) { }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { }
        }
        return volume;
    }
}

internal sealed class CoordinationIssueReportPreviewCommand : ReadCommand
{
    public CoordinationIssueReportPreviewCommand() : base("coordination_issue_report_preview", "Preview reviewed clash issue report without creating a file.") { }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); BimContextStore.Require(doc, args.Value<string>("context_id")); MepSafety.GuardWrite(doc, Enumerable.Empty<Element>());
        var scan = CoordinationScanStore.Require(doc, args.Value<string>("scan_id"));
        var output = CombineSafety.LocalOutputDirectory(args); var paths = CombineSafety.ReportPaths(output, args.Value<string>("report_name")); CombineSafety.RequireNoConflict(paths.Json, paths.Html);
        var report = BuildReport(scan, args);
        var token = new PreviewToken
        {
            PreviewId = Guid.NewGuid().ToString("N"), Operation = "coordination_issue_report", ArgumentsJson = args.ToString(Formatting.None), DocumentFingerprint = MepSafety.DocumentFingerprint(doc),
            ResourceFingerprint = CombineSafety.Fingerprint(scan.ResourceFingerprint, output, paths.Json, paths.Html), ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(McpConstants.PreviewLifetimeSeconds)
        };
        BridgeServer.Current?.Store(token);
        return new JObject { ["preview_id"] = token.PreviewId, ["scan_id"] = scan.ScanId, ["expires_at_utc"] = token.ExpiresAtUtc, ["model_changed"] = false, ["files_written"] = false, ["report_summary"] = new JObject { ["finding_count"] = scan.Findings.Count, ["decision_count"] = ((JArray)report["issues"]!).Count, ["json_path"] = paths.Json, ["html_path"] = paths.Html }, ["next"] = "Require the engineer's confirmation before coordination_issue_report_apply." };
    }

    internal static JObject BuildReport(CoordinationScanStore.Scan scan, JObject args)
    {
        if (scan.Findings.Count == 0) throw new CommandResultException(ErrorCodes.InvalidParam, "The scan has no findings to report.");
        var findings = scan.Findings.OfType<JObject>().ToDictionary(item => item.Value<string>("finding_id") ?? string.Empty, StringComparer.Ordinal);
        var decisions = (args["decisions"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        if (decisions.Count != findings.Count) throw new CommandResultException(ErrorCodes.InvalidParam, "A decision is required for every scan finding.");
        var issues = new JArray(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var decision in decisions)
        {
            var id = decision.Value<string>("finding_id") ?? string.Empty;
            if (!findings.TryGetValue(id, out var finding) || !seen.Add(id)) throw new CommandResultException(ErrorCodes.InvalidParam, "Each decision must reference one unique finding_id from the scan.");
            issues.Add(new JObject { ["finding"] = finding.DeepClone(), ["engineer_decision"] = decision.DeepClone() });
        }
        return new JObject { ["report_kind"] = "dscons_combine_issue_report_v1", ["scan_id"] = scan.ScanId, ["generated_at_utc"] = DateTimeOffset.UtcNow, ["issues"] = issues, ["citation_policy"] = "Citation stores only canonical path, SHA256 and heading. Private course content is not copied into this report." };
    }
}

internal sealed class CoordinationIssueReportApplyCommand : ReadCommand
{
    public CoordinationIssueReportApplyCommand() : base("coordination_issue_report_apply", "Write a confirmed issue report with staging, checksum and cleanup.") { Capability.IsWrite = true; }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); var id = args.Value<string>("preview_id");
        if (string.IsNullOrWhiteSpace(id) || BridgeServer.Current == null || !BridgeServer.Current.TryTake(id!, out var token)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Issue report preview is missing, expired or already used.");
        if (token.ExpiresAtUtc < DateTimeOffset.UtcNow) throw new CommandResultException(ErrorCodes.PreviewExpired, "Issue report preview expired; preview again.");
        if (!string.Equals(token.Operation, "coordination_issue_report", StringComparison.Ordinal) || !string.Equals(token.DocumentFingerprint, MepSafety.DocumentFingerprint(doc), StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Document or operation fingerprint changed; preview again.");
        MepSafety.GuardWrite(doc, Enumerable.Empty<Element>());
        var previewArgs = JObject.Parse(token.ArgumentsJson); BimContextStore.Require(doc, previewArgs.Value<string>("context_id"));
        var scan = CoordinationScanStore.Require(doc, previewArgs.Value<string>("scan_id")); var output = CombineSafety.LocalOutputDirectory(previewArgs); var paths = CombineSafety.ReportPaths(output, previewArgs.Value<string>("report_name"));
        if (!string.Equals(token.ResourceFingerprint, CombineSafety.Fingerprint(scan.ResourceFingerprint, output, paths.Json, paths.Html), StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Scan or output resource fingerprint changed; preview again.");
        CombineSafety.RequireNoConflict(paths.Json, paths.Html); var report = CoordinationIssueReportPreviewCommand.BuildReport(scan, previewArgs);
        var guid = Guid.NewGuid().ToString("N"); var jsonStage = Path.Combine(output, "." + guid + ".issue.json.stage"); var htmlStage = Path.Combine(output, "." + guid + ".issue.html.stage");
        var published = new List<string>();
        try
        {
            File.WriteAllText(jsonStage, report.ToString(Formatting.Indented), new UTF8Encoding(false));
            File.WriteAllText(htmlStage, IssueHtml(report), new UTF8Encoding(false));
            File.Move(jsonStage, paths.Json); published.Add(paths.Json); File.Move(htmlStage, paths.Html); published.Add(paths.Html);
            if (!File.Exists(paths.Json) || !File.Exists(paths.Html)) throw new CommandResultException(ErrorCodes.VerificationFailed, "Issue report publish could not be read back.");
            return new JObject { ["preview_id"] = token.PreviewId, ["model_changed"] = false, ["files"] = new JObject { ["json"] = new JObject { ["path"] = paths.Json, ["sha256"] = CombineSafety.Sha256(paths.Json) }, ["html"] = new JObject { ["path"] = paths.Html, ["sha256"] = CombineSafety.Sha256(paths.Html) } }, ["verification"] = new JObject { ["verified"] = true, ["mode"] = "post_publish_read_back", ["no_overwrite"] = true } };
        }
        catch (CommandResultException) { Cleanup(jsonStage, htmlStage, published); throw; }
        catch (Exception ex) { Cleanup(jsonStage, htmlStage, published); throw new CommandResultException(ErrorCodes.TransactionFailed, "Issue report staging/publish failed and created files were cleaned up: " + ex.Message); }
    }

    private static string IssueHtml(JObject report)
    {
        var rows = ((JArray)report["issues"]!).OfType<JObject>().Select(issue =>
        {
            var finding = issue["finding"] as JObject ?? new JObject(); var decision = issue["engineer_decision"] as JObject ?? new JObject();
            return "<tr><td>" + System.Net.WebUtility.HtmlEncode(finding.Value<string>("finding_id")) + "</td><td>" + System.Net.WebUtility.HtmlEncode(decision.Value<string>("decision")) + "</td><td>" + System.Net.WebUtility.HtmlEncode(decision.Value<string>("priority")) + "</td><td>" + System.Net.WebUtility.HtmlEncode(decision.Value<string>("note")) + "</td></tr>";
        });
        return "<!doctype html><html><head><meta charset=\"utf-8\"><title>DSCons Combine Issue Report</title></head><body><h1>DSCons Combine Issue Report</h1><p>Scan: " + System.Net.WebUtility.HtmlEncode(report.Value<string>("scan_id")) + "</p><table border=\"1\"><thead><tr><th>Finding</th><th>Decision</th><th>Priority</th><th>Note</th></tr></thead><tbody>" + string.Concat(rows) + "</tbody></table><p>Private course content is not embedded; citations remain path/checksum/heading evidence only.</p></body></html>";
    }
    private static void Cleanup(string jsonStage, string htmlStage, IEnumerable<string> published)
    {
        foreach (var path in published.Concat(new[] { jsonStage, htmlStage })) try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

internal sealed class CoordinationSectionPreviewCommand : ReadCommand
{
    public CoordinationSectionPreviewCommand() : base("coordination_section_preview", "Preview Combine review sections inside a rolled-back TransactionGroup.") { Capability.IsWrite = true; }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); BimContextStore.Require(doc, args.Value<string>("context_id")); MepSafety.GuardWrite(doc, SectionTargets(doc, args));
        var scan = CoordinationScanStore.Require(doc, args.Value<string>("scan_id")); var plans = Plans(doc, scan, args); var validation = Preview(doc, plans);
        var token = new PreviewToken { PreviewId = Guid.NewGuid().ToString("N"), Operation = "coordination_section", ArgumentsJson = args.ToString(Formatting.None), DocumentFingerprint = MepSafety.DocumentFingerprint(doc), TargetFingerprint = MepSafety.TargetFingerprint(SectionTargets(doc, args)), ResourceFingerprint = CombineSafety.Fingerprint(scan.ResourceFingerprint, string.Join("|", plans.Select(plan => plan.FindingId + ":" + plan.Name + ":" + plan.Point))), ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(McpConstants.PreviewLifetimeSeconds) };
        BridgeServer.Current?.Store(token);
        return new JObject { ["preview_id"] = token.PreviewId, ["scan_id"] = scan.ScanId, ["expires_at_utc"] = token.ExpiresAtUtc, ["model_changed"] = false, ["validation_level"] = "revit_transactiongroup_rollback", ["sections"] = validation, ["next"] = "Wait for the exact confirmation XÁC NHẬN TẠO MẶT CẮT COMBINE before coordination_section_apply." };
    }

    internal static IEnumerable<Element> SectionTargets(Document doc, JObject args)
    {
        var ids = new List<long> { args.Value<long>("view_family_type_id") }; if (args.Value<long?>("view_template_id") is long template) ids.Add(template);
        return ids.Distinct().Select(RevitIdCompatibility.Eid).Select(doc.GetElement).Where(element => element != null)!;
    }

    internal static List<SectionPlan> Plans(Document doc, CoordinationScanStore.Scan scan, JObject args)
    {
        var type = doc.GetElement(RevitIdCompatibility.Eid(args.Value<long>("view_family_type_id"))) as ViewFamilyType;
        if (type == null || type.ViewFamily != ViewFamily.Section) throw new CommandResultException(ErrorCodes.InvalidParam, "view_family_type_id must reference an existing Section ViewFamilyType.");
        var template = RevitIdCompatibility.Eid(args.Value<long?>("view_template_id") ?? ElementId.InvalidElementId.Val());
        if (template != ElementId.InvalidElementId)
        {
            var viewTemplate = doc.GetElement(template) as View;
            if (viewTemplate == null || !viewTemplate.IsTemplate) throw new CommandResultException(ErrorCodes.InvalidParam, "view_template_id must reference a View Template in the active document.");
        }
        var findingIds = (args["finding_ids"] as JArray ?? new JArray()).Values<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .ToList(); if (findingIds.Count == 0) throw new CommandResultException(ErrorCodes.InvalidParam, "finding_ids is required.");
        var findings = scan.Findings.OfType<JObject>().ToDictionary(item => item.Value<string>("finding_id") ?? string.Empty, StringComparer.Ordinal);
        var existing = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(view => view.Name), StringComparer.OrdinalIgnoreCase);
        var prefix = args.Value<string>("name_prefix")?.Trim() ?? string.Empty; var scale = args.Value<int>("scale"); var margin = args.Value<double>("crop_margin_mm") / 304.8; var depth = args.Value<double>("depth_mm") / 304.8;
        if (string.IsNullOrWhiteSpace(prefix) || scale < 1 || margin <= 0 || depth <= 0) throw new CommandResultException(ErrorCodes.InvalidParam, "name_prefix, scale, crop_margin_mm and depth_mm must be positive valid values.");
        var plans = new List<SectionPlan>();
        foreach (var id in findingIds)
        {
            if (!findings.TryGetValue(id, out var finding) || finding == null) throw new CommandResultException(ErrorCodes.InvalidParam, "finding_ids must belong to scan_id.");
            var point = MepData.PointMm(finding["location"] as JObject ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Finding does not contain a usable host location."));
            var suffix = id.Length > 24 ? id.Substring(id.Length - 24) : id; var name = prefix + "-" + suffix;
            if (name.Length > 120 || !existing.Add(name)) throw new CommandResultException(ErrorCodes.FileConflict, "Section view name already exists or is invalid: " + name);
            plans.Add(new SectionPlan { FindingId = id, Point = point, Name = name, ViewFamilyTypeId = type.Id, ViewTemplateId = template, Scale = scale, MarginFt = margin, DepthFt = depth });
        }
        return plans;
    }

    internal static JArray Preview(Document doc, IReadOnlyCollection<SectionPlan> plans)
    {
        using var revisionSuppression = DocumentRevisionTracker.Suppress();
        using var group = new TransactionGroup(doc, "Validate DSCons Combine section preview"); group.Start();
        try
        {
            List<ViewSection> views;
            using (var transaction = new Transaction(doc, "Simulate Combine sections"))
            {
                transaction.Start(); views = Create(doc, plans); if (transaction.Commit() != TransactionStatus.Committed) throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rejected the Combine section preview.");
            }
            var readBack = ReadBack(doc, views); group.RollBack(); return readBack;
        }
        catch (CommandResultException) { group.RollBack(); throw; }
        catch (Exception ex) { group.RollBack(); throw new CommandResultException(ErrorCodes.TransactionFailed, "Combine section preview rolled back: " + ex.Message); }
    }

    internal static List<ViewSection> Create(Document doc, IEnumerable<SectionPlan> plans)
    {
        var result = new List<ViewSection>();
        foreach (var plan in plans)
        {
            var box = new BoundingBoxXYZ { Transform = SectionTransform(plan.Point), Min = new XYZ(-plan.MarginFt, -plan.MarginFt, -plan.DepthFt / 2), Max = new XYZ(plan.MarginFt, plan.MarginFt, plan.DepthFt / 2), Enabled = true };
            var view = ViewSection.CreateSection(doc, plan.ViewFamilyTypeId, box); view.Name = plan.Name; view.Scale = plan.Scale; if (plan.ViewTemplateId != ElementId.InvalidElementId) view.ViewTemplateId = plan.ViewTemplateId; result.Add(view);
        }
        return result;
    }

    internal static JArray ReadBack(Document doc, IEnumerable<ViewSection> views) => new(views.Select(view =>
    {
        var crop = view.CropBox; var template = view.ViewTemplateId;
        return new JObject { ["view_id"] = view.Id.Val(), ["name"] = view.Name, ["scale"] = view.Scale, ["crop_width_mm"] = crop == null ? null : Math.Round((crop.Max.X - crop.Min.X) * 304.8, 2), ["crop_height_mm"] = crop == null ? null : Math.Round((crop.Max.Y - crop.Min.Y) * 304.8, 2), ["depth_mm"] = crop == null ? null : Math.Round((crop.Max.Z - crop.Min.Z) * 304.8, 2), ["view_template_id"] = template == ElementId.InvalidElementId ? null : template.Val(), ["view_template"] = template == ElementId.InvalidElementId ? null : (doc.GetElement(template) as View)?.Name };
    }));

    private static Transform SectionTransform(XYZ origin)
    {
        var transform = Transform.Identity; transform.Origin = origin; transform.BasisX = XYZ.BasisX; transform.BasisY = XYZ.BasisZ; transform.BasisZ = XYZ.BasisX.CrossProduct(XYZ.BasisZ); return transform;
    }

    internal sealed class SectionPlan
    {
        public string FindingId { get; set; } = string.Empty;
        public XYZ Point { get; set; } = XYZ.Zero;
        public string Name { get; set; } = string.Empty;
        public ElementId ViewFamilyTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId ViewTemplateId { get; set; } = ElementId.InvalidElementId;
        public int Scale { get; set; }
        public double MarginFt { get; set; }
        public double DepthFt { get; set; }
    }
}

internal sealed class CoordinationSectionApplyCommand : ReadCommand
{
    public CoordinationSectionApplyCommand() : base("coordination_section_apply", "Create confirmed Combine sections atomically and read them back.") { Capability.IsWrite = true; }
    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app); var id = args.Value<string>("preview_id");
        if (string.IsNullOrWhiteSpace(id) || BridgeServer.Current == null || !BridgeServer.Current.TryTake(id!, out var token)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Section preview is missing, expired or already used.");
        if (token.ExpiresAtUtc < DateTimeOffset.UtcNow) throw new CommandResultException(ErrorCodes.PreviewExpired, "Section preview expired; preview again.");
        if (!string.Equals(token.Operation, "coordination_section", StringComparison.Ordinal) || !string.Equals(token.DocumentFingerprint, MepSafety.DocumentFingerprint(doc), StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Document or operation fingerprint changed; preview again.");
        var previewArgs = JObject.Parse(token.ArgumentsJson); BimContextStore.Require(doc, previewArgs.Value<string>("context_id")); MepSafety.GuardWrite(doc, CoordinationSectionPreviewCommand.SectionTargets(doc, previewArgs));
        var scan = CoordinationScanStore.Require(doc, previewArgs.Value<string>("scan_id")); var plans = CoordinationSectionPreviewCommand.Plans(doc, scan, previewArgs);
        var resource = CombineSafety.Fingerprint(scan.ResourceFingerprint, string.Join("|", plans.Select(plan => plan.FindingId + ":" + plan.Name + ":" + plan.Point)));
        if (!string.Equals(token.ResourceFingerprint, resource, StringComparison.Ordinal) || !string.Equals(token.TargetFingerprint, MepSafety.TargetFingerprint(CoordinationSectionPreviewCommand.SectionTargets(doc, previewArgs)), StringComparison.Ordinal)) throw new CommandResultException(ErrorCodes.PreviewInvalid, "Section resource, type or template changed; preview again.");
        using var group = new TransactionGroup(doc, "DSCons Combine sections"); group.Start();
        try
        {
            List<ViewSection> views;
            using (var transaction = new Transaction(doc, "Create confirmed Combine sections"))
            {
                transaction.Start(); views = CoordinationSectionPreviewCommand.Create(doc, plans); if (transaction.Commit() != TransactionStatus.Committed) throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rejected the Combine section transaction.");
            }
            var readBack = CoordinationSectionPreviewCommand.ReadBack(doc, views); if (readBack.Count != plans.Count) throw new CommandResultException(ErrorCodes.VerificationFailed, "Not every created section could be read back before commit.");
            group.Assimilate(); return new JObject { ["preview_id"] = token.PreviewId, ["created_view_ids"] = new JArray(views.Select(view => view.Id.Val())), ["verification"] = new JObject { ["verified"] = true, ["mode"] = "post_commit_read_back", ["sections"] = readBack }, ["note"] = "No Save or Sync was called. Use documentation_plan then documentation_apply separately to place confirmed sections on a sheet." };
        }
        catch (CommandResultException) { group.RollBack(); throw; }
        catch (Exception ex) { group.RollBack(); throw new CommandResultException(ErrorCodes.TransactionFailed, "Combine section creation rolled back: " + ex.Message); }
    }
}
