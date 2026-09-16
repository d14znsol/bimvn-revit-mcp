import type { Tool } from "@modelcontextprotocol/sdk/types.js";
import { defineTool, emptySchema } from "./common.js";

const sourcePath = { type: "string", minLength: 1 } as const;
const lod = { enum: ["LOD_300"] } as const;
const familyKinds = ["axial_fan", "inline_fan", "centrifugal_fan", "fcu", "ahu", "pump", "air_terminal", "duct_accessory", "pipe_accessory", "plumbing_fixture", "sprinkler", "panel", "lighting_fixture", "conduit_junction_box", "cable_tray_fitting"] as const;

/** PDF/CAD evidence and adapter tools.  These schemas intentionally expose
 * only mapped fields; arbitrary Revit geometry/parameters are not accepted. */
export function registerFamilyPlatformTools(): Tool[] {
  return [
    defineTool("family_source_inspect", "Đọc metadata PDF/DWG/DXF cục bộ và tạo SourceEvidence; không dựng hoặc sửa model.", {
      type: "object", properties: { source_path: sourcePath, source_kind: { enum: ["pdf", "dwg", "dxf"] }, approved_demo_directory: sourcePath, page: { type: "integer", minimum: 1 }, layers: { type: "array", items: { type: "string" } } },
      required: ["source_path", "source_kind", "approved_demo_directory"], additionalProperties: false,
    }),
    defineTool("family_spec_preview", "Chuẩn hóa SourceEvidence thành FamilySpec với trường confirmed/uncertain/missing; cần người dùng xác nhận trước khi dựng.", {
      type: "object", properties: { evidence_id: { type: "string", minLength: 1 }, family_kind: { enum: familyKinds }, confirmed_fields: { type: "object" }, notes: { type: "string" } },
      required: ["evidence_id", "family_kind"], additionalProperties: false,
    }),
    defineTool("family_build_preview", "Preview Family adapter HVAC trong tài liệu tạm rồi rollback; trả geometry, parameter, formula, connector và finding.", {
      type: "object", properties: { spec_id: { type: "string", minLength: 1 }, family_kind: { enum: familyKinds }, template_path: { ...sourcePath, description: "Tùy chọn nâng cao; bỏ trống để MCP tự chọn template đúng Revit/category." }, approved_demo_directory: sourcePath, family_name: { type: "string", minLength: 1, maxLength: 80 }, type_name: { type: "string", minLength: 1, maxLength: 80 }, lod, connector_mode: { enum: ["none", "round_hvac"] } },
      required: ["spec_id", "family_kind", "approved_demo_directory", "family_name", "type_name", "lod"], additionalProperties: false,
    }, true),
    defineTool("family_build_apply", "Apply duy nhất preview Family đã xác nhận; staging GUID, reopen/checksum/read-back và không ghi đè output.", {
      type: "object", properties: { preview_id: { type: "string", minLength: 1 } }, required: ["preview_id"], additionalProperties: false,
    }, true),
    defineTool("cad_geometry_inspect", "Đọc units/layers/geometry bounds của DWG/DXF cục bộ và tạo CAD evidence; không import mù.", {
      type: "object", properties: { source_path: sourcePath, approved_demo_directory: sourcePath, units: { enum: ["mm", "cm", "m", "inch", "ft", "unknown"] }, layers: { type: "array", items: { type: "string" } } },
      required: ["source_path", "approved_demo_directory"], additionalProperties: false,
    }),
    defineTool("cad_to_revit_preview", "Map CAD evidence sang route/đối tượng Revit được adapter hỗ trợ và trả Change Set preview; geometry không map được thành finding.", {
      type: "object", properties: { evidence_id: { type: "string", minLength: 1 }, context_id: { type: "string", minLength: 1 }, mapping: { enum: ["route", "reference_only"] }, kind: { enum: ["pipe", "duct", "conduit", "cable_tray"] }, type_id: { type: "integer" }, system_type_id: { type: "integer" }, level_id: { type: "integer" }, confirm_units: { enum: ["mm", "cm", "m", "inch", "ft"] } },
      required: ["evidence_id", "context_id", "mapping", "confirm_units"], additionalProperties: false,
    }, true),
    defineTool("cad_to_revit_apply", "Apply atomic Change Set CAD đã xác nhận và trả post-commit verification/audit; không tự reroute.", {
      type: "object", properties: { preview_id: { type: "string", minLength: 1 } }, required: ["preview_id"], additionalProperties: false,
    }, true),
  ];
}
