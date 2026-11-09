const fs = require('node:fs');
const path = require('node:path');
const zlib = require('node:zlib');
const blocks = JSON.parse(fs.readFileSync(path.join(__dirname,'content.json'),'utf8'));
const esc = s => String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
const run = (text,bold=false,size=23) => `<w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial" w:cs="Arial"/><w:color w:val="000000"/><w:sz w:val="${size}"/>${bold?'<w:b/>':''}</w:rPr><w:t xml:space="preserve">${esc(text)}</w:t></w:r>`;
const para = (text,style='Normal',extra='') => `<w:p><w:pPr><w:pStyle w:val="${style}"/>${extra}<w:widowControl/></w:pPr>${run(text,style!=='Normal',style==='Title'?36:style==='Heading1'?29:style==='Heading2'?25:23)}</w:p>`;
let body='',html='';
for(const b of blocks){
 if(b.kind==='page'){body+='<w:p><w:r><w:br w:type="page"/></w:r></w:p>';html+='<div class="pagebreak"></div>';continue;}
 if(b.kind==='title'){body+=para(b.text,'Title');html+=`<h1>${esc(b.text)}</h1>`;continue;}
 if(b.kind==='h'){body+=para(b.text,b.level===2?'Heading2':'Heading1');html+=`<h${b.level===2?3:2}>${esc(b.text)}</h${b.level===2?3:2}>`;continue;}
 if(b.kind==='p'){body+=para(b.text);html+=`<p>${esc(b.text)}</p>`;continue;}
 const widths=b.widths;
 body+=`<w:tbl><w:tblPr><w:tblW w:w="9200" w:type="dxa"/><w:tblLayout w:type="fixed"/><w:tblBorders>${['top','left','bottom','right','insideH','insideV'].map(x=>`<w:${x} w:val="single" w:sz="4" w:color="D9D9D9"/>`).join('')}</w:tblBorders><w:tblCellMar><w:top w:w="95" w:type="dxa"/><w:left w:w="100" w:type="dxa"/><w:bottom w:w="95" w:type="dxa"/><w:right w:w="100" w:type="dxa"/></w:tblCellMar></w:tblPr><w:tblGrid>${widths.map(w=>`<w:gridCol w:w="${w}"/>`).join('')}</w:tblGrid>`;
 html+='<table><colgroup>'+widths.map(w=>`<col style="width:${w/92}%">`).join('')+'</colgroup>';
 b.rows.forEach((row,i)=>{
 body+=`<w:tr><w:trPr>${i===0?'<w:tblHeader/>':''}<w:cantSplit/></w:trPr>`;
 html+='<tr>';
 row.forEach((c,j)=>{
 body+=`<w:tc><w:tcPr><w:tcW w:w="${widths[j]}" w:type="dxa"/><w:vAlign w:val="center"/>${i===0?'<w:shd w:fill="E8EFF5"/>':''}</w:tcPr><w:p><w:pPr><w:spacing w:after="30" w:line="260" w:lineRule="auto"/></w:pPr>${run(c,i===0,21)}</w:p></w:tc>`;
 html+=`<${i===0?'th':'td'}>${esc(c)}</${i===0?'th':'td'}>`;
 });body+='</w:tr>';html+='</tr>';
 });body+='</w:tbl>'+para('');html+='</table>';
}
body+='<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1134" w:right="1353" w:bottom="1134" w:left="1353" w:header="500" w:footer="500"/></w:sectPr>';
const entries={
 '[Content_Types].xml':'<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/></Types>',
 '_rels/.rels':'<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>',
 'word/document.xml':`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>${body}</w:body></w:document>`,
 'word/_rels/document.xml.rels':'<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>',
 'word/styles.xml':'<?xml version="1.0" encoding="UTF-8"?><w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="23"/><w:color w:val="000000"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after="120" w:line="285" w:lineRule="auto"/></w:pPr></w:pPrDefault></w:docDefaults><w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style><w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:pPr><w:keepNext/><w:spacing w:after="200"/></w:pPr></w:style><w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:pPr><w:keepNext/><w:keepLines/><w:outlineLvl w:val="0"/><w:spacing w:before="220" w:after="120"/></w:pPr></w:style><w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:pPr><w:keepNext/><w:keepLines/><w:outlineLvl w:val="1"/><w:spacing w:before="160" w:after="80"/></w:pPr></w:style></w:styles>'
};
const crcTable=Array.from({length:256},(_,n)=>{for(let i=0;i<8;i++)n=(n&1)?0xedb88320^(n>>>1):n>>>1;return n>>>0;});
function crc32(buf){let n=0xffffffff;for(const b of buf)n=crcTable[(n^b)&255]^(n>>>8);return(n^0xffffffff)>>>0;}
let chunks=[],central=[],offset=0;
for(const [name,text] of Object.entries(entries)){
 const n=Buffer.from(name),raw=Buffer.from(text),data=zlib.deflateRawSync(raw),crc=crc32(raw),local=Buffer.alloc(30),cen=Buffer.alloc(46);
 local.writeUInt32LE(0x04034b50,0);local.writeUInt16LE(20,4);local.writeUInt16LE(8,8);local.writeUInt32LE(crc,14);local.writeUInt32LE(data.length,18);local.writeUInt32LE(raw.length,22);local.writeUInt16LE(n.length,26);
 cen.writeUInt32LE(0x02014b50,0);cen.writeUInt16LE(20,4);cen.writeUInt16LE(20,6);cen.writeUInt16LE(8,10);cen.writeUInt32LE(crc,16);cen.writeUInt32LE(data.length,20);cen.writeUInt32LE(raw.length,24);cen.writeUInt16LE(n.length,28);cen.writeUInt32LE(offset,42);
 chunks.push(local,n,data);central.push(cen,n);offset+=local.length+n.length+data.length;
}
const directory=Buffer.concat(central),end=Buffer.alloc(22);end.writeUInt32LE(0x06054b50,0);end.writeUInt16LE(Object.keys(entries).length,8);end.writeUInt16LE(Object.keys(entries).length,10);end.writeUInt32LE(directory.length,12);end.writeUInt32LE(offset,16);
fs.writeFileSync(path.join(__dirname,'AI_Usage_Report_Hung_Tham_Khao.docx'),Buffer.concat([...chunks,directory,end]));
fs.writeFileSync(path.join(__dirname,'AI_Usage_Report_Hung_Tham_Khao.html'),`<!doctype html><html lang="vi"><meta charset="utf-8"><title>Báo cáo sử dụng AI</title><style>body{font:16px/1.55 Arial,sans-serif;color:#000;max-width:860px;margin:36px auto;padding:24px}h1{font-size:28px}h2{font-size:23px;margin-top:30px}h3{font-size:19px}table{width:100%;border-collapse:collapse;table-layout:fixed;margin:14px 0 22px;font-size:14px;line-height:1.4}td,th{border:1px solid #d9d9d9;padding:9px;text-align:left;vertical-align:middle;overflow-wrap:anywhere}th{background:#e8eff5}.pagebreak{margin-top:44px}@media print{body{margin:0;max-width:none}.pagebreak{break-before:page}h1,h2,h3{break-after:avoid}tr{break-inside:avoid}}</style>${html}</html>`);
console.log(JSON.stringify({blocks:blocks.length,tables:blocks.filter(b=>b.kind==='table').length,output:'AI_Usage_Report_Hung_Tham_Khao.docx',titleBorders:0}));
