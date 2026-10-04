---
name: bim-v2-production
audience: AI agent and BIM/MEP developer
---

# DSCons BIM Production V2 SOP

```text
Catalog + context → model batch/Change Set → coordination review
→ quantity snapshot → documented views/sheets
```

1. Call `document_info`, then `bim_context_snapshot` in the current turn. Pass its `context_id` to every V2 write.
2. Call `bim_model_catalog`; only use returned Level, Type, System Type, View Family Type, View Template and Title Block IDs from that document.
3. Send explicit route specifications to `model_create_batch`, or combine supported operations in `bim_changeset_preview`. Apply only a valid preview with `bim_changeset_apply`.
4. Before rerouting, call `coordination_links` and `coordination_scan`. Bounding-box findings are evidence for engineer review, not automatic route instructions.
5. Run `quantity_takeoff` only after reviewed changes. Treat output as a current-model snapshot; do not silently add wastage, pricing or procurement factors.
6. Call `documentation_plan` before `documentation_apply`. It may create Floor Plan views, apply an explicit template, create sheets and place explicitly identified existing views. It never Save/Syncs, publishes, prints or edits a model template.
7. Report post-commit `verification` and audit evidence after a write. One failed Change Set operation rolls back the full set.
