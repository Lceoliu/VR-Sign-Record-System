import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const JSON_CHUNK = 0x4e4f534a;
const BIN_CHUNK = 0x004e4942;
const GLB_MAGIC = 0x46546c67;
const maxTextureSize = Number.parseInt(process.argv[2] ?? "1024", 10);
const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, "..");
const resourceRoot = path.resolve(
  repositoryRoot,
  "signvr_unity",
  "Assets",
  "Resources",
);
const ffmpeg = path.resolve(
  "C:/Program Files (x86)/FFmpeg/ffmpeg-8.0-full_build/bin/ffmpeg.exe",
);

if (!Number.isInteger(maxTextureSize) || maxTextureSize < 128) {
  throw new Error("Invalid maximum texture size: " + process.argv[2]);
}
if (!resourceRoot.startsWith(repositoryRoot) || !fs.existsSync(ffmpeg)) {
  throw new Error("The resource root or FFmpeg executable is unavailable.");
}

const glbPaths = fs
  .readdirSync(resourceRoot)
  .filter((name) => name.toLowerCase().endsWith(".glb"))
  .map((name) => path.join(resourceRoot, name))
  .sort((left, right) => fs.statSync(right).size - fs.statSync(left).size);

const totalBefore = glbPaths.reduce(
  (sum, glbPath) => sum + fs.statSync(glbPath).size,
  0,
);

for (const glbPath of glbPaths) {
  optimizeGlb(glbPath);
}

const totalAfter = glbPaths.reduce(
  (sum, glbPath) => sum + fs.statSync(glbPath).size,
  0,
);
console.log(
  "TOTAL: " + toMegabytes(totalBefore) + " -> " +
    toMegabytes(totalAfter) + " MB",
);

function optimizeGlb(glbPath) {
  const source = fs.readFileSync(glbPath);
  const { document, binary } = readGlb(source);
  const replacements = new Map();
  const temporaryDirectory = fs.mkdtempSync(
    path.join(os.tmpdir(), "signvr-glb-"),
  );

  try {
    for (let index = 0; index < (document.images?.length ?? 0); index += 1) {
      const image = document.images[index];
      if (image.bufferView === undefined || !image.mimeType) {
        continue;
      }

      const view = document.bufferViews[image.bufferView];
      if ((view.buffer ?? 0) !== 0) {
        throw new Error("Only GLB buffer 0 is supported.");
      }

      const extension =
        image.mimeType === "image/jpeg"
          ? ".jpg"
          : image.mimeType === "image/png"
            ? ".png"
            : null;
      if (extension === null) {
        continue;
      }

      const inputPath = path.join(
        temporaryDirectory,
        "image-" + index + "-input" + extension,
      );
      const outputPath = path.join(
        temporaryDirectory,
        "image-" + index + "-output" + extension,
      );
      const start = view.byteOffset ?? 0;
      fs.writeFileSync(
        inputPath,
        binary.subarray(start, start + view.byteLength),
      );

      const scale =
        "scale=w='min(" + maxTextureSize + ",iw)':" +
        "h='min(" + maxTextureSize + ",ih)':" +
        "force_original_aspect_ratio=decrease";
      const codecOptions =
        extension === ".jpg"
          ? ["-q:v", "4"]
          : ["-compression_level", "9"];
      const result = spawnSync(
        ffmpeg,
        [
          "-hide_banner",
          "-loglevel",
          "error",
          "-y",
          "-i",
          inputPath,
          "-vf",
          scale,
          "-frames:v",
          "1",
          ...codecOptions,
          outputPath,
        ],
        { encoding: "utf8" },
      );
      if (result.status !== 0 || !fs.existsSync(outputPath)) {
        throw new Error(
          "FFmpeg failed for " + path.basename(glbPath) +
            " image " + index + ": " + result.stderr,
        );
      }

      replacements.set(image.bufferView, fs.readFileSync(outputPath));
    }

    const rebuiltBinary = rebuildBinary(document, binary, replacements);
    const rebuiltGlb = writeGlb(document, rebuiltBinary);
    readGlb(rebuiltGlb);

    const temporaryGlb =
      glbPath + "." + process.pid + ".optimized.tmp";
    fs.writeFileSync(temporaryGlb, rebuiltGlb);
    if (
      !path.resolve(temporaryGlb).startsWith(resourceRoot) ||
      fs.statSync(temporaryGlb).size <= 0
    ) {
      throw new Error("Invalid temporary GLB: " + temporaryGlb);
    }

    const oldSize = source.length;
    fs.copyFileSync(temporaryGlb, glbPath);
    fs.unlinkSync(temporaryGlb);
    console.log(
      "OPTIMIZED " + path.basename(glbPath) + ": " +
        toMegabytes(oldSize) + " -> " +
        toMegabytes(rebuiltGlb.length) + " MB",
    );
  } finally {
    const resolvedTemporary = path.resolve(temporaryDirectory);
    if (resolvedTemporary.startsWith(path.resolve(os.tmpdir()))) {
      fs.rmSync(resolvedTemporary, { recursive: true, force: true });
    }
  }
}

