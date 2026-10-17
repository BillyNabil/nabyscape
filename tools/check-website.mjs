// Dependency-free static-site integrity check. Usage: node tools/check-website.mjs [site-root]
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const root = path.resolve(process.argv[2] || path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'website'));
const files = [];
const walk = directory => {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) walk(full); else files.push(full);
  }
};
walk(root);
const errors = [];
let links = 0;
const html = files.filter(file => file.endsWith('.html'));
const idCache = new Map();
for (const file of html) {
  const source = fs.readFileSync(file, 'utf8');
  const ids = [...source.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]);
  if (new Set(ids).size !== ids.length) errors.push(`${file}: duplicate id`);
  idCache.set(file, new Set(ids));
}
for (const file of html) {
  const source = fs.readFileSync(file, 'utf8');
  if (!/<html\b[^>]*\blang=/.test(source)) errors.push(`${file}: missing document language`);
  if (!/<title>[^<]+<\/title>/.test(source)) errors.push(`${file}: missing title`);
  for (const image of source.matchAll(/<img\b[^>]*>/g)) {
    if (!/\balt="[^"]*"/.test(image[0])) errors.push(`${file}: image missing alt`);
  }
  for (const match of source.matchAll(/\b(?:href|src)="([^"]+)"/g)) {
    const reference = match[1];
    if (/^(?:https?:|mailto:|data:)/i.test(reference)) continue;
    const [pathname, fragment] = reference.split('#');
    const relative = pathname.split('?')[0];
    let target = relative.startsWith('/') ? path.join(root, relative) : path.resolve(path.dirname(file), relative || path.basename(file));
    if (fs.existsSync(target) && fs.statSync(target).isDirectory()) target = path.join(target, 'index.html');
    links++;
    if (!fs.existsSync(target)) { errors.push(`${path.relative(root,file)}: missing ${reference}`); continue; }
    if (fragment && target.endsWith('.html') && !idCache.get(target)?.has(decodeURIComponent(fragment))) errors.push(`${path.relative(root,file)}: missing anchor ${reference}`);
  }
}
for (const file of files.filter(file => /\.(?:m?js)$/.test(file))) {
  const result = spawnSync(process.execPath, ['--check', file], { encoding: 'utf8' });
  if (result.status !== 0) errors.push(`${path.relative(root,file)}: ${result.stderr.trim()}`);
  // Check local media references in the data-driven gallery as well as HTML src attributes.
  const source = fs.readFileSync(file, 'utf8');
  for (const match of source.matchAll(/['"]([\w-]+\.(?:png|webp|jpe?g|mp4))['"]/g)) {
    if (!fs.existsSync(path.join(root,'assets',match[1]))) errors.push(`${path.relative(root,file)}: missing gallery asset ${match[1]}`);
  }
  for (const match of source.matchAll(/['"]\/docs\/#([\w-]+)['"]/g)) {
    if (!idCache.get(path.join(root,'docs','index.html'))?.has(match[1])) errors.push(`Missing guide topic ${match[1]}`);
  }
}
if (errors.length) {
  console.error(errors.join('\n'));
  process.exitCode = 1;
} else {
  const bytes = files.reduce((total,file) => total + fs.statSync(file).size, 0);
  console.log(`PASS: ${html.length} HTML pages, ${links} local links/assets/anchors; JavaScript syntax and gallery assets valid. ${files.length} files, ${(bytes/1024).toFixed(0)} KiB.`);
}
