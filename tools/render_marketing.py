"""Compose marketing assets from committed WPF captures; no app is launched.

python tools/render_marketing.py [static-site-dist]
Requires Pillow. Historical showcase assets and render_showcase.py are retained.
"""
from pathlib import Path
from collections import deque
import shutil
import sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'Assets' / 'showcase'
FONT = Path('C:/Windows/Fonts')


def font(size, bold=False):
    return ImageFont.truetype(str(FONT / ('seguisb.ttf' if bold else 'segoeui.ttf')), size)


def atmosphere(size):
    small = Image.new('RGB', (240, 150))
    px = small.load()
    for y in range(150):
        for x in range(240):
            blue = max(0, 1 - ((x-164)/180)**2 - ((y-90)/105)**2)
            plum = max(0, 1 - ((x-36)/160)**2 - ((y-120)/90)**2)
            px[x, y] = (round(17+blue*21+plum*20), round(21+blue*27+plum*9), round(28+blue*40+plum*21))
    return small.resize(size, Image.Resampling.BICUBIC).convert('RGBA')


def put(img, name, xy, width):
    raw = Image.open(ASSETS / name).convert('RGBA')
    raw = raw.resize((width, round(raw.height*width/raw.width)), Image.Resampling.LANCZOS)
    shadow = Image.new('RGBA', img.size)
    shadow.paste((0,0,0,140), (xy[0],xy[1]+20,xy[0]+raw.width,xy[1]+20+raw.height))
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(28)))
    img.alpha_composite(raw, xy)


def text(img, xy, value, size, bold=False, color='#f3f1ee'):
    ImageDraw.Draw(img).text(xy, value, font=font(size,bold), fill=color, spacing=0)


def banner(size=(1600,900)):
    im=atmosphere(size)
    logo=Image.open(ROOT/'src/NabyScape/Assets/island.ico').convert('RGBA').resize((42,42),Image.Resampling.LANCZOS)
    im.alpha_composite(logo,(72,58))
    text(im,(128,56),'NabyScape',30,True)
    text(im,(72,176),'A little space.',90,True)
    text(im,(72,272),'A lot closer.',90,True)
    text(im,(76,421),'Music, system stats and everyday tools.',27,color='#b4becb')
    text(im,(76,463),'A customizable island for Windows 11.',27,color='#b4becb')
    put(im,'media-raw.png',(882,210),640)
    put(im,'closed-raw.png',(72,617),460)
    text(im,(76,726),'SMALL BY DEFAULT. READY WHEN YOU ARE.',18,True,color='#b4becb')
    text(im,(76,824),'LOCAL DATA  /  OPEN SOURCE  /  WINDOWS 11 x64',17,color='#b4becb')
    text(im,(1116,824),'Actual WPF interface · v2.4.2',17,color='#b4becb')
    im.convert('RGB').save(ASSETS/'marketing-hero.png',optimize=True)
    social=atmosphere((1280,640))
    social.alpha_composite(logo,(54,42))
    text(social,(111,40),'NabyScape',28,True)
    text(social,(54,146),'A little space.',68,True)
    text(social,(54,224),'A lot closer.',68,True)
    text(social,(58,342),'Music, system stats and everyday tools.',22,color='#b4becb')
    text(social,(58,381),'A customizable island for Windows 11.',22,color='#b4becb')
    put(social,'media-raw.png',(720,153),510)
    put(social,'closed-raw.png',(58,474),410)
    text(social,(58,581),'LOCAL DATA  /  OPEN SOURCE',16,True,color='#b4becb')
    social.convert('RGB').save(ASSETS/'marketing-social.png',optimize=True)
    poster=atmosphere((1280,800))
    put(poster,'media-raw.png',(367,0),546)
    text(poster,(64,520),'Small by default.',55,True)
    text(poster,(64,590),'See the actual Windows app in motion.',28,color='#b4becb')
    text(poster,(64,728),'26 seconds · silent · sample media · v2.4.2',20,color='#b4becb')
    poster.convert('RGB').save(ASSETS/'marketing-video-poster.jpg',quality=90,optimize=True)


banner()
if len(sys.argv)>1:
    target=Path(sys.argv[1]).resolve()/'assets'
    target.mkdir(parents=True,exist_ok=True)
    for name in ['closed-raw.png','hover-music-raw.png','media-raw.png','marketing-social.png','marketing-video-poster.jpg','nabyscape-demo.mp4']:
        shutil.copy2(ASSETS/name,target/name)
    for name in ['system','tasks','shelf','tools']:
        # Retain the real pixel content; remove only the surrounding historical caption/background.
        original=Image.open(ASSETS/(name+'.png')).convert('RGB')
        mask=original.crop((170,180,930,702)).point(lambda p: 255 if p<12 else 0).convert('RGB')
        pixels=mask.load()
        points=[(x,y) for y in range(mask.height) for x in range(mask.width) if min(pixels[x,y])==255]
        left=min(p[0] for p in points)+170; top=min(p[1] for p in points)+180
        right=max(p[0] for p in points)+171; bottom=max(p[1] for p in points)+181
        cut=original.crop((left,top,right,bottom)).convert('RGBA')
        # Remove only background connected to the crop's exterior. Black WPF panel
        # surfaces isolate every control and text pixel from this flood fill.
        cp=cut.load(); seen=set(); queue=deque()
        for x in range(cut.width):
            queue.extend([(x,0),(x,cut.height-1)])
        for y in range(cut.height):
            queue.extend([(0,y),(cut.width-1,y)])
        while queue:
            x,y=queue.popleft()
            if (x,y) in seen or x<0 or y<0 or x>=cut.width or y>=cut.height:
                continue
            seen.add((x,y))
            if max(cp[x,y][:3])<=24:
                continue
            cp[x,y]=(0,0,0,0)
            queue.extend([(x-1,y),(x+1,y),(x,y-1),(x,y+1)])
        cut.save(target/(name+'.webp'),quality=92,method=6)
    Image.open(ASSETS/'settings-appearance.png').save(target/'settings.webp',quality=92,method=6)
    Image.open(ROOT/'docs/evidence/integration-2.5.0/native-mica-wgc.jpg').save(target/'settings-source-preview.webp',quality=88,method=6)
    Image.open(ROOT/'src/NabyScape/Assets/island.ico').convert('RGBA').resize((64,64),Image.Resampling.LANCZOS).save(target/'logo.png')
    shutil.copy2(ROOT/'src/NabyScape/Assets/island.ico',target/'favicon.ico')
    shutil.copy2(ROOT/'docs/presets/midnight-notch.json',target/'Midnight-Notch.json')
    print('Marketing assets:',target)
