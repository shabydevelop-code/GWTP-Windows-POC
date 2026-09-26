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

## Pre-integration Windows runtime verification complete — 2026-09-25
- The planned pre-integration Windows runtime checks are complete and manually verified.
- Multi-step element navigation works: Previous/Next switches between distinct UIA targets and starts fresh tracking without leaving stale overlays.
- Foreground behavior works: guidance/highlight hide when the tracked application moves to the background and return when its host becomes foreground again.
- Windows validation works in the current test harness: an invalid value blocks Next and the expected value allows progression to the next target.
- The runtime remained stable during the completed navigation/foreground/validation verification.
- The earlier AppHangB1 remains documented as an isolated observed hang with no confirmed validation cause. Persistent runtime diagnostics remain enabled so a recurrence can be localized.
- The Windows runtime is therefore ready for the next integration slice: production-compatible Web -> Windows -> Web handoff using the existing GWTP learner/progress model rather than the local in-memory harness.

## Pending Windows target/window test — 2026-09-25
- The local multi-step harness now supports a next step whose Windows target is not currently available.
- When navigation reaches an unavailable Windows target, the active highlight/guidance is removed and the runtime enters an explicit pending-target state without advancing again or showing a stale bubble.
- Pending discovery is event-driven through UI Automation `WindowPattern.WindowOpenedEvent`; no timer, retry loop, or periodic UIA scan is used.
- Each relevant window-open event triggers one generic rediscovery attempt using the pending step's existing process/session/UIA identity. Unrelated windows are ignored when the authored target still cannot be resolved.
- Once the target becomes resolvable, the pending watcher is removed and normal element tracking plus guidance starts automatically.
- Closing/replacing the active training state disposes the pending watcher.
- Manual verification required: author/select two local test targets, make the second target unavailable, navigate from step 1 to step 2, verify that no guidance is shown while waiting, then open the second target's window and verify that highlight/guidance appears automatically.
- Current scope intentionally validates a target that becomes available because a window opens. Targets materializing dynamically inside an already-open window will require an appropriate lifecycle signal during production integration rather than polling.
- The pending-target navigation harness is now decoupled from the earlier fixed `GWTP` validation test: Next advances directly to the pending target so window-readiness can be tested independently. The generic UIA value-reading/validation capability remains in the runtime code for later API-driven authored validation rules; production validation must run only when a guide step explicitly defines one.

## Known limitations / next work
1. Name + AutomationId + ControlType can still be ambiguous; robust hierarchy/fallback identity is not implemented.
2. Guidance Previous/Next is not yet connected to GWTP.Api learner progress.
3. A separate step runtime/platform model is required in the main GWTP data/API model; existing TargetType must remain element/none.
4. Secure learner/token handoff between the browser extension and Windows runtime remains to be designed.
5. After Windows targeting is hardened, implement the first production-compatible WEB -> WINDOWS -> WEB integration slice.

## Shared target-model review — 2026-09-26
- Main GWTP Web GuideStep and the Windows runtime target identity were reviewed together before integration/schema work.
- Runtime belongs to GuideStep (`web` / `windows`) and remains separate from `TargetType=element|none`.
- Runtime-neutral fields are order/instruction/screen/target type/validation; element target descriptors are runtime-specific.
- Current Windows `ElementIdentity` provides ProcessName + AutomationId + Name + ControlType and rediscovery is already scoped to the runtime's current SessionId.
- This identity is deliberately not being promoted to the persisted production DTO yet: hierarchy/fallback identity remains the next design/hardening task.
- No temporary runtime-only DB field or CSS-selector reuse was introduced. After Windows target identity is hardened, main GWTP can add runtime + typed target persistence/API/Editor support as one coherent integration slice.

