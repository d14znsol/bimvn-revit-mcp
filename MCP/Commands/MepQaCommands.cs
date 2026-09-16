using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

/// <summary>
/// Read-only connectivity triage. A finding is intentionally a signal for
/// engineer review; open connectors may be valid route endpoints.
/// </summary>
internal sealed class MepConnectivityQaCommand : ReadCommand
{
    public MepConnectivityQaCommand() : base("mep_qa_connectivity", "Read-only QA scan for open MEP connectors and missing Pipe/Duct system assignments.") { }

    public override JObject Execute(UIApplication app, JObject args)
    {
        var doc = Document(app);
        var categories = new HashSet<string>((args["categories"] as JArray ?? new JArray()).Values<string>().Where(value => value != null)!, StringComparer.OrdinalIgnoreCase);
        var system = args.Value<string>("system_name") ?? string.Empty;
        var level = args.Value<string>("level_name") ?? string.Empty;
        var requested = args.Value<int?>("limit") ?? 200;
        var limit = Math.Min(1000, Math.Max(1, requested));
        var candidates = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .Where(MepData.IsMep)
            .Where(element => categories.Count == 0 || (element.Category != null && categories.Contains(element.Category.Name)))
            .Where(element => string.IsNullOrWhiteSpace(system) || MepData.SystemName(element).IndexOf(system, StringComparison.OrdinalIgnoreCase) >= 0)
            .Where(element => string.IsNullOrWhiteSpace(level) || string.Equals(doc.GetElement(element.LevelId)?.Name, level, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToList();

        var findings = new JArray();
        foreach (var element in candidates)
        {
            var connectors = MepData.Connectors(element).ToList();
            var open = connectors.Where(connector => !connector.IsConnected).ToList();
            if (open.Count > 0)
            {
                findings.Add(new JObject
                {
                    ["code"] = "OpenConnector",
                    ["severity"] = "review",
                    ["element_id"] = element.Id.Val(),
                    ["category"] = element.Category?.Name,
                    ["system_name"] = MepData.SystemName(element),
                    ["message"] = $"{open.Count} connector(s) are open. This can be intentional at a route endpoint and requires engineering review.",
                    ["connectors"] = new JArray(open.Select(MepData.ConnectorDetail))
                });
            }

            if ((element is Pipe || element is Duct) && string.IsNullOrWhiteSpace(MepData.SystemName(element)))
            {
                findings.Add(new JObject
                {
                    ["code"] = "MissingSystemAssignment",
                    ["severity"] = "review",
                    ["element_id"] = element.Id.Val(),
                    ["category"] = element.Category?.Name,
                    ["message"] = "Pipe or Duct has no readable System Name. Confirm the intended system assignment."
                });
            }
        }

        return new JObject
        {
            ["scope"] = "document",
            ["scanned_element_count"] = candidates.Count,
            ["finding_count"] = findings.Count,
            ["findings"] = findings,
            ["disclaimer"] = "Findings are read-only review signals. This tool does not repair or mutate the model."
        };
    }
}
