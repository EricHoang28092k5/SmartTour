/**
 * Đọc PRD_SmartTour.md, render khối ```mermaid thành PNG, xuất Word (.docx) qua Pandoc.
 * Chạy từ thư mục repo: node tools/export-prd-docx.mjs
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, "..");
const prdPath = path.join(repoRoot, "PRD_SmartTour.md");
const outDir = path.join(repoRoot, "docs", "PRD_SmartTour_docx_build");
const assetsDir = path.join(outDir, "assets");

function run(cmd, args, opts = {}) {
  const r = spawnSync(cmd, args, {
    stdio: "inherit",
    encoding: "utf8",
    ...opts,
  });
  if (r.status !== 0) {
    throw new Error(`${cmd} ${args.join(" ")} exited ${r.status}`);
  }
}

function npxMmdc(inputMmd, outputPng) {
  const npx = process.platform === "win32" ? "npx.cmd" : "npx";
  run(
    npx,
    [
      "-y",
      "@mermaid-js/mermaid-cli@11.4.0",
      "-i",
      inputMmd,
      "-o",
      outputPng,
    ],
    { shell: true, cwd: repoRoot }
  );
}

fs.rmSync(outDir, { recursive: true, force: true });
fs.mkdirSync(assetsDir, { recursive: true });

const md = fs.readFileSync(prdPath, "utf8");
const re = /```mermaid\r?\n([\s\S]*?)```/g;
let n = 0;
let result = "";
let last = 0;
let m;

while ((m = re.exec(md)) !== null) {
  result += md.slice(last, m.index);
  n++;
  const body = m[1].replace(/\r\n/g, "\n").trimEnd() + "\n";
  const base = `diagram_${String(n).padStart(3, "0")}`;
  const mmdPath = path.join(assetsDir, `${base}.mmd`);
  const pngPath = path.join(assetsDir, `${base}.png`);
  fs.writeFileSync(mmdPath, body, "utf8");
  console.error(`[${n}] ${base}.mmd -> .png`);
  npxMmdc(mmdPath, pngPath);
  fs.unlinkSync(mmdPath);
  result += `\n![Sơ đồ ${n} (Mermaid)](assets/${base}.png)\n\n`;
  last = re.lastIndex;
}
result += md.slice(last);

const mdOut = path.join(outDir, "PRD_SmartTour.md");
fs.writeFileSync(mdOut, result, "utf8");

const docxPath = path.join(repoRoot, "docs", "PRD_SmartTour.docx");
run("pandoc", [
  mdOut,
  "-o",
  docxPath,
  "--from",
  "markdown+yaml_metadata_block",
  "--toc",
  "--toc-depth=3",
  "-M",
  "title=PRD SmartTour",
  "-M",
  "lang=vi",
], { cwd: outDir });

console.error("OK:", docxPath);