function readGlb(buffer) {
  if (
    buffer.length < 20 ||
    buffer.readUInt32LE(0) !== GLB_MAGIC ||
    buffer.readUInt32LE(4) !== 2 ||
    buffer.readUInt32LE(8) !== buffer.length
  ) {
    throw new Error("Invalid GLB 2.0 header.");
  }

  let jsonBuffer;
  let binary;
  for (let offset = 12; offset < buffer.length; ) {
    const length = buffer.readUInt32LE(offset);
    const type = buffer.readUInt32LE(offset + 4);
    const data = buffer.subarray(offset + 8, offset + 8 + length);
    if (type === JSON_CHUNK) jsonBuffer = data;
    if (type === BIN_CHUNK) binary = data;
    offset += 8 + length;
  }
  if (!jsonBuffer || !binary) {
    throw new Error("GLB must contain JSON and BIN chunks.");
  }

  const json = jsonBuffer.toString("utf8").replace(/[\u0000 ]+$/u, "");
  return { document: JSON.parse(json), binary };
}

function rebuildBinary(document, binary, replacements) {
  const chunks = [];
  let offset = 0;
  for (let index = 0; index < document.bufferViews.length; index += 1) {
    const view = document.bufferViews[index];
    const originalOffset = view.byteOffset ?? 0;
    const data =
      replacements.get(index) ??
      binary.subarray(originalOffset, originalOffset + view.byteLength);
    const padding = (4 - (offset % 4)) % 4;
    if (padding > 0) {
      chunks.push(Buffer.alloc(padding));
      offset += padding;
    }
    view.byteOffset = offset;
    view.byteLength = data.length;
    chunks.push(data);
    offset += data.length;
  }

  document.buffers[0].byteLength = offset;
  return Buffer.concat(chunks, offset);
}

function writeGlb(document, binary) {
  const jsonData = Buffer.from(JSON.stringify(document), "utf8");
  const jsonPadding = (4 - (jsonData.length % 4)) % 4;
  const binaryPadding = (4 - (binary.length % 4)) % 4;
  const paddedJson = Buffer.concat([
    jsonData,
    Buffer.alloc(jsonPadding, 0x20),
  ]);
  const paddedBinary = Buffer.concat([
    binary,
    Buffer.alloc(binaryPadding),
  ]);
  const totalLength = 12 + 8 + paddedJson.length + 8 + paddedBinary.length;
  const output = Buffer.alloc(totalLength);
  output.writeUInt32LE(GLB_MAGIC, 0);
  output.writeUInt32LE(2, 4);
  output.writeUInt32LE(totalLength, 8);
  output.writeUInt32LE(paddedJson.length, 12);
  output.writeUInt32LE(JSON_CHUNK, 16);
  paddedJson.copy(output, 20);
  const binaryHeader = 20 + paddedJson.length;
  output.writeUInt32LE(paddedBinary.length, binaryHeader);
  output.writeUInt32LE(BIN_CHUNK, binaryHeader + 4);
  paddedBinary.copy(output, binaryHeader + 8);
  return output;
}

function toMegabytes(bytes) {
  return (bytes / 1024 / 1024).toFixed(2);
}
