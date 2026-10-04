using DSCons.RevitMcp.Contracts;

static void Check(bool actual, string message) { if (!actual) throw new InvalidOperationException(message); }

Check(McpConstants.ServerName == "dscons-revit-mcp", "Unexpected MCP server name.");
Check(McpConstants.DefaultPort == 43827, "Unexpected bridge port.");
Check(McpConstants.PreviewLifetimeSeconds == 90, "Preview lifetime must be 90 seconds.");
Check(McpConstants.RequestTimeoutSeconds == 120, "Preview/apply transport timeout must allow a rolled-back Revit transaction to finish.");
Check(McpConstants.CreateSessionSecret() != McpConstants.CreateSessionSecret(), "Session secret must be random.");
Check(typeof(IRevitMcpRuntime).GetProperty(nameof(IRevitMcpRuntime.StatusText)) != null, "Runtime status contract is missing.");
var tool = new ToolCapability { Name = "mep_create_route", IsWrite = true, IsDestructive = false, Scope = ToolScopes.Document, RequiresFreshContext = true, Prerequisites = new List<string> { "Document context is fresh." } };
Check(tool.IsWrite && !tool.IsDestructive, "Capability read/write classification was lost.");
Check(tool.Scope == ToolScopes.Document && tool.RequiresFreshContext, "Capability scope/context metadata was lost.");
Check(tool.Prerequisites.Count == 1, "Capability prerequisites contract is invalid.");
var cache = new ToolCache(); cache.Tools.Add(tool); Check(cache.Tools.Count == 1, "Tool cache contract is invalid.");
Check(MepRoutePlanner.Validate(new[] { new MepPointMm(0, 0, 0), new MepPointMm(1000, 0, 0), new MepPointMm(1000, 500, 0) }).IsValid, "Orthogonal route should be valid.");
Check(!MepRoutePlanner.Validate(new[] { new MepPointMm(0, 0, 0), new MepPointMm(1000, 500, 0) }).IsValid, "Diagonal route must be rejected.");
Check(!MepRoutePlanner.Validate(new[] { new MepPointMm(0, 0, 0), new MepPointMm(1000, 0, 0), new MepPointMm(2000, 0, 0) }).IsValid, "Redundant collinear corner must be rejected.");
Console.WriteLine("DSCons MCP contract tests passed.");
