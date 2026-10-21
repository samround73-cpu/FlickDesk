# Flick website

The landing page published to GitHub Pages by `.github/workflows/pages.yml`.

- `scenes.py`: the seven animated scenes (plain HTML + CSS keyframes on a 1250 × 833 stage) and their captions.
- `build.py`: writes `index.html` and `flick-live.js` (the same scenes for the gallery on landing.great919.win).
- `render.py`: still pictures (`stills/`, shown when motion is reduced) and the README GIFs. Needs Playwright and Pillow.

After changing a scene:

```
python3 build.py
python3 render.py stills stills
python3 render.py gifs ../docs/images flick,send,resume
```
