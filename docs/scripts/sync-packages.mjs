// Generates the "Integrations" section from the package READMEs under ../src and the package table in
// the root README, so the site never duplicates (and drifts from) the package docs.
import { readFileSync, writeFileSync, mkdirSync, rmSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const out = join(root, 'docs', 'src', 'content', 'docs', 'integrations');
const BASE = '/Sheridan.Aspire.Pulumi';
const REPO = 'https://github.com/two4suited/Sheridan.Aspire.Pulumi';

const slugify = (s) => s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
const yaml = (s) => `"${s.replace(/\\/g, '\\\\').replace(/"/g, '\\"')}"`;

// Rows look like: | Area | [`Package`](src/Package) | Description |   (Area is blank for continuation rows)
const rows = [];
let area = 'Core';
for (const line of readFileSync(join(root, 'README.md'), 'utf8').split('\n')) {
  const m = line.match(/^\|\s*([^|]*?)\s*\|\s*\[`([^`]+)`\]\([^)]*\)\s*\|\s*(.+?)\s*\|\s*$/);
  if (!m) continue;
  if (m[1]) area = m[1];
  rows.push({ area, id: m[2], description: m[3].replace(/`/g, '') });
}

rmSync(out, { recursive: true, force: true });
const areas = new Map();
let order = 0;
for (const r of rows) {
  const readme = join(root, 'src', r.id, 'README.md');
  if (!existsSync(readme)) { console.warn(`no README for ${r.id}`); continue; }
  let body = readFileSync(readme, 'utf8').replace(/^# .+\n+/, '');
  // Rewrite repo-relative links to GitHub so they work on the site.
  body = body.replace(/\]\((?!https?:|#|\/)([^)]+)\)/g, (_, p) => `](${REPO}/blob/main/src/${r.id}/${p})`);
  // Like aspire.dev, link the package from its Install section.
  body = body.replace(/^## Install\n\n/m, `## Install\n\nAdd the [\`${r.id}\`](https://www.nuget.org/packages/${r.id}) NuGet package to your AppHost:\n\n`);
  const dir = join(out, slugify(r.area));
  mkdirSync(dir, { recursive: true });
  const short = r.id.replace(/^Sheridan\.Aspire\.Pulumi\.?(Azure\.)?/, '') || 'Core';
  const fm = [
    '---',
    `title: ${yaml(r.id)}`,
    `description: ${yaml(r.description)}`,
    `sidebar:`,
    `  label: ${yaml(short)}`,
    `  order: ${order++}`,
    `editUrl: ${REPO}/edit/main/src/${r.id}/README.md`,
    '---',
    '',
  ].join('\n');
  const links = `[NuGet](https://www.nuget.org/packages/${r.id}) · [Source](${REPO}/tree/main/src/${r.id})\n\n`;
  writeFileSync(join(dir, `${slugify(r.id)}.md`), fm + links + body);
  if (!areas.has(r.area)) areas.set(r.area, []);
  areas.get(r.area).push({ ...r, short, slug: `${slugify(r.area)}/${slugify(r.id)}` });
}

let idx = '---\ntitle: Integrations\ndescription: Every Sheridan.Aspire.Pulumi package, grouped by area.\nsidebar:\n  order: -1\n---\n\n';
idx += 'Each package mirrors the official `Aspire.Hosting.Azure.*` integration (`AddPulumiX` instead of `AddAzureX`). Install the ones you need from [nuget.org](https://www.nuget.org/profiles/two4suited) (see [Quick start](' + BASE + '/get-started/quick-start/)).\n\n';
for (const [a, items] of areas) {
  idx += `## ${a}\n\n| Package | Provisions | NuGet |\n| --- | --- | --- |\n`;
  for (const i of items) idx += `| [\`${i.id}\`](${BASE}/integrations/${i.slug}/) | ${i.description} | [nuget.org](https://www.nuget.org/packages/${i.id}) |\n`;
  idx += '\n';
}
writeFileSync(join(out, 'index.md'), idx);
// Sidebar groups consumed by astro.config.mjs.
const sidebar = [{ label: 'Overview', slug: 'integrations' }, ...[...areas].map(([label, items]) => ({
  label, collapsed: true, items: items.map((i) => ({ label: i.short, slug: `integrations/${i.slug}` })),
}))];
writeFileSync(join(root, 'docs', 'integrations.generated.json'), JSON.stringify(sidebar, null, 2) + '\n');
console.log(`Synced ${rows.length} package pages in ${areas.size} areas.`);
