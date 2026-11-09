// Extract the verified Database Design section from the exported offline SDS.
// Run after preview-server's "save offline" button has exported the current HTML/SVG.
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const offline = fs.readFileSync(path.join(root, 'SDS_HUNG_OFFLINE.html'), 'utf8');
const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, 'database-schema-manifest.json'), 'utf8'));
if ((offline.match(/<svg\b/g) || []).length !== 7 || /<script\b/i.test(offline)) throw Error('Offline SDS must contain seven rendered diagrams and no scripts');
const head = /<head>([\s\S]*?)<\/head>/.exec(offline)?.[1];
const start = offline.indexOf('<h2 id="3-database-design-toan-bo-billiarddb">');
const end = offline.indexOf('<h1 id="ii-detailed-code-design">', start);
if (!head || start < 0 || end < 0) throw Error('Cannot find exported Database Design section');
const section = offline.slice(start, end);
for (let i = 0; i < manifest.tables.length; i++) {
  const table = manifest.tables[i];
  const id = '3-' + (i + 1) + '-' + table.name.toLowerCase();
  const blockStart = section.indexOf('<h3 id="' + id + '">');
  if (blockStart < 0) throw Error('Missing table heading: ' + table.name);
  const blockEnd = section.indexOf('<h3 ', blockStart + 1);
  const block = section.slice(blockStart, blockEnd < 0 ? section.length : blockEnd);
  for (const column of table.columns) if (!block.includes('<td><code>' + column + '</code></td>')) throw Error('Missing column: ' + table.name + '.' + column);
}
const columnTableCount = (section.match(/<table class="schema-table">/g) || []).length;
if (columnTableCount !== 24 || (section.match(/<svg\b/g) || []).length !== 1) throw Error('Expected 24 column tables and one database diagram');
if (section.includes('\uFFFD')) throw Error('Unicode replacement character found');
const result = '<!doctype html><html lang="vi"><head>' + head.replace(/<title>[\s\S]*?<\/title>/, '<title>Database Design — BMS — 24 bảng</title>') + '</head><body><main><h1>BMS — Database Design đầy đủ</h1>' + section + '</main></body></html>';
fs.writeFileSync(path.join(root, 'DATABASE_DESIGN.html'), result);
console.log('Verified standalone offline HTML: 24 table descriptions, ' + manifest.columnCount + ' columns, one embedded SVG, no scripts.');
