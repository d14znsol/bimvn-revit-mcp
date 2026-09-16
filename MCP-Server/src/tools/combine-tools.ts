import type { Tool } from "@modelcontextprotocol/sdk/types.js";
import { defineTool } from "./common.js";

const id = { type: "integer", minimum: 1 } as const;
const ids = { type: "array", minItems: 1, maxItems: 200, items: id } as const;
const path = { type: "string", minLength: 1, maxLength: 4096 } as const;

const decisionSchema = {
  type: "object",
  properties: {
    finding_id: { type: "string", minLength: 1, maxLength: 120 },
    decision: { enum: ["real_clash", "false_positive", "accepted", "needs_information"] },
    priority: { enum: ["critical", "high", "medium", "low"] },
    protected_element_ids: { type: "array", items: id },
    movable_element_ids: { type: "array", items: id },
    note: { type: "string", maxLength: 4000 },
    knowledge_citations: { type: "array", maxItems: 10, items: { type: "object", properties: { canonical_path: path, sha256: { type: "string", pattern: "^[a-f0-9]{64}$" }, heading: { type: "string", maxLength: 300 } }, required: ["canonical_path", "sha256"], additionalProperties: false } },
  },
  required: ["finding_id", "decision", "priority", "protected_element_ids", "movable_element_ids", "note"],
  additionalProperties: false,
} as const;

/** Revit-first Combine/Shop tools. Solid detection is deliberately separate
 * from clearance triage so a conservative distance check cannot be presented
 * as a solid-intersection certificate. */
export function registerCombineTools(): Tool[] {
  return [
    defineTool("dscons_knowledge_search", "Tìm kiếm cục bộ trong thư mục kiến thức đã được người dùng duyệt; chỉ đọc TỔNG HỢP KHÓA HỌC.md và *_summary.md, không upload hay sao chép học liệu.", {
      type: "object",
      properties: { approved_knowledge_directory: path, query: { type: "string", minLength: 2, maxLength: 300 }, course_filter: { type: "string", minLength: 1, maxLength: 300 }, limit: { type: "integer", minimum: 1, maximum: 20 } },
      required: ["approved_knowledge_directory", "query"], additionalProperties: false,
    }),
    defineTool("coordination_solid_scan", "Quét va chạm Revit-first: bounding-box chỉ prefilter, Solid intersection là bằng chứng exact; clearance chỉ trả clearance_triage và không tự reroute.", {
      type: "object",
      properties: {
        context_id: { type: "string", minLength: 1 }, source_element_ids: { type: "array", maxItems: 200, items: id }, link_instance_ids: { type: "array", maxItems: 100, items: id }, linked_categories: { type: "array", maxItems: 100, items: { type: "string", minLength: 1, maxLength: 200 } }, scan_mode: { enum: ["solid_intersection", "clearance_triage"] }, clearance_mm: { type: "number", minimum: 0, maximum: 5000 }, tolerance_mm3: { type: "number", exclusiveMinimum: 0, maximum: 1000000 }, limit: { type: "integer", minimum: 1, maximum: 1000 },
      },
      required: ["context_id", "scan_mode"], additionalProperties: false,
    }),
    defineTool("coordination_issue_report_preview", "Preview báo cáo issue từ scan và quyết định của kỹ sư; chưa ghi JSON/HTML. Citation chỉ giữ path/checksum/heading, không sao chép nội dung khóa học.", {
      type: "object",
      properties: { context_id: { type: "string", minLength: 1 }, scan_id: { type: "string", minLength: 1 }, approved_output_directory: path, report_name: { type: "string", minLength: 1, maxLength: 80, pattern: "^[A-Za-z0-9][A-Za-z0-9._ -]*$" }, decisions: { type: "array", minItems: 1, maxItems: 1000, items: decisionSchema } },
      required: ["context_id", "scan_id", "approved_output_directory", "report_name", "decisions"], additionalProperties: false,
    }),
    defineTool("coordination_issue_report_apply", "Ghi duy nhất báo cáo issue đã preview vào staging GUID rồi publish JSON/HTML không-ghi-đè, checksum và read-back.", {
      type: "object", properties: { preview_id: { type: "string", minLength: 1 } }, required: ["preview_id"], additionalProperties: false,
    }, true),
    defineTool("coordination_section_preview", "Tạo tạm section quanh issue trong TransactionGroup rồi rollback; trả crop/depth/template để kỹ sư review trước khi tạo thật.", {
      type: "object",
      properties: { context_id: { type: "string", minLength: 1 }, scan_id: { type: "string", minLength: 1 }, finding_ids: { ...ids, maxItems: 30 }, view_family_type_id: id, view_template_id: id, scale: { type: "integer", minimum: 1, maximum: 1000 }, crop_margin_mm: { type: "number", exclusiveMinimum: 0, maximum: 10000 }, depth_mm: { type: "number", exclusiveMinimum: 0, maximum: 10000 }, name_prefix: { type: "string", minLength: 1, maxLength: 80, pattern: "^[A-Za-z0-9][A-Za-z0-9._ -]*$" } },
      required: ["context_id", "scan_id", "finding_ids", "view_family_type_id", "scale", "crop_margin_mm", "depth_mm", "name_prefix"], additionalProperties: false,
    }),
    defineTool("coordination_section_apply", "Chỉ tạo section Combine từ preview_id còn hiệu lực sau câu xác nhận chính xác “XÁC NHẬN TẠO MẶT CẮT COMBINE”; read-back trước assimilate, không Save/Sync.", {
      type: "object", properties: { preview_id: { type: "string", minLength: 1 } }, required: ["preview_id"], additionalProperties: false,
    }, true),
  ];
}
