# Migration and rollback

DSCons MCP hiện có artifact và migration path cho Revit 2019–2027. Runtime
support vẫn được ghi riêng theo từng năm; Revit 2027 là POC cho đến khi chạy
đủ copied-model runtime matrix.

```powershell
./scripts/build-mcp.ps1 -RevitVersion 2023
./scripts/test-contracts.ps1
# Chỉ khi người dùng đồng ý và toàn bộ Revit đã đóng:
./scripts/install-mcp.ps1 -RevitVersion 2023 -ConfirmInstall
```

Installer cài manifest của `MCP/` và chỉ thay các artifact MCP legacy của đúng Revit year: `mcp-servers-for-revit.addin`, `revit-mcp.addin`, `revit_mcp_plugin`. Trước khi thay, script backup tại `%LocalAppData%\DSCons\RevitMcp\migration-backup\<year>`. Không thao tác bất kỳ add-in khác.

Sau runtime test fail, rollback:

```powershell
./scripts/uninstall-mcp.ps1 -RevitVersion 2023 -ConfirmUninstall -RestoreLegacy
```

Preflight trước migration:

```powershell
.\scripts\preflight-mcp.ps1 -RevitVersion 2023
```

Khi Revit 2023 đã đóng hoàn toàn và người dùng xác nhận thay MCP cũ, chạy:

```powershell
.\scripts\install-mcp.ps1 -RevitVersion 2023 -ConfirmInstall
```

Không install/uninstall nếu bất kỳ process Revit đang chạy.
