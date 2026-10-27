from pathlib import Path
from lxml import etree as E
from PIL import Image
import os, zipfile

folder=Path(__file__).parent
source=Path(os.environ['TEMP'])/'hung-review/current.docx'
dest=Path(os.environ['TEMP'])/'hung-review/sequence-easy.docx'
ns={'w':'http://schemas.openxmlformats.org/wordprocessingml/2006/main','a':'http://schemas.openxmlformats.org/drawingml/2006/main','wp':'http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing','r':'http://schemas.openxmlformats.org/officeDocument/2006/relationships'}
replacements={'rId12':'05-login','rId13':'06-create-staff','rId14':'07-change-password'}
with zipfile.ZipFile(source) as z:
    root=E.fromstring(z.read('word/document.xml'))
    rels={r.get('Id'):r.get('Target') for r in E.fromstring(z.read('word/_rels/document.xml.rels'))}
    media={}
    for rid,name in replacements.items():
        found=root.xpath('//a:blip[@r:embed="'+rid+'"]',namespaces=ns)
        assert len(found)==1
        drawing=found[0].xpath('ancestor::w:drawing',namespaces=ns)[0]
        extent=drawing.xpath('.//wp:extent',namespaces=ns)[0]
        w,h=Image.open(folder/(name+'.png')).size
        cx=int(extent.get('cx')); cy=round(cx*h/w)
        # Keep the current width and the image's natural proportions.
        extent.set('cy',str(cy))
        for ext in drawing.xpath('.//a:xfrm/a:ext',namespaces=ns): ext.set('cy',str(cy));ext.set('cx',str(cx))
        media['word/'+rels[rid]]=(folder/(name+'.png')).read_bytes()
    xml=E.tostring(root,xml_declaration=True,encoding='UTF-8',standalone=True)
    with zipfile.ZipFile(dest,'w') as out:
        for entry in z.infolist():
            out.writestr(entry,media.get(entry.filename,xml if entry.filename=='word/document.xml' else z.read(entry.filename)))
with zipfile.ZipFile(source) as a, zipfile.ZipFile(dest) as b:
    changed=[n for n in a.namelist() if a.read(n)!=b.read(n)]
    assert set(changed)==set(media)|{'word/document.xml'}
    ax=E.fromstring(a.read('word/document.xml')); bx=E.fromstring(b.read('word/document.xml'))
    assert ax.xpath('//w:t/text()',namespaces=ns)==bx.xpath('//w:t/text()',namespaces=ns)
    print('Changed only three images and their display proportions. All document text preserved.')
    print(dest)
