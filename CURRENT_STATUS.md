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

## Transient UI collision handling — 2026-09-25
- Added an event-driven first pass for transient menu UI.
- ElementTrackingService listens to UIA MenuOpened/MenuClosed events and collects visible Menu bounds belonging to the tracked target process.
- Guidance placement now evaluates below/above/right/left candidates and rejects positions that intersect visible transient UI.
- If no safe candidate exists, the guidance window is hidden temporarily; tracking remains active and the next menu/target event can restore it.
- This is intentionally generic at the target-process/UIA level and contains no Notepad-specific rule.
- The target highlight remains visible; only guidance placement reacts to transient UI collision.
- Manual verification is required with Notepad View and additional popup/dropdown/dialog cases before broadening the transient-control detection set.

## Known limitations / next work
1. Process rediscovery is not yet scoped to the current Windows SessionId and is therefore not RDS-safe.
2. Name + AutomationId + ControlType can still be ambiguous; robust hierarchy/fallback identity is not implemented.
3. Guidance Previous/Next is still POC UI and is not connected to GWTP.Api learner progress.
4. A separate step runtime/platform model is required in the main GWTP data/API model; existing TargetType must remain element/none.
5. Secure learner/token handoff between the browser extension and Windows runtime remains to be designed.
6. After Windows targeting is hardened, implement the minimal WEB -> WINDOWS -> WEB integration POC.
