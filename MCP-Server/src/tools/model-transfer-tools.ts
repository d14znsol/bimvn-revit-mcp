import type { Tool } from "@modelcontextprotocol/sdk/types.js";
import { defineTool, emptySchema, idsSchema } from "./common.js";

const sourceKey = { type: "string", pattern: "^[a-z][a-z0-9_-]{0,99}$" } as const;

export function registerModelTransferTools(): Tool[] {
  return [
    defineTool("model_transfer_extract", "Đọc snapshot ModelTransferPackage cục bộ từ Revit 2025: MEP route/equipment, resource, physical connector và dependency group. Không ghi model hay tải dữ liệu ra ngoài.", {
      type: "object", properties: { expected_source_revit_version: { enum: ["2025"] }, scope_element_ids: idsSchema, include_connected_network: { type: "boolean" }, max_elements: { type: "integer", minimum: 1, maximum: 500 } }, required: ["expected_source_revit_version", "scope_element_ids"], additionalProperties: false,
    }),
    defineTool("model_transfer_destination_catalog", "Đọc catalog đích 1.1 từ Revit 2023 để mapping rõ Level, MEP Type/System Type; FamilySymbol chỉ là inventory chưa tương thích cho tới khi có chữ ký kiểm chứng. Không chọn theo tên hoặc sửa model.", {
      type: "object", properties: { expected_target_revit_version: { enum: ["2023"] }, limit: { type: "integer", minimum: 1, maximum: 5000 } }, required: ["expected_target_revit_version"], additionalProperties: false,
    }),
    defineTool("model_transfer_plan", "Lập ModelTransferPlan 2025→2023 từ package/catalog 1.1 checksum-bound và mapping ID đích được duyệt. Chỉ cho route độc lập một đoạn ngang đủ size; chặn Family chưa có chữ ký, host/fitting/slope/transform hoặc mapping không tương thích.", {
      type: "object", properties: {
        package_id: { type: "string" }, catalog_id: { type: "string" }, staged_project_confirmed: { enum: [true] }, approved_group_keys: { type: "array", uniqueItems: true, items: { type: "object", properties: { group_key: sourceKey }, required: ["group_key"], additionalProperties: false } },
        tolerance_mm: { type: "number", exclusiveMinimum: 0, maximum: 0.1 },
        mappings: { type: "array", minItems: 1, maxItems: 5000, items: { type: "object", properties: { source_key: sourceKey, kind: { enum: ["level", "route_type", "system_type", "family_symbol", "envelope_type"] }, destination_id: { type: "integer", minimum: 1 } }, required: ["source_key", "kind", "destination_id"], additionalProperties: false } },
      }, required: ["package_id", "catalog_id", "staged_project_confirmed", "mappings"], additionalProperties: false,
    }),
    defineTool("model_transfer_preview", "Preview toàn bộ ModelTransferPlan đã duyệt bằng TransactionGroup rollback trên Project staging Revit 2023. Không tạo file, không Save/Sync và không cho partial network.", {
      type: "object", properties: { plan_id: { type: "string" }, context_id: { type: "string" }, destination_staging_confirmed: { enum: [true] } }, required: ["plan_id", "context_id", "destination_staging_confirmed"], additionalProperties: false,
    }),
    defineTool("model_transfer_apply", "Apply một preview chuyển model chưa dùng sau xác nhận của người dùng, rồi lập ModelTransferReport từ Revit post-commit read-back. Không Save/Sync; reopen comparison là gate riêng.", {
      type: "object", properties: { transfer_preview_id: { type: "string" } }, required: ["transfer_preview_id"], additionalProperties: false,
    }, true),
    defineTool("model_transfer_reopen_verify", "Đối chiếu read-only ModelTransferReport với Project staging Revit 2023 sau khi người dùng tự lưu bản đích riêng và mở lại. Không sửa model hay Save/Sync.", {
      type: "object", properties: { report_id: { type: "string" } }, required: ["report_id"], additionalProperties: false,
    }),
    defineTool("model_transfer_report", "Đọc lại ModelTransferPlan/preview cục bộ để giải thích trạng thái, nhóm bị chặn và điều kiện nghiệm thu; không chạm Revit.", {
      type: "object", properties: { plan_id: { type: "string" }, transfer_preview_id: { type: "string" }, report_id: { type: "string" } }, additionalProperties: false,
    }),
  ];
}
