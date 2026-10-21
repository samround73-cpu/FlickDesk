"""
The Flick scenes as drawn for the Projects gallery on landing.great919.win, in the same style as
the Dynamic Island cards next to them: each card is the PC screen itself, with a warm wallpaper
from the island palette, close-up black panels at the top, and soft placeholder windows lower
down where the caption sits. Same 1250 x 833 stage and plain CSS loops as scenes.py.
"""
from scenes import CURSOR, FOLDER, H, W, WIFI_OFF, WIFI_ON, _qr

# Wallpapers borrowed from the Dynamic Island gallery so the two tabs feel like one set.
WALL = {
    "purple": "radial-gradient(110% 90% at 85% 105%, #7A4A6B 0%, transparent 60%), radial-gradient(90% 80% at 0% 0%, #2F2346 0%, transparent 60%), linear-gradient(170deg, #18131F, #211829 60%, #120E17)",
    "amber": "radial-gradient(110% 90% at 90% 100%, #A8642C 0%, transparent 60%), radial-gradient(90% 80% at 0% 0%, #3B2A1C 0%, transparent 60%), linear-gradient(170deg, #1B140F, #241A12 60%, #120D09)",
    "teal": "radial-gradient(110% 90% at 10% 100%, #2C6B6B 0%, transparent 60%), radial-gradient(90% 80% at 100% 0%, #1D3346 0%, transparent 60%), linear-gradient(170deg, #0F1A1C, #132326 60%, #0B1213)",
    "blue": "radial-gradient(110% 90% at 0% 100%, #3E5C76 0%, transparent 60%), radial-gradient(90% 80% at 100% 10%, #1F3B52 0%, transparent 60%), linear-gradient(170deg, #121A24, #17222F 60%, #0E141B)",
    "green": "radial-gradient(110% 90% at 100% 100%, #66754F 0%, transparent 60%), radial-gradient(90% 80% at 0% 0%, #2E3D2C 0%, transparent 60%), linear-gradient(170deg, #141A13, #1C2419 60%, #10140F)",
    "rose": "radial-gradient(120% 90% at 15% 110%, #C2643F 0%, transparent 55%), radial-gradient(90% 80% at 100% 0%, #5B2F4A 0%, transparent 60%), linear-gradient(160deg, #2B1D24, #3A2228 55%, #1E1418)",
}
BG = {"flick": WALL["purple"], "send": WALL["amber"], "pair": WALL["teal"], "resume": WALL["blue"],
      "folders": WALL["green"], "clip": WALL["rose"], "effects": WALL["purple"]}


def ghosts(a=(90, 560, 560, 300), b=(700, 600, 470, 260)) -> str:
    """Two soft placeholder windows low on the screen, like the island cards."""
    out = []
    for x, y, w, h in (a, b):
        out.append(
            f'<div class="fs-ghost" style="left:{x}px;top:{y}px;width:{w}px;height:{h}px">'
            f'<i style="width:{int(w * .4)}px;top:40px"></i><i style="width:{int(w * .66)}px;top:74px"></i><i style="width:{int(w * .52)}px;top:108px"></i></div>')
    return "".join(out)


