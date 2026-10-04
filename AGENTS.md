# DSCons Revit MCP agent instructions

This is the canonical project router for all AI clients.

Read in this order before changing the project:

1. `PROJECT.md`
2. `README.md`
3. the relevant file under `docs/`

The maintainers' workspace may also contain a private `.agents/` ledger. When
it exists, read `.agents/AGENTS.md` and the files it names after `PROJECT.md`.
The public repository and learner workflow must never depend on that private
ledger being present.

The MCP server is `MCP-Server/`. The Revit add-in bridge is `MCP/`. The
`MCP.CoreRuntime/` project is an internal hot-reload implementation detail of
the add-in; it is not a second MCP server and must never be registered in an AI
client. This project is independent from `DSCons-MEP-Tools`.

Do not install, uninstall, configure an AI client, open or close Revit, or
modify a model without explicit user confirmation. Prefer read-only runtime
checks before any write operation. Keep the project ledger current.

For any answer or operation tied to live Revit state, re-anchor in the current
turn: call `document_info`, and also `get_active_view` or `get_selection` when
the claim/action depends on that view or selection. Before MEP writes, inspect
the current target/type/connector data and report the post-commit
`verification` read-back; never rely on context or element state from an
earlier turn.
