const fs = require("node:fs");
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const { createRequire } = require("node:module");

async function main() {
  const runtime = path.resolve(process.argv[2]);
  const resultsPath = path.resolve(process.argv[3] || ".test-data/test-results.json");
  const requireBridge = createRequire(path.join(runtime, "bin", "llbot", "package.json"));
  const { encode, isSilk, getWavFileInfo, getDuration } = requireBridge("silk-wasm");
  const ffmpeg = path.join(runtime, "bin", "llbot", "ffmpeg.exe");
  const results = JSON.parse(fs.readFileSync(resultsPath, "utf8"));
  const rows = [];
  for (const clip of results.AudioMetrics) {
    const wav = fs.readFileSync(clip.FilePath);
    const sampleRate = getWavFileInfo(wav).fmt.sampleRate;
    let encoded;
    let converted = false;
    if ([8000, 12000, 16000, 24000, 32000, 44100, 48000].includes(sampleRate)) {
      encoded = await encode(wav, 0);
    } else {
      const conversion = spawnSync(ffmpeg, [
        "-hide_banner", "-loglevel", "error", "-i", clip.FilePath,
        "-ar", "24000", "-ac", "1", "-f", "s16le", "pipe:1",
      ], { windowsHide: true, maxBuffer: 30_000_000 });
      if (conversion.status !== 0) throw new Error(`FFmpeg failed for ${clip.Name}: ${conversion.stderr}`);
      encoded = await encode(conversion.stdout, 24000);
      converted = true;
    }
    if (!isSilk(encoded.data) || getDuration(encoded.data) <= 0)
      throw new Error(`Invalid SILK for ${clip.Name}`);
    const deltaSeconds = Math.abs(encoded.duration / 1000 - clip.Seconds);
    if (deltaSeconds > 0.08) throw new Error(`SILK duration changed for ${clip.Name}: ${deltaSeconds}`);
    rows.push({ voice: clip.Name, sampleRate, converted, bytes: encoded.data.length, deltaSeconds });
  }
  const output = path.join(path.dirname(resultsPath), "qq-audio-results.json");
  fs.writeFileSync(output, JSON.stringify({ count: rows.length, rows }, null, 2));
  console.log(`${rows.length} original WAV voices encoded to valid QQ SILK; results: ${output}`);
}

main().catch((error) => { console.error(error.message); process.exitCode = 1; });
