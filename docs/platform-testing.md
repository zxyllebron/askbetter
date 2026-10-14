# Platform verification

The macOS and Ubuntu ports are preview builds. A fixture is a separate editor application created for testing; it is not a signed-in Codex client. Passing a fixture test does not establish compatibility with every upstream client version.

## Shared backend

- Real model check on the development Windows machine: GPT-6 Luna / Low / Fast, the reference camera IMU/UDP prompt, approximately **6.0 seconds**. The output retained the reference image format, the final robot-arm goal, its current inability to receive data, and the current camera-transmitter-only task.
- Offline integration checks exercise three instruction files, UTF-8, fresh sessions, final-versus-commentary routing, stale thread/turn isolation, early notifications, model/speed rejection, invalid and empty results, denied tool requests, timeout recovery, model pagination, and settings persistence.
- Geometry/draft checks cover negative monitor coordinates, scaling, resize settling, and changed-text/window guards.

## macOS

Native Apple Silicon and Intel builds use Swift/AppKit and a bundled Python 3.12 helper. Checks include:

- Compilation and local ad-hoc signature verification.
- Actual packaged helper requests for all three modes, using a local fake app-server.
- Startup diagnostics and native geometry/settling checks.
- Native standalone rendering, five menu labels, Enter, Shift+Enter, repeated Enter, and marked-text confirmation handling.
- Real cross-process Accessibility reading/replacement against a separate native AppKit editor fixture, rejecting changed drafts/window titles and never clicking Send.
- Actual controller/AX-observer checks for minimize and last-window close while discovery is deliberately blocked; minimized windows cannot reuse cached editor controls.

The release is not Apple-notarized. Real Codex contenteditable behavior, attachments, clipboard fallback, multiple displays, full-screen Spaces, and input methods require validation on target client versions.

## Ubuntu 24.04

Target: GNOME 46. Desktop CI uses separate X11 and nested Wayland sessions. The test pipeline exercises the actual GNOME extension, AT-SPI adapter, asynchronous service, and a fake app-server with real editor windows.

Checks cover the circular button's 26-pixel size and five-item menu, input/model-selector positioning, three mode requests, replacement and undo, edits made while waiting, same text in another window, and hide/reappear behavior during resizing.

Lifecycle checks minimize, hide, restore, and close the last editor window while polling is blocked and the menu is open. The circle must disappear without a backend response and remain hidden after delayed state could arrive.

The browser-backed fixture complements the native GTK fixture. Chromium checks run through native X11, native Wayland, and XWayland inside a Wayland GNOME session. Neither fixture has a real Codex account. Signed-in Codex integration, fractional scaling, attachments, and input methods remain target-client verification items. The Ubuntu CI machines use x86-64; ARM Linux hardware has not been validated.

On 2026-10-13, a signed-in desktop check passed on Ubuntu 24.04.3, GNOME 46, Wayland with the Codex desktop running through XWayland (desktop package 26.1007.21434, CLI 0.162.0-alpha.17.2). A Chinese draft was read from the real ProseMirror input, optimized with GPT-6 Luna / Low / Fast in approximately 7.8 seconds, replaced in that same input, and restored exactly by undo. The check invoked the same Optimize/Undo service methods as the circular button and did not send the chat draft. This establishes this client/environment sample, not every Codex release or attachment/IME combination.

The rich Chromium fixture now uses embedded paragraph objects, matching the actual client behavior that previously produced the U+FFFC object placeholder. Its X11 integration check also passes multiline replacement/undo, three editing modes, clipboard restoration, changed-draft/window rejection, positioning, and resize settling. Fresh source-ZIP installation was checked with isolated user settings: the application-menu entry is valid, the bundled desktop CLI is found without PATH, and the first-run editor preserves initial button mode for the next desktop login.

The same-window chat regression now switches through `Work with Codex`, `Work with ChatGPT`, and compact `Ask ChatGPT` editors, including a model picker named `Select ChatGPT model` placed immediately beside the field. The wand follows each layout, compact positioning stays outside the text field, and replacement/undo pass in all three. Switching to another chat with identical draft text while optimization is pending preserves the destination draft. On the actual Linux desktop, the compact ChatGPT label and adjacent layout were reproduced and recognized after the fix.

KDE, other compositors, other GNOME major versions, browser tabs, and IDE input boxes are not covered by this package. No claim of universal Linux compatibility is made.

## Reproducing checks

Windows adds native WPF/WinEvent lifecycle checks with editor discovery blocked. Closing, minimizing, hiding, and switching away must hide the circle within 150 ms on the test machine, invalidate delayed placement, and release window hooks/timers at exit. A real provider on a separate UI thread deliberately delays text reads; the helper must remain responsive and must not apply its optimized reply to a background editor or record a failed replacement for undo. These measured fixture timings are not a universal OS scheduling guarantee.

The GitHub Actions workflow is [.github/workflows/platforms.yml](../.github/workflows/platforms.yml). Individual run logs and report artifacts identify the OS, fixture type, completed checks, and source revision. Test helper credentials are simulated; no Codex login files are uploaded to runners or included in release packages.

For a real-client check, confirm the button appears beside the model selector, click a harmless draft once, verify replacement without sending, change the draft while waiting, switch chats/windows, resize/zoom, test undo and the standalone dialog, and inspect attachment preservation.
