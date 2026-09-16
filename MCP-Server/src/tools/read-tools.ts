import type { Tool } from "@modelcontextprotocol/sdk/types.js";
import { defineTool, emptySchema, idsSchema } from "./common.js";

export function registerReadTools(): Tool[] {
  return [
    defineTool("system_status", "Trạng thái bridge, document và worksharing.", emptySchema),
    defineTool("document_info", "Thông tin document Revit đang active.", emptySchema),
    defineTool("get_active_view", "Thông tin active view.", emptySchema),
    defineTool("get_selection", "Các element đang được chọn trong Revit.", emptySchema),
    defineTool("get_capabilities", "Danh sách tool và phân loại read/write.", emptySchema),
    defineTool("mep_element_detail", "Category/Class, Family/Type, type_id, System, Level, size, parameters và bounding box MEP.", {
      type: "object",
      properties: { element_ids: idsSchema },
      required: ["element_ids"],
      additionalProperties: false,
    }),
    defineTool("mep_connector_network", "Connector, domain, profile, size, hướng và phần tử liên quan.", {
      type: "object",
      properties: { element_ids: idsSchema },
      required: ["element_ids"],
      additionalProperties: false,
    }),
    defineTool("mep_filter_elements", "Lọc MEP kết hợp System Name, Category và Level.", {
      type: "object",
      properties: {
        categories: { type: "array", items: { type: "string" } },
        system_name: { type: "string" },
        level_name: { type: "string" },
        limit: { type: "integer", minimum: 1, maximum: 1000 },
      },
      additionalProperties: false,
    }),
    defineTool("mep_qa_connectivity", "Quét QA chỉ đọc: connector hở và Pipe/Duct chưa có System Name. Finding là tín hiệu cần kỹ sư rà soát, không tự sửa model.", {
      type: "object",
      properties: {
        categories: { type: "array", items: { type: "string" } },
        system_name: { type: "string" },
        level_name: { type: "string" },
        limit: { type: "integer", minimum: 1, maximum: 1000 },
      },
      additionalProperties: false,
    }),
  ];
}