CSS = r"""
.fs { position: absolute; inset: 0; overflow: hidden; font-family: Inter, 'Segoe UI Variable Text', 'Segoe UI', system-ui, sans-serif; color: #F5F5F7; }
.fs * { box-sizing: border-box; }
.fs-ghost { position: absolute; border-radius: 22px; background: rgba(255,255,255,.055); border: 1.5px solid rgba(255,255,255,.08); }
.fs-ghost i { position: absolute; left: 34px; height: 14px; border-radius: 7px; background: rgba(255,255,255,.1); }
.fs-panel { position: absolute; background: #000; border-radius: 44px; border: 1.5px solid rgba(255,255,255,.07); box-shadow: 0 30px 80px rgba(0,0,0,.45); }
.fs-grey { color: #9A9A9A; } .fs-dim { color: #6E6E73; } .fs-good { color: #4CD98A; } .fs-warn { color: #FFB38A; } .fs-gold { color: #E8D18A; }
.fs-dot { display: inline-block; width: 12px; height: 12px; border-radius: 6px; background: #4CD98A; }
.fs-p1 { background: linear-gradient(135deg, #F4A261, #E76F51); } .fs-p2 { background: linear-gradient(160deg, #5EC2E8, #2D6FA3); }
.fs-p3 { background: linear-gradient(140deg, #9BD18A, #3E8E6A); } .fs-p4 { background: linear-gradient(135deg, #C9A7FF, #6A4FD1); }
.fs-p5 { background: linear-gradient(150deg, #FFD58A, #E79E3B); } .fs-p6 { background: linear-gradient(135deg, #FF9AB8, #C2457A); }
.fs-p7 { background: linear-gradient(135deg, #7FE3C4, #2E8C8C); } .fs-p8 { background: linear-gradient(135deg, #A3B7FF, #3B4FB0); }
.fs-p9 { background: linear-gradient(135deg, #F7B2A0, #B5564C); }
.fs-cursor { position: absolute; right: -16px; bottom: -26px; width: 36px; height: 46px; filter: drop-shadow(0 3px 6px rgba(0,0,0,.6)); }
.fs-ring { position: absolute; border-radius: 50%; border: 6px solid; opacity: 0; }
.fs-ring.r1 { border-color: rgba(61,199,255,.75); } .fs-ring.r2 { border-color: rgba(155,123,255,.6); } .fs-ring.r3 { border-color: rgba(255,107,214,.45); }

/* 01 Flick it ---------------------------------------------------------------- */
.fs-flick .fs-phone { position: absolute; left: 905px; top: 110px; width: 340px; height: 720px; border-radius: 58px; background: #000;
  border: 3px solid rgba(255,255,255,.16); transform: rotate(-7deg); box-shadow: 0 40px 90px rgba(0,0,0,.5); padding: 54px 26px; }
.fs-flick .fs-grid { display: grid; grid-template-columns: repeat(3, 88px); gap: 7px; margin-top: 22px; }
.fs-flick .fs-grid i { display: block; width: 88px; height: 88px; border-radius: 12px; }
.fs-flick .fs-chip { display: inline-flex; align-items: center; gap: 10px; padding: 9px 16px; border-radius: 999px; background: #161618; font-size: 18px; margin-top: 12px; }
.fs-flick .fs-wash { position: absolute; right: 0; top: 0; bottom: 0; width: 760px; opacity: 0;
  background: radial-gradient(60% 55% at 100% 42%, rgba(124,196,255,.55), rgba(155,123,255,.22) 50%, rgba(255,107,214,0) 80%);
  animation: fs-wash 5s ease-out infinite; }
@keyframes fs-wash { 0%, 30% { opacity: 0; } 40% { opacity: 1; } 70% { opacity: .5; } 88%, 100% { opacity: 0; } }
.fs-flick .fs-ring { left: 640px; top: 120px; width: 560px; height: 440px; animation: fs-ring 5s ease-out infinite; }
.fs-flick .fs-ring.r2 { animation-delay: .18s; } .fs-flick .fs-ring.r3 { animation-delay: .36s; }
@keyframes fs-ring { 0%, 30% { transform: scale(.3); opacity: 0; } 36% { transform: scale(.5); opacity: 1; } 76%, 100% { transform: scale(1.9); opacity: 0; } }
.fs-flick .fs-fly { position: absolute; left: 1004px; top: 370px; width: 88px; height: 88px; border-radius: 12px; transform: rotate(-7deg);
  animation: fs-flick-fly 5s cubic-bezier(.3,.7,.3,1) infinite; }
@keyframes fs-flick-fly {
  0% { transform: rotate(-7deg); opacity: 1; box-shadow: none; }
  12%, 18% { transform: translateY(-10px) scale(1.2) rotate(-11deg); opacity: 1; box-shadow: 0 0 0 4px rgba(255,255,255,.9), 0 26px 44px rgba(0,0,0,.6); }
  36% { transform: translate(-830px, -264px) scale(1.18) rotate(0deg); opacity: 1; box-shadow: 0 0 0 0 rgba(255,255,255,0); }
  40% { transform: translate(-830px, -264px) scale(1.18); opacity: 0; }
  92% { transform: rotate(-7deg); opacity: 0; } 100% { transform: rotate(-7deg); opacity: 1; } }
.fs-flick .fs-saved { left: 120px; top: 70px; width: 600px; height: 150px; display: flex; align-items: center; gap: 26px; padding: 0 26px; border-radius: 50px;
  opacity: 0; transform-origin: 20% 50%; animation: fs-pop 5s cubic-bezier(.2,.8,.2,1) infinite; }
.fs-flick .fs-saved i { width: 104px; height: 104px; border-radius: 22px; flex-shrink: 0; }
@keyframes fs-pop { 0%, 35% { opacity: 0; transform: scale(.72); } 42% { opacity: 1; transform: scale(1.03); } 46%, 88% { opacity: 1; transform: none; } 95%, 100% { opacity: 0; transform: scale(.96); } }

/* 02 Drag to the edge -------------------------------------------------------- */
.fs-send .fs-card { right: 70px; top: 50px; width: 340px; height: 420px; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 22px;
  border-color: rgba(124,196,255,.8); box-shadow: 0 0 60px -8px rgba(124,196,255,.65), 0 30px 80px rgba(0,0,0,.45); opacity: 0;
  animation: fs-card 6s cubic-bezier(.2,.8,.2,1) infinite; }
@keyframes fs-card { 0%, 14% { opacity: 0; transform: translateX(90px) scale(.94); } 24% { opacity: 1; transform: none; } 86% { opacity: 1; transform: none; } 94%, 100% { opacity: 0; transform: translateX(90px) scale(.96); } }
.fs-send .fs-glow { width: 160px; height: 160px; border-radius: 80px; display: flex; align-items: center; justify-content: center;
  background: radial-gradient(circle at 50% 45%, rgba(124,196,255,.38), rgba(124,196,255,0) 68%); animation: fs-breathe 1.4s ease-in-out infinite alternate; }
@keyframes fs-breathe { to { transform: scale(1.08); } }
.fs-send .fs-glyph { position: relative; width: 60px; height: 100px; border-radius: 16px; border: 3px solid #E6EEF8; overflow: hidden; }
.fs-send .fs-glyph::after { content: ""; position: absolute; left: 22px; top: 7px; width: 10px; height: 3px; border-radius: 2px; background: #E6EEF8; }
.fs-send .fs-glyph i { position: absolute; inset: 4px; border-radius: 10px; opacity: 0; animation: fs-in 6s ease-out infinite; }
@keyframes fs-in { 0%, 41% { opacity: 0; } 48%, 90% { opacity: 1; } 96%, 100% { opacity: 0; } }
.fs-send .fs-status { position: relative; width: 300px; height: 64px; text-align: center; }
.fs-send .fs-status b { display: block; font-size: 28px; font-weight: 600; }
.fs-send .fs-status span { position: absolute; left: 0; right: 0; top: 38px; font-size: 21px; opacity: 0; }
.fs-send .s1 { animation: fs-s1 6s infinite; } .fs-send .s2 { animation: fs-s2 6s infinite; } .fs-send .s3 { animation: fs-s3 6s infinite; }
@keyframes fs-s1 { 0%, 39% { opacity: 1; } 41%, 100% { opacity: 0; } }
@keyframes fs-s2 { 0%, 40% { opacity: 0; } 42%, 65% { opacity: 1; } 67%, 100% { opacity: 0; } }
@keyframes fs-s3 { 0%, 66% { opacity: 0; } 68%, 100% { opacity: 1; } }
.fs-send .fs-bar { position: absolute; left: 44px; right: 44px; bottom: 38px; height: 7px; border-radius: 4px; background: #232326; overflow: hidden; opacity: 0; animation: fs-barshow 6s infinite; }
.fs-send .fs-bar i { position: absolute; inset: 0; transform-origin: 0 50%; background: linear-gradient(90deg, #3DC7FF, #9B7BFF, #FF6BD6); animation: fs-barfill 6s linear infinite; }
@keyframes fs-barshow { 0%, 40% { opacity: 0; } 42%, 70% { opacity: 1; } 74%, 100% { opacity: 0; } }
@keyframes fs-barfill { 0%, 41% { transform: scaleX(0); } 66%, 100% { transform: scaleX(1); } }
.fs-send .fs-edge { position: absolute; right: 0; top: 40px; width: 6px; height: 460px; border-radius: 3px; opacity: 0;
  background: linear-gradient(180deg, rgba(124,196,255,0), #7CC4FF, rgba(124,196,255,0)); animation: fs-card 6s infinite; }
.fs-send .fs-drag { position: absolute; left: 150px; top: 300px; width: 176px; height: 132px; animation: fs-drag 6s cubic-bezier(.45,.05,.3,1) infinite; }
.fs-send .fs-drag i { position: absolute; inset: 0; border-radius: 14px; transform: rotate(-6deg); box-shadow: 0 24px 50px rgba(0,0,0,.5); }
@keyframes fs-drag { 0% { transform: translate(-50px, 50px); opacity: 0; } 6% { opacity: 1; transform: none; }
  36% { transform: translate(690px, -110px); opacity: 1; } 42% { transform: translate(720px, -120px) scale(.3); opacity: 0; } 100% { transform: translate(720px, -120px) scale(.3); opacity: 0; } }
.fs-send .fs-arrived { left: 110px; top: 70px; width: 560px; height: 120px; display: flex; align-items: center; gap: 22px; padding: 0 22px; border-radius: 40px;
  opacity: 0; transform-origin: 20% 50%; animation: fs-arrived 6s cubic-bezier(.2,.8,.2,1) infinite; }
.fs-send .fs-arrived i { width: 80px; height: 80px; border-radius: 18px; flex-shrink: 0; }
@keyframes fs-arrived { 0%, 66% { opacity: 0; transform: scale(.72); } 72% { opacity: 1; transform: scale(1.03); } 76%, 90% { opacity: 1; transform: none; } 96%, 100% { opacity: 0; } }

/* 03 Pairing ----------------------------------------------------------------- */
.fs-pair .fs-big { left: 60px; top: 40px; width: 1130px; height: 440px; }
.fs-pair .fs-qr { position: absolute; left: 44px; top: 44px; width: 352px; height: 352px; padding: 15px; border-radius: 28px; background: #fff; }
.fs-pair .fs-num { opacity: 0; animation: fs-num 5s infinite; }
@keyframes fs-num { 0%, 38% { opacity: 0; transform: translateY(10px); } 44%, 92% { opacity: 1; transform: none; } 98%, 100% { opacity: 0; } }
.fs-pair .fs-paired { opacity: 0; animation: fs-paired 5s infinite; }
@keyframes fs-paired { 0%, 60% { opacity: 0; } 66%, 92% { opacity: 1; } 98%, 100% { opacity: 0; } }
.fs-pair .fs-mini { position: absolute; left: 862px; top: 34px; width: 230px; height: 372px; border-radius: 40px; border: 3px solid rgba(255,255,255,.18); background: #0B0B0C; }
.fs-pair .fs-view { position: absolute; left: 30px; top: 34px; width: 164px; height: 164px; border-radius: 22px; border: 4px solid #7CC4FF; overflow: hidden;
  display: flex; align-items: center; justify-content: center; background: #151515; animation: fs-view 5s infinite; }
@keyframes fs-view { 0%, 34% { border-color: #7CC4FF; } 38%, 92% { border-color: #4CD98A; } 100% { border-color: #7CC4FF; } }
.fs-pair .fs-scan { position: absolute; left: 0; right: 0; top: 0; height: 3px; background: #7CC4FF; box-shadow: 0 0 18px 4px rgba(124,196,255,.6); animation: fs-scan 5s ease-in-out infinite; }
@keyframes fs-scan { 0% { transform: translateY(8px); opacity: 1; } 32% { transform: translateY(148px); opacity: 1; } 36%, 100% { opacity: 0; transform: translateY(148px); } }
.fs-pair .fs-yes { position: absolute; left: 24px; right: 24px; bottom: 26px; padding: 13px; border-radius: 16px; background: #7CC4FF; color: #0E1520; text-align: center;
  font-weight: 600; font-size: 19px; animation: fs-press 5s infinite; }
@keyframes fs-press { 0%, 52% { transform: none; filter: none; } 55% { transform: scale(.94); filter: brightness(1.2); } 60%, 100% { transform: none; filter: none; } }

/* 04 Big files --------------------------------------------------------------- */
.fs-resume .fs-big { left: 110px; top: 60px; width: 1030px; height: 380px; padding: 50px 56px; }
.fs-resume .fs-h { margin: 0; font-size: 36px; font-weight: 600; letter-spacing: -.01em; }
.fs-resume .fs-lines { position: relative; height: 36px; margin-top: 14px; font-size: 23px; }
.fs-resume .fs-lines span { position: absolute; left: 0; top: 0; white-space: nowrap; opacity: 0; }
.fs-resume .l1 { animation: fs-r1 6s infinite; } .fs-resume .l2 { animation: fs-r2 6s infinite; } .fs-resume .l3 { animation: fs-r3 6s infinite; } .fs-resume .l4 { animation: fs-r4 6s infinite; }
@keyframes fs-r1 { 0%, 34% { opacity: 1; } 36%, 100% { opacity: 0; } }
@keyframes fs-r2 { 0%, 35% { opacity: 0; } 37%, 53% { opacity: 1; } 55%, 100% { opacity: 0; } }
@keyframes fs-r3 { 0%, 54% { opacity: 0; } 56%, 87% { opacity: 1; } 89%, 100% { opacity: 0; } }
@keyframes fs-r4 { 0%, 88% { opacity: 0; } 90%, 100% { opacity: 1; } }
.fs-resume .fs-track { position: relative; margin-top: 44px; height: 16px; border-radius: 8px; background: #1C1C1E; overflow: hidden; }
.fs-resume .fs-track i { position: absolute; inset: 0; transform-origin: 0 50%; }
.fs-resume .fs-track .f { background: linear-gradient(90deg, #3DC7FF, #9B7BFF, #FF6BD6); animation: fs-rfill 6s linear infinite; }
.fs-resume .fs-track .a { background: #FFB38A; opacity: 0; animation: fs-rfill 6s linear infinite, fs-amber 6s infinite; }
@keyframes fs-rfill { 0% { transform: scaleX(0); } 34% { transform: scaleX(.58); } 54% { transform: scaleX(.58); } 88%, 100% { transform: scaleX(1); } }
@keyframes fs-amber { 0%, 35% { opacity: 0; } 37%, 53% { opacity: .9; } 55%, 100% { opacity: 0; } }
.fs-resume .fs-wifi { position: absolute; right: 56px; top: 50px; width: 56px; height: 56px; }
.fs-resume .fs-wifi > div { position: absolute; inset: 0; } .fs-resume .fs-wifi svg { width: 56px; height: 56px; }
.fs-resume .fs-wifi .on { animation: fs-ron 6s infinite; } .fs-resume .fs-wifi .off { opacity: 0; animation: fs-r2 6s infinite; }
@keyframes fs-ron { 0%, 35% { opacity: 1; } 37%, 53% { opacity: 0; } 55%, 100% { opacity: 1; } }
.fs-resume .fs-devs { display: flex; align-items: center; gap: 22px; margin-top: 30px; font-size: 19px; color: #6E6E73; }
.fs-resume .fs-dash { flex: 1; height: 4px; border-radius: 2px; background: repeating-linear-gradient(90deg, #7CC4FF 0 16px, transparent 16px 30px); background-size: 60px 4px;
  animation: fs-dash 1s linear infinite, fs-dashdim 6s infinite; }
@keyframes fs-dash { to { background-position: 60px 0; } }
@keyframes fs-dashdim { 0%, 35% { opacity: 1; } 37%, 53% { opacity: .15; } 55%, 100% { opacity: 1; } }

/* 05 Folders ----------------------------------------------------------------- */
.fs-folders .fs-big { left: 60px; top: 40px; width: 1130px; height: 440px; overflow: hidden; }
.fs-folders .fs-src { position: absolute; left: 70px; top: 120px; width: 260px; text-align: center; }
.fs-folders .fs-src svg { width: 240px; height: 190px; }
.fs-folders .fs-file { position: absolute; width: 130px; height: 98px; border-radius: 12px; box-shadow: 0 14px 30px rgba(0,0,0,.45); top: -160px; opacity: 0; }
.fs-folders .fs-file.a { left: 60px; animation: fs-filein 5s ease-in infinite; } .fs-folders .fs-file.b { left: 20px; animation: fs-filein 5s ease-in infinite .12s; }
.fs-folders .fs-file.c { left: 100px; animation: fs-filein 5s ease-in infinite .24s; }
@keyframes fs-filein { 0% { transform: translateY(-20px) rotate(-8deg); opacity: 0; } 6% { opacity: 1; } 22% { transform: translateY(170px) scale(.5); opacity: 0; } 100% { opacity: 0; transform: translateY(170px) scale(.5); } }
.fs-folders .fs-bundle { position: absolute; left: 330px; top: 190px; padding: 14px 22px; border-radius: 999px; background: #1C1C1E; border: 1.5px solid #5C86D6;
  font-size: 20px; white-space: nowrap; box-shadow: 0 0 34px -6px rgba(92,134,214,.8); opacity: 0; animation: fs-bundle 5s cubic-bezier(.5,.1,.3,1) infinite; }
@keyframes fs-bundle { 0%, 24% { transform: none; opacity: 0; } 28% { opacity: 1; transform: none; } 50% { transform: translateX(250px) scale(.9); opacity: 1; } 54%, 100% { transform: translateX(280px) scale(.6); opacity: 0; } }
.fs-folders .fs-list { position: absolute; left: 640px; top: 36px; width: 452px; height: 368px; border-radius: 30px; background: #0E0E10; padding: 28px 30px; }
.fs-folders .fs-row { display: flex; align-items: center; gap: 16px; padding: 11px 4px; border-bottom: 1px solid #1C1C1E; font-size: 19px; opacity: 0; animation: fs-rowin 5s infinite; }
.fs-folders .fs-row i { width: 30px; height: 30px; border-radius: 7px; flex-shrink: 0; }
@keyframes fs-rowin { 0%, 52% { opacity: 0; transform: translateX(24px); } 58%, 92% { opacity: 1; transform: none; } 98%, 100% { opacity: 0; } }

/* 06 Clipboard --------------------------------------------------------------- */
.fs-clip .fs-addr { left: 60px; top: 40px; width: 1130px; height: 92px; border-radius: 46px; display: flex; align-items: center; gap: 18px; padding: 0 34px; font-size: 22px; white-space: nowrap; overflow: hidden; }
.fs-clip .fs-addr b { font-weight: 500; color: #F5F5F7; opacity: 0; animation: fs-paste 4.5s infinite; }
@keyframes fs-paste { 0%, 64% { opacity: 0; } 68%, 92% { opacity: 1; } 98%, 100% { opacity: 0; } }
.fs-clip .fs-caret { display: inline-block; width: 2px; height: 26px; background: #7CC4FF; animation: fs-caret 1s steps(1) infinite; }
@keyframes fs-caret { 50% { opacity: 0; } }
.fs-clip .fs-copied { left: 200px; top: 176px; width: 850px; height: 168px; display: flex; align-items: center; gap: 28px; padding: 0 30px; border-radius: 56px;
  opacity: 0; transform-origin: 50% 0; animation: fs-copied 4.5s cubic-bezier(.2,.8,.2,1) infinite; }
@keyframes fs-copied { 0%, 26% { opacity: 0; transform: scale(.6, .5); } 34% { opacity: 1; transform: scale(1.02); } 38%, 86% { opacity: 1; transform: none; } 94%, 100% { opacity: 0; transform: scale(.9); } }
.fs-clip .fs-icon { width: 108px; height: 108px; border-radius: 26px; flex-shrink: 0; display: grid; place-items: center;
  background: linear-gradient(135deg, #3DC7FF, #9B7BFF 55%, #FF6BD6); }
.fs-clip .fs-sending { left: 445px; top: 176px; width: 360px; height: 64px; border-radius: 32px; display: flex; align-items: center; justify-content: center; gap: 12px;
  font-size: 19px; opacity: 0; animation: fs-sending 4.5s ease-out infinite; }
@keyframes fs-sending { 0%, 4% { opacity: 0; transform: scale(.8); } 10%, 22% { opacity: 1; transform: none; } 27%, 100% { opacity: 0; transform: scale(1.1); } }

/* 07 Effects ----------------------------------------------------------------- */
.fs-fx .fs-surge { position: absolute; inset: 0; opacity: 0; animation: fs-show1 6s infinite; }
.fs-fx .fs-surge i { position: absolute; top: -10%; bottom: -10%; right: -600px; width: 600px; filter: blur(34px);
  background: linear-gradient(90deg, rgba(255,107,214,0), rgba(155,123,255,.55), rgba(61,199,255,.85), rgba(255,255,255,.5), rgba(61,199,255,0));
  animation: fs-surge 6s cubic-bezier(.3,.6,.3,1) infinite; }
@keyframes fs-surge { 0% { transform: none; } 30%, 100% { transform: translateX(-1900px); } }
.fs-fx .fs-bezel { position: absolute; inset: 0; opacity: 0; box-shadow: inset 0 0 0 6px rgba(124,196,255,.8), inset 0 0 80px rgba(155,123,255,.45); animation: fs-bezel 6s infinite; }
@keyframes fs-bezel { 0% { opacity: 0; } 6% { opacity: 1; } 26%, 100% { opacity: 0; } }
.fs-fx .fs-ripple { position: absolute; inset: 0; opacity: 0; animation: fs-show2 6s infinite; }
.fs-fx .fs-ripple .fs-ring { right: -300px; top: 110px; width: 600px; height: 500px; animation: fs-ring2 2s ease-out infinite; }
.fs-fx .fs-ripple .fs-ring.r2 { animation-delay: .25s; } .fs-fx .fs-ripple .fs-ring.r3 { animation-delay: .5s; }
@keyframes fs-ring2 { 0% { transform: scale(.25); opacity: 0; } 12% { opacity: 1; } 100% { transform: scale(2.2); opacity: 0; } }
.fs-fx .fs-halo { position: absolute; inset: 0; opacity: 0; animation: fs-show3 6s infinite; }
.fs-fx .fs-halo i { position: absolute; inset: 0;
  box-shadow: inset 0 0 0 7px rgba(61,199,255,.85), inset 0 0 50px 8px rgba(155,123,255,.55), inset 0 0 160px 14px rgba(255,107,214,.25);
  animation: fs-pulse 1.5s ease-in-out infinite alternate; }
@keyframes fs-pulse { from { opacity: .55; } to { opacity: 1; } }
@keyframes fs-show1 { 0%, 31% { opacity: 1; } 34%, 100% { opacity: 0; } }
@keyframes fs-show2 { 0%, 32% { opacity: 0; } 35%, 64% { opacity: 1; } 67%, 100% { opacity: 0; } }
@keyframes fs-show3 { 0%, 65% { opacity: 0; } 68%, 97% { opacity: 1; } 100% { opacity: 0; } }
.fs-fx .fs-pill { left: 405px; top: 30px; width: 440px; height: 80px; border-radius: 0 0 40px 40px; border-top: 0; font-size: 26px; font-weight: 600; }
.fs-fx .fs-pill span { position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; gap: 14px; opacity: 0; }
.fs-fx .fs-pill .e1 { animation: fs-show1 6s infinite; } .fs-fx .fs-pill .e2 { animation: fs-show2 6s infinite; } .fs-fx .fs-pill .e3 { animation: fs-show3 6s infinite; }
"""

