# DSCons MCP installer layout

The canonical Revit manifest is [`../MCP/DSCons.RevitMcp.addin`](../MCP/DSCons.RevitMcp.addin). There are no version-specific `.addin` files.

`build-mcp.ps1` copies that manifest into each versioned artifact. `install-mcp.ps1` copies the manifest byte-for-byte into the selected Revit year only after an explicit confirmation and with a legacy backup.

The migration backup also covers the legacy manifest aliases `mcp-servers-for-revit.addin` and `revit-mcp.addin`, plus the `revit_mcp_plugin` folder.

Installed layout:

```text
%APPDATA%\Autodesk\Revit\Addins\2023\
  DSCons.RevitMcp.addin
  DSConsRevitMcp\
    DSCons.RevitMcp.dll              # stable loader
    DSCons.RevitMcp.Contracts.dll    # loader/runtime contract
    runtime\
      DSCons.RevitMcp.CoreRuntime.dll
      Newtonsoft.Json.dll
```
