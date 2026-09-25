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

## Immediate host-window close handling — 2026-09-25
- Modern applications may keep their process alive briefly after the visible host window closes, so Process.Exited is not sufficient for immediate guidance cleanup.
- ElementTrackingService now also listens for process-scoped EVENT_OBJECT_HIDE for the tracked host HWND, restricted to OBJID_WINDOW.
- Host-window HIDE or DESTROY is treated as terminal for the current tracked target and raises ElementUnavailable immediately; Process.Exited remains a lifecycle fallback.
- Minimize continues to use the UIA IsOffscreen temporary-hidden path and must remain resumable on Restore.
- No polling or delay was introduced.

## Host window state classification — 2026-09-25
- Host-window HIDE is no longer treated unconditionally as terminal because minimize can also hide a window.
- On HIDE, the runtime evaluates the tracked HWND with IsWindow, IsIconic, and IsWindowVisible: destroyed/non-window -> unavailable; minimized -> temporarily hidden; hidden non-minimized host -> unavailable.
- A lightweight EVENT_SYSTEM_FOREGROUND hook triggers the same O(1) host-HWND state evaluation when foreground ownership changes. This avoids waiting for Process.Exited in applications that retain their process/window infrastructure briefly after visible close.
- The foreground hook performs no UIA tree scan and no polling.
- DESTROY and Process.Exited remain terminal lifecycle signals.

## Immediate minimize lifecycle — 2026-09-25
- Minimize/Restore no longer waits for UIA IsOffscreen propagation.
- ElementTrackingService listens to process-scoped EVENT_SYSTEM_MINIMIZESTART and EVENT_SYSTEM_MINIMIZEEND.
- MINIMIZESTART hides guidance/highlight immediately while retaining tracking; MINIMIZEEND immediately refreshes the tracked element and restores overlays when bounds are valid.
- This remains fully event-driven and adds no timer, polling, or UIA-tree scan.

## Tracking lifecycle instrumentation — 2026-09-25
- Temporary diagnostic instrumentation records millisecond timestamps for UIA property changes, relevant WinEvents, host-window state evaluation, RefreshBounds, and ElementUnavailable.
- Diagnostic lines are written to Debug output and surfaced in the runtime StatusText so event latency can be measured without adding polling or delays.
- This instrumentation is specifically intended to identify which native/UIA lifecycle signal is actually delayed during minimize/close before simplifying the production hook set.
- After the reliable lifecycle signal is established, remove or reduce user-visible diagnostic output and retain only appropriate production logging.

## Persistent tracking diagnostic log — 2026-09-25
- Tracking lifecycle diagnostics are also appended to %LOCALAPPDATA%\GWTP\Logs\windows-tracking.log.
- Each line includes the local date plus millisecond-resolution event time.
- Logging failures are swallowed so diagnostics cannot interfere with runtime tracking.
- This is temporary diagnostic instrumentation for isolating delayed host-window close notification and should be reduced/removed after the production lifecycle signal is established.

## Close-action timing instrumentation — 2026-09-25
- Temporary process-scoped EVENT_OBJECT_INVOKED logging was added to correlate user control invocation with host-window HIDE/DESTROY timing.
- INVOKED is diagnostic only and does not currently change runtime lifecycle state.
- The goal is to determine whether the visible close action is observable earlier than EVENT_OBJECT_HIDE without polling or application-specific logic.

## Native window-chain diagnostic — 2026-09-25
- Temporary instrumentation now records the first native HWND associated with the tracked UIA element, its GA_ROOT top-level HWND, any GW_OWNER HWND, and the Win32 class names of root/owner.
- This is diagnostic only: runtime lifecycle behavior still uses the existing root host HWND.
- Purpose: verify whether delayed close notification is caused by tracking a framework/host window whose lifecycle differs from the user-visible application window, before considering any heuristic close detection.
- No polling, timing heuristic, or application-specific rule was introduced.

## Minimize event window-chain fix — 2026-09-25
- MINIMIZESTART/MINIMIZEEND are no longer accepted only when the event HWND exactly equals the stored root host HWND.
- The runtime now accepts minimize lifecycle events when the HWND belongs to the tracked native window chain (element/root/owner or resolves to the same GA_ROOT).
- This fixes frameworks where minimize events are emitted for a related native HWND rather than the exact UIA-derived root HWND.
- Matching remains HWND-based and event-driven; no polling, delay, or application-specific rule was added.

