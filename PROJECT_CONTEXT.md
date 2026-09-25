# GWTP Windows POC — Project Context

## Purpose
This repository is the Windows desktop proof of concept for Generic Web Training Platform (GWTP). It validates UI Automation element selection, highlighting, guidance overlays, and runtime tracking for Windows applications. The main GWTP repository remains responsible for the API, database, guide authoring, learner identity, and progress.

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

## Runtime principles
- Prefer event/state-driven tracking and readiness.
- Do not use arbitrary delays or aggressive polling as the mechanism that makes runtime behavior work.
- Temporary invisibility (for example minimizing a host window) is not the same as element destruction.
- Closing the target application or losing the UIA element permanently should stop its overlay/tracking.
- Minimize/offscreen should hide guidance while retaining tracking so Restore can show it again.

## Immediate POC goal
Validate WEB -> WINDOWS -> WEB using one guide and one GWTP learner/progress state. Before integration, harden Windows tracking, add SessionId-safe process targeting, and define persistence/DTOs for generic Windows targets.

## Repository workflow
GitHub main is the source of truth. Inspect current code and these context/status files before substantial changes. Functional or architectural changes must update CURRENT_STATUS.md in the same change set.
