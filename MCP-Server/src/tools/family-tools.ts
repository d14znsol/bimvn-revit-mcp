import type { Tool } from "@modelcontextprotocol/sdk/types.js";
import { defineTool, emptySchema } from "./common.js";

const pointSchema = {
  type: "object",
  properties: {
    x_mm: { type: "number" },
    y_mm: { type: "number" },
    z_mm: { type: "number" },
  },
  required: ["x_mm", "y_mm", "z_mm"],
  additionalProperties: false,
} as const;

/** A deliberately narrow Family capability: it creates only the approved
 * four-blade axial-fan POC and never exposes arbitrary Revit Family APIs. */
export function registerFamilyTools(): Tool[] {
  return [
    defineTool("family_inspect", "Đọc Family/Project đang active: loại document, category/template, unit, reference plane, parameter, hình học và connector.", emptySchema),
    defineTool("family_axial_fan_preview", "Mô phỏng Family quạt hướng trục 4 cánh trong Family document tạm, rollback toàn bộ và trả preview_id có thời hạn.", {
      type: "object",
      properties: {
        template_path: { type: "string", minLength: 1, description: "Tùy chọn nâng cao. Bỏ trống để MCP tự tìm Metric Mechanical Equipment.rft đúng phiên bản Revit." },
        approved_demo_directory: { type: "string", minLength: 1 },
        family_name: { type: "string", minLength: 1, maxLength: 80 },
        type_name: { type: "string", minLength: 1, maxLength: 80 },
        diameter_mm: { type: "number", exclusiveMinimum: 0 },
        length_mm: { type: "number", exclusiveMinimum: 0 },
        blade_count: { type: "integer", minimum: 4, maximum: 4 },
        type_code: { type: "string", minLength: 1, maxLength: 80 },
        connector_mode: { enum: ["none", "round_hvac"] },
      },
      required: ["approved_demo_directory", "family_name", "type_name", "diameter_mm", "length_mm", "blade_count", "type_code", "connector_mode"],
      additionalProperties: false,
    }),
    defineTool("family_axial_fan_apply", "Tạo và kiểm tra Family quạt từ preview_id hợp lệ, lưu .rfa vào thư mục demo đã duyệt và đọc lại kết quả.", {
      type: "object", properties: { preview_id: { type: "string", minLength: 1 } }, required: ["preview_id"], additionalProperties: false,
    }, true),
    defineTool("family_load_place_preview", "Mô phỏng load Family và đặt instance vào Project copy đã khai báo, rollback toàn bộ rồi trả preview_id.", {
      type: "object",
      properties: {
        expected_project_path: { type: "string", minLength: 1 },
        expected_view_id: { type: "integer" },
        approved_demo_directory: { type: "string", minLength: 1 },
        family_path: { type: "string", minLength: 1 },
        type_name: { type: "string", minLength: 1, maxLength: 80 },
        level_id: { type: "integer" },
        location: pointSchema,
      },
      required: ["expected_project_path", "expected_view_id", "approved_demo_directory", "family_path", "type_name", "level_id", "location"],
      additionalProperties: false,
    }),
    defineTool("family_load_place_apply", "Load và đặt Family từ preview_id hợp lệ; đọc lại Family, Type, Instance, Level và vị trí trước commit.", {
      type: "object", properties: { preview_id: { type: "string", minLength: 1 } }, required: ["preview_id"], additionalProperties: false,
    }, true),
  ];
}
