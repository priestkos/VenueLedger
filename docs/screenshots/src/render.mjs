#!/usr/bin/env node
/**
 * Renders docs/screenshots/src/*.html to PNG via Playwright.
 * Usage: node render.mjs
 */
import { chromium } from 'playwright';
import { fileURLToPath } from 'url';
import path from 'path';
import fs from 'fs';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const srcDir = __dirname;
const outDir = path.join(srcDir, '..');

const pages = [
  { html: '01-ledger-tab.html', png: '01-ledger-tab.png', width: 1280, height: 720 },
  { html: '02-board-tab.html', png: '02-board-tab.png', width: 1280, height: 720 },
  { html: '03-export-tab.html', png: '03-export-tab.png', width: 1280, height: 720 },
  { html: '04-slash-commands.html', png: '04-slash-commands.png', width: 1280, height: 720 },
];

const browser = await chromium.launch();
const context = await browser.newContext({ deviceScaleFactor: 1 });

for (const page of pages) {
  const filePath = path.join(srcDir, page.html);
  const p = await context.newPage();
  await p.setViewportSize({ width: page.width, height: page.height });
  await p.goto('file://' + filePath);
  await p.waitForTimeout(200);
  const outPath = path.join(outDir, page.png);
  await p.screenshot({ path: outPath, type: 'png' });
  await p.close();
  const stat = fs.statSync(outPath);
  console.log(outPath, (stat.size / 1024).toFixed(1) + ' KB');
}

await browser.close();

let total = 0;
for (const page of pages) {
  total += fs.statSync(path.join(outDir, page.png)).size;
}
console.log('Total PNG size:', (total / 1024 / 1024).toFixed(2), 'MB');
