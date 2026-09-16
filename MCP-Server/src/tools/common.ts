import type { Tool } from "@modelcontextprotocol/sdk/types.js";

export const emptySchema: Tool["inputSchema"] = { type: "object", additionalProperties: false };
export const idsSchema = { type: "array", items: { type: "integer" } } as const;

export function defineTool(
  name: string,
  description: string,
  inputSchema: Tool["inputSchema"],
  write = false,
): Tool {
  return {
    name,
    description,
    inputSchema,
    annotations: {
      readOnlyHint: !write,
      destructiveHint: false,
    },
  };
}
