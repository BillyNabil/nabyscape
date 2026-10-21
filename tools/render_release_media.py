"""Compose release media from the isolated actual WPF showcase. No tests run.
python tools/render_release_media.py outputs/showcase-2.10.0
"""
from pathlib import Path
from functools import lru_cache
import json
import shutil
import subprocess
import sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT=Path(__file__).resolve().parents[1]
SRC=Path(sys.argv[1]).resolve()
OUT=ROOT/'Assets/showcase'
WEB=ROOT/'website/assets'
FONT=Path('C:/Windows/Fonts')
OUT.mkdir(parents=True,exist_ok=True)
@lru_cache(maxsize=24)
def font(size,bold=False):return ImageFont.truetype(str(FONT/('seguisb.ttf' if bold else 'segoeui.ttf')),size)
def background(size):
    small=Image.new('RGB',(160,100));px=small.load()
    for y in range(100):
        for x in range(160):
            blue=max(0,1-((x-120)/95)**2-((y-58)/58)**2)
            rose=max(0,1-((x-40)/72)**2-((y-91)/58)**2)
            px[x,y]=(round(12+blue*13+rose*23),round(15+blue*26+rose*9),round(22+blue*44+rose*25))
    return small.resize(size,Image.Resampling.BICUBIC).convert('RGBA')
def shot(name):
    im=Image.open(SRC/(name+'.png')).convert('RGBA');return im.crop(im.getbbox())
def place(canvas,im,xy,width=None):
    if width:im=im.resize((width,round(im.height*width/im.width)),Image.Resampling.LANCZOS)
    canvas.alpha_composite(im,tuple(map(round,xy)))
def text(im,xy,value,size,bold=False,color='#F2F2F5'):ImageDraw.Draw(im).text(xy,value,font=font(size,bold),fill=color,spacing=6)
def save(im,path):im.convert('RGB').save(path,optimize=True)

for name in ['closed','hover-music','media','weather','system','notes','tasks','shelf','calendar','focus','tools']:
    im=shot(name);im.save(OUT/(name+'-raw.png'));im.save(WEB/(name+'-raw.png'))
    if name in ['weather','system','notes','tasks','shelf','tools']:im.save(WEB/(name+'.webp'),quality=92)
for category in ['appearance','ambience','layout','content']:
    shutil.copy2(SRC/('settings-'+category+'.png'),OUT/('settings-'+category+'.png'))
    Image.open(SRC/('settings-'+category+'.png')).convert('RGB').save(WEB/('settings-'+category+'.webp'),quality=90)

hero=background((1600,900));logo=Image.open(ROOT/'src/NabyScape/Assets/island.ico').convert('RGBA').resize((42,42))
hero.alpha_composite(logo,(76,56));text(hero,(135,53),'NabyScape',30,True);text(hero,(76,180),'Your desktop.\nA little closer.',78,True)
text(hero,(80,407),'Music, weather, and everyday tools.\nA calm island, placed your way.',25,color='#B1BDCE')
text(hero,(80,684),'WINDOWS 11   /   OPEN SOURCE   /   LOCAL DATA',17,color='#92A7BF')
place(hero,shot('media'),(830,152),650);place(hero,shot('closed'),(901,650),502)
save(hero,OUT/'release-hero.png');save(hero,WEB/'marketing-hero.png');hero.resize((1280,720)).convert('RGB').save(WEB/'marketing-social.png')
sheet=background((1600,810));text(sheet,(70,50),'A quieter daily rhythm.',48,True);text(sheet,(74,121),'A compact forecast. Your tools in your order. The familiar music controls.',23,color='#B1BDCE')
place(sheet,shot('weather'),(80,225),650);place(sheet,shot('focus'),(870,225),650);save(sheet,OUT/'release-widgets.png')
meta=json.loads((SRC/'chapters.json').read_text(encoding='utf-8-sig'))
# Normalize transparent WPF frames to a consistent documentary canvas before encoding.
frames=SRC/'composed';frames.mkdir(exist_ok=True)
base=background((1280,800))
for index in range(meta['frames']):
    frame=Image.open(SRC/'frames'/f'{index:05d}.png').convert('RGBA');frame=frame.crop(frame.getbbox())
    ratio=min(1000/frame.width,480/frame.height);frame=frame.resize((round(frame.width*ratio),round(frame.height*ratio)),Image.Resampling.LANCZOS)
    canvas=base.copy();place(canvas,frame,((1280-frame.width)/2,30))
    chapter=next(c for c in reversed(meta['chapters']) if index/30>=c['start'])
    text(canvas,(64,554),chapter['caption'],38,True);text(canvas,(66,648),'NabyScape 2.10 · Windows 11',22,color='#B1BDCE');text(canvas,(66,740),'Actual WPF views · original sample content · synthetic audio signal · silent',16,color='#92A7BF')
    save(canvas,frames/f'{index:05d}.png')
    if index==int(7*30):save(canvas,OUT/'release-video-poster.jpg');save(canvas,WEB/'marketing-video-poster.jpg')
subprocess.run(['ffmpeg','-y','-hide_banner','-loglevel','error','-framerate','30','-i',str(frames/'%05d.png'),'-c:v','libx264','-crf','20','-preset','medium','-pix_fmt','yuv420p','-movflags','+faststart','-an',str(OUT/'nabyscape-demo.mp4')],check=True)
shutil.copy2(OUT/'nabyscape-demo.mp4',WEB/'nabyscape-demo.mp4')
subprocess.run(['ffmpeg','-y','-hide_banner','-loglevel','error','-i',str(OUT/'nabyscape-demo.mp4'),'-filter_complex','fps=10,scale=720:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=96[p];[b][p]paletteuse=dither=bayer','-loop','0',str(OUT/'release-demo.gif')],check=True)
print(json.dumps({'version':'2.10.0','seconds':meta['frames']/30,'frames':meta['frames'],'movie':str(OUT/'nabyscape-demo.mp4')}))
