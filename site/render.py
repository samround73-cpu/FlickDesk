"""
Renders still pictures and GIFs of the scenes (needs Playwright + Pillow):

  python3 render.py stills <out-dir> [width height]   one JPG per scene (default 1250 x 833)
  python3 render.py gifs <out-dir> key[,key...]       looping GIFs for the README (640 px wide, 15 fps)
  add --site to use the island-style scenes made for landing.great919.win (site_scenes.py)

Frames are taken by pausing every CSS animation at an exact time, so the output is the same each run.
"""
import asyncio
import io
import pathlib
import sys

from PIL import Image
from playwright.async_api import async_playwright

import importlib

_mod = importlib.import_module("site_scenes" if "--site" in sys.argv else "scenes")
if "--site" in sys.argv:
    sys.argv.remove("--site")
CSS, LOOP, SCENES, STILL = _mod.CSS, _mod.LOOP, _mod.SCENES, _mod.STILL
from scenes import H, W

CHROME = "/opt/pw-browsers/chromium-1194/chrome-linux/chrome"
PAGE = (f"<html><head><style>body{{margin:0;background:#000}}{CSS}"
        f".stage{{position:relative;width:{W}px;height:{H}px;overflow:hidden}}</style></head>"
        f"<body><div class='stage' id='s'></div></body></html>")


async def frames(key, times):
    async with async_playwright() as p:
        b = await p.chromium.launch(executable_path=CHROME)
        pg = await b.new_page(viewport={"width": W, "height": H})
        await pg.set_content(PAGE)
        await pg.evaluate("(h) => document.getElementById('s').innerHTML = h", SCENES[key])
        await pg.wait_for_timeout(200)
        shots = []
        for t in times:
            await pg.evaluate("(t) => document.getAnimations().forEach(a => { a.pause(); a.currentTime = t; })", t * 1000)
            await pg.wait_for_timeout(15)
            shots.append(Image.open(io.BytesIO(await pg.screenshot())).convert("RGB"))
        await b.close()
        return shots


def stills(out, size):
    out.mkdir(parents=True, exist_ok=True)
    for key in SCENES:
        img = asyncio.run(frames(key, [STILL[key]]))[0]
        if size != (W, H):
            img = img.resize(size, Image.LANCZOS)
        img.save(out / f"{key}.jpg", quality=86, optimize=True, progressive=True)
        print("still", key)


def gif(out, key, width=640, fps=15):
    n = int(LOOP[key] * fps)
    shots = asyncio.run(frames(key, [i / fps for i in range(n)]))
    h = round(width * H / W)
    small = [s.resize((width, h), Image.LANCZOS) for s in shots]
    # One shared palette keeps the colours steady from frame to frame.
    palette = small[len(small) // 3].quantize(colors=200, method=Image.MEDIANCUT)
    pal = [s.quantize(palette=palette, dither=Image.Dither.NONE) for s in small]
    out.mkdir(parents=True, exist_ok=True)
    pal[0].save(out / f"{key}.gif", save_all=True, append_images=pal[1:], duration=round(1000 / fps), loop=0, optimize=True, disposal=1)
    print("gif", key, (out / f"{key}.gif").stat().st_size // 1024, "KB")


if __name__ == "__main__":
    mode, out = sys.argv[1], pathlib.Path(sys.argv[2])
    if mode == "stills":
        size = (int(sys.argv[3]), int(sys.argv[4])) if len(sys.argv) > 4 else (W, H)
        stills(out, size)
    else:
        for k in sys.argv[3].split(","):
            gif(out, k)
