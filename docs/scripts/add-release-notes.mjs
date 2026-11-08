// Adds a release's notes to docs/src/content/docs/reference/release-notes.md.
//
// Usage: node scripts/add-release-notes.mjs <tag> <notes-file> [--prerelease] [--date YYYY-MM-DD]
//
// Idempotent: does nothing if a "## <tag>" heading is already on the page. The notes are the
// GitHub Release body, with its headings demoted so they nest under the release heading.
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const args = process.argv.slice(2);
const flag = (name) => args.includes(name);
const option = (name) => (args.includes(name) ? args[args.indexOf(name) + 1] : undefined);
const [tag, notesFile] = args.filter((a, i) => !a.startsWith('--') && !['--date'].includes(args[i - 1]));
if (!tag || !notesFile) {
  console.error('Usage: add-release-notes.mjs <tag> <notes-file> [--prerelease] [--date YYYY-MM-DD]');
  process.exit(1);
}

const page = join(dirname(fileURLToPath(import.meta.url)), '../src/content/docs/reference/release-notes.md');
const marker = '<!-- releases:start -->';
const content = readFileSync(page, 'utf8');
if (!content.includes(marker)) throw new Error(`Marker ${marker} not found in ${page}`);

if (new RegExp(`^## ${tag.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\b`, 'm').test(content)) {
  console.log(`${tag} is already in the release notes; nothing to do.`);
  process.exit(0);
}

// Demote headings by two levels (## -> ####) so they sit under "## <tag>"; leave fenced code alone.
let inFence = false;
const notes = readFileSync(notesFile, 'utf8')
  .replace(/\r\n/g, '\n')
  .trim()
  .split('\n')
  .map((line) => {
    if (/^\s*```/.test(line)) inFence = !inFence;
    return !inFence && /^#{1,4}\s/.test(line) ? `##${line}` : line;
  })
  .join('\n');

const date = option('--date') ?? new Date().toISOString().slice(0, 10);
const heading = `## ${tag}${flag('--prerelease') ? ' (pre-release)' : ''}`;
const section = `${heading}\n\n_${date}_ · [GitHub release](https://github.com/two4suited/Sheridan.Aspire.Pulumi/releases/tag/${tag})\n\n${notes || '_No notes._'}\n`;

writeFileSync(page, content.replace(marker, `${marker}\n\n${section}`));
console.log(`Added ${tag} to ${page}`);
