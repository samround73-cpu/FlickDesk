"""
Animated Flick scenes, shared by the GitHub Pages site (site/index.html), the gallery on
landing.great919.win (flick-live.js) and the README GIFs.

Every scene is drawn on a 1250 x 833 stage (the size the website's .live-stage uses) and loops
with plain CSS animations, so nothing ticks in JavaScript. Class names start with "fx-".
"""
import random

W, H = 1250, 833

# ---------------------------------------------------------------- a QR-looking grid (decorative)

def _qr(size: int) -> str:
    rnd = random.Random(7)
    n = 29
    cells = []

    def finder(x0, y0):
        for y in range(7):
            for x in range(7):
                if x in (0, 6) or y in (0, 6) or (2 <= x <= 4 and 2 <= y <= 4):
                    cells.append((x0 + x, y0 + y))

    for fx, fy in [(0, 0), (n - 7, 0), (0, n - 7)]:
        finder(fx, fy)

    def reserved(x, y):
        return (x < 8 and y < 8) or (x >= n - 8 and y < 8) or (x < 8 and y >= n - 8)

    for y in range(n):
        for x in range(n):
            if not reserved(x, y) and rnd.random() < 0.47:
                cells.append((x, y))
    rects = "".join(f'<rect x="{x}" y="{y}" width="1.04" height="1.04"/>' for x, y in cells)
    return (f'<svg viewBox="-2 -2 {n + 4} {n + 4}" width="{size}" height="{size}" aria-hidden="true">'
            f'<rect x="-2" y="-2" width="{n + 4}" height="{n + 4}" fill="#fff"/><g fill="#0E1222">{rects}</g></svg>')


CURSOR = ('<svg class="fx-cursor" width="30" height="38" viewBox="0 0 24 30" aria-hidden="true">'
          '<path d="M2 2 L2 24 L8 18 L12 28 L16 26 L12 16 L20 16 Z" fill="#fff" stroke="#000" stroke-width="1.5"/></svg>')

WIFI_ON = ('<svg width="44" height="44" viewBox="0 0 24 24" fill="none" stroke="#7CC4FF" stroke-width="2" stroke-linecap="round" aria-hidden="true">'
           '<path d="M2 8.5a15 15 0 0 1 20 0"/><path d="M5 12a10 10 0 0 1 14 0"/><path d="M8.5 15.5a5 5 0 0 1 7 0"/><circle cx="12" cy="19" r="1.2" fill="#7CC4FF"/></svg>')
WIFI_OFF = ('<svg width="44" height="44" viewBox="0 0 24 24" fill="none" stroke="#FFB38A" stroke-width="2" stroke-linecap="round" aria-hidden="true">'
            '<path d="M2 8.5a15 15 0 0 1 20 0" opacity=".35"/><path d="M5 12a10 10 0 0 1 14 0" opacity=".35"/><path d="M8.5 15.5a5 5 0 0 1 7 0" opacity=".35"/>'
            '<circle cx="12" cy="19" r="1.2" fill="#FFB38A"/><path d="M3 3l18 18"/></svg>')
FOLDER = ('<svg width="190" height="150" viewBox="0 0 190 150" aria-hidden="true">'
          '<path d="M10 28a14 14 0 0 1 14-14h46l16 16h80a14 14 0 0 1 14 14v84a14 14 0 0 1-14 14H24a14 14 0 0 1-14-14z" fill="#3A5FA8"/>'
          '<path d="M10 52a14 14 0 0 1 14-14h142a14 14 0 0 1 14 14v76a14 14 0 0 1-14 14H24a14 14 0 0 1-14-14z" fill="#5C86D6"/></svg>')

# ---------------------------------------------------------------- shared look

