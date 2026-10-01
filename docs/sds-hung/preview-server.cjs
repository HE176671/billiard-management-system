// Optional localhost-only preview/export server. Stop with Ctrl+C after export.
const http=require('node:http'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'..');
const names=['01-architecture','02-packages','03-database','04-classes','05-login','06-create-staff','07-change-password'];
http.createServer(async(req,res)=>{
 if(req.method==='GET' && (req.url==='/' || req.url==='/SDS_HUNG.html')) {
  res.setHeader('Content-Type','text/html; charset=utf-8');res.end(fs.readFileSync(path.join(root,'SDS_HUNG.html')));return;
 }
 if(req.method==='POST' && req.url==='/export' && req.headers.origin==='http://127.0.0.1:8766') {
  try {
   let data='',size=0;
   for await(const chunk of req){size+=chunk.length;if(size>3000000)throw Error('Export too large');data+=chunk.toString('utf8');}
   const payload=JSON.parse(data);
   if(typeof payload.html!=='string'||!payload.html.includes('SDS BMS')||!Array.isArray(payload.svgs)||payload.svgs.length!==7||payload.svgs.some(s=>typeof s!=='string'||!s.startsWith('<svg'))) throw Error('Invalid export');
   fs.writeFileSync(path.join(root,'SDS_HUNG_OFFLINE.html'),payload.html);
   payload.svgs.forEach((svg,i)=>fs.writeFileSync(path.join(__dirname,names[i]+'.svg'),svg));
   res.end('Exported offline HTML and 7 SVG files.');
  }catch(e){res.writeHead(400);res.end(e.message);}return;
 }
 res.writeHead(404);res.end();
}).listen(8766,'127.0.0.1',()=>console.log('SDS preview/export at http://127.0.0.1:8766'));