## Host close lifecycle decision — 2026-09-25
- Diagnostics across Notepad and Microsoft Word confirmed that GWTP reacts to EVENT_OBJECT_HIDE/DESTROY within milliseconds, while applications may delay those definitive lifecycle events by several seconds after the visible close action.
- Production runtime continues to use definitive HIDE/DESTROY/Process.Exited lifecycle signals rather than inferring closure from focus loss, movement, animation, or other heuristics.
- WM_CLOSE interception through cross-process message hooks was not adopted: it would add injection/bitness/deployment complexity and represents a close request that applications may cancel or defer.
- Minimize remains event-driven through EVENT_SYSTEM_MINIMIZESTART/MINIMIZEEND and window-chain matching, preserving immediate hide/restore behavior.
- Temporary timestamp logging, user-visible diagnostic output, foreground diagnostic hook, and EVENT_OBJECT_INVOKED diagnostic hook have been removed.
- The temporary native window-chain fields remain in use for generic minimize-event matching; no application-specific rule was introduced.
- Known limitation: definitive host-window close detection can lag the user's close action when the host application delays HIDE/DESTROY.

## Visibility lifecycle diagnostic result — 2026-09-25
- Temporary visibility instrumentation was removed after validation.
- Minimize produces an early, unambiguous non-visible state: IsIconic=True and IsOffscreen=True, followed by EVENT_SYSTEM_MINIMIZESTART.
- Close testing showed no earlier usable visibility transition: immediately before EVENT_OBJECT_HIDE the host remained IsWindow=True, IsVisible=True, IsIconic=False and the tracked UIA element remained IsOffscreen=False with valid bounds.
- EVENT_OBJECT_HIDE was the first observed existing signal at which IsWindowVisible became false; GWTP therefore keeps definitive HIDE/DESTROY/Process.Exited close handling rather than introducing a heuristic.
- No timer, polling, desktop scan, cross-process close-message hook, or application-specific close behavior was added.

## Runtime shutdown ordering — 2026-09-25
- MainWindow now tears down the active tracker, highlight, and guidance window during the WPF Closing phase rather than waiting for Closed.
- The element-selection timer is also stopped during Closing.
- This ensures GWTP-owned top-level overlay windows are explicitly closed before the main window completes shutdown, avoiding overlay lifetime extending beyond the initiating window's close sequence.
- This change is runtime-generic and does not alter host-application close detection or Minimize/Restore behavior.

## Integration/deployment decisions — 2026-09-25
- The browser Extension remains the learner controller for Web-only, Windows-only, and Hybrid guides; the Windows Runtime will not duplicate guide-selection/start/resume learner UI.
- Deployment matrix: Web-only = Extension without Windows Runtime; Windows-only = Extension + Windows Runtime; Hybrid = Extension + Windows Runtime.
- Windows Runtime is optional for GWTP as a whole. A station that is not intended for Windows training can omit it entirely.
- On Windows-capable stations the Runtime may start with the Windows/RDS session or manually and remain available. With no Windows training target it performs no element tracking and displays no overlays; no additional launcher process or special idle subsystem is planned.
- Windows application detection and application launch are separate concerns. Guide-level launch configuration must support generic launch mechanisms such as EXE, BAT/script plus arguments/working directory, shortcut, or URI/enterprise launcher.
- For Windows-only guide start, an existing suitable application instance in the current session should be reused/activated; otherwise the configured application launch mechanism may be invoked.
- For Hybrid transitions, if the Web workflow already launched the required Windows application, the Runtime should detect/reuse it rather than launch another instance.

