# GWTP Windows POC — Current Status

Last updated: 2026-09-25

## Implemented
- WPF element picker uses UI Automation FromPoint and highlights the hovered control.
- Selected element identity currently stores Name, AutomationId, ControlType, and ProcessName.
- FindElement rediscovery is generic by process name plus UIA properties; no Notepad-specific targeting is hard-coded.
- HighlightWindow provides a transparent, click-through, non-activating target outline.
- GuidanceWindow provides a separate non-activating guidance bubble positioned near the target.
- ElementTrackingService follows target movement using UIA BoundingRectangle/IsOffscreen property changes and WinEvent EVENT_OBJECT_LOCATIONCHANGE.
- A low-frequency 2-second health refresh exists as a fallback; event-driven tracking is the primary mechanism.
- Manual verification confirmed that highlight and guidance remain attached while the Notepad window moves.

## Minimize / Restore fix — 2026-09-25
Previously, IsOffscreen or an empty/invalid BoundingRectangle raised ElementUnavailable. MainWindow then disposed the tracker, so minimizing the host application hid the overlays correctly but restoring the application could not bring them back.

The tracker now distinguishes temporary invisibility from true element unavailability:
- IsOffscreen / invalid bounds -> ElementTemporarilyHidden.
- MainWindow closes only the highlight and guidance windows; the ElementTrackingService remains active.
- When the element becomes visible again and valid bounds are observed, BoundsChanged recreates/repositions the highlight and guidance automatically.
- ElementNotAvailableException still raises ElementUnavailable and closes/disposes the complete training overlay.

Manual verification of Restore behavior is still required after pulling this change.

## Transient UI collision experiment — 2026-09-25
- An initial UIA MenuOpened/MenuClosed collision experiment was tested and reverted.
- The implementation scanned the desktop UIA tree for visible Menu controls during normal target refreshes. This made element tracking noticeably less immediate and did not solve the observed Notepad menu overlap reliably.
- The proven movement tracking and Minimize/Restore behavior have been restored unchanged.
- Any next collision-management approach must not perform RootElement descendant scans on the hot tracking path. Prefer direct WinEvent/window geometry or another lightweight event-driven mechanism, and validate it separately before integrating it into target movement tracking.

## Known limitations / next work
1. Process rediscovery is not yet scoped to the current Windows SessionId and is therefore not RDS-safe.
2. Name + AutomationId + ControlType can still be ambiguous; robust hierarchy/fallback identity is not implemented.
3. Guidance Previous/Next is still POC UI and is not connected to GWTP.Api learner progress.
4. A separate step runtime/platform model is required in the main GWTP data/API model; existing TargetType must remain element/none.
5. Secure learner/token handoff between the browser extension and Windows runtime remains to be designed.
6. After Windows targeting is hardened, implement the minimal WEB -> WINDOWS -> WEB integration POC.
