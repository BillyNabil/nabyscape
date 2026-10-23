"""Compose documentation from real WPF captures; requires Pillow and FFmpeg.

python tools/render_showcase.py path/to/diagnostic-showcase
The capture directory must contain chapters.json, frames/ and the named PNGs.
python tools/render_showcase.py --social-only
Rebuild only the repository link banner from the committed app captures.
"""
from pathlib import Path
import json
import subprocess
import sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
SRC = Path(sys.argv[1]).resolve()
OUT = ROOT / 'Assets' / 'showcase'
OUT.mkdir(parents=True, exist_ok=True)
INK = '#171b24'
MUTED = '#536070'
FONT = Path('C:/Windows/Fonts')


def font(size, bold=False):
    return ImageFont.truetype(str(FONT / ('seguisb.ttf' if bold else 'segoeui.ttf')), size)


def backdrop(size):
    w, h = size
    small = Image.new('RGB', (160, 100))
    px = small.load()
    for y in range(100):
        for x in range(160):
            blue = max(0, (x / 159 - .47) / .53) ** 1.7
            warm = max(0, 1 - abs(y / 99 - .5) * 2) * .45
            a, b = (244, 243 - 9 * warm, 235 - 6 * warm), (170, 193, 229)
            px[x, y] = tuple(round(a[i] * (1 - blue) + b[i] * blue) for i in range(3))
    return small.resize((w, h), Image.Resampling.BICUBIC).convert('RGBA')


def text(img, xy, value, size=26, bold=False, fill=INK):
    ImageDraw.Draw(img).text(xy, value, font=font(size, bold), fill=fill, spacing=5)


def shot(name):
    im = Image.open(SRC / f'{name}.png').convert('RGBA')
    return im.crop(im.getbbox())


