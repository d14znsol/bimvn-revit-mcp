using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using DSCons.RevitMcp.Contracts;

namespace DSCons.RevitMcp.Core;

internal interface IRevitCommand
{
    ToolCapability Capability { get; }
    JObject Execute(UIApplication application, JObject args);
}

internal sealed class CommandResultException : Exception
{
    public CommandResultException(string code, string message) : base(message) { Code = code; }
    public string Code { get; }
}
