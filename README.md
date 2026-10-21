# Flick + FlickDesk

**Flick files between your Android phone and Windows PC — with a burst of light.**

[![Build](https://github.com/samround73-cpu/FlickDesk/actions/workflows/build.yml/badge.svg)](https://github.com/samround73-cpu/FlickDesk/actions/workflows/build.yml)

**Download:** grab `FlickDesk-…-windows-x64.exe` and `Flick-…-android.apk` from the
[latest release](https://github.com/samround73-cpu/FlickDesk/releases/latest).
Every push to `main` also builds both (see the run's *Artifacts* on the [Actions tab](https://github.com/samround73-cpu/FlickDesk/actions/workflows/build.yml)).

**Making a release:** tag a commit `vX.Y.Z` and push the tag – CI builds both apps and publishes the release with the files attached.

Current version: 0.3.1

Hold your phone next to your screen, long-press a photo, **flick it towards the PC**. A ripple of
spectral light washes in from that edge of the monitor and the file lands on your desktop.
The other way round: drag a file onto the glowing strip at the screen edge, or use the tray.

```
flick/
├── android/   Flick — Kotlin + Jetpack Compose (Android 10+)
└── windows/   FlickDesk — C# / WPF tray app (.NET 8, Windows 10/11)
```

## What's new in 0.3
- **Spectral Surge** (new default) on PC and phone: the whole screen answers. The bezel ignites from the
  phone's side and runs round the frame, a bright glass wave crosses the screen, spectral rings ride it,
  a warm/cool caustic line leads, and a flare with an anamorphic streak marks where the file came in.
  A brief dim behind the light means it reads on white windows too.
- On Windows the light is now drawn by the **Windows compositor (GPU)** in its own click-through window,
  so it covers the full screen at your display's refresh rate. Settings → General → *Full-screen light (GPU)*
  switches back to the classic renderer if it ever doesn't show.
- **Sending to the phone** now ends with the screen's light collapsing into the phone's edge, and the drop
  zone is a soft leak of light from the bezel with a "Drop to send to …" pill instead of a box.
- New presets: **Halo** (just the glowing frame) and a full-screen **Ripple**; saturated palettes.
- Build output moved: `bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\FlickDesk.exe`.

## What was new in 0.2
- **QR pairing + end-to-end encryption.** FlickDesk shows a QR code; the phone scans it and both screens
  show the same 6-digit number. Every transfer after that is AES-256-GCM encrypted with a key only
  those two devices have. (Can't scan? Tap the PC under "Nearby" or enter its IP — you confirm the
  number on both screens instead.)
- **Refined effects** on both PC and phone: *Spectral Ripple* (three soft spectral rings with a
  glassy warm/cool hairline), *Aurora Veil*, *Prism Sweep*, *Liquid Glass*, *Quiet* — with
  palettes (Spectrum, Aurora, Ember, Glacier), intensity, speed, an optional chime, "stay quiet in
  full-screen apps" and "reduce motion".
- **Several PCs, one per direction.** Flick left for the desktop, up for the laptop. Tap a PC chip to change its side.
- **Stacks.** Tap photos to select them, long-press any of them and flick once — they all go, and land fanned on the PC.
- **Clipboard both ways.** Send text/links from the phone's Clipboard tab (or Share → Flick with text);
  FlickDesk copies it automatically. Tray → *Send clipboard* goes the other way.
- **History** on both sides, an **arrival card** on the phone (Open / Share), a **tray flyout** on Windows
  (status, quick actions, last clipboard, today's transfers) and **Pause for 1 hour**.
- **Receive in the background** on the phone (a quiet ongoing notification keeps Flick listening).

> **Upgrading from 0.1:** the protocol changed (it's encrypted now), so update **both** apps and pair once
> with the QR code. Old pairings are ignored.

## Build & run

### Windows (FlickDesk)
1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download).
2. In `windows/FlickDesk`: `dotnet run` — or build an exe you can double-click:
   `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
   → `bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\FlickDesk.exe`
3. **Allow it through Windows Firewall on Private networks** when asked.
4. First run opens *Pair a phone*. Later: **left-click** the tray orb for the flyout, **right-click** for the menu.
   Settings → Effects to pick a look; Settings → General to start with Windows.

### Android (Flick)
1. Open `android/` in Android Studio; if asked for a Gradle JDK choose **JVM 21**.
2. Plug in your phone (USB debugging on) and press ▶ Run.
3. Tap **+** → scan the code on your PC → check the number matches → **Yes, pair**.

## Using it
- **Phone → PC:** long-press a photo until it lifts, flick it towards the PC (or drag it to that edge). The angle
  of the flick moves where along the PC's edge the light appears. Tap photos first to build a stack.
- **PC → phone:** drag files onto the thin strip on the phone's edge of the screen (it widens as you hover),
  or flyout → *Send files*. Photos land in Pictures/Flick, videos in Movies/Flick, other files in Download/Flick.
- **Tell each side where the other is:** on the phone, tap the PC chip → *Left / Above / Right*; in FlickDesk,
  Settings → *My phone sits on the* Left / Right / Bottom. (Phone to the right of the laptop ⇒ phone: Left, PC: Right.)

## How it works
| Piece | Android | Windows |
|---|---|---|
| Discovery | `Discovery` listens for UDP beacons on 47800 | `DiscoveryBeacon` broadcasts every 2 s |
| Pairing | `FlickClient.pair/confirm`, CameraX + ML Kit scanner | `PairingService`, `PairWindow` (QRCoder) |
| Encryption | `Crypto.kt` (`FlickCrypto`, `SecureChannel`) | `Crypto.cs` — byte-for-byte the same |
| Phone → PC | `FlickClient` → PC:47801 | `TransferServer` |
| PC → phone | `FlickServer` on 47802 (kept alive by `ReceiveService`) | `PhoneClient` |
| Effects | `EdgeEffects.kt` — native paints, BlurMaskFilter, screen blending | `OverlayWindow` + `EffectTextures` — pre-rendered ring bitmaps, cached, GPU-transformed |

### Protocol v2
Each TCP connection starts with **one plain JSON line**:

```jsonc
// Pairing (plain). With a QR code, mac = HMAC-SHA256(token, "flick-pair-v2" ‖ pub ‖ deviceId)
{"v":2,"type":"pair","deviceId":"…","deviceName":"Pixel 8","port":47802,"pub":"<SPKI b64>","qr":true,"mac":"…"}
  ← {"ok":true,"pcId":"…","name":"DESKTOP-1","pub":"<SPKI b64>","needsPcApproval":false,"mac":"HMAC(token,'flick-pair-v2-pc'‖pcPub‖phonePub)"}

// Everything else: open an encrypted channel
{"v":2,"from":"<deviceId or pcId>","nonce":"<16 random bytes b64>"}
  ← {"ok":true}            then AES-GCM frames both ways
```

Keys: `K = HKDF-SHA256(ECDH-P256 secret, salt = QR token or 32×0x00, "flick-v2-key")`;
per connection `S = HKDF-SHA256(K, salt = nonce, "flick-v2-session")`. Frame = `[4-byte BE length][ciphertext ‖ tag]`,
GCM nonce = `[0,0,0,dir] ‖ 8-byte BE counter` (dir 0 = client→server). Check number = first 4 bytes of
`SHA-256("flick-v2-sas" ‖ phonePub ‖ pcPub)` mod 10⁶.

Messages inside the channel (JSON frames):
`pair-confirm` · `hello {deviceName, port}` · `clip {text}` · `unpair` ·
`file {name, size, mime, edgePos, batchIndex, batchCount}` → `{"ok":true}` (ready) → data frames → `{"ok":true,"savedAs"}`.

## Known limits
- Overlay and drop strip use the **primary monitor**.
- **Folders** aren't sent (zip them). The PC → phone direction targets the most recently seen phone.
- Guest/office Wi-Fi often blocks device-to-device traffic; use "Enter IP" if the PC never appears.
- Keys are stored in `%AppData%\FlickDesk\settings.json` and the app's private storage on the phone.
