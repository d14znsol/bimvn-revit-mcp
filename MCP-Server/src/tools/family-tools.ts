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
    defineTool("family_shared_nested_probe_preview", "Tạm load/đặt Shared Nested Family vào Project copy, đổi Shared Parameter và kiểm field Schedule rồi rollback toàn bộ. Không tạo Tag hoặc thay đổi lưu lại.", {
      type: "object",
      properties: {
        expected_project_path: { type: "string", minLength: 1 },
        expected_view_id: { type: "integer" },
        copied_project_confirmed: { enum: [true] },
        approved_demo_directory: { type: "string", minLength: 1 },
        family_path: { type: "string", minLength: 1 },
        type_name: { type: "string", minLength: 1, maxLength: 80 },
        level_id: { type: "integer" },
        location: pointSchema,
        shared_parameter_guid: { type: "string", minLength: 36, maxLength: 36 },
        changed_value_mm: { type: "number", exclusiveMinimum: 0, maximum: 1000000 },
        expected_shared_child_count: { type: "integer", minimum: 1, maximum: 20 },
      },
      required: ["expected_project_path", "expected_view_id", "copied_project_confirmed", "approved_demo_directory", "family_path", "type_name", "level_id", "location", "shared_parameter_guid", "changed_value_mm", "expected_shared_child_count"],
      additionalProperties: false,
    }),
    defineTool("family_library_benchmark_preview", "Đo RFA byte/hash, flex min/nominal/max, load, đặt nhiều instance và regenerate cho một cặp Family monolithic/nested đã xác nhận tương đương; mọi thay đổi Family/Project đều rollback.", {
      type: "object",
      properties: {
        expected_project_path: { type: "string", minLength: 1 },
        expected_view_id: { type: "integer" },
        copied_project_confirmed: { enum: [true] },
        approved_demo_directory: { type: "string", minLength: 1 },
        level_id: { type: "integer" },
        location: pointSchema,
        isolated_origin_confirmed: { enum: [true], description: "Xác nhận origin cách xa phần tử thật; benchmark tạm đặt nhiều instance rồi rollback." },
        equivalent_function_confirmed: { enum: [true], description: "Xác nhận hai RFA cùng công năng/phạm vi. MCP chỉ kiểm category, placement và flex tối thiểu; không tự suy ra tương đương hình học." },
        instance_count: { type: "integer", minimum: 1, maximum: 100 },
        instance_spacing_mm: { type: "number", minimum: 100, maximum: 1000000 },
        families: {
          type: "array", minItems: 2, maxItems: 2,
          items: {
            type: "object",
            properties: {
              role: { enum: ["monolithic", "nested"] },
              family_path: { type: "string", minLength: 1 },
              type_name: { type: "string", minLength: 1, maxLength: 80 },
              flex_parameter: { type: "string", minLength: 1, maxLength: 128 },
              flex_cases: {
                type: "array", minItems: 3, maxItems: 3,
                items: {
                  type: "object",
                  properties: { name: { enum: ["min", "nominal", "max"] }, value_mm: { type: "number", exclusiveMinimum: 0, maximum: 1000000 } },
                  required: ["name", "value_mm"], additionalProperties: false,
                },
              },
            },
            required: ["role", "family_path", "type_name", "flex_parameter", "flex_cases"], additionalProperties: false,
          },
        },
      },
      required: ["expected_project_path", "expected_view_id", "copied_project_confirmed", "approved_demo_directory", "level_id", "location", "isolated_origin_confirmed", "equivalent_function_confirmed", "instance_count", "instance_spacing_mm", "families"],
      additionalProperties: false,
    }),
    defineTool("family_routing_probe_preview", "Temporarily verify that Revit selects the requested Duct/Pipe Fitting from Routing Preferences and connects every scenario port; rollback the whole copied Project transaction group.", {
      type: "object",
      properties: {
        expected_project_path: { type: "string", minLength: 1 },
        expected_view_id: { type: "integer" },
        copied_project_confirmed: { enum: [true], description: "Confirms the open file is a dedicated copied Project. The probe rolls back, but Revit still performs a temporary Project transaction." },
        approved_demo_directory: { type: "string", minLength: 1 },
        family_path: { type: "string", minLength: 1, description: "An RFA not already loaded in this copied Project; a same-name Family is blocked fail-closed." },
        type_name: { type: "string", minLength: 1, maxLength: 80 },
        curve_kind: { enum: ["pipe", "duct"] },
        curve_type_id: { type: "integer" },
        system_type_id: { type: "integer" },
        level_id: { type: "integer" },
        location: pointSchema,
      },
      required: ["expected_project_path", "expected_view_id", "copied_project_confirmed", "approved_demo_directory", "family_path", "type_name", "curve_kind", "curve_type_id", "system_type_id", "level_id", "location"],
      additionalProperties: false,
    }),
    defineTool("family_break_into_ui_preflight", "Read-only preflight for a controlled native Revit Break Into/Valve Breaks Into UI placement. It validates the RFA and returns a short-lived record; it never performs native UI placement by API.", {
      type: "object",
      properties: {
        expected_project_path: { type: "string", minLength: 1 },
        expected_view_id: { type: "integer" },
        copied_project_confirmed: { enum: [true], description: "Confirms a dedicated copied Project is open. The following UI operation may change this copied model." },
        approved_demo_directory: { type: "string", minLength: 1 },
        family_path: { type: "string", minLength: 1, description: "The RFA to be loaded/placed manually through Revit's native Break Into workflow." },
        type_name: { type: "string", minLength: 1, maxLength: 80 },
        curve_kind: { enum: ["pipe", "duct"] },
      },
      required: ["expected_project_path", "expected_view_id", "copied_project_confirmed", "approved_demo_directory", "family_path", "type_name", "curve_kind"],
      additionalProperties: false,
    }),
    defineTool("family_break_into_ui_verify", "Read back a manually placed native Break Into/Valve Breaks Into result: exact Family Type, two aligned physical ports and the two connected Pipe/Duct segments. It does not claim which UI command was clicked or system calculation.", {
      type: "object",
      properties: {
        preflight_id: { type: "string", minLength: 1 },
        accessory_instance_id: { type: "integer" },
        segment_ids: { type: "array", minItems: 2, maxItems: 2, items: { type: "integer" } },
      },
      required: ["preflight_id", "accessory_instance_id", "segment_ids"],
      additionalProperties: false,
    }),
    defineTool("family_hosting_ui_preflight", "Read-only preflight for controlled native Revit placement of a wall/ceiling/floor/roof-hosted or two-level Family. It validates exact RFA placement behaviour and target eligibility; it never places, rehosts or cuts by API.", {
      type: "object",
      properties: {
        expected_project_path: { type: "string", minLength: 1 },
        expected_view_id: { type: "integer" },
        copied_project_confirmed: { enum: [true], description: "Confirms a dedicated copied Project is open. The subsequent controlled UI placement may change only this copied model." },
        approved_demo_directory: { type: "string", minLength: 1 },
        family_path: { type: "string", minLength: 1 },
        type_name: { type: "string", minLength: 1, maxLength: 80 },
        host: { type: "object", properties: { mode: { enum: ["wall", "ceiling", "floor", "roof", "two_level"] }, host_element_id: { type: "integer" }, base_level_id: { type: "integer" }, top_level_id: { type: "integer" } }, required: ["mode"], additionalProperties: false },
        require_void_cut: { type: "boolean", description: "When true, only physical host modes are accepted and the RFA must expose Cut with Voids When Loaded plus a native void form." },
      },
      required: ["expected_project_path", "expected_view_id", "copied_project_confirmed", "approved_demo_directory", "family_path", "type_name", "host", "require_void_cut"],
      additionalProperties: false,
    }),
    defineTool("family_hosting_ui_verify", "Read back a manually placed hosted/two-level Family: exact Family/Type, expected host or Base/Top Levels, and optional void-cut relationship. It does not prove rehost history or rotate/mirror/type-change operations.", {
      type: "object",
      properties: { preflight_id: { type: "string", minLength: 1 }, instance_id: { type: "integer" } },
      required: ["preflight_id", "instance_id"],
      additionalProperties: false,
    }),
  ];
}
