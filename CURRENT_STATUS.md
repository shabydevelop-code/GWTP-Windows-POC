# GWTP Windows Runtime — Current Status

Last updated: 2026-09-25

## Implemented
- WPF element picker uses UI Automation FromPoint and highlights the hovered control.
- Selected element identity currently stores Name, AutomationId, ControlType, and ProcessName. Runtime rediscovery additionally scopes matching processes to the current Windows SessionId.
- FindElement rediscovery is generic by process name plus UIA properties; no Notepad-specific targeting is hard-coded.
- HighlightWindow provides a transparent, click-through, non-activating target outline.
- GuidanceWindow provides a separate non-activating guidance bubble positioned near the target.
- ElementTrackingService follows target movement using UIA BoundingRectangle/IsOffscreen property changes and WinEvent EVENT_OBJECT_LOCATIONCHANGE.
- Element tracking is fully event-driven; the former 2-second health-check polling timer has been removed.
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

## Manual guidance positioning — 2026-09-25
- GuidanceWindow now has a dedicated transparent drag handle at the top of the bubble.
- Dragging the handle switches that guidance instance to manual positioning.
- While manual positioning is active, target movement continues to update the highlight immediately but no longer forces the guidance bubble back beside the target.
- Manual positioning is intentionally local to the current GuidanceWindow instance. Closing/recreating the guidance window (including temporary hide/restore) returns it to automatic positioning.
- This provides a lightweight escape hatch when menus, dropdowns, dialogs, or application content would otherwise be covered, without adding UIA-tree scans to the tracking hot path.

## Web/Windows guidance visual parity — 2026-09-25
- Windows GuidanceWindow structure and styling now mirrors the current Web learner guidance bubble from `extension/content/overlay/training-runner.js`.
- Matched the current Web bubble's 340px maximum-width concept, white surface, #D6008F guidance accent, 2px border / 3px top border, 12px radius, #172033 text, centered controls, and six-dot #94A3B8 drag handle.
- Removed the Windows-only visible "Step 1" heading because the Web bubble renders the authored instruction as the primary content.
- Previous/Next remain POC controls until API progress wiring, but their visual structure now follows the Web bubble.
- Existing manual drag behavior and event-driven element tracking are preserved.

## Event-only tracking — 2026-09-25
- Removed the 2-second DispatcherTimer health check from ElementTrackingService.
- Normal learner tracking now performs no periodic polling: bounds refreshes are triggered by UI Automation property-change events and WinEvent EVENT_OBJECT_LOCATIONCHANGE only.
- Start() still performs one immediate RefreshBounds() to render the initial state.
- This reduces unnecessary per-session background work for the intended multi-user/RDS architecture.
- Minimize/Restore and host-close behavior should be re-verified manually without the fallback timer.

## Production baseline rule — 2026-09-25
- This repository is now explicitly developed under production assumptions rather than as disposable POC code.
- New implementation decisions must be generic, maintainable, resource-conscious, secure, reliable, and suitable for RDS/multi-session deployment.
- Temporary demonstration shortcuts, application-specific hard-coding, and architecture that is expected to be replaced later are not acceptable.
- Incremental delivery remains preferred, but each increment must fit the intended production architecture.
- Existing items that are not yet production-ready remain documented below as known limitations and must be resolved rather than normalized as POC behavior.

## Host application lifecycle tracking — 2026-09-25
- Closing the tracked host application is now handled explicitly and event-driven; it no longer depends on periodic health polling.
- ElementTrackingService resolves the host window process once at Start(), subscribes to Process.Exited, and installs a process-scoped EVENT_OBJECT_DESTROY WinEvent hook in addition to the existing location-change hook.
- Destruction of the tracked host window or exit of its process raises ElementUnavailable so MainWindow removes the highlight/guidance and disposes tracking.
- WinEvent hooks are scoped to the host process to reduce unrelated desktop events and background work.
- Dispose unsubscribes Process.Exited, disposes the Process object, and unhooks both WinEvent hooks.
- This is production-oriented lifecycle handling and preserves the event-only/no-polling rule.

## RDS session-safe target rediscovery — 2026-09-25
- FindElement now restricts ProcessName matches to processes whose Process.SessionId equals the Windows Runtime's current SessionId.
- Processes from other simultaneous RDS sessions are excluded before UIA candidate matching.
- SessionId is runtime context and is not persisted as part of the reusable authored element identity.
- Process handles returned by GetProcessesByName are disposed after inspection.
- This removes the previously documented cross-session process-discovery gap while preserving generic application targeting.

## Known limitations / next work
1. Name + AutomationId + ControlType can still be ambiguous; robust hierarchy/fallback identity is not implemented.
2. Guidance Previous/Next is not yet connected to GWTP.Api learner progress.
3. A separate step runtime/platform model is required in the main GWTP data/API model; existing TargetType must remain element/none.
4. Secure learner/token handoff between the browser extension and Windows runtime remains to be designed.
5. After Windows targeting is hardened, implement the first production-compatible WEB -> WINDOWS -> WEB integration slice.
