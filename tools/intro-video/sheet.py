import sys, subprocess, os, json
from PIL import Image, ImageDraw, ImageFont
# usage: sheet.py out.jpg cols thumb_w  file[@t] ...
out, cols, tw = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
items = sys.argv[4:]
font = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 15)
thumbs = []
for it in items:
    if '@' in it:
        f, t = it.rsplit('@', 1); t = float(t)
    else:
        f = it
        d = float(subprocess.run(['ffprobe','-v','error','-show_entries','format=duration','-of','csv=p=0',f],capture_output=True,text=True).stdout.strip())
        t = d/2
    tmp = out + '.tmp.png'
    subprocess.run(['ffmpeg','-v','error','-y','-ss',str(t),'-i',f,'-frames:v','1','-vf',f'scale={tw}:-2',tmp],check=True)
    im = Image.open(tmp).convert('RGB'); im.load()
    dr = ImageDraw.Draw(im)
    lab = os.path.basename(f)[:22] + f' @{t:.1f}'
    dr.rectangle([0,0,im.width,20], fill=(0,0,0))
    dr.text((3,1), lab, fill=(255,230,0), font=font)
    thumbs.append(im)
    os.remove(tmp)
h = max(t.height for t in thumbs)
rows = (len(thumbs)+cols-1)//cols
sheet = Image.new('RGB', (cols*tw, rows*h), (30,30,30))
for i, t in enumerate(thumbs):
    sheet.paste(t, ((i%cols)*tw, (i//cols)*h))
sheet.save(out, quality=82)
print(out, sheet.size)
