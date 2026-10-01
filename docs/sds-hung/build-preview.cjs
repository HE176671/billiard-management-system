// Generate a readable HTML preview and editable Mermaid files from SDS_HUNG.md.
// No dependencies, no database access, no modification of application source.
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const input = fs.readFileSync(path.join(root, 'SDS_HUNG.md'), 'utf8');
const names = ['01-architecture','02-packages','03-database','04-classes','05-login','06-create-staff','07-change-password'];
const esc = s => s.replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;').replaceAll('"','&quot;');
function inline(s) {
  const snippets = [];
  s = s.replace(/`([^`]+)`/g, (_, v) => `@@CODE${snippets.push(`<code>${esc(v)}</code>`) - 1}@@`);
  s = esc(s).replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
  s = s.replace(/\[([^\]]+)\]\((https?:\/\/[^)]+)\)/g, '<a href="$2">$1</a>');
  return s.replace(/@@CODE(\d+)@@/g, (_, i) => snippets[Number(i)]);
}
const lines = input.split(/\r?\n/);
const out = [];
let n = 0;
for (let i=0; i<lines.length;) {
  const line = lines[i];
  if (!line.trim()) {i++; continue;}
  if (line === '```mermaid') {
    const code=[]; i++;
    while (i<lines.length && lines[i] !== '```') code.push(lines[i++]);
    if (i===lines.length) throw new Error('Unclosed Mermaid block');
    i++;
    const name=names[n++];
    if (!name) throw new Error('Unexpected diagram count');
    fs.writeFileSync(path.join(__dirname, name+'.mmd'), code.join('\n')+'\n');
    out.push(`<figure><div class="diagram"><pre class="mermaid">${esc(code.join('\n'))}</pre></div><details><summary>Xem mã sơ đồ</summary><pre>${esc(code.join('\n'))}</pre></details></figure>`);
    continue;
  }
  const heading = /^(#{1,6}) (.+)$/.exec(line);
  if (heading) {const level=heading[1].length; out.push(`<h${level}>${inline(heading[2])}</h${level}>`);i++;continue;}
  if (line.startsWith('|')) {
    const rows=[];
    while(i<lines.length && lines[i].startsWith('|')) rows.push(lines[i++]);
    out.push('<div class="tablewrap"><table>');
    rows.forEach((row, index)=>{
      if (/^\|[\s:|\-]+\|$/.test(row)) return;
      const tag=index===0?'th':'td';
      const cells=row.slice(1,-1).split('|').map(v=>`<${tag}>${inline(v.trim())}</${tag}>`).join('');
      out.push(index===0?`<thead><tr>${cells}</tr></thead><tbody>`:`<tr>${cells}</tr>`);
    });
    out.push('</tbody></table></div>'); continue;
  }
  if (line.startsWith('- ')) {
    out.push('<ul>');
    while(i<lines.length && lines[i].startsWith('- ')) out.push(`<li>${inline(lines[i++].slice(2))}</li>`);
    out.push('</ul>'); continue;
  }
  const paragraph=[];
  while(i<lines.length && lines[i].trim() && !/^(#|\||```|- )/.test(lines[i])) paragraph.push(lines[i++]);
  if (!paragraph.length) throw new Error(`Unexpected markdown line ${i}`);
  out.push(`<p>${inline(paragraph.join(' '))}</p>`);
}
if(n!==7) throw new Error(`Expected 7 diagrams, got ${n}`);
const html=`<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>SDS BMS — Phần Hùng</title><style>
*{box-sizing:border-box}body{margin:0;background:#eef2f6;color:#172332;font:16px/1.65 "Segoe UI",Arial,sans-serif}main{max-width:1150px;margin:32px auto;padding:48px 60px;background:white;border:1px solid #dde3e9}h1{font-size:30px;margin:48px 0 20px}h1:first-of-type{margin-top:16px}h2{font-size:24px;margin:34px 0 16px}h3{font-size:19px;margin:28px 0 12px}h1,h2,h3{line-height:1.3;color:#111;break-after:avoid}p{margin:12px 0}code{font:0.89em Consolas,monospace;overflow-wrap:anywhere}a{color:#125694}table{border-collapse:collapse;width:100%;font-size:14px;line-height:1.5;margin:16px 0 26px}th,td{border:1px solid #d9d9d9;padding:10px 12px;text-align:left;vertical-align:middle;overflow-wrap:anywhere}th{background:#e8eff6;color:#111}tbody tr:nth-child(even){background:#f8fafc}tr{break-inside:avoid}thead{display:table-header-group}.tablewrap{overflow-x:auto}figure{margin:24px 0;padding:20px 10px;border:1px solid #dae3ec;background:#fff;break-inside:avoid}.diagram{overflow:auto;text-align:center}.mermaid{margin:0}.mermaid svg{max-width:100%;height:auto}details{margin:12px;font-size:13px}details pre{overflow:auto;background:#f3f5f7;padding:14px;text-align:left}.notice{background:#edf4fc;border-left:4px solid #386f9e;padding:14px 18px;font-size:14px}.controls{display:flex;gap:10px;flex-wrap:wrap;margin-bottom:14px}button{border:1px solid #bdccd9;border-radius:4px;background:#fff;padding:8px 14px;cursor:pointer;font:inherit;font-size:14px}.error{color:#a31b16} @media(max-width:800px){main{margin:0;padding:24px 18px}table{font-size:12px}th,td{padding:8px}}@media print{body{background:#fff;font-size:10pt}main{margin:0;padding:0;border:0;max-width:none}.controls,.notice,details{display:none}h1{font-size:20pt}h2{font-size:16pt}h3{font-size:13pt}table{font-size:9pt}.tablewrap,.diagram{overflow:visible}figure{padding:8px;break-inside:avoid}h1:not(:first-of-type){break-before:page}a{color:inherit;text-decoration:none}}@page{size:A4;margin:16mm}
</style></head><body><main><div class="controls"><button onclick="window.print()">In / Lưu PDF</button><button id="download" disabled>Tải bản HTML offline</button></div><div class="notice" id="status">Đang tải bộ vẽ Mermaid. Lần mở bản này cần Internet để hiển thị 7 sơ đồ. Có thể tải bản HTML offline sau khi các sơ đồ hiện đủ.</div>${out.join('\n')}</main><script type="module">
try {
 const {default: mermaid}=await import('https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs');
 mermaid.initialize({startOnLoad:false,securityLevel:'strict',theme:'neutral',fontFamily:'Segoe UI, Arial, sans-serif',flowchart:{htmlLabels:true,useMaxWidth:true},sequence:{useMaxWidth:true,wrap:true,actorMargin:25,messageMargin:28},er:{useMaxWidth:true}});
 await mermaid.run({querySelector:'.mermaid'});
 const rendered=document.querySelectorAll('.mermaid svg').length;
 if(rendered!==7) throw new Error('Chỉ vẽ được '+rendered+'/7 sơ đồ');
 document.getElementById('status').textContent='Đã hiển thị đủ 7 sơ đồ. Dùng nút bên dưới mỗi sơ đồ để lưu SVG, hoặc tải bản HTML offline.';
 document.querySelectorAll('figure').forEach((fig,i)=>{
  const button=document.createElement('button');button.className='controls';button.textContent='Tải sơ đồ '+(i+1)+' dạng SVG';
  button.onclick=()=>{const svg=fig.querySelector('svg');const a=document.createElement('a');const u=URL.createObjectURL(new Blob([new XMLSerializer().serializeToString(svg)],{type:'image/svg+xml;charset=utf-8'}));a.href=u;a.download='SDS-Hung-diagram-'+(i+1)+'.svg';a.click();setTimeout(()=>URL.revokeObjectURL(u),2000);};fig.append(button);
 });
 if(location.hostname==='127.0.0.1') {
  const save=document.createElement('button');save.textContent='Lưu HTML offline và SVG vào docs';save.className='controls';
  save.onclick=async()=>{try{const copy=document.documentElement.cloneNode(true);copy.querySelectorAll('script,.controls,.notice').forEach(e=>e.remove());const svgs=Array.from(document.querySelectorAll('.mermaid svg'),s=>new XMLSerializer().serializeToString(s));const r=await fetch('/export',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({html:'<!doctype html>'+copy.outerHTML,svgs})});if(!r.ok)throw new Error(await r.text());save.textContent='Đã lưu HTML offline và 7 SVG vào docs';save.disabled=true;}catch(e){save.textContent='Lỗi lưu: '+e.message;}};document.querySelector('.controls').append(save);
 }
 document.getElementById('download').disabled=false;
 document.getElementById('download').onclick=()=>{
  const copy=document.documentElement.cloneNode(true);copy.querySelectorAll('script,.controls,.notice').forEach(e=>e.remove());
  const a=document.createElement('a');const u=URL.createObjectURL(new Blob(['<!doctype html>'+copy.outerHTML],{type:'text/html;charset=utf-8'}));a.href=u;a.download='SDS_HUNG_OFFLINE.html';a.click();setTimeout(()=>URL.revokeObjectURL(u),2000);
 };
}catch(e){document.getElementById('status').classList.add('error');document.getElementById('status').textContent='Chưa vẽ được sơ đồ: '+e.message+'. Nội dung vẫn đọc được; mã nguồn sơ đồ nằm trong mục Xem mã sơ đồ và các file .mmd.';console.error(e);}
</script></body></html>`;
fs.writeFileSync(path.join(root,'SDS_HUNG.html'),html);
console.log(`Generated SDS_HUNG.html and ${n} Mermaid source files.`);
