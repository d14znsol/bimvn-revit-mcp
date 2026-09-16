# DSCons Revit MCP Add-in

This project is the Revit-side add-in bridge of DSCons MCP. Revit loads
`DSCons.RevitMcp.dll` through the canonical `MCP/DSCons.RevitMcp.addin`
manifest. The stable loader shadow/byte-loads
`MCP.CoreRuntime/DSCons.RevitMcp.CoreRuntime.dll`, which opens the loopback TCP
bridge and marshals every Revit API operation to the Revit main thread through
`ExternalEvent`.

The add-in is not the MCP Server and does not contain MCP client configuration
or call an AI API. Its public boundary is the local JSON-RPC bridge used by
`MCP-Server`. The Ribbon has Status, MCP On/Off and Reload Core controls. On/Off
starts or stops only the Revit CoreRuntime/bridge and its owned session; it does
not own or kill the Node stdio process started by an AI client. Reload Core
reloads runtime command changes without replacing the loader DLL.

Build a target version with:

```powershell
dotnet build .\MCP\DSCons.RevitMcp.csproj -c Release -p:RevitVersion=2027
dotnet build .\MCP\DSCons.RevitMcp.csproj -c Release -p:RevitVersion=2023
```