## Windows target hierarchy hardening — 2026-09-26 (pending manual verification)
- The local UIA target identity now captures one nearby meaningful ControlView ancestor in addition to ProcessName + AutomationId + Name + ControlType.
- Ancestor capture walks at most eight ControlView parents and records the first parent that is a Window or exposes AutomationId/Name; this deliberately avoids persisting a brittle full UIA path.
- Rediscovery now treats ambiguity safely: after current-session process filtering, a unique leaf match is accepted; multiple leaf matches are filtered by the captured ancestor; if the result is still ambiguous, no element is returned rather than attaching guidance to an arbitrary first candidate.
- Diagnostic logging records ambiguous candidate counts and whether ancestor identity resolved the target.
- This remains POC/runtime identity data only; nothing was copied into the main GWTP repository and no production DB/API schema was changed.
- Manual verification required before promoting this shape to a persisted Windows target DTO: verify ordinary existing targets still rediscover, then verify two controls with the same leaf identity under different meaningful parents resolve to the originally selected control.

## UI responsiveness during host shutdown — 2026-09-26 (pending manual verification)
- A new observation showed that the GWTP WPF window itself becomes unresponsive during the same host-shutdown interval in which guidance remains visible.
- Inspection identified synchronous cross-process UI Automation reads of BoundingRectangle/IsOffscreen inside `RefreshBounds()` running on the WPF Dispatcher. A target application/provider can block these UIA calls during shutdown, which would block the entire GWTP UI thread even before a definitive HIDE/DESTROY signal arrives.
- Bounds/offscreen reads now run off the WPF UI thread. Only the resulting state/event dispatch returns to the Dispatcher.
- Refresh requests are coalesced so UIA property/location/foreground events cannot create parallel bounds-read work while one cross-process read is still outstanding.
- This change does not infer host closure, add polling, or change the existing definitive HIDE/DESTROY/Process.Exited lifecycle contract. Its purpose is to keep GWTP responsive even when a UIA provider stalls.
- Manual verification required: track a Notepad element, close Notepad, immediately interact with/move the GWTP window during the previous delay interval, and observe whether the runtime stays responsive. Also verify normal move/minimize/restore tracking remains correct.

## Host-shutdown freeze diagnostic — 2026-09-26
- Manual verification after moving BoundingRectangle/IsOffscreen reads off the WPF Dispatcher showed that GWTP still freezes while Notepad is closing. Therefore synchronous bounds reads were not the sole cause.
- No second speculative behavioral fix was introduced. Targeted persistent diagnostics were added around UIA property callbacks, foreground Dispatcher work, unavailable notification, tracker disposal, and specifically Automation.RemoveAutomationPropertyChangedEventHandler.
- Next reproduction should inspect the last Begin/End pair in windows-runtime.log to identify the exact blocking boundary before changing lifecycle architecture.

## Host-shutdown freeze root cause and fix — 2026-09-26 (pending manual verification)
- Persistent diagnostics isolated the UI freeze precisely: during Notepad shutdown, `Automation.RemoveAutomationPropertyChangedEventHandler` blocked from 12:09:24.674 to 12:09:30.694 (about 6.02 seconds) while running inside tracker Dispose on the WPF UI thread.
- UIA property-handler removal now runs off the WPF Dispatcher. Tracker Dispose marks the tracker disposed first, so any late UIA callback cannot mutate active runtime state; GWTP-owned process/WinEvent resources continue to be detached synchronously and immediately.
- This targets the measured blocking boundary rather than inferring application closure or adding polling/timeouts.
- Manual verification required: close Notepad while guidance is active and verify the GWTP window remains responsive and the guidance/highlight are removed immediately when unavailable notification is received, even if the background UIA unsubscription itself still takes several seconds.

## Host-shutdown fix manually verified — 2026-09-26
- Manual verification passed after moving UIA property-handler removal off the WPF UI thread.
- Verified sequence: active target tracking -> Minimize -> Restore -> Close.
- Minimize/Restore continues to preserve and restore guidance correctly.
- Closing Notepad now removes guidance/highlight promptly and the GWTP window remains responsive throughout shutdown.
- The earlier apparent several-second host-close lifecycle limitation was therefore caused, in the reproduced case, by synchronous `Automation.RemoveAutomationPropertyChangedEventHandler` blocking the WPF Dispatcher during tracker disposal rather than by a late definitive close signal.
- The previous host-close delay should no longer be treated as an active known limitation for the verified scenario. Persistent diagnostics remain available for future provider-specific stalls.
- The POC selection flow intentionally shows guidance immediately after selecting a UIA element because selection also creates/activates an in-memory test step. This is a test-harness behavior only. In production, Editor target selection will capture/store the Windows target descriptor; guidance is rendered only by Preview or Learner execution.