CSS = r"""
.fx { position: absolute; inset: 0; overflow: hidden; font-family: Inter, 'Segoe UI', system-ui, sans-serif; color: #F2F3F5;
  background: radial-gradient(120% 90% at 15% 0%, #2B3446 0%, #141821 55%, #0D1016 100%); }
.fx * { box-sizing: border-box; }
.fx-mon { position: absolute; border-radius: 22px; background: #0A0C11; border: 3px solid #2E3340; box-shadow: 0 40px 90px rgba(0,0,0,.55); }
.fx-scr { position: absolute; inset: 12px; border-radius: 12px; overflow: hidden; background: linear-gradient(160deg, #1E2740, #0F131C); }
.fx-win { position: absolute; border-radius: 12px; background: #1B1F28; border: 1px solid #262B36; }
.fx-phone { position: absolute; border-radius: 48px; background: #05070A; border: 3px solid #3A404D; box-shadow: 0 40px 90px rgba(0,0,0,.6); }
.fx-pscr { position: absolute; inset: 12px; border-radius: 36px; overflow: hidden; background: #0E1016; }
.fx-p1 { background: linear-gradient(135deg, #F4A261, #E76F51); } .fx-p2 { background: linear-gradient(160deg, #5EC2E8, #2D6FA3); }
.fx-p3 { background: linear-gradient(140deg, #9BD18A, #3E8E6A); } .fx-p4 { background: linear-gradient(135deg, #C9A7FF, #6A4FD1); }
.fx-p5 { background: linear-gradient(150deg, #FFD58A, #E79E3B); } .fx-p6 { background: linear-gradient(135deg, #FF9AB8, #C2457A); }
.fx-p7 { background: linear-gradient(135deg, #7FE3C4, #2E8C8C); } .fx-p8 { background: linear-gradient(135deg, #A3B7FF, #3B4FB0); }
.fx-p9 { background: linear-gradient(135deg, #F7B2A0, #B5564C); }
.fx-muted { color: #A3A9B6; } .fx-faint { color: #8C93A1; } .fx-good { color: #4CD98A; } .fx-warn { color: #FFB38A; }
.fx-dot { display: inline-block; width: 10px; height: 10px; border-radius: 5px; background: #4CD98A; }
.fx-chip { display: inline-flex; align-items: center; gap: 10px; padding: 9px 16px; border-radius: 999px; background: #1C2029; border: 1px solid #2A2E37; font-size: 17px; }
.fx-grid { position: absolute; display: grid; grid-template-columns: repeat(3, 74px); gap: 6px; }
.fx-grid i { display: block; width: 74px; height: 74px; border-radius: 9px; }
.fx-stack { position: absolute; inset: 0; }
.fx-stack > * { position: absolute; inset: 0; }
.fx-cursor { position: absolute; right: -14px; bottom: -22px; filter: drop-shadow(0 2px 4px rgba(0,0,0,.5)); }

/* ---------------- 01 Flick it ---------------- */
.fx-flick .fx-fly { position: absolute; left: 1006px; top: 357px; width: 74px; height: 74px; border-radius: 9px;
  animation: fx-flick-fly 5s cubic-bezier(.3,.7,.3,1) infinite; box-shadow: 0 0 0 3px rgba(255,255,255,.9); }
@keyframes fx-flick-fly {
  0% { transform: none; opacity: 1; box-shadow: 0 0 0 0 rgba(255,255,255,0); }
  12%, 18% { transform: translateY(-8px) scale(1.18) rotate(-4deg); opacity: 1; box-shadow: 0 0 0 3px rgba(255,255,255,.9), 0 24px 40px rgba(0,0,0,.6); }
  34% { transform: translate(-330px, -50px) scale(1.5) rotate(-16deg); opacity: 1; }
  40% { transform: translate(-440px, -60px) scale(1.5) rotate(-20deg); opacity: 0; }
  92% { transform: none; opacity: 0; }
  100% { transform: none; opacity: 1; box-shadow: 0 0 0 0 rgba(255,255,255,0); }
}
.fx-flick .fx-edge { position: absolute; right: 0; top: 0; bottom: 0; width: 320px; opacity: 0;
  background: linear-gradient(270deg, rgba(124,196,255,.65), rgba(155,123,255,.3) 38%, rgba(255,107,214,0));
  animation: fx-edge-wash 5s ease-out infinite; }
@keyframes fx-edge-wash { 0%, 34% { opacity: 0; } 42% { opacity: 1; } 72% { opacity: .55; } 88%, 100% { opacity: 0; } }
.fx-flick .fx-ring, .fx-fx .fx-ring { position: absolute; border-radius: 50%; border: 5px solid; opacity: 0; }
.fx-flick .fx-ring { right: -260px; top: 50px; width: 520px; height: 420px; animation: fx-ring 5s ease-out infinite; }
.fx-flick .fx-ring.r2 { animation-delay: .18s; } .fx-flick .fx-ring.r3 { animation-delay: .36s; }
.fx-ring.r1 { border-color: rgba(61,199,255,.75); } .fx-ring.r2 { border-color: rgba(155,123,255,.6); } .fx-ring.r3 { border-color: rgba(255,107,214,.45); }
@keyframes fx-ring { 0%, 36% { transform: scale(.2); opacity: 0; } 41% { transform: scale(.45); opacity: 1; } 80%, 100% { transform: scale(1.7); opacity: 0; } }
.fx-flick .fx-saved { position: absolute; right: 150px; top: 150px; width: 250px; padding: 11px; border-radius: 16px; background: #1A1D25;
  border: 1px solid #2E3340; box-shadow: 0 26px 60px rgba(0,0,0,.55); opacity: 0; animation: fx-saved 5s cubic-bezier(.2,.8,.2,1) infinite; }
.fx-flick .fx-saved i { display: block; height: 140px; border-radius: 9px; }
@keyframes fx-saved { 0%, 42% { opacity: 0; transform: translateX(90px) rotate(6deg) scale(.9); }
  52% { opacity: 1; transform: rotate(-3deg); } 86% { opacity: 1; transform: rotate(-3deg); } 94%, 100% { opacity: 0; transform: rotate(-3deg) translateY(10px); } }
.fx-flick .fx-pglow { position: absolute; left: 0; top: 0; bottom: 0; width: 90px; opacity: 0;
  background: linear-gradient(90deg, rgba(124,196,255,.6), rgba(155,123,255,0)); animation: fx-pglow 5s ease-out infinite; }
@keyframes fx-pglow { 0%, 16% { opacity: 0; } 26% { opacity: 1; } 44%, 100% { opacity: 0; } }

/* ---------------- 02 Drag to the edge ---------------- */
.fx-send .fx-drag { position: absolute; left: 230px; top: 470px; width: 130px; height: 96px; animation: fx-drag 6s cubic-bezier(.45,.05,.3,1) infinite; }
.fx-send .fx-drag i { position: absolute; inset: 0; border-radius: 10px; box-shadow: 0 18px 40px rgba(0,0,0,.5); transform: rotate(-6deg); }
@keyframes fx-drag { 0% { transform: translate(-40px, 40px); opacity: 0; } 6% { opacity: 1; transform: none; }
  36% { transform: translate(400px, -160px); opacity: 1; } 42% { transform: translate(420px, -170px) scale(.3); opacity: 0; } 100% { opacity: 0; transform: translate(420px, -170px) scale(.3); } }
.fx-send .fx-halo { position: absolute; right: 30px; top: 110px; width: 230px; height: 276px; border-radius: 30px; background: rgba(24,27,35,.86);
  border: 1px solid #7CC4FF; box-shadow: 0 0 46px -6px rgba(124,196,255,.7), 0 30px 70px rgba(0,0,0,.5);
  display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 16px; opacity: 0; animation: fx-halo 6s cubic-bezier(.2,.8,.2,1) infinite; }
@keyframes fx-halo { 0%, 16% { opacity: 0; transform: translateX(70px) scale(.94); } 26% { opacity: 1; transform: none; }
  86% { opacity: 1; transform: none; } 94%, 100% { opacity: 0; transform: translateX(70px) scale(.96); } }
.fx-glow { width: 116px; height: 116px; border-radius: 58px; display: flex; align-items: center; justify-content: center;
  background: radial-gradient(circle at 50% 45%, rgba(124,196,255,.36), rgba(124,196,255,0) 68%); animation: fx-breathe 1.4s ease-in-out infinite alternate; }
@keyframes fx-breathe { to { transform: scale(1.08); } }
.fx-glyph { position: relative; width: 44px; height: 74px; border-radius: 12px; border: 2.5px solid #E6EEF8; overflow: hidden; }
.fx-glyph::after { content: ""; position: absolute; left: 16px; top: 5px; width: 8px; height: 2px; border-radius: 1px; background: #E6EEF8; }
.fx-send .fx-glyph i { position: absolute; inset: 3px; border-radius: 7px; opacity: 0; animation: fx-glyph-photo 6s ease-out infinite; }
@keyframes fx-glyph-photo { 0%, 41% { opacity: 0; } 48%, 90% { opacity: 1; } 96%, 100% { opacity: 0; } }
.fx-send .fx-status { position: relative; width: 200px; height: 46px; text-align: center; }
.fx-send .fx-status b { display: block; font-size: 18px; font-weight: 600; }
.fx-send .fx-status span { position: absolute; left: 0; right: 0; top: 26px; font-size: 14.5px; opacity: 0; }
.fx-send .fx-status .s1 { animation: fx-s1 6s infinite; } .fx-send .fx-status .s2 { animation: fx-s2 6s infinite; } .fx-send .fx-status .s3 { animation: fx-s3 6s infinite; }
@keyframes fx-s1 { 0%, 39% { opacity: 1; } 41%, 100% { opacity: 0; } }
@keyframes fx-s2 { 0%, 40% { opacity: 0; } 42%, 65% { opacity: 1; } 67%, 100% { opacity: 0; } }
@keyframes fx-s3 { 0%, 66% { opacity: 0; } 68%, 100% { opacity: 1; } }
.fx-send .fx-bar { position: absolute; left: 30px; right: 30px; bottom: 26px; height: 5px; border-radius: 3px; background: #262A33; overflow: hidden; opacity: 0; animation: fx-barshow 6s infinite; }
.fx-send .fx-bar i { position: absolute; inset: 0; transform-origin: 0 50%; background: linear-gradient(90deg, #3DC7FF, #9B7BFF, #FF6BD6); animation: fx-barfill 6s linear infinite; }
@keyframes fx-barshow { 0%, 40% { opacity: 0; } 42%, 70% { opacity: 1; } 74%, 100% { opacity: 0; } }
@keyframes fx-barfill { 0%, 41% { transform: scaleX(0); } 66%, 100% { transform: scaleX(1); } }
.fx-send .fx-line { position: absolute; right: 0; top: 90px; width: 4px; height: 320px; border-radius: 2px; opacity: 0;
  background: linear-gradient(180deg, rgba(124,196,255,0), #7CC4FF, rgba(124,196,255,0)); animation: fx-halo 6s infinite; }
.fx-send .fx-pglow { position: absolute; inset: 0; opacity: 0; background: radial-gradient(120% 70% at 0% 45%, rgba(124,196,255,.5), rgba(155,123,255,.15) 40%, rgba(14,16,22,0) 75%);
  animation: fx-pglow2 6s ease-out infinite; }
@keyframes fx-pglow2 { 0%, 56% { opacity: 0; } 64% { opacity: 1; } 90%, 100% { opacity: 0; } }
.fx-send .fx-arrive { position: absolute; left: 20px; right: 20px; bottom: 40px; padding: 12px; border-radius: 20px; background: #1A1D25; border: 1px solid #2E3340;
  opacity: 0; animation: fx-arrive 6s cubic-bezier(.2,.8,.2,1) infinite; }
.fx-send .fx-arrive i { display: block; height: 120px; border-radius: 12px; }
@keyframes fx-arrive { 0%, 62% { opacity: 0; transform: translateY(50px); } 70% { opacity: 1; transform: none; } 92% { opacity: 1; transform: none; } 98%, 100% { opacity: 0; } }

/* ---------------- 03 Pairing ---------------- */
.fx-pair .fx-pcwin { position: absolute; left: 70px; top: 150px; width: 640px; height: 420px; border-radius: 18px; background: #141720; border: 1px solid #2A2E37;
  box-shadow: 0 30px 80px rgba(0,0,0,.55); padding: 40px; display: flex; gap: 38px; }
.fx-pair .fx-qrbox { padding: 14px; background: #fff; border-radius: 16px; height: 248px; }
.fx-pair .fx-num { font-size: 48px; font-weight: 600; letter-spacing: .08em; opacity: 0; animation: fx-num 5s infinite; }
@keyframes fx-num { 0%, 38% { opacity: 0; transform: translateY(8px); } 44%, 92% { opacity: 1; transform: none; } 98%, 100% { opacity: 0; } }
.fx-pair .fx-paired { opacity: 0; animation: fx-paired 5s infinite; }
@keyframes fx-paired { 0%, 60% { opacity: 0; } 66%, 92% { opacity: 1; } 98%, 100% { opacity: 0; } }
.fx-pair .fx-view { position: absolute; left: 40px; top: 70px; width: 212px; height: 212px; border-radius: 26px; border: 4px solid #7CC4FF; overflow: hidden;
  background: #151925; display: flex; align-items: center; justify-content: center; animation: fx-view 5s infinite; }
@keyframes fx-view { 0%, 34% { border-color: #7CC4FF; } 38%, 92% { border-color: #4CD98A; } 100% { border-color: #7CC4FF; } }
.fx-pair .fx-scan { position: absolute; left: 0; right: 0; top: 0; height: 3px; background: #7CC4FF; box-shadow: 0 0 18px 4px rgba(124,196,255,.6); animation: fx-scan 5s ease-in-out infinite; }
@keyframes fx-scan { 0% { transform: translateY(10px); opacity: 1; } 32% { transform: translateY(196px); opacity: 1; } 36%, 100% { opacity: 0; transform: translateY(196px); } }
.fx-pair .fx-yes { margin-top: 18px; width: 100%; padding: 15px; border-radius: 16px; background: #7CC4FF; color: #0E1520; text-align: center; font-weight: 600; font-size: 18px;
  animation: fx-press 5s infinite; }
@keyframes fx-press { 0%, 52% { transform: none; filter: none; } 55% { transform: scale(.95); filter: brightness(1.2); } 60%, 100% { transform: none; filter: none; } }

/* ---------------- 04 Big files resume ---------------- */
.fx-resume .fx-card { position: absolute; left: 215px; top: 210px; width: 820px; height: 330px; border-radius: 26px; background: rgba(24,27,35,.94);
  border: 1px solid rgba(255,255,255,.1); box-shadow: 0 30px 80px rgba(0,0,0,.55); padding: 42px 46px; }
.fx-resume h4 { margin: 0; font-size: 30px; font-weight: 600; }
.fx-resume .fx-lines { position: relative; height: 34px; margin-top: 12px; font-size: 21px; }
.fx-resume .fx-lines span { position: absolute; left: 0; top: 0; opacity: 0; white-space: nowrap; }
.fx-resume .l1 { animation: fx-r1 6s infinite; } .fx-resume .l2 { animation: fx-r2 6s infinite; } .fx-resume .l3 { animation: fx-r3 6s infinite; } .fx-resume .l4 { animation: fx-r4 6s infinite; }
@keyframes fx-r1 { 0%, 34% { opacity: 1; } 36%, 100% { opacity: 0; } }
@keyframes fx-r2 { 0%, 35% { opacity: 0; } 37%, 53% { opacity: 1; } 55%, 100% { opacity: 0; } }
@keyframes fx-r3 { 0%, 54% { opacity: 0; } 56%, 87% { opacity: 1; } 89%, 100% { opacity: 0; } }
@keyframes fx-r4 { 0%, 88% { opacity: 0; } 90%, 100% { opacity: 1; } }
.fx-resume .fx-track { position: relative; margin-top: 40px; height: 14px; border-radius: 7px; background: #262A33; overflow: hidden; }
.fx-resume .fx-track i { position: absolute; inset: 0; transform-origin: 0 50%; }
.fx-resume .fx-track .f { background: linear-gradient(90deg, #3DC7FF, #9B7BFF, #FF6BD6); animation: fx-rfill 6s linear infinite; }
.fx-resume .fx-track .a { background: #FFB38A; opacity: 0; animation: fx-rfill 6s linear infinite, fx-amber 6s infinite; }
@keyframes fx-rfill { 0% { transform: scaleX(0); } 34% { transform: scaleX(.58); } 54% { transform: scaleX(.58); } 88%, 100% { transform: scaleX(1); } }
@keyframes fx-amber { 0%, 35% { opacity: 0; } 37%, 53% { opacity: .9; } 55%, 100% { opacity: 0; } }
.fx-resume .fx-wifi { position: absolute; right: 46px; top: 40px; width: 44px; height: 44px; }
.fx-resume .fx-wifi .on { animation: fx-r-on 6s infinite; } .fx-resume .fx-wifi .off { opacity: 0; animation: fx-r2 6s infinite; }
@keyframes fx-r-on { 0%, 35% { opacity: 1; } 37%, 53% { opacity: 0; } 55%, 100% { opacity: 1; } }
.fx-resume .fx-devs { position: absolute; left: 0; right: 0; top: 600px; display: flex; justify-content: center; align-items: center; gap: 26px; font-size: 18px; color: #A3A9B6; }
.fx-resume .fx-dash { width: 340px; height: 4px; border-radius: 2px; background: repeating-linear-gradient(90deg, #7CC4FF 0 14px, transparent 14px 26px); background-size: 52px 4px;
  animation: fx-dash 1s linear infinite, fx-dashdim 6s infinite; }
@keyframes fx-dash { to { background-position: 52px 0; } }
@keyframes fx-dashdim { 0%, 35% { opacity: 1; } 37%, 53% { opacity: .15; } 55%, 100% { opacity: 1; } }

/* ---------------- 05 Folders ---------------- */
.fx-folders .fx-src { position: absolute; left: 110px; top: 250px; width: 260px; text-align: center; animation: fx-fade 5s infinite; }
.fx-folders .fx-file { position: absolute; left: 70px; width: 120px; height: 90px; border-radius: 10px; box-shadow: 0 14px 30px rgba(0,0,0,.45); }
.fx-folders .fx-file.a { top: -130px; animation: fx-filein 5s ease-in infinite; } .fx-folders .fx-file.b { top: -150px; left: 40px; animation: fx-filein 5s ease-in infinite .12s; }
.fx-folders .fx-file.c { top: -140px; left: 100px; animation: fx-filein 5s ease-in infinite .24s; }
@keyframes fx-filein { 0% { transform: translateY(-30px) rotate(-8deg); opacity: 0; } 6% { opacity: 1; } 22% { transform: translateY(110px) scale(.5); opacity: 0; } 100% { opacity: 0; transform: translateY(110px) scale(.5); } }
.fx-folders .fx-bundle { position: absolute; left: 150px; top: 440px; padding: 12px 18px; border-radius: 999px; background: #1C2029; border: 1px solid #3A5FA8;
  font-size: 18px; white-space: nowrap; box-shadow: 0 0 30px -6px rgba(92,134,214,.8); animation: fx-bundle 5s cubic-bezier(.5,.1,.3,1) infinite; }
@keyframes fx-bundle { 0%, 24% { transform: none; opacity: 0; } 28% { opacity: 1; transform: none; } 40% { transform: translate(330px, -150px); }
  52% { transform: translate(690px, -60px) scale(.6); opacity: 1; } 56%, 100% { transform: translate(690px, -60px) scale(.4); opacity: 0; } }
.fx-folders .fx-row { display: flex; align-items: center; gap: 14px; padding: 12px 6px; border-bottom: 1px solid #22252D; font-size: 17px; opacity: 0; animation: fx-rowin 5s infinite; }
.fx-folders .fx-row i { width: 34px; height: 34px; border-radius: 7px; flex-shrink: 0; }
@keyframes fx-rowin { 0%, 54% { opacity: 0; transform: translateX(24px); } 60%, 92% { opacity: 1; transform: none; } 98%, 100% { opacity: 0; } }
@keyframes fx-fade { 0%, 88% { opacity: 1; } 96% { opacity: 0; } 100% { opacity: 1; } }

/* ---------------- 06 Clipboard ---------------- */
.fx-clip .fx-field { margin-top: 14px; padding: 14px 16px; border-radius: 14px; background: #171A21; border: 1px solid #2A2E37; font-size: 16px; color: #DCE7F5; font-family: 'Cascadia Mono', Consolas, monospace; }
.fx-clip .fx-btn { margin-top: 14px; padding: 14px 8px; border-radius: 14px; background: #7CC4FF; color: #0E1520; font-weight: 600; font-size: 15px; white-space: nowrap; text-align: center; animation: fx-cpress 4.5s infinite; }
@keyframes fx-cpress { 0%, 16% { transform: none; filter: none; } 19% { transform: scale(.95); filter: brightness(1.2); } 24%, 100% { transform: none; filter: none; } }
.fx-clip .fx-chipfly { position: absolute; left: 150px; top: 430px; padding: 10px 16px; border-radius: 12px; background: #7CC4FF; color: #0E1520; font-weight: 600; font-size: 16px;
  opacity: 0; animation: fx-chipfly 4.5s cubic-bezier(.5,.1,.3,1) infinite; }
@keyframes fx-chipfly { 0%, 20% { opacity: 0; transform: none; } 24% { opacity: 1; } 44% { opacity: 1; transform: translate(640px, 40px) scale(.9); } 48%, 100% { opacity: 0; transform: translate(660px, 40px) scale(.6); } }
.fx-clip .fx-toast { position: absolute; right: 24px; bottom: 24px; width: 360px; padding: 18px 20px; border-radius: 16px; background: rgba(26,29,37,.97); border: 1px solid #2E3340;
  box-shadow: 0 26px 60px rgba(0,0,0,.5); opacity: 0; animation: fx-toast 4.5s cubic-bezier(.2,.8,.2,1) infinite; }
@keyframes fx-toast { 0%, 44% { opacity: 0; transform: translateY(30px); } 52%, 88% { opacity: 1; transform: none; } 96%, 100% { opacity: 0; } }
.fx-clip .fx-addr { position: absolute; left: 24px; right: 24px; top: 20px; height: 46px; border-radius: 23px; background: #12151C; border: 1px solid #2A2E37; padding: 12px 20px;
  font-size: 16px; color: #A3A9B6; overflow: hidden; white-space: nowrap; }
.fx-clip .fx-addr b { font-weight: 400; color: #DCE7F5; opacity: 0; animation: fx-paste 4.5s infinite; }
@keyframes fx-paste { 0%, 66% { opacity: 0; } 70%, 92% { opacity: 1; } 98%, 100% { opacity: 0; } }

/* ---------------- 07 Effects ---------------- */
.fx-fx .fx-scr { background: linear-gradient(160deg, #1E2740, #0F131C); }
.fx-fx .fx-surge { position: absolute; inset: 0; opacity: 0; animation: fx-show1 6s infinite; }
.fx-fx .fx-surge i { position: absolute; top: -10%; bottom: -10%; width: 520px; right: -520px; filter: blur(30px);
  background: linear-gradient(90deg, rgba(255,107,214,0), rgba(155,123,255,.55), rgba(61,199,255,.85), rgba(255,255,255,.55), rgba(61,199,255,0));
  animation: fx-surge 6s cubic-bezier(.3,.6,.3,1) infinite; }
@keyframes fx-surge { 0% { transform: translateX(0); } 30% { transform: translateX(-1450px); } 100% { transform: translateX(-1450px); } }
.fx-fx .fx-bezel { position: absolute; inset: 0; border-radius: 12px; opacity: 0; box-shadow: inset 0 0 0 4px rgba(124,196,255,.8), inset 0 0 60px rgba(155,123,255,.45); animation: fx-bezel 6s infinite; }
@keyframes fx-bezel { 0% { opacity: 0; } 6% { opacity: 1; } 26% { opacity: 0; } 100% { opacity: 0; } }
.fx-fx .fx-ripple { position: absolute; inset: 0; opacity: 0; animation: fx-show2 6s infinite; }
.fx-fx .fx-ripple .fx-ring { right: -230px; top: 90px; width: 460px; height: 400px; animation: fx-ring2 2s ease-out infinite; }
.fx-fx .fx-ripple .fx-ring.r2 { animation-delay: .25s; } .fx-fx .fx-ripple .fx-ring.r3 { animation-delay: .5s; }
@keyframes fx-ring2 { 0% { transform: scale(.25); opacity: 0; } 12% { opacity: 1; } 100% { transform: scale(2.2); opacity: 0; } }
.fx-fx .fx-haloe { position: absolute; inset: 0; opacity: 0; animation: fx-show3 6s infinite; }
.fx-fx .fx-haloe i { position: absolute; inset: 0; border-radius: 12px;
  box-shadow: inset 0 0 0 5px rgba(61,199,255,.85), inset 0 0 40px 6px rgba(155,123,255,.55), inset 0 0 120px 10px rgba(255,107,214,.25);
  animation: fx-halopulse 1.5s ease-in-out infinite alternate; }
@keyframes fx-halopulse { from { opacity: .55; } to { opacity: 1; } }
@keyframes fx-show1 { 0%, 31% { opacity: 1; } 34%, 100% { opacity: 0; } }
@keyframes fx-show2 { 0%, 32% { opacity: 0; } 35%, 64% { opacity: 1; } 67%, 100% { opacity: 0; } }
@keyframes fx-show3 { 0%, 65% { opacity: 0; } 68%, 97% { opacity: 1; } 100% { opacity: 0; } }
.fx-fx .fx-label { position: absolute; left: 50%; bottom: 40px; width: 300px; height: 52px; margin-left: -150px; border-radius: 26px; background: rgba(14,16,22,.85);
  border: 1px solid #2E3340; font-size: 19px; font-weight: 600; }
.fx-fx .fx-label span { position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; opacity: 0; }
.fx-fx .fx-label .e1 { animation: fx-show1 6s infinite; } .fx-fx .fx-label .e2 { animation: fx-show2 6s infinite; } .fx-fx .fx-label .e3 { animation: fx-show3 6s infinite; }
"""

