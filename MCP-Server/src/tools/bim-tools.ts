import type { Tool } from "@modelcontextprotocol/sdk/types.js";
import { defineTool, emptySchema, idsSchema } from "./common.js";

const pointSchema = {
  type: "object",
  properties: { x_mm: { type: "number" }, y_mm: { type: "number" }, z_mm: { type: "number" } },
  required: ["x_mm", "y_mm", "z_mm"],
  additionalProperties: false,
} as const;

const routeSchema = {
  type: "object",
  properties: {
    name: { type: "string" },
    kind: { enum: ["pipe", "duct", "conduit", "cable_tray"] },
    type_id: { type: "integer" }, system_type_id: { type: "integer" }, level_id: { type: "integer" },
    diameter_mm: { type: "number", exclusiveMinimum: 0 },
    demo_tag: { type: "string", minLength: 1 },
    inline_accessory: {
      type: "object",
      properties: { type_id: { type: "integer" }, segment_index: { type: "integer", minimum: 0 }, offset_mm: { type: "number", exclusiveMinimum: 0 } },
      required: ["type_id", "segment_index", "offset_mm"],
      additionalProperties: false,
    },
    points: { type: "array", minItems: 2, items: pointSchema },
  },
  required: ["kind", "type_id", "level_id", "points"],
  additionalProperties: false,
} as const;

const changeOperationSchema = {
  type: "object",
  properties: { operation: { type: "string" }, arguments: { type: "object" } },
  required: ["operation", "arguments"],
  additionalProperties: false,
} as const;

const scheduleSchema = {
  type: "object",
  properties: {
    name: { type: "string", minLength: 1 },
    category: { enum: ["pipes", "pipe_fittings", "pipe_accessories"] },
    fields: {
      type: "array", minItems: 1, uniqueItems: true,
      items: { enum: ["family_and_type", "system_type", "reference_level", "diameter", "length", "system_name", "system_abbreviation", "size", "area", "count"] },
    },
    sort_by: {
      type: "array", uniqueItems: true,
      items: {
        type: "object",
        properties: {
          field: { enum: ["family_and_type", "system_type", "reference_level", "diameter", "length", "system_name", "system_abbreviation", "size", "area", "count"] },
          order: { enum: ["ascending", "descending"] },
          show_header: { type: "boolean" }, show_footer: { type: "boolean" },
          show_footer_count: { type: "boolean" }, blank_line: { type: "boolean" },
        },
        required: ["field", "order"], additionalProperties: false,
      },
    },
    total_fields: {
      type: "array", uniqueItems: true,
      items: { enum: ["diameter", "length", "area", "count"] },
    },
    is_itemized: { type: "boolean" },
    show_grand_total_count: { type: "boolean" },
    learner_requirements_confirmed: { enum: [true] },
    filter: {
      type: "object",
      properties: { field: { enum: ["comments"] }, operator: { enum: ["equals"] }, value: { type: "string", minLength: 1 }, hidden: { enum: [true] } },
      required: ["field", "operator", "value", "hidden"],
      additionalProperties: false,
    },
  },
  required: ["name", "category", "fields", "sort_by", "total_fields", "learner_requirements_confirmed", "filter"],
  additionalProperties: false,
} as const;

const sheetPlacementSchema = {
  type: "object",
  properties: {
    view_id: { type: "integer" },
    layout_mode: { enum: ["fit_to_title_block", "manual"] },
    learner_layout_confirmed: { enum: [true] },
    x_mm: { type: "number" }, y_mm: { type: "number" },
    fit_element_ids: idsSchema,
    allowed_scales: { type: "array", minItems: 1, uniqueItems: true, items: { type: "integer", minimum: 1, maximum: 5000 } },
    edge_margin_mm: { type: "number", minimum: 0 }, crop_margin_mm: { type: "number", minimum: 0 },
    reserved_left_mm: { type: "number", minimum: 0 }, reserved_right_mm: { type: "number", minimum: 0 },
    reserved_bottom_mm: { type: "number", minimum: 0 }, reserved_top_mm: { type: "number", minimum: 0 },
  },
  required: ["view_id", "layout_mode", "learner_layout_confirmed"],
  additionalProperties: false,
} as const;

const sheetSchema = {
  type: "object",
  properties: {
    number: { type: "string" }, name: { type: "string" }, title_block_type_id: { type: "integer" },
    learner_title_block_confirmed: { enum: [true] },
    placements: { type: "array", items: sheetPlacementSchema },
  },
  required: ["number", "name", "title_block_type_id", "learner_title_block_confirmed"],
  additionalProperties: false,
} as const;

/** Tools for the V2 BIM production pipeline. Read tools deliberately return
 * evidence; write tools still use the Revit rollback-preview boundary. */
