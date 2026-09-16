import { createHash } from "node:crypto";
import { lstat, readdir, readFile, realpath } from "node:fs/promises";
import path from "node:path";

const MAX_FILES = 1000;
const MAX_EXCERPT = 480;

export class KnowledgeSearchError extends Error {
  constructor(public readonly code: "PathBlocked" | "InvalidParam", message: string) { super(message); }
}

export type KnowledgeSearchArguments = {
  approved_knowledge_directory: string;
  query: string;
  course_filter?: string;
  limit?: number;
};

function isBlockedPath(value: string): boolean {
  return value.startsWith("\\\\") || value.startsWith("//") || value.startsWith("\\\\?\\") || /^\\\\[^\\/]+[\\/][^\\/]+/.test(value);
}

function normalize(value: string): string {
  return value.normalize("NFD").replace(/\p{Diacritic}/gu, "").toLocaleLowerCase("vi-VN");
}

function isAllowedSummaryFile(name: string): boolean {
  const normalized = normalize(name);
  return normalized === "tong hop khoa hoc.md" || normalized.endsWith("_summary.md");
}

function requireSafeRelativeFilter(value: string | undefined): string | undefined {
  if (value === undefined || value.trim() === "") return undefined;
  if (path.isAbsolute(value) || path.win32.isAbsolute(value) || isBlockedPath(value) || value.split(/[\\/]/).some((part) => part === ".."))
    throw new KnowledgeSearchError("PathBlocked", "course_filter must be a relative course-name filter without traversal.");
  return normalize(value);
}

async function approvedRoot(raw: string): Promise<string> {
  if (!raw || !path.isAbsolute(raw) || isBlockedPath(raw))
    throw new KnowledgeSearchError("PathBlocked", "approved_knowledge_directory must be an existing local absolute directory; UNC/network paths are blocked.");
  const canonical = await realpath(raw).catch(() => { throw new KnowledgeSearchError("PathBlocked", "approved_knowledge_directory does not exist or cannot be resolved."); });
  const info = await lstat(canonical);
  if (!info.isDirectory()) throw new KnowledgeSearchError("PathBlocked", "approved_knowledge_directory must be a directory.");
  return canonical;
}

async function collectSummaries(root: string, current = root, found: string[] = []): Promise<string[]> {
  if (found.length >= MAX_FILES) return found;
  const entries = await readdir(current, { withFileTypes: true });
  for (const entry of entries) {
    if (found.length >= MAX_FILES) break;
    if (entry.isSymbolicLink()) continue;
    const child = path.join(current, entry.name);
    if (entry.isDirectory()) await collectSummaries(root, child, found);
    else if (entry.isFile() && isAllowedSummaryFile(entry.name)) {
      const canonical = await realpath(child);
      const relative = path.relative(root, canonical);
      if (relative && !relative.startsWith("..") && !path.isAbsolute(relative)) found.push(canonical);
    }
  }
  return found;
}

function headingFor(text: string, matchIndex: number): string {
  const before = text.slice(0, Math.max(0, matchIndex));
  const headings = [...before.matchAll(/^#{1,6}\s+(.+)$/gm)];
  return headings.length > 0 ? headings[headings.length - 1][1].trim().slice(0, 300) : "(không có heading)";
}

function excerptFor(text: string, matchIndex: number): string {
  const start = Math.max(0, matchIndex - Math.floor(MAX_EXCERPT / 3));
  const end = Math.min(text.length, start + MAX_EXCERPT);
  return text.slice(start, end).replace(/\s+/g, " ").trim();
}

/** Local-only search for explicitly approved course summaries. It intentionally
 * does not read transcript/raw-media names, send data over the network, or
 * persist an index outside the caller supplied root. */
export async function searchKnowledge(args: KnowledgeSearchArguments): Promise<Record<string, unknown>> {
  const root = await approvedRoot(args.approved_knowledge_directory);
  const query = args.query?.trim();
  if (!query || query.length < 2) throw new KnowledgeSearchError("InvalidParam", "query must contain at least two characters.");
  const filter = requireSafeRelativeFilter(args.course_filter);
  const limit = Math.min(20, Math.max(1, args.limit ?? 5));
  const files = await collectSummaries(root);
  const queryTerms = normalize(query).split(/\s+/).filter((term) => term.length > 1);
  const matches: Record<string, unknown>[] = [];
  for (const file of files) {
    if (filter && !normalize(path.relative(root, file)).includes(filter)) continue;
    const content = await readFile(file, "utf8");
    const normalized = normalize(content);
    const indices = queryTerms.map((term) => normalized.indexOf(term));
    if (indices.some((index) => index < 0)) continue;
    const matchIndex = Math.min(...indices);
    matches.push({
      heading: headingFor(content, matchIndex),
      excerpt: excerptFor(content, matchIndex),
      canonical_path: file,
      sha256: createHash("sha256").update(content, "utf8").digest("hex"),
    });
    if (matches.length >= limit) break;
  }
  return {
    approved_knowledge_directory: root,
    query,
    files_considered: files.length,
    result_count: matches.length,
    matches,
    note: "Chỉ tìm trong TỔNG HỢP KHÓA HỌC.md và *_summary.md dưới thư mục đã duyệt. Kết quả là citation/evidence riêng tư, không phải tiêu chuẩn kỹ thuật hoặc pháp lý.",
  };
}