# ---------------------------------------------------------------- the scenes

def _phone_home(extra: str = "", hole: bool = False) -> str:
    tiles = ["fx-p2", "fx-p3", "fx-p4", "fx-p5", "" if hole else "fx-p1", "fx-p6", "fx-p7", "fx-p8", "fx-p9", "fx-p3", "fx-p4", "fx-p2"]
    grid = "".join(f'<i class="{t}"></i>' if t else '<i style="background:#1C2029"></i>' for t in tiles)
    return (f'<div style="position:absolute;left:22px;top:46px;font-size:30px;font-weight:600">Flick</div>'
            f'<div style="position:absolute;left:22px;top:92px"><span class="fx-chip"><span class="fx-dot"></span>&larr; DESKTOP-PC</span></div>'
            f'<div class="fx-grid" style="left:22px;top:152px">{grid}</div>{extra}')


SCENES = {
    "flick": f"""
<div class="fx fx-flick">
  <div class="fx-mon" style="left:50px;top:110px;width:780px;height:520px"><div class="fx-scr">
    <div class="fx-win" style="left:36px;top:36px;width:360px;height:240px;opacity:.75"></div>
    <div class="fx-win" style="left:250px;top:230px;width:320px;height:200px;opacity:.55"></div>
    <div class="fx-edge"></div>
    <div class="fx-ring r1"></div><div class="fx-ring r2"></div><div class="fx-ring r3"></div>
    <div class="fx-saved"><i class="fx-p1"></i><div style="font-size:15px;font-weight:600;margin-top:10px">IMG_2041.jpg</div><div class="fx-faint" style="font-size:13px;margin-top:2px">Saved to Desktop</div></div>
  </div></div>
  <div style="position:absolute;left:390px;top:630px;width:80px;height:46px;background:linear-gradient(#2E3340,#1A1D25);clip-path:polygon(20% 0,80% 0,100% 100%,0 100%)"></div>
  <div class="fx-phone" style="left:900px;top:130px;width:296px;height:600px"><div class="fx-pscr">{_phone_home(hole=True)}<div class="fx-pglow"></div></div></div>
  <div class="fx-fly fx-p1"></div>
</div>""",

    "send": f"""
<div class="fx fx-send">
  <div class="fx-mon" style="left:40px;top:80px;width:860px;height:560px"><div class="fx-scr">
    <div class="fx-win" style="left:36px;top:36px;width:380px;height:250px;opacity:.75"></div>
    <div class="fx-win" style="left:110px;top:310px;width:300px;height:190px;opacity:.55"></div>
    <div class="fx-line"></div>
    <div class="fx-halo">
      <div class="fx-glow"><div class="fx-glyph"><i class="fx-p1"></i></div></div>
      <div class="fx-status"><b>Galaxy S23 Ultra</b>
        <span class="s1 fx-muted">Release to send 1 photo</span><span class="s2 fx-muted">Sending&hellip;</span><span class="s3 fx-good">Sent &#10003;</span></div>
      <div class="fx-bar"><i></i></div>
    </div>
  </div></div>
  <div class="fx-drag"><i class="fx-p1"></i>{CURSOR}</div>
  <div class="fx-phone" style="left:960px;top:140px;width:260px;height:540px;border-radius:42px"><div class="fx-pscr" style="border-radius:32px">
    <div class="fx-pglow"></div>
    <div class="fx-arrive"><i class="fx-p1"></i><div style="font-size:15px;font-weight:600;margin-top:10px">IMG_2041.jpg</div><div class="fx-muted" style="font-size:12.5px;margin-top:2px">From DESKTOP-PC &middot; 2.4 MB</div></div>
  </div></div>
</div>""",

    "pair": f"""
<div class="fx fx-pair">
  <div class="fx-pcwin">
    <div class="fx-qrbox">{_qr(220)}</div>
    <div style="display:flex;flex-direction:column;gap:14px;padding-top:4px">
      <div style="font-size:28px;font-weight:600">Pair a phone</div>
      <div class="fx-muted" style="font-size:17px;line-height:1.6">1. Open Flick on your phone<br>2. Tap + and scan this code<br>3. Check the number matches</div>
      <div class="fx-faint" style="font-size:14px;margin-top:14px;letter-spacing:.06em">CHECK NUMBER</div>
      <div class="fx-num">482 913</div>
      <div class="fx-paired fx-good" style="font-size:17px;display:flex;align-items:center;gap:10px"><span class="fx-dot"></span>Galaxy S23 Ultra paired</div>
    </div>
  </div>
  <div class="fx-phone" style="left:850px;top:80px;width:310px;height:670px"><div class="fx-pscr">
    <div class="fx-view">{_qr(170)}<div class="fx-scan"></div></div>
    <div style="position:absolute;left:24px;right:24px;top:310px;text-align:center">
      <div style="font-size:19px;font-weight:600;white-space:nowrap">Pair with DESKTOP-PC?</div>
      <div class="fx-muted" style="font-size:14px;margin-top:8px">Check this matches your PC</div>
      <div class="fx-num" style="font-size:40px;margin-top:10px">482 913</div>
      <div class="fx-yes">Yes, pair</div>
    </div>
  </div></div>
</div>""",

    "resume": f"""
<div class="fx fx-resume">
  <div class="fx-card">
    <h4>Receiving Holiday-4K.mp4</h4>
    <div class="fx-lines">
      <span class="l1 fx-muted">From Galaxy S23 &middot; 1.9 GB of 3.4 GB &middot; 28 MB/s &middot; 1 min left</span>
      <span class="l2 fx-warn">Connection dropped &ndash; resuming (1/4)&hellip;</span>
      <span class="l3 fx-muted">Resumed from 1.9 GB &middot; 31 MB/s &middot; 48 s left</span>
      <span class="l4 fx-good">Saved to Desktop &#10003;</span>
    </div>
    <div class="fx-track"><i class="f"></i><i class="a"></i></div>
    <div class="fx-wifi"><div class="fx-stack"><div class="on">{WIFI_ON}</div><div class="off">{WIFI_OFF}</div></div></div>
  </div>
  <div class="fx-devs"><span>Galaxy S23</span><div class="fx-dash"></div><span>DESKTOP-PC</span></div>
</div>""",

    "folders": f"""
<div class="fx fx-folders">
  <div class="fx-src">
    <div class="fx-file a fx-p2"></div><div class="fx-file b fx-p5"></div><div class="fx-file c fx-p6"></div>
    <div style="position:relative">{FOLDER}</div>
    <div style="font-size:22px;font-weight:600;margin-top:8px">Holiday</div>
    <div class="fx-muted" style="font-size:16px;margin-top:4px">214 files &middot; 6 folders</div>
  </div>
  <div class="fx-phone" style="left:860px;top:90px;width:300px;height:650px"><div class="fx-pscr" style="padding:56px 22px">
    <div class="fx-faint" style="font-size:14px">Download &rsaquo; Flick</div>
    <div style="font-size:26px;font-weight:600;margin:6px 0 14px">Holiday</div>
    <div class="fx-row"><i style="background:#3A5FA8"></i>Day 1 &ndash; Lisbon</div>
    <div class="fx-row" style="animation-delay:.1s"><i style="background:#3A5FA8"></i>Day 2 &ndash; Sintra</div>
    <div class="fx-row" style="animation-delay:.2s"><i class="fx-p1"></i>IMG_3001.jpg</div>
    <div class="fx-row" style="animation-delay:.3s"><i class="fx-p7"></i>IMG_3002.jpg</div>
    <div class="fx-row" style="animation-delay:.4s"><i class="fx-p4"></i>beach.mp4</div>
    <div class="fx-row" style="animation-delay:.5s"><i style="background:#2A2E37"></i>itinerary.pdf</div>
  </div></div>
  <div class="fx-bundle"><span style="display:inline-block;width:16px;height:13px;border-radius:3px;background:#5C86D6;margin-right:10px;vertical-align:-1px"></span>Holiday &middot; 1.2 GB</div>
</div>""",

    "clip": f"""
<div class="fx fx-clip">
  <div class="fx-phone" style="left:80px;top:110px;width:300px;height:620px"><div class="fx-pscr" style="padding:56px 22px">
    <div style="font-size:28px;font-weight:600">Flick</div>
    <div class="fx-faint" style="font-size:13px;letter-spacing:.06em;margin-top:28px">SEND TEXT TO YOUR PC</div>
    <div class="fx-field">maps.app.goo.gl/x7Qe9&hellip;</div>
    <div class="fx-btn">Send to DESKTOP-PC &rarr;</div>
  </div></div>
  <div class="fx-chipfly">maps.app.goo.gl/x7Qe9&hellip;</div>
  <div class="fx-mon" style="left:480px;top:100px;width:720px;height:480px"><div class="fx-scr">
    <div class="fx-win" style="left:24px;top:84px;width:640px;height:330px;opacity:.7"></div>
    <div class="fx-addr">Search or type a URL &nbsp;<b>maps.app.goo.gl/x7Qe9&hellip;</b></div>
    <div class="fx-toast">
      <div class="fx-faint" style="font-size:13px;letter-spacing:.06em">COPIED FROM GALAXY S23</div>
      <div style="font-size:16px;margin-top:8px;font-family:'Cascadia Mono',Consolas,monospace;color:#DCE7F5">maps.app.goo.gl/x7Qe9&hellip;</div>
      <div class="fx-good" style="font-size:14px;margin-top:8px">On this PC&rsquo;s clipboard &mdash; just paste</div>
    </div>
  </div></div>
</div>""",

    "effects": """
<div class="fx fx-fx">
  <div class="fx-mon" style="left:175px;top:70px;width:900px;height:600px"><div class="fx-scr">
    <div class="fx-win" style="left:40px;top:40px;width:420px;height:280px;opacity:.6"></div>
    <div class="fx-win" style="left:330px;top:250px;width:380px;height:240px;opacity:.45"></div>
    <div class="fx-surge"><i></i><div class="fx-bezel"></div></div>
    <div class="fx-ripple"><div class="fx-ring r1"></div><div class="fx-ring r2"></div><div class="fx-ring r3"></div></div>
    <div class="fx-haloe"><i></i></div>
    <div class="fx-label"><span class="e1">Spectral Surge</span><span class="e2">Ripple</span><span class="e3">Halo</span></div>
  </div></div>
  <div style="position:absolute;left:585px;top:670px;width:80px;height:46px;background:linear-gradient(#2E3340,#1A1D25);clip-path:polygon(20% 0,80% 0,100% 100%,0 100%)"></div>
</div>""",
}

