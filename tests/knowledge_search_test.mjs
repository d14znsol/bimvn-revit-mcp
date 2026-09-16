import assert from "node:assert/strict";
import { mkdtemp, mkdir, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { KnowledgeSearchError, searchKnowledge } from "../MCP-Server/build/knowledge-search.js";

const root = await mkdtemp(path.join(os.tmpdir(), "dscons-knowledge-test-"));
try {
  const course = path.join(root, "combine-course");
  await mkdir(course);
  await writeFile(path.join(course, "TỔNG HỢP KHÓA HỌC.md"), "# Combine\nXử lý khu vực bất lợi trước khi chi tiết hóa.\n", "utf8");
  await writeFile(path.join(course, "shop_summary.md"), "# Shop\nMặt cắt kiểm tra tại khu vực bất lợi.\n", "utf8");
  await writeFile(path.join(course, "lesson-notes.md"), "khu vực bất lợi -- this non-summary file must never be searched\n", "utf8");

  const result = await searchKnowledge({ approved_knowledge_directory: root, query: "khu vực bất lợi", limit: 5 });
  assert.equal(result.result_count, 2);
  assert.equal(result.files_considered, 2);
  for (const match of result.matches) {
    assert.match(match.sha256, /^[a-f0-9]{64}$/);
    assert.ok(match.excerpt.length <= 480);
    assert.ok(!match.canonical_path.endsWith("lesson-notes.md"));
  }
  await assert.rejects(
    () => searchKnowledge({ approved_knowledge_directory: root, query: "combine", course_filter: "../escape" }),
    (error) => error instanceof KnowledgeSearchError && error.code === "PathBlocked",
  );
  await assert.rejects(
    () => searchKnowledge({ approved_knowledge_directory: "\\\\server\\share", query: "combine" }),
    (error) => error instanceof KnowledgeSearchError && error.code === "PathBlocked",
  );
  console.log("PASS knowledge search: Unicode, checksum, bounded excerpt, transcript exclusion and path guards");
} finally {
  await rm(root, { recursive: true, force: true });
}
