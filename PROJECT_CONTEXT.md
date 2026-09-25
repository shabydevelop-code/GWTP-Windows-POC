# GWTP Windows Runtime — Project Context

## Purpose
This repository contains the Windows desktop runtime for Generic Workplace Training Platform (GWTP). Development in this repository must be treated as production development, not disposable proof-of-concept code. The main GWTP repository remains responsible for the API, database, guide authoring, learner identity, and progress.

## Product naming
- GWTP = **Generic Workplace Training Platform**.
- The former expansion **Generic Web Training Platform** is retired because Web is now one supported runtime alongside Windows.
- Existing technical identifiers using the `GWTP` acronym remain unchanged.
- User-facing component names may omit the word `Generic`; for example, the browser extension is displayed as **GWTP - Workplace Training Platform**.

## Technology
- .NET 8
- WPF
- Windows UI Automation (UIA)
- Win32 window/event APIs

## Architecture direction
The production Windows runtime must remain application-generic. An editor selects a visual UIA element and GWTP derives a reusable target descriptor rather than hard-coding application-specific behavior.

A Windows target is expected to include application/process identity plus UIA identity such as AutomationId, Name, ControlType, and hierarchy/fallback data where needed.

For RDS/multi-user deployment, UIA and overlays must run once per interactive Windows session. The central GWTP.Api can remain a Windows Service. Process discovery must eventually be scoped to the current Windows SessionId.

## Hybrid Web + Windows model
A single guide may alternate between Web and Windows steps. Runtime/platform therefore belongs at step level. The existing GWTP TargetType meaning (element / none) must not be repurposed as Web / Windows; a separate runtime/platform field is required.

The API remains the source of truth for learner progress. The browser extension renders Web steps and the Windows runtime renders Windows steps. Switching runtime must be automatic and must not require a second learner login.

## Deployment / learner-controller model
- The browser Extension remains the learner controller for all guide types: Web-only, Windows-only, and Hybrid. It owns learner-facing guide selection/start/resume/navigation rather than duplicating a second learner UI inside the Windows Runtime.
- Web-only stations require the Extension but do not require the Windows Runtime to be installed or running.
- Windows-only and Hybrid stations require both the Extension and the Windows Runtime.
- The Windows Runtime is an optional capability component, not a prerequisite for existing Web-only GWTP operation.
- On stations that need Windows training, the Windows Runtime may start with the Windows/RDS session or be started manually and remain running. When no Windows target is active it should have no element tracking or guidance overlays; no separate launcher process or special polling-based idle subsystem is required.
- A Windows-only guide may define guide-level application detection and launch configuration so the runtime can reuse an existing application instance or launch it when absent. Launch configuration must be generic enough for EXE, BAT/script with environment arguments, shortcut, or URI/launcher scenarios; application detection is separate from the launch mechanism.
- Hybrid guides must not relaunch a Windows application when the preceding Web workflow already opened an appropriate instance in the same Windows session.

## Runtime principles
- Prefer event/state-driven tracking and readiness.
- Do not use arbitrary delays or aggressive polling as the mechanism that makes runtime behavior work.
- Temporary invisibility (for example minimizing a host window) is not the same as element destruction.
- Closing the target application or losing the UIA element permanently should stop its overlay/tracking.
- Minimize/offscreen should hide guidance while retaining tracking so Restore can show it again.

## Production-development rule
- Every implementation decision must assume the code is intended to reach production.
- Do not introduce temporary POC shortcuts, application-specific hard-coding, throwaway architecture, or known scalability/security/reliability debt merely to demonstrate a scenario.
- Changes must be generic, maintainable, resource-conscious, RDS/multi-session aware, and compatible with the intended Web + Windows architecture.
- A narrowly scoped incremental implementation is acceptable, but its design must be production-compatible and must not require replacing the architecture later.
- When a production requirement is not implemented yet, record it explicitly as a known limitation rather than hiding it behind a POC assumption.

## Immediate integration goal
Implement WEB -> WINDOWS -> WEB using one guide and one GWTP learner/progress state. Before integration, harden Windows tracking, add SessionId-safe process targeting, and define persistence/DTOs for generic Windows targets.

## Repository workflow
GitHub main is the source of truth. Inspect current code and these context/status files before substantial changes. Functional or architectural changes must update CURRENT_STATUS.md in the same change set.
