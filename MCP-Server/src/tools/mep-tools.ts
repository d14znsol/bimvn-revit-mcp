import type { Tool } from "@modelcontextprotocol/sdk/types.js";
import { defineTool, idsSchema } from "./common.js";

const routeSchema: Tool["inputSchema"] = {
  type: "object",
  properties: {
    kind: { enum: ["pipe", "duct", "conduit", "cable_tray"] },
    type_id: { type: "integer" },
    system_type_id: { type: "integer" },
    level_id: { type: "integer" },
    diameter_mm: { type: "number", exclusiveMinimum: 0 },
    width_mm: { type: "number", exclusiveMinimum: 0 },
    height_mm: { type: "number", exclusiveMinimum: 0 },
    demo_tag: { type: "string", minLength: 1 },
    inline_accessory: {
      type: "object",
      properties: {
        type_id: { type: "integer" },
        segment_index: { type: "integer", minimum: 0 },
        offset_mm: { type: "number", exclusiveMinimum: 0 },
      },
      required: ["type_id", "segment_index", "offset_mm"],
      additionalProperties: false,
    },
    points: {
      type: "array",
      minItems: 2,
      items: {
        type: "object",
        properties: {
          x_mm: { type: "number" },
          y_mm: { type: "number" },
          z_mm: { type: "number" },
        },
        required: ["x_mm", "y_mm", "z_mm"],
        additionalProperties: false,
      },
    },
  },
  required: ["kind", "type_id", "level_id", "points"],
  additionalProperties: false,
};

const connectSchema: Tool["inputSchema"] = {
  type: "object",
  properties: {
    first_element_id: { type: "integer" },
    second_element_id: { type: "integer" },
  },
  required: ["first_element_id", "second_element_id"],
  additionalProperties: false,
};

export function registerMepTools(): Tool[] {
  return [
    defineTool("mep_preview", "Preview thao tác MEP trước khi thay đổi model.", {
      type: "object",
      properties: { operation: { type: "string" }, arguments: { type: "object" } },
      required: ["operation"],
      additionalProperties: false,
    }),
    defineTool("mep_apply_preview", "Apply một preview MEP còn hợp lệ.", {
      type: "object",
      properties: { preview_id: { type: "string" } },
      required: ["preview_id"],
      additionalProperties: false,
    }, true),
    defineTool("mep_create_route", "Tạo route Pipe, Duct, Conduit hoặc Cable Tray theo các đoạn vuông góc.", routeSchema, true),
    defineTool("mep_connect", "Nối hai element MEP và tạo fitting hợp lệ.", connectSchema, true),
    defineTool("mep_disconnect", "Ngắt hai connector MEP đang nối trực tiếp.", connectSchema, true),
    defineTool("mep_move_route", "Dịch route MEP theo millimetre.", {
      type: "object",
      properties: { element_ids: idsSchema, dx_mm: { type: "number" }, dy_mm: { type: "number" }, dz_mm: { type: "number" } },
      required: ["element_ids"],
      additionalProperties: false,
    }, true),
    defineTool("mep_change_type", "Đổi Type cho element MEP.", {
      type: "object",
      properties: { element_ids: idsSchema, type_id: { type: "integer" } },
      required: ["element_ids", "type_id"],
      additionalProperties: false,
    }, true),
    defineTool("mep_change_size", "Đổi kích thước MEP.", {
      type: "object",
      properties: { element_ids: idsSchema, diameter_mm: { type: "number" }, width_mm: { type: "number" }, height_mm: { type: "number" } },
      required: ["element_ids"],
      additionalProperties: false,
    }, true),
  ];
}