# Seconds per loop, and the moment that makes the best still picture.
LOOP = {"flick": 5, "send": 6, "pair": 5, "resume": 6, "folders": 5, "clip": 4.5, "effects": 6}
STILL = {"flick": 2.3, "send": 1.9, "pair": 3.4, "resume": 2.6, "folders": 3.2, "clip": 3.0, "effects": 0.7}

# Captions (shared by the website gallery and the GitHub page).
CAPTIONS = [
    ("flick", "Flick it", "Throw it at the screen",
     "Long-press a photo and flick it towards the PC. Light washes in from that edge and it lands on the desktop."),
    ("send", "Drop zone", "Drag it to the edge",
     "Drag files to the side your phone is on. A card slides in; let go and they arrive on the phone."),
    ("pair", "Pairing", "Paired with a QR code",
     "Scan once and check the six digits match. Everything after that is encrypted end to end."),
    ("resume", "Big files", "Picks up where it stopped",
     "Progress with speed and time left, and if the Wi-Fi drops it carries on from the same byte."),
    ("folders", "Folders", "Whole folders, both ways",
     "Packed into one bundle on the way and unpacked on arrival, subfolders and all."),
    ("clip", "Clipboard", "Copy here, paste there",
     "Send a link or some text from the phone and it is already on the PC's clipboard."),
    ("effects", "Effects", "Pick your light",
     "Surge, Ripple, Halo, Glass or Quiet, with palettes, speed and an optional 8-bit chime."),
]