export function registerBimTools(): Tool[] {
  return [
    defineTool("bim_context_snapshot", "Đóng băng ngữ cảnh active document/view/selection thành context_id có thời hạn để liên kết các bước dựng hình, coordination, BOQ và hồ sơ.", emptySchema),
    defineTool("bim_model_catalog", "Liệt kê Level, MEP Type/System Type, MEP FamilySymbol, View Family Type, View Template và Title Block ID của model đang active để dựng hình/hồ sơ không phải đoán ID.", {
      type: "object", properties: { limit: { type: "integer", minimum: 1, maximum: 2000 } }, additionalProperties: false,
    }),
    defineTool("mep_network_explore", "Truy vết network MEP từ các element/connector gốc; trả topology, hệ thống, endpoint và evidence chỉ đọc.", {
      type: "object", properties: { element_ids: idsSchema, max_depth: { type: "integer", minimum: 1, maximum: 20 }, limit: { type: "integer", minimum: 1, maximum: 2000 } }, required: ["element_ids"], additionalProperties: false,
    }),
    defineTool("coordination_links", "Liệt kê Revit Link đang load để chuẩn bị combine/coordination; không chỉnh sửa link hay model.", emptySchema),
    defineTool("coordination_scan", "Quét coordination MEP với linked Architecture/Structure bằng bounding-box evidence. Kết quả là review issue, không tự reroute.", {
      type: "object", properties: { source_element_ids: idsSchema, linked_categories: { type: "array", items: { type: "string" } }, clearance_mm: { type: "number", minimum: 0, maximum: 5000 }, limit: { type: "integer", minimum: 1, maximum: 1000 } }, additionalProperties: false,
    }),
    defineTool("quantity_takeoff", "Bóc tách khối lượng model theo category/system/level/type, có length/count/area/volume và group breakdown truy xuất từ Revit.", {
      type: "object", properties: { categories: { type: "array", items: { type: "string" } }, system_name: { type: "string" }, level_name: { type: "string" }, group_by: { enum: ["category", "system", "level", "type"] }, limit: { type: "integer", minimum: 1, maximum: 10000 } }, additionalProperties: false,
    }),
    defineTool("documentation_plan", "Discovery field Schedule và các khung tên/kích thước đang có; kiểm tra lựa chọn khung, crop scope, vùng chừa ô tên và tỷ lệ của học viên trước preview/apply. Không tự chọn khung hoặc dùng tọa độ cố định.", {
      type: "object", properties: { schedule_discovery_categories: { type: "array", uniqueItems: true, items: { enum: ["pipes", "pipe_fittings", "pipe_accessories"] } }, discover_title_blocks: { type: "boolean" }, view_ids: idsSchema, view_template_id: { type: "integer" }, title_block_type_id: { type: "integer" }, schedules: { type: "array", items: scheduleSchema }, sheets: { type: "array", items: sheetSchema } }, additionalProperties: false,
    }),
    defineTool("model_create_batch", "Dựng nhiều route MEP từ specification trong một Change Set; tự preview rollback rồi mới apply khi gọi trực tiếp.", {
      type: "object", properties: { context_id: { type: "string" }, routes: { type: "array", minItems: 1, maxItems: 100, items: routeSchema } }, required: ["context_id", "routes"], additionalProperties: false,
    }, true),
    defineTool("bim_changeset_preview", "Preview một Change Set gồm các thao tác V1/V2 trong một Revit TransactionGroup rồi rollback toàn bộ.", {
      type: "object", properties: { context_id: { type: "string" }, name: { type: "string" }, operations: { type: "array", minItems: 1, maxItems: 50, items: changeOperationSchema } }, required: ["context_id", "operations"], additionalProperties: false,
    }),
    defineTool("bim_changeset_apply", "Apply một Change Set preview còn hiệu lực; mọi operation commit cùng nhau hoặc rollback toàn bộ.", {
      type: "object", properties: { preview_id: { type: "string" } }, required: ["preview_id"], additionalProperties: false,
    }, true),
    defineTool("documentation_apply", "Tạo Floor Plan, Schedule và Sheet từ lựa chọn đã xác nhận. fit_to_title_block crop theo element scope, thử allowed scales và chỉ PASS khi viewport kể cả nhãn nằm trong vùng khung hữu dụng. Preview trước, không Save/Sync.", {
      type: "object", properties: { context_id: { type: "string" }, view_template_id: { type: "integer" }, plan_views: { type: "array", items: { type: "object", properties: { level_id: { type: "integer" }, view_family_type_id: { type: "integer" }, name: { type: "string" } }, required: ["level_id", "view_family_type_id"], additionalProperties: false } }, schedules: { type: "array", items: scheduleSchema }, sheets: { type: "array", items: sheetSchema } }, required: ["context_id"], additionalProperties: false,
    }, true),
  ];
}