def place(img, im, xy, width=None, shadow=True):
    if width:
        im = im.resize((width, round(im.height * width / im.width)), Image.Resampling.LANCZOS)
    x, y = map(round, xy)
    if shadow:
        layer = Image.new('RGBA', img.size)
        alpha = im.getchannel('A').point(lambda a: a // 6)
        layer.paste(Image.new('RGBA', im.size, (22, 28, 44)), (x, y + 16), alpha)
        img.alpha_composite(layer.filter(ImageFilter.GaussianBlur(22)))
    img.alpha_composite(im, (x, y))
    return im.size


def save(img, name):
    img.convert('RGB').save(OUT / name, optimize=True)


def label(img, number, title, subtitle, x, y):
    text(img, (x, y), number, 19, True, MUTED)
    text(img, (x, y + 37), title, 36, True)
    text(img, (x, y + 91), subtitle, 21, fill=MUTED)


logo = Image.open(ROOT / 'src' / 'NabyScape' / 'Assets' / 'island.ico').convert('RGBA')
# GitHub's social image is separate from the README hero; use its 2:1 format.
social = backdrop((1280, 640))
place(social, logo, (64, 56), 46, False)
text(social, (124, 58), 'NabyScape', 32, True)
text(social, (64, 155), 'Your desktop,\nwithin reach.', 74, True)
text(social, (69, 356), 'Music, quick tools, and system stats.\nA customizable island for Windows 11.', 24, fill=MUTED)
place(social, Image.open(OUT / 'media-raw.png').convert('RGBA'), (699, 157), 513)
place(social, Image.open(OUT / 'closed-raw.png').convert('RGBA'), (735, 493), 442)
text(social, (70, 526), 'OPEN SOURCE  /  LOCAL DATA', 20, True, MUTED)
text(social, (70, 582), 'github.com/BillyNabil/nabyscape', 20, fill=MUTED)
save(social, 'social-preview.png')
if sys.argv[1] == '--social-only':
    print('Social preview ready:', OUT / 'social-preview.png')
    sys.exit(0)

# A legible repository header with the app itself as the hero.
hero = backdrop((1600, 1000))
place(hero, logo, (80, 57), 42, False)
text(hero, (135, 57), 'NabyScape', 31, True)
text(hero, (83, 173), 'Your desktop,\nwithin reach.', 96, True)
text(hero, (88, 431), 'Music, quick tools, and a quieter workflow.\nA customizable island for Windows 11.', 29, fill=MUTED)
text(hero, (88, 554), 'LOCAL DATA  /  OPEN SOURCE  /  WINDOWS 11', 18, True, MUTED)
place(hero, shot('media'), (896, 175), 610)
place(hero, shot('hover-music'), (910, 632), 580)
text(hero, (89, 699), 'Small by default.', 35, True)
place(hero, shot('closed'), (90, 765), 500)
text(hero, (90, 894), 'Midnight Notch showcase · v2.4.2', 21, fill=MUTED)
save(hero, 'hero.png')

# Closed, preview and panel in one continuous reading direction.
states = backdrop((1600, 720))
text(states, (60, 38), 'One island. Three ways in.', 46, True)
for number, title, subtitle, name, x, width in [
    ('01', 'Glance', 'Artwork, waveform, bold clock.', 'closed', 60, 370),
    ('02', 'Hover', 'Playback without the full panel.', 'hover-music', 530, 455),
    ('03', 'Open', 'Seek, switch sessions, set volume.', 'media', 1060, 480),
]:
    label(states, number, title, subtitle, x, 143)
    place(states, shot(name), (x, 325), width)
text(states, (60, 657), 'Hover → scroll or swipe between previews → click to open', 23, fill=MUTED)
save(states, 'interaction.png')

# Individual feature posters retain full readable controls.
features = [
    ('system', 'A quick look at your PC.', 'CPU, memory, GPU and uptime. Details in their own tabs.'),
    ('notes', 'Room for a thought.', 'One local document, shared across the island and mini window.'),
    ('tasks', 'The next small thing.', 'Add a task. Check it off. Keep your momentum.'),
    ('shelf', 'Files, ready when you are.', 'Stage references, select a few, and drag them out together.'),
    ('calendar', 'Keep the day in view.', 'A local calendar with events you add yourself.'),
    ('tools', 'Everyday tools, one place.', 'Show only the tools and hover pages you actually use.'),
    ('focus', 'A little time to focus.', 'A timer and work/break sessions with shared countdowns.'),
    ('detached-notes', 'Take a preview with you.', 'Drag the grip to detach. Close it without stopping the activity.'),
]
for name, title, description in features:
    poster = backdrop((1100, 780))
    text(poster, (54, 35), title, 42, True)
    text(poster, (56, 102), description, 22, fill=MUTED)
    im = shot(name)
    width = min(650, round(im.width * min(1.17, 500 / im.height)))
    place(poster, im, ((1100 - width) // 2, 190), width)
    text(poster, (56, 727), 'NabyScape 2.4.2 · Midnight Notch', 18, fill=MUTED)
    save(poster, name + '.png')

for name in ['closed', 'hover-music', 'media']:
    im = shot(name)
    im.save(OUT / (name + '-raw.png'), optimize=True)

# Eight real Settings pages, no illustrative substitutes for the controls.
for category in ['appearance', 'layout', 'content', 'controls', 'audio', 'system', 'launch', 'application']:
    Image.open(SRC / ('settings-' + category + '.png')).save(OUT / ('settings-' + category + '.png'), optimize=True)

settings = backdrop((1600, 1280))
text(settings, (60, 38), 'Make it feel like your desktop.', 55, True)
text(settings, (63, 122), 'Eight focused categories. Live previews. Settings that stay where you left them.', 26, fill=MUTED)
place(settings, Image.open(OUT / 'settings-appearance.png').convert('RGBA'), (120, 216), 1360)
save(settings, 'settings.png')

# Measured values are scoped to the matched local workload, not a universal claim.
perf = backdrop((1600, 740))
text(perf, (60, 38), 'Less background work. Same visual detail.', 49, True)
text(perf, (64, 116), 'Median total-machine CPU · v2.4.1 → v2.4.2 · local controlled workloads', 25, fill=MUTED)
draw = ImageDraw.Draw(perf)
for row, (name, before, after) in enumerate([
    ('Hidden island', .322, .019), ('Idle, no music', .310, .013),
    ('Media panel', 1.710, .666), ('System overview', 1.175, .174),
]):
    y = 210 + row * 105
    text(perf, (65, y), name, 27, True)
    for i, (v, color) in enumerate([(before, '#a6afbc'), (after, '#293e59')]):
        draw.rounded_rectangle((480, y + i * 24 + 5, 480 + max(3, v / 1.71 * 700), y + i * 24 + 20), 7, fill=color)
    text(perf, (1250, y + 3), f'{before:.3f}% → {after:.3f}%', 25, True)
text(perf, (65, 666), '3 × 60 s per scene · 16 logical processors · sample media · full methodology linked below', 21, fill=MUTED)
save(perf, 'performance.png')

# Silent MP4 and GitHub-friendly inline GIF from the actual WPF frame sequence.
meta = json.loads((SRC / 'chapters.json').read_text(encoding='utf-8-sig'))
bg = backdrop((1280, 800))
text(bg, (64, 600), 'NabyScape', 28, True)
text(bg, (64, 650), 'Midnight Notch · Windows 11 · v2.4.2', 20, fill=MUTED)
text(bg, (64, 746), 'Actual app views · sample media and local content · silent demo', 17, fill=MUTED)
bg_path = SRC / 'video-background.png'
save_path = bg.convert('RGB'); save_path.save(bg_path)
filters = ['[0:v][1:v]overlay=(W-w)/2:0:shortest=1[scene]']
last = 'scene'
for i, chapter in enumerate(meta['chapters']):
    file = SRC / f'caption-{i}.txt'
    file.write_text(chapter['caption'], encoding='utf-8')
    # Paths are relative to SRC, avoiding Windows drive-colon escaping in filters.
    end = chapter['start'] + chapter['seconds']
    filters.append(f"[{last}]drawtext=fontfile='C\\:/Windows/Fonts/seguisb.ttf':textfile='caption-{i}.txt':fontcolor=0x171b24:fontsize=42:x=64:y=527:enable='gte(t,{chapter['start']})*lt(t,{end})'[cap{i}]")
    last = f'cap{i}'
filters.append(f'[{last}]format=yuv420p[out]')
filter_path = SRC / 'video-filter.txt'
filter_path.write_text(';\n'.join(filters), encoding='utf-8')
subprocess.run(['ffmpeg', '-y', '-hide_banner', '-loglevel', 'error', '-loop', '1', '-framerate', '30', '-i', str(bg_path),
    '-framerate', '30', '-i', str(SRC / 'frames' / '%05d.png'), '-filter_complex_script', str(filter_path),
    '-map', '[out]', '-t', str(meta['frames'] / 30), '-c:v', 'libx264', '-crf', '19', '-preset', 'medium',
    '-movflags', '+faststart', '-an', str(OUT / 'nabyscape-demo.mp4')], check=True, cwd=SRC)
subprocess.run(['ffmpeg', '-y', '-hide_banner', '-loglevel', 'error', '-i', str(OUT / 'nabyscape-demo.mp4'),
    '-filter_complex', 'fps=15,scale=960:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=128:stats_mode=diff[p];[b][p]paletteuse=dither=sierra2_4a:diff_mode=rectangle',
    '-loop', '0', str(OUT / 'demo.gif')], check=True)
print('Showcase ready:', OUT)