LINK = ('<svg width="54" height="54" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2.2" stroke-linecap="round" aria-hidden="true">'
        '<path d="M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1"/><path d="M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1"/></svg>')
SEARCH = ('<svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="#9A9A9A" stroke-width="2" stroke-linecap="round" aria-hidden="true">'
          '<circle cx="11" cy="11" r="6.5"/><path d="M16 16l4 4"/></svg>')
DOT = lambda c: f'<i style="width:12px;height:12px;border-radius:6px;background:{c};display:inline-block"></i>'

SCENES = {
    "flick": f"""
<div class="fs fs-flick">{ghosts()}
  <div class="fs-wash"></div>
  <div class="fs-ring r1"></div><div class="fs-ring r2"></div><div class="fs-ring r3"></div>
  <div class="fs-phone">
    <div style="font-size:36px;font-weight:700">Flick</div>
    <div class="fs-chip"><span class="fs-dot"></span>&larr; DESKTOP-PC</div>
    <div class="fs-grid"><i class="fs-p2"></i><i class="fs-p3"></i><i class="fs-p4"></i><i class="fs-p5"></i><i style="background:#161618"></i><i class="fs-p6"></i>
      <i class="fs-p7"></i><i class="fs-p8"></i><i class="fs-p9"></i><i class="fs-p3"></i><i class="fs-p4"></i><i class="fs-p2"></i></div>
  </div>
  <div class="fs-panel fs-saved"><i class="fs-p1"></i><div><div style="font-size:31px;font-weight:600">IMG_2041.jpg</div><div class="fs-grey" style="font-size:22px;margin-top:4px">Saved to Desktop &middot; click to open</div></div></div>
  <div class="fs-fly fs-p1"></div>
</div>""",

    "send": f"""
<div class="fs fs-send">{ghosts()}
  <div class="fs-edge"></div>
  <div class="fs-panel fs-card">
    <div class="fs-glow"><div class="fs-glyph"><i class="fs-p1"></i></div></div>
    <div class="fs-status"><b>Galaxy S23 Ultra</b><span class="s1 fs-grey">Release to send 1 photo</span><span class="s2 fs-grey">Sending&hellip;</span><span class="s3 fs-good">Sent &#10003;</span></div>
    <div class="fs-bar"><i></i></div>
  </div>
  <div class="fs-panel fs-arrived"><i class="fs-p1"></i><div><div style="font-size:26px;font-weight:600">On your Galaxy S23</div><div class="fs-grey" style="font-size:20px;margin-top:3px">IMG_2041.jpg &middot; Pictures/Flick</div></div></div>
  <div class="fs-drag"><i class="fs-p1"></i>{CURSOR.replace('class="fx-cursor"', 'class="fs-cursor"')}</div>
</div>""",

    "pair": f"""
<div class="fs fs-pair">{ghosts()}
  <div class="fs-panel fs-big">
    <div class="fs-qr">{_qr(322)}</div>
    <div style="position:absolute;left:440px;top:52px;width:390px">
      <div style="font-size:36px;font-weight:600">Pair a phone</div>
      <div class="fs-grey" style="font-size:21px;line-height:1.6;margin-top:10px">Open Flick, tap + and scan.<br>Check the number matches.</div>
      <div class="fs-dim" style="font-size:16px;letter-spacing:.12em;margin-top:30px">CHECK NUMBER</div>
      <div class="fs-num" style="font-size:66px;font-weight:600;letter-spacing:.06em;line-height:1.1;margin-top:4px">482 913</div>
      <div class="fs-paired fs-good" style="font-size:20px;margin-top:10px;display:flex;align-items:center;gap:10px"><span class="fs-dot"></span>Galaxy S23 Ultra paired</div>
    </div>
    <div class="fs-mini">
      <div class="fs-view">{_qr(126)}<div class="fs-scan"></div></div>
      <div class="fs-num" style="position:absolute;left:0;right:0;top:218px;text-align:center;font-size:32px;font-weight:600;letter-spacing:.05em">482 913</div>
      <div class="fs-yes">Yes, pair</div>
    </div>
  </div>
</div>""",

    "resume": f"""
<div class="fs fs-resume">{ghosts()}
  <div class="fs-panel fs-big">
    <div class="fs-h">Receiving Holiday-4K.mp4</div>
    <div class="fs-lines">
      <span class="l1 fs-grey">From Galaxy S23 &middot; 1.9 GB of 3.4 GB &middot; 28 MB/s &middot; 1 min left</span>
      <span class="l2 fs-warn">Connection dropped &ndash; resuming (1/4)&hellip;</span>
      <span class="l3 fs-grey">Resumed from 1.9 GB &middot; 31 MB/s &middot; 48 s left</span>
      <span class="l4 fs-good">Saved to Desktop &#10003;</span>
    </div>
    <div class="fs-track"><i class="f"></i><i class="a"></i></div>
    <div class="fs-devs"><span>Galaxy S23</span><div class="fs-dash"></div><span>DESKTOP-PC</span></div>
    <div class="fs-wifi"><div class="on">{WIFI_ON}</div><div class="off">{WIFI_OFF}</div></div>
  </div>
</div>""",

    "folders": f"""
<div class="fs fs-folders">{ghosts()}
  <div class="fs-panel fs-big">
    <div class="fs-src">
      <div class="fs-file a fs-p2"></div><div class="fs-file b fs-p5"></div><div class="fs-file c fs-p6"></div>
      {FOLDER}
      <div style="font-size:28px;font-weight:600;margin-top:10px">Holiday</div>
      <div class="fs-grey" style="font-size:19px;margin-top:4px">214 files &middot; 6 folders</div>
    </div>
    <div class="fs-list">
      <div class="fs-dim" style="font-size:16px">Download &rsaquo; Flick</div>
      <div style="font-size:28px;font-weight:600;margin:4px 0 10px">Holiday</div>
      <div class="fs-row"><i style="background:#3A5FA8"></i>Day 1 &ndash; Lisbon</div>
      <div class="fs-row" style="animation-delay:.1s"><i style="background:#3A5FA8"></i>Day 2 &ndash; Sintra</div>
      <div class="fs-row" style="animation-delay:.2s"><i class="fs-p1"></i>IMG_3001.jpg</div>
      <div class="fs-row" style="animation-delay:.3s"><i class="fs-p7"></i>IMG_3002.jpg</div>
      <div class="fs-row" style="animation-delay:.4s;border:0"><i class="fs-p4"></i>beach.mp4</div>
    </div>
    <div class="fs-bundle"><span style="display:inline-block;width:18px;height:15px;border-radius:4px;background:#5C86D6;margin-right:10px;vertical-align:-1px"></span>Holiday &middot; 1.2 GB</div>
  </div>
</div>""",

    "clip": f"""
<div class="fs fs-clip">{ghosts()}
  <div class="fs-panel fs-addr">{SEARCH}<span class="fs-grey">Search or type a URL</span><span class="fs-caret"></span><b>maps.app.goo.gl/x7Qe9Lp</b></div>
  <div class="fs-panel fs-sending">{DOT('#7CC4FF')}<span class="fs-grey">Galaxy S23 is sending text</span></div>
  <div class="fs-panel fs-copied"><div class="fs-icon">{LINK}</div>
    <div><div style="font-size:32px;font-weight:600">Copied from Galaxy S23</div>
      <div class="fs-grey" style="font-size:22px;margin-top:6px">maps.app.goo.gl/x7Qe9Lp &middot; just paste</div></div></div>
</div>""",

    "effects": f"""
<div class="fs fs-fx">{ghosts((90, 300, 560, 380), (700, 380, 470, 300))}
  <div class="fs-surge"><i></i><div class="fs-bezel"></div></div>
  <div class="fs-ripple"><div class="fs-ring r1"></div><div class="fs-ring r2"></div><div class="fs-ring r3"></div></div>
  <div class="fs-halo"><i></i></div>
  <div class="fs-panel fs-pill"><span class="e1">{DOT('#7CC4FF')}Spectral Surge</span><span class="e2">{DOT('#9B7BFF')}Ripple</span><span class="e3">{DOT('#FF6BD6')}Halo</span></div>
</div>""",
}

# Each card's wallpaper sits on the scene's root element.
for _k, _bg in BG.items():
    SCENES[_k] = SCENES[_k].replace('<div class="fs ', f'<div style="background:{_bg}" class="fs ', 1)

LOOP = {"flick": 5, "send": 6, "pair": 5, "resume": 6, "folders": 5, "clip": 4.5, "effects": 6}
STILL = {"flick": 2.6, "send": 1.9, "pair": 3.4, "resume": 2.6, "folders": 3.2, "clip": 2.4, "effects": 0.7}
