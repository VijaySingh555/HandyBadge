# HandyBadge Remote v0.2

A small Windows tray app that turns swipes on your badge into key presses, so you can move through PowerPoint slides (or anything else that uses the arrow keys). It talks to the badge directly over Bluetooth LE. No phone, Python or install needed.

## Build (GitHub)
1. Create a private repository and upload everything in this folder, keeping the `.github` and `src` folders.
2. GitHub Actions runs **Build HandyBadge Remote** on every push to `main`. Open the run under **Actions** and download the **HandyBadgeRemote** artifact.
3. Optional: push a tag such as `v0.2` to publish a release with a zip attached.

The artifact contains `HandyBadgeRemote-v0_2.exe`, plus any support files the build adds. Keep them together in one folder.

## Requirements
- Windows 10 or 11 with Bluetooth LE (built in or a dongle).
- .NET Framework 4.8, which Windows 10/11 already include.
- The badge switched on and **not connected to HandyBadge on your phone** (it allows one connection at a time).
- Keep this off firm-managed laptops unless IT has approved it.

## First run
1. Run the exe. Windows SmartScreen may warn because it isn't signed: choose **More info → Run anyway**.
2. A slide-and-arrow icon appears in the system tray (check the ^ overflow). The app searches for a badge whose name starts with `BG` and connects.
3. Right-click the icon → **Calibrate (swipe left twice)**. Swipe left on the badge, wait for the "swipe left once more" message, then swipe left again. You'll see **Calibrated**.
4. Open a PowerPoint slideshow and swipe: right sends →, left sends ←.

Calibration is saved, so you only repeat it after uploading new images. If direction is ever the wrong way round, use **Flip direction**.

## Test checklist
1. Tray tooltip shows `BG02 · image 1 of 3` (or similar) and changes as you swipe.
2. In Notepad with some text, swipes move the cursor left and right.
3. In a PowerPoint slideshow, swipes change slides.
4. **Pause keys** stops key presses; swipes still show in the tooltip.
5. Switch the badge off: the app says it lost the badge and reconnects when it's back on.

## Stream to badge (v0.2)
Right-click the tray icon → **Stream to badge**, then pick a mode. The badge switches to its live screen; swipes pause while streaming, and the badge's **shutter tap** takes over.

| Mode | Shows | Shutter tap |
|---|---|---|
| **Screen** | The monitor your mouse is on, fitted to the circle | Cycles zoom: whole screen → 2× → 4× around the mouse → whole |
| **Region** | A square you drag out with **Pick region…** (the circle shows what fits) | Sends `ShutterKey` (default Right) |
| **Presenter view** | Slide *n* of *N*, elapsed time, clock, next slide title | Next slide, straight through PowerPoint (works even if the slideshow isn't focused) |
| **Off** | Back to swipe mode | — |

- Frames are only sent when the picture changes, plus a keep-alive every 2 s. Frame rate is capped at 3 fps by default (**Frame rate** submenu, 1–5).
- Presenter view needs desktop PowerPoint running a slideshow; otherwise it shows the timer and clock and the shutter sends `ShutterKey`. **Reset presenter timer** restarts the clock.
- **Screen mode shows whatever is on your screen on a badge others can see.** Use Region or Presenter view around confidential material.
- Streaming uses noticeably more badge battery.

### Streaming tests
1. **Screen:** the badge shows your desktop with a red pointer dot; three shutter taps go 2×, 4×, back to whole.
2. **Region:** pick a square over a slide; it fills the badge; the shutter tap sends Right.
3. **Presenter:** start a slideshow; the badge shows the slide number and next title; the shutter tap advances; numbers update.
4. **Off:** the badge returns to its images and swipes work again.
5. Leave a static slide for a few minutes: the badge should stay in live mode. If it drops out, lower `KeepAliveMs`.

## Tray menu
- **Connect / Disconnect**, **Calibrate**, **Flip direction**, **Stream to badge**
- **Pause keys**, **Swap left and right keys**, **Start with Windows**
- **Open settings file**, **Reload settings**, **Open log**, **About** (shows the version), **Exit**

## Settings
`%APPDATA%\HandyBadgeRemote\settings.ini`. Edit, then choose **Reload settings**.

| Setting | Default | Meaning |
|---|---|---|
| BadgeName | BG | Name prefix to search for |
| LeftKey / RightKey | Left / Right | Keys to send. Also: Up, Down, PageUp, PageDown, Space, Enter, Home, End, B, W, F5, None |
| SwapKeys | false | Swap the two keys |
| PollMs | 500 | How often to check the badge (200–5000 ms) |
| StreamFps | 3 | Streaming frame-rate cap (1–5) |
| StreamQuality | 40 | Streaming JPEG quality (10–90); lower is faster |
| KeepAliveMs | 2000 | Resend the frame this often when nothing changes |
| ShutterKey | Right | Key the shutter tap sends in Region mode |
| StreamRegion | set by Pick region | x,y,size of the square to stream |
| LastAddress, Ring, Calibrated | set by the app | Delete to forget the badge or calibration |

## How it works
Each stored image reports its storage footprint (JPEG + 16 KB, rounded up to 4 KB) in the badge's stored-faces reply. The app reads that every half-second; when it changes, the image changed, and the known loop order gives the direction. It needs three images with different footprints. HandyBadge v0.12 checks this when you upload.

Streaming uses the badge's live-preview channel (the same as HandyBadge's camera mode) with HandyBadge's JPEG encoder ported to C#.

Limits: about 0.5–1 s of lag; doesn't work during HandyBadge live streaming or in the badge's torch; swipes between two images with the same footprint are invisible.

## Troubleshooting
- **No badge found:** disconnect HandyBadge on the phone, or switch the phone's Bluetooth off. Check the badge is awake.
- **Connects then drops:** move closer, or try another Bluetooth adapter. **Open log** shows each step.
- **Keys do nothing in one program:** programs running as administrator ignore keys from normal apps. Run both the same way.
- **Antivirus quarantines it:** an unsigned app that sends keystrokes can trigger this. Restore it or add an exception on your own PC.

## Versions
Bump the version in three places on each build: `AssemblyName` and `Version` (plus `app.manifest`) in `src/HandyBadgeRemote.csproj`, and `AppInfo.Version` in `src/Program.cs`. Record it in `Version Control - HandyBadge Remote.html`.
