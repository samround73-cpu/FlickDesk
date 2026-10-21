"""
Builds the Flick web pages from scenes.py:

  python3 build.py            → site/index.html            (the GitHub Pages landing page)
                              → site/flick-live.js         (live gallery for landing.great919.win)

Still pictures and GIFs are made separately by render.py (needs Playwright).
"""
import html
import json
import pathlib

from scenes import CAPTIONS, CSS, H, SCENES, W

HERE = pathlib.Path(__file__).parent
VERSION = "0.4.0"
REPO = "https://github.com/samround73-cpu/FlickDesk"
DL_WIN = f"{REPO}/releases/latest/download/FlickDesk-windows-x64.exe"
DL_APK = f"{REPO}/releases/latest/download/Flick-android.apk"


def scene(key: str, label: str) -> str:
    return (f'<div class="scene" role="img" aria-label="{html.escape(label)}">'
            f'<img class="still" src="stills/{key}.jpg" alt="" loading="lazy">'
            f'<div class="stage">{SCENES[key]}</div></div>')


def feature_cards() -> str:
    out = []
    for i, (key, kicker, title, text) in enumerate(CAPTIONS[1:], start=2):
        out.append(f"""
      <article class="feature reveal">
        {scene(key, title)}
        <div class="feature-text">
          <span class="kicker">{i:02d} &middot; {kicker}</span>
          <h3>{title}</h3>
          <p>{text}</p>
        </div>
      </article>""")
    return "".join(out)


