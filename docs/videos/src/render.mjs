#!/usr/bin/env node
/**
 * Renders docs/videos/mockup-walkthrough.mp4 and mockup-walkthrough.gif
 * from docs/videos/src/walkthrough.html via Playwright + ffmpeg.
 *
 * Usage:
 *   cd docs/videos/src && npm install && npx playwright install chromium && node render.mjs
 */
import { chromium } from 'playwright';
import { fileURLToPath } from 'url';
import path from 'path';
import fs from 'fs';
import { spawnSync } from 'child_process';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const walkthroughHtml = path.join(__dirname, 'walkthrough.html');
const outDir = path.join(__dirname, '..');
const mp4Path = path.join(outDir, 'mockup-walkthrough.mp4');
const gifPath = path.join(outDir, 'mockup-walkthrough.gif');
const tmpDir = path.join(__dirname, '.render-tmp');

function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

function runFfmpeg(args) {
  const result = spawnSync('ffmpeg', args, { encoding: 'utf8' });
  if (result.status !== 0) {
    console.error(result.stderr);
    throw new Error('ffmpeg failed');
  }
}

function formatBytes(n) {
  if (n < 1024 * 1024) return (n / 1024).toFixed(1) + ' KB';
  return (n / 1024 / 1024).toFixed(2) + ' MB';
}

fs.rmSync(tmpDir, { recursive: true, force: true });
fs.mkdirSync(tmpDir, { recursive: true });

const browser = await chromium.launch();
const context = await browser.newContext({
  viewport: { width: 1280, height: 720 },
  deviceScaleFactor: 1,
  recordVideo: { dir: tmpDir, size: { width: 1280, height: 720 } },
});
const page = await context.newPage();
await page.goto('file://' + walkthroughHtml);
await page.waitForTimeout(400);

const steps = [
  async () => {
    await page.evaluate(() => window.__walkthrough.setCaption(''));
    await sleep(4500);
    await page.evaluate(() => window.__walkthrough.hideDisclaimer());
    await sleep(1000);
  },
  async () => {
    await page.evaluate(() =>
      window.__walkthrough.setCaption('Ledger tab — filterable list, open/closed status, and venue editor'),
    );
    await sleep(3000);
    await page.evaluate(() => window.__walkthrough.selectVenue('crystal'));
    await sleep(2200);
    await page.evaluate(() => window.__walkthrough.selectVenue('example'));
    await sleep(1800);
  },
  async () => {
    await page.evaluate(() => window.__walkthrough.showTab('board'));
    await page.evaluate(() =>
      window.__walkthrough.setCaption('Board tab — projected hours and Today day strip (crimson line = now)'),
    );
    await sleep(2800);
    await page.evaluate(() => window.__walkthrough.toggle24h());
    await sleep(1400);
    await page.evaluate(() => window.__walkthrough.toggle24h());
    await sleep(2200);
  },
  async () => {
    await page.evaluate(() => window.__walkthrough.showTab('export'));
    await page.evaluate(() =>
      window.__walkthrough.setCaption('Export tab — toggles, live preview, Copy list / Save .txt'),
    );
    await sleep(2200);
    await page.evaluate(() => window.__walkthrough.toggleNotes());
    await sleep(1400);
    await page.evaluate(() => window.__walkthrough.toggleNotes());
    await sleep(1000);
    await page.evaluate(() => window.__walkthrough.clickCopy());
    await sleep(2500);
  },
  async () => {
    await page.evaluate(() => window.__walkthrough.showChatScene());
    await page.evaluate(() =>
      window.__walkthrough.setCaption('`/venue` commands — list, copy, and export in Dalamud chat'),
    );
    await sleep(9000);
  },
];

for (const step of steps) {
  await step();
}

await page.close();
await context.close();
await browser.close();

const webmFiles = fs.readdirSync(tmpDir).filter((f) => f.endsWith('.webm'));
if (webmFiles.length !== 1) {
  throw new Error('Expected one webm from Playwright, got: ' + webmFiles.join(', '));
}
const webmPath = path.join(tmpDir, webmFiles[0]);

runFfmpeg([
  '-y',
  '-i',
  webmPath,
  '-an',
  '-c:v',
  'libx264',
  '-pix_fmt',
  'yuv420p',
  '-crf',
  '26',
  '-preset',
  'medium',
  '-movflags',
  '+faststart',
  mp4Path,
]);

runFfmpeg([
  '-y',
  '-i',
  mp4Path,
  '-vf',
  'fps=8,scale=800:-1:flags=lanczos,split[s0][s1];[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=3',
  '-loop',
  '0',
  gifPath,
]);

const mp4Size = fs.statSync(mp4Path).size;
const gifSize = fs.statSync(gifPath).size;
console.log(mp4Path, formatBytes(mp4Size));
console.log(gifPath, formatBytes(gifSize));

if (mp4Size > 10 * 1024 * 1024) {
  console.warn('Warning: MP4 exceeds 10 MB — re-encode with higher CRF if needed.');
}
if (gifSize > 5 * 1024 * 1024) {
  console.warn('Warning: GIF exceeds 5 MB — reduce fps or width.');
}

fs.rmSync(tmpDir, { recursive: true, force: true });