## Repeatable Windows target ambiguity harness — 2026-09-26
- Added an explicit POC-only Ambiguity Test window reachable from the main runtime window.
- The harness contains two visible `Continue` buttons with the same leaf Name, AutomationId (`SharedContinue`) and ControlType, under distinct parent groups (`GroupA` and `GroupB`).
- This creates a deterministic ambiguity case for validating ancestor-based target rediscovery without depending on the UIA structure of an external application.
- Test procedure: open Ambiguity Test, select Group A Continue, verify rediscovery/highlight remains on Group A; repeat for Group B. Diagnostics should record `FindElement.Ambiguous count=2` followed by `FindElement.ResolvedByAncestor`.
- The harness is test-only and is not part of the future Editor/Learner production flow.

## Ambiguity harness isolated into external process — 2026-09-26 (pending manual verification)
- Manual testing showed target rediscovery selected the expected ambiguous control, but guidance did not track correctly when the test window lived inside the GWTP runtime process.
- That setup was invalid for end-to-end tracking because production targets are external applications and the runtime WinEvent hooks intentionally use WINEVENT_SKIPOWNPROCESS.
- Removed the in-process AmbiguityTestWindow and added a separate WPF AmbiguityTestHost executable/project containing the same two duplicate Continue controls under GroupA/GroupB.
- The POC's Open Ambiguity Test action now launches that host as a separate process. Runtime targeting/tracking logic was not changed to accommodate the test.
- Manual verification required: select each Continue control independently, verify ancestor-based rediscovery returns the selected group, then move/minimize/restore/close the external test host and verify guidance/highlight tracking behaves like an ordinary external Windows application.

## Ambiguity runtime follow-up — 2026-09-26 (pending manual verification)
- External ambiguity harness successfully rediscovered the selected duplicate target, confirming the ancestor discriminator works in the exercised case.
- Manual test exposed two runtime issues: Prev/Next between already-present targets takes roughly half a second, and minimizing the target host can briefly move guidance to the upper-left corner.
- Minimize race hardened: the background bounds read now checks the host HWND visibility/minimized state before publishing UIA bounds and re-checks minimized state after the UIA read. Minimized/hidden hosts produce the temporary-hidden path instead of a BoundsChanged event.
- Step-transition diagnostics now measure total FindElement/root FindAll/ancestor-resolution timing and log StepTransition Begin/End. No artificial delay or polling was added.
- Manual verification required: confirm minimize no longer flashes guidance at the upper-left corner; exercise repeated Prev/Next and inspect windows-runtime.log elapsedMs values before optimizing lookup.

## Process-scoped Windows target lookup — 2026-09-26 (pending manual verification)
- Measured Prev/Next latency was isolated to the desktop-wide UIA lookup: RootElement.FindAll(TreeScope.Descendants) consumed about 482-583 ms per transition in the ambiguity harness, while ancestor disambiguation and tracker disposal were only a few milliseconds.
- Replaced the desktop-wide descendant scan with a two-stage lookup: enumerate only desktop top-level Window elements, keep windows whose ProcessId belongs to the authored ProcessName in the current Windows session, then search descendants only inside those process windows.
- Existing leaf identity matching and ancestor ambiguity resolution remain unchanged semantically. No cache, polling, sleep, or fixed delay was introduced.
- Added timing diagnostics for top-level process-window enumeration, per-window descendant lookup, unique resolution, and ancestor resolution.
- Manual verification required: repeat Prev/Next in the external ambiguity host, confirm Group A/Group B still resolve correctly, and compare elapsedMs values with the previous ~0.5 s desktop-wide baseline.