## Multi-step Windows navigation test harness — 2026-09-25
- Guidance Previous/Next controls are now wired locally for a pre-integration runtime test.
- Each newly selected UIA element is appended as a temporary in-memory test step; selecting two or more controls creates a local step sequence.
- Previous/Next switches the active target by disposing the previous ElementTrackingService, rediscovering the selected step target through the existing generic identity logic, and starting fresh tracking for the new target.
- Navigation buttons reflect whether a previous/next local test step exists.
- This is deliberately an in-memory validation harness, not a second persistence model: production guide steps and progress will come from GWTP.Api during integration.
- Next verification: select at least two distinct controls in the same application and verify Next/Previous moves highlight/guidance cleanly with no stale overlay or tracking.
- After navigation is verified, add a similarly scoped Windows validation test before API integration.

## Foreground-aware Windows guidance — 2026-09-25
- Windows guidance/highlight must not remain Topmost over unrelated foreground applications.
- ElementTrackingService now listens to EVENT_SYSTEM_FOREGROUND as a desktop-level event while a target is actively tracked.
- If foreground moves outside the tracked host window chain, guidance/highlight are temporarily hidden while tracking remains alive.
- When the tracked host window becomes foreground again, the tracker refreshes bounds and the overlays are recreated/repositioned immediately.
- Foreground loss is explicitly temporary visibility only; it is not interpreted as application close and does not advance/dispose learner tracking.
- The foreground hook exists only while an element is actively tracked and is removed in Dispose; no polling or desktop UIA scan was added.
- Re-verify multi-step Previous/Next with the target application moving behind/in front of other applications.

## Windows validation test harness — 2026-09-25
- The local multi-step harness now validates the current Windows target before allowing Next.
- For this pre-integration test only, the expected value is the fixed string `GWTP`; this is test-harness configuration and is not an application-specific production rule.
- Value extraction is generic through UI Automation: ValuePattern is preferred and TextPattern is used as a fallback.
- If the target does not expose a readable value, the target disappears, or the value does not equal the expected test value, Next remains on the current step and the guidance bubble shows an inline validation message.
- A successful validation clears the message and advances to the next local test step.
- This harness deliberately does not persist validation definitions. Production validation engine/expression comes from authored GWTP guide-step data during integration, preserving the existing principle that GWTP validates only explicitly authored learning rules.
- Manual verification completed: with a writable text control as step 1 and another target as step 2, Next remained blocked for a non-matching value and advanced after the value was exactly `GWTP`. The runtime remained stable during the validation test.

## Runtime stability observation during validation test — 2026-09-25
- During manual testing of the new Windows validation harness, the GWTP Windows Runtime process exited unexpectedly at an as-yet unidentified point.
- The cause has not been diagnosed and must not be assumed to be the validation logic without evidence.
- Before treating multi-step validation as verified or beginning Web/Windows integration, reproduce the failure and capture the exception/process-exit evidence so the runtime crash path can be fixed.
- The validation harness has since been manually verified and remained stable. The earlier AppHangB1 remains a recorded stability incident rather than a confirmed validation defect; persistent diagnostics remain available if it recurs.

## Persistent runtime hang diagnostics — 2026-09-25
- Windows Error Reporting confirmed the observed unexpected disappearance was an AppHangB1 (the runtime stopped responding and was closed), not a normal managed exception crash.
- Persistent diagnostics are now written to `%LOCALAPPDATA%\GWTP\Logs\windows-runtime.log`.
- Runtime startup/exit and managed failure channels (WPF DispatcherUnhandledException, AppDomain UnhandledException, and TaskScheduler UnobservedTaskException) are recorded without allowing logging failures to affect runtime behavior.
- The current validation path records boundaries around target rediscovery and value reading, including the desktop RootElement.FindAll call, so a future hang can be localized by the last completed operation.
- This instrumentation is diagnostic only and does not add polling, timers, or application-specific behavior.
- If the hang reproduces, inspect the final lines of windows-runtime.log before changing UIA architecture.

## Known limitations / next work
1. Name + AutomationId + ControlType can still be ambiguous; robust hierarchy/fallback identity is not implemented.
2. Guidance Previous/Next is not yet connected to GWTP.Api learner progress.
3. A separate step runtime/platform model is required in the main GWTP data/API model; existing TargetType must remain element/none.
4. Secure learner/token handoff between the browser extension and Windows runtime remains to be designed.
5. After Windows targeting is hardened, implement the first production-compatible WEB -> WINDOWS -> WEB integration slice.