PAGE = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Flick – throw files between your phone and PC</title>
<meta name="description" content="Flick photos and files between your Android phone and Windows PC. Long-press, flick towards the screen, and it lands on your desktop with a burst of light. Local, encrypted, free.">
<meta property="og:title" content="Flick + FlickDesk">
<meta property="og:description" content="Throw files between your Android phone and Windows PC.">
<meta property="og:image" content="stills/flick.jpg">
<link rel="icon" href="icon.png">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet">
<style>
:root { --bg: #090B10; --panel: #11141B; --line: #222632; --text: #F2F3F5; --muted: #A3A9B6; --faint: #7D8492; --blue: #7CC4FF;
  --spectrum: linear-gradient(90deg, #3DC7FF, #9B7BFF 55%, #FF6BD6); }
* { box-sizing: border-box; }
html { scroll-behavior: smooth; }
body { margin: 0; background: var(--bg); color: var(--text); font-family: Inter, 'Segoe UI', system-ui, sans-serif; line-height: 1.55; -webkit-font-smoothing: antialiased; }
a { color: var(--blue); }
.wrap { width: min(1160px, 100% - 32px); margin: 0 auto; }

/* nav */
.nav { position: sticky; top: 0; z-index: 10; backdrop-filter: blur(14px); background: rgba(9,11,16,.72); border-bottom: 1px solid rgba(255,255,255,.06); }
.nav .wrap { display: flex; align-items: center; gap: 24px; height: 64px; }
.brand { display: flex; align-items: center; gap: 10px; font-weight: 700; font-size: 19px; color: var(--text); text-decoration: none; }
.brand img { width: 30px; height: 30px; border-radius: 8px; }
.nav nav { display: flex; gap: 22px; margin-left: auto; }
.nav nav a { color: var(--muted); text-decoration: none; font-size: 15px; }
.nav nav a:hover { color: var(--text); }
.btn { display: inline-flex; align-items: center; justify-content: center; gap: 10px; min-height: 46px; padding: 0 20px; border-radius: 12px; font-weight: 600; font-size: 15.5px;
  text-decoration: none; border: 1px solid transparent; transition: transform .2s, box-shadow .2s, background .2s; }
.btn:hover { transform: translateY(-1px); }
.btn.primary { background: var(--text); color: #0B0D12; box-shadow: 0 10px 30px -10px rgba(124,196,255,.6); }
.btn.ghost { color: var(--text); border-color: #2E3340; background: rgba(255,255,255,.03); }
.btn svg { width: 18px; height: 18px; }
.nav .btn { min-height: 38px; padding: 0 14px; font-size: 14px; }

/* hero */
.hero { position: relative; padding: 72px 0 40px; overflow: hidden; }
.hero::before { content: ""; position: absolute; inset: -30% -10% auto 30%; height: 760px; pointer-events: none;
  background: radial-gradient(closest-side, rgba(155,123,255,.22), transparent 70%), radial-gradient(closest-side at 70% 40%, rgba(61,199,255,.18), transparent 70%); }
.hero .wrap { position: relative; display: grid; grid-template-columns: minmax(0, 5fr) minmax(0, 7fr); gap: 48px; align-items: center; }
.eyebrow { display: inline-flex; align-items: center; gap: 8px; font-size: 13px; font-weight: 600; letter-spacing: .12em; text-transform: uppercase; color: var(--muted); }
.eyebrow::before { content: ""; width: 26px; height: 3px; border-radius: 2px; background: var(--spectrum); }
h1 { font-size: clamp(42px, 6vw, 72px); line-height: 1.02; letter-spacing: -.035em; margin: 16px 0 18px; font-weight: 700; }
h1 em { font-style: normal; background: var(--spectrum); -webkit-background-clip: text; background-clip: text; color: transparent; }
.lede { font-size: 19px; color: var(--muted); margin: 0 0 28px; max-width: 34em; }
.cta { display: flex; flex-wrap: wrap; gap: 12px; }
.meta { margin-top: 18px; font-size: 14px; color: var(--faint); }
.hero-scene { position: relative; }
.hero-scene::after { content: ""; position: absolute; inset: 8% -4% -8%; z-index: -1; filter: blur(60px); opacity: .5; background: var(--spectrum); border-radius: 50%; }

/* scenes */
.scene { position: relative; aspect-ratio: __W__ / __H__; border-radius: 22px; overflow: hidden; background: #0D1016;
  box-shadow: 0 40px 80px -40px rgba(0,0,0,.9), 0 0 0 1px rgba(255,255,255,.06); }
.scene .stage { position: absolute; left: 0; top: 0; width: __W__px; height: __H__px; transform-origin: 0 0; transform: scale(var(--s, .4)); }
.scene .still { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; display: none; }
.scene.paused .stage * { animation-play-state: paused !important; }
@media (prefers-reduced-motion: reduce) { .scene .stage { display: none; } .scene .still { display: block; } }

/* sections */
section { padding: 64px 0; }
.section-head { max-width: 44em; margin-bottom: 34px; }
h2 { font-size: clamp(30px, 4vw, 44px); letter-spacing: -.03em; line-height: 1.1; margin: 10px 0 12px; }
.section-head p { color: var(--muted); font-size: 18px; margin: 0; }
.features { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 26px; }
.feature { background: var(--panel); border: 1px solid var(--line); border-radius: 26px; padding: 14px; transition: transform .3s, border-color .3s; }
.feature:hover { transform: translateY(-3px); border-color: #343A48; }
.feature .scene { border-radius: 16px; }
.feature-text { padding: 18px 10px 8px; }
.kicker { font-size: 12px; font-weight: 600; letter-spacing: .14em; text-transform: uppercase; color: var(--blue); }
.feature h3 { font-size: 23px; margin: 6px 0 6px; letter-spacing: -.01em; }
.feature p { margin: 0; color: var(--muted); }

.steps { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 20px; counter-reset: step; }
.step { position: relative; background: var(--panel); border: 1px solid var(--line); border-radius: 22px; padding: 26px; }
.step::before { counter-increment: step; content: counter(step); display: grid; place-items: center; width: 40px; height: 40px; border-radius: 12px; margin-bottom: 16px;
  font-weight: 700; color: #0B0D12; background: var(--spectrum); }
.step h3 { margin: 0 0 6px; font-size: 19px; }
.step p { margin: 0; color: var(--muted); font-size: 15.5px; }

.privacy { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 20px; }
.privacy div { border-top: 2px solid #2A2F3B; padding-top: 18px; }
.privacy h3 { margin: 0 0 6px; font-size: 18px; }
.privacy p { margin: 0; color: var(--muted); font-size: 15.5px; }

.downloads { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 22px; }
.dl { position: relative; overflow: hidden; background: var(--panel); border: 1px solid var(--line); border-radius: 26px; padding: 30px; }
.dl::after { content: ""; position: absolute; right: -80px; top: -80px; width: 240px; height: 240px; border-radius: 50%; background: var(--spectrum); filter: blur(70px); opacity: .25; }
.dl h3 { margin: 0 0 4px; font-size: 24px; }
.dl p { color: var(--muted); margin: 0 0 22px; }
.dl ul { margin: 0 0 24px; padding-left: 18px; color: var(--muted); font-size: 15px; }

footer { border-top: 1px solid rgba(255,255,255,.06); padding: 34px 0 48px; color: var(--faint); font-size: 14px; }
footer .wrap { display: flex; flex-wrap: wrap; gap: 18px; justify-content: space-between; }
footer a { color: var(--muted); text-decoration: none; margin-left: 18px; }

.reveal { opacity: 0; transform: translateY(24px); transition: opacity .7s ease, transform .7s cubic-bezier(.16,1,.3,1); }
.reveal.in { opacity: 1; transform: none; }
@media (prefers-reduced-motion: reduce) { .reveal { opacity: 1; transform: none; transition: none; } }

@media (max-width: 900px) {
  .hero .wrap { grid-template-columns: minmax(0, 1fr); }
  .features, .downloads { grid-template-columns: minmax(0, 1fr); }
  .steps, .privacy { grid-template-columns: minmax(0, 1fr); }
  .nav nav { display: none; }
  .nav .btn { margin-left: auto; }
}
__CSS__
</style>
</head>
<body>
<header class="nav"><div class="wrap">
  <a class="brand" href="#top"><img src="icon.png" alt="">Flick</a>
  <nav aria-label="Sections"><a href="#features">Features</a><a href="#setup">Set up</a><a href="#privacy">Privacy</a><a href="__REPO__">GitHub</a></nav>
  <a class="btn primary" href="#download">Download</a>
</div></header>

<main id="top">
  <section class="hero"><div class="wrap">
    <div>
      <span class="eyebrow">Android &harr; Windows</span>
      <h1>Throw files at your <em>PC</em>.</h1>
      <p class="lede">Hold your phone next to your screen, long-press a photo and flick it towards the PC. Light washes in from that edge and the file lands on your desktop. Drag files to the screen edge to send them back.</p>
      <div class="cta">
        <a class="btn primary" href="__DL_WIN__"><svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M3 5.5 10.5 4.4v7.1H3zm8.5-1.2L21 3v8.5h-9.5zM3 12.5h7.5v7.1L3 18.5zm8.5 0H21V21l-9.5-1.3z"/></svg>Download for Windows</a>
        <a class="btn ghost" href="__DL_APK__"><svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M17.6 9.48 19.44 6.3a.5.5 0 0 0-.87-.5l-1.86 3.22a11.4 11.4 0 0 0-9.42 0L5.43 5.8a.5.5 0 0 0-.87.5L6.4 9.48A10.8 10.8 0 0 0 1 18h22a10.8 10.8 0 0 0-5.4-8.52M7 15.25a1.25 1.25 0 1 1 0-2.5 1.25 1.25 0 0 1 0 2.5m10 0a1.25 1.25 0 1 1 0-2.5 1.25 1.25 0 0 1 0 2.5"/></svg>Get the Android app</a>
      </div>
      <p class="meta">Version __VERSION__ &middot; free and open source &middot; no account, no cloud</p>
    </div>
    <div class="hero-scene">__HERO__</div>
  </div></section>

  <section id="features"><div class="wrap">
    <div class="section-head reveal">
      <span class="eyebrow">How it feels</span>
      <h2>Both directions, no cables, no cloud.</h2>
      <p>Everything stays on your own Wi-Fi. Photos, videos, whole folders and clipboard text, with a little light show each way.</p>
    </div>
    <div class="features">__FEATURES__
    </div>
  </div></section>

  <section id="setup"><div class="wrap">
    <div class="section-head reveal">
      <span class="eyebrow">Set up</span>
      <h2>Ready in three minutes.</h2>
      <p>Your phone and PC just need to be on the same Wi-Fi.</p>
    </div>
    <div class="steps">
      <div class="step reveal"><h3>Run FlickDesk on Windows</h3><p>One file, nothing to install. Allow it on <b>Private networks</b> when Windows Firewall asks. It lives in the system tray.</p></div>
      <div class="step reveal"><h3>Install Flick on Android</h3><p>Open the APK on your phone and allow the install. Android 10 or newer.</p></div>
      <div class="step reveal"><h3>Scan the QR code</h3><p>In Flick, tap <b>+</b>, scan the code on your PC and check the six-digit number matches. That's it.</p></div>
    </div>
  </div></section>

  <section id="privacy"><div class="wrap">
    <div class="section-head reveal">
      <span class="eyebrow">Private by design</span>
      <h2>Your files never leave your network.</h2>
    </div>
    <div class="privacy">
      <div class="reveal"><h3>Local only</h3><p>Devices find each other on your Wi-Fi and connect directly. Nothing goes through the internet.</p></div>
      <div class="reveal"><h3>Paired with a check number</h3><p>A one-time QR code and a six-digit number on both screens prove you paired the right device.</p></div>
      <div class="reveal"><h3>End-to-end encrypted</h3><p>ECDH P-256 key exchange, a fresh key for every connection, and AES-256-GCM for every byte.</p></div>
    </div>
  </div></section>

  <section id="download"><div class="wrap">
    <div class="section-head reveal">
      <span class="eyebrow">Download</span>
      <h2>Get both halves.</h2>
      <p>These links always point to the latest release.</p>
    </div>
    <div class="downloads">
      <div class="dl reveal">
        <h3>FlickDesk for Windows</h3><p>The tray app: receives flicks and sends files to your phone.</p>
        <ul><li>Windows 10 (1903+) or 11, 64-bit</li><li>Single exe; SmartScreen may ask once (More info &rarr; Run anyway)</li></ul>
        <a class="btn primary" href="__DL_WIN__">Download FlickDesk-windows-x64.exe</a>
      </div>
      <div class="dl reveal">
        <h3>Flick for Android</h3><p>The phone app: flick photos and files, receive in the background.</p>
        <ul><li>Android 10 or newer</li><li>Allow &ldquo;install unknown apps&rdquo; when asked</li></ul>
        <a class="btn primary" href="__DL_APK__">Download Flick-android.apk</a>
      </div>
    </div>
  </div></section>
</main>

<footer><div class="wrap">
  <span>Flick + FlickDesk &middot; made by Sam Round</span>
  <span><a href="__REPO__">GitHub</a><a href="__REPO__/releases">Releases</a><a href="__REPO__/issues">Report a problem</a></span>
</div></footer>

<script>
// Each scene is drawn at __W__x__H__ and scaled to its box; scenes off screen are paused.
(function () {
  var scenes = document.querySelectorAll('.scene');
  var fit = function (el) { el.style.setProperty('--s', (el.clientWidth / __W__).toFixed(4)); };
  var ro = new ResizeObserver(function (entries) { entries.forEach(function (e) { fit(e.target); }); });
  var io = new IntersectionObserver(function (entries) {
    entries.forEach(function (e) { e.target.classList.toggle('paused', !e.isIntersecting); });
  }, { rootMargin: '100px 0px' });
  scenes.forEach(function (s) { fit(s); ro.observe(s); io.observe(s); });
  document.addEventListener('visibilitychange', function () {
    scenes.forEach(function (s) { if (document.hidden) s.classList.add('paused'); });
  });
  var rio = new IntersectionObserver(function (entries) {
    entries.forEach(function (e) { if (e.isIntersecting) { e.target.classList.add('in'); rio.unobserve(e.target); } });
  }, { threshold: .12 });
  document.querySelectorAll('.reveal').forEach(function (el) { rio.observe(el); });
})();
</script>
</body>
</html>
"""


def build_page() -> str:
    page = PAGE
    for k, v in {
        "__CSS__": CSS, "__HERO__": scene("flick", "A photo flicked from the phone lands on the PC"),
        "__FEATURES__": feature_cards(), "__W__": str(W), "__H__": str(H),
        "__REPO__": REPO, "__DL_WIN__": DL_WIN, "__DL_APK__": DL_APK, "__VERSION__": VERSION,
    }.items():
        page = page.replace(k, v)
    return page


LIVE_JS = """// Live Flick scenes for the Home page gallery (generated by FlickDesk/site/build.py – edit scenes.py there).
// Each gallery card's picture is replaced by an animated copy of that scene, drawn at %(W)dx%(H)d and
// scaled to fit. Runs only with Motion on Full, while the gallery is on screen and the tab is visible;
// otherwise the still pictures in projects/ are shown.
(function () {
  var drift = document.getElementById('flickDrift');
  if (!drift) return;
  var CSS = %(css)s;
  var T = %(scenes)s;
  var style = document.createElement('style');
  style.textContent = CSS + '.flick-stage{width:%(W)dpx!important;height:%(H)dpx!important}';
  document.head.appendChild(style);

  var reduce = window.matchMedia('(prefers-reduced-motion: reduce)');
  var on = function () { return !reduce.matches && (window.motionLevel ? window.motionLevel() : 'full') === 'full'; };
  var keyOf = function (card) { var img = card.querySelector('img'); var m = img && /flick-([a-z]+)\\.jpg/.exec(img.getAttribute('src')); return m ? m[1] : null; };
  var cardIO = new IntersectionObserver(function (entries) {
    entries.forEach(function (e) { e.target.classList.toggle('is-seen', e.isIntersecting); });
  }, { root: drift, rootMargin: '0px 60px' });
  function mount() {
    drift.querySelectorAll('.drift-card').forEach(function (card) {
      if (card._live) return;
      var key = keyOf(card);
      if (!key || !T[key]) return;
      var stage = document.createElement('div');
      stage.className = 'live-stage flick-stage';
      stage.setAttribute('aria-hidden', 'true');
      stage.innerHTML = T[key];
      card.insertBefore(stage, card.querySelector('.drift-text'));
      card._live = true;
      cardIO.observe(card);
    });
  }
  var visible = false, mounted = false;
  function fit() {
    var card = drift.querySelector('.drift-card');
    if (card && card.clientWidth) drift.style.setProperty('--live-s', (card.clientWidth / %(W)d).toFixed(4));
  }
  function update() {
    var live = on();
    var run = live && visible && !document.hidden;
    if (run && !mounted) { mount(); mounted = true; }
    if (run) fit();
    drift.classList.toggle('is-live', live && mounted);
    drift.classList.toggle('is-running', run);
  }
  new IntersectionObserver(function (e) { visible = e[0].isIntersecting; update(); }, { rootMargin: '300px 0px' }).observe(drift);
  new MutationObserver(function () { if (mounted) mount(); }).observe(drift.querySelector('.drift-track'), { childList: true });
  new MutationObserver(update).observe(document.documentElement, { attributes: true, attributeFilter: ['data-motion'] });
  document.addEventListener('visibilitychange', update);
  window.addEventListener('resize', fit);
  update();
})();
"""


def build_live_js() -> str:
    return LIVE_JS % {"W": W, "H": H, "css": json.dumps(CSS), "scenes": json.dumps(SCENES)}


if __name__ == "__main__":
    (HERE / "index.html").write_text(build_page(), encoding="utf-8")
    (HERE / "flick-live.js").write_text(build_live_js(), encoding="utf-8")
    print("wrote index.html and flick-live.js")