## Process-scoped lookup manually verified — 2026-09-26
- Manual verification passed after replacing the desktop-wide UIA descendant scan with process-window-scoped lookup.
- Prev/Next transitions in the external ambiguity harness are now responsive.
- Group A/Group B duplicate targets continue to resolve correctly through ancestor disambiguation.
- The minimize/restore overlay behavior is also verified as correct in the same test cycle.
- This closes the measured ~0.5 s transition-latency issue and the minimize upper-left overlay flash observed during ambiguity testing.

## Windows UIA target laboratory — 2026-09-26 (pending manual matrix)
- Expanded the former ambiguity-only external host into a deterministic Windows UIA Test Host for hardening the persisted Windows Target Descriptor before main DB/API integration.
- Current scenarios: T01 duplicate leaf identity under distinct parents; T02 TextBox with AutomationId; T03 TextBox without authored AutomationId; T04 Button without authored AutomationId; T05 stable AutomationId with runtime-changing Name; T06 deep container hierarchy; T07 dynamically appearing/disappearing target; T08 second top-level window in the same process.
- T01 remains the repeatable ancestor-disambiguation case already manually verified.
- T05 intentionally tests whether persisted Name should be mandatory matching data when a stable AutomationId exists.
- T07 exercises event-driven unavailable/pending-target behavior without polling.
- T08 exercises process-scoped lookup when one process owns multiple top-level windows.
- The host is test infrastructure only; no new production descriptor fields were added and the main GWTP DB/API remain unchanged.
- Next: execute the matrix, record which current identity fields are stable/insufficient, then freeze the first production Windows Target Descriptor contract from evidence.

## Windows GUI sanity foundation — 2026-09-26 (pending first local run)
- Added WindowsRuntime.GuiTests as a separate executable GUI/E2E test project plus root run-sanity.ps1.
- Test policy mirrors the Web suite: behavioral verification must operate through the public GUI/UI Automation surface of the real executables, not by directly invoking MainWindow.FindElement, ElementTrackingService, or other runtime internals.
- run-sanity.ps1 builds the runtime, external UIA Test Host, and GUI runner, then executes the GUI suite.
- Initial automated coverage opens the external Test Host by invoking the real runtime GUI button and exercises T02-T08 through UI Automation: controls with/without authored AutomationId, dynamic Name mutation, deep hierarchy, dynamic visibility, and a second top-level window in the same process.
- T01 end-to-end picker/ancestor rediscovery and overlay lifecycle tests remain to be automated through the GUI; no test-only runtime API was added to bypass the picker.
- The test runner uses bounded waits only as test safety synchronization around observable GUI state; production runtime remains event-driven with no polling.

## Windows GUI overlay/lifecycle regression — 2026-09-26 (pending first run)
- Extended WindowsRuntime.GuiTests beyond UIA exposure checks into real runtime GUI behavior.
- T01 now drives the real Select Element button, moves the physical cursor to each duplicate target, performs a real left-click, and verifies the runtime-created highlight/guidance windows against the selected target geometry.
- T01 then invokes Previous/Next through the visible GuidanceWindow controls and verifies rediscovery returns to the correct duplicate target under its authored ancestor rather than the other identical leaf.
- Added GUI lifecycle checks for host-window movement (overlay follows target), Minimize (overlays disappear), Restore (overlays reattach), unrelated foreground activation (guidance hides), tracked-host foreground return (guidance returns), and tracked-host close (overlays disappear and the runtime remains responsive enough to reopen the Test Host).
- GuidanceWindow and HighlightWindow now expose stable window Titles for accessibility/UI Automation discovery; this does not add a test-only runtime API or bypass runtime behavior.
- The suite still treats observable GUI state as the assertion surface. Win32 is used only to reproduce real desktop actions such as physical mouse click, move, minimize/restore, foreground activation, and close.
- First local execution of the expanded suite is required before this becomes the new green baseline.
