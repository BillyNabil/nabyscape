# Russian localization implementation plan

> **For agentic workers:** Use superpowers:subagent-driven-development to implement the scoped translation tasks and independently review the result.

**Goal:** Deliver a complete Russian application interface so the user can understand the available features.
**Architecture:** Retain English internal IDs and existing localization API. Add an embedded Russian string catalog split by UI ownership; localize only application-owned captions, never user data. Preserve existing languages and profile data.
**Tech Stack:** C#, WPF, .NET 10, JSON embedded resources, existing self-tests and isolated UI diagnostics.
**Spec:** docs/superpowers/specs/2026-11-09-russian-localization.md (approved in chat).

## Global constraints
- First translation, then a separate screen-overlap task.
- Keep original licenses and user content. Never translate stored enum values, provider IDs, filenames or API data.
- Use Russian by default for this build and preserve explicit language choices.
- Keep fork updates from overwriting this build.
- Build and inspect an isolated test profile before delivery.

## Review focus
- English strings bypassing localization: audit UI calls, dynamic status and rendered previews.
- Translated ComboBox labels breaking stored values: retain English values and translate presentation only.
- Format placeholders and multiline strings: validate all format placeholders before build.
- Russian labels clipping: inspect settings and compact widgets at normal and enlarged scale.
- Existing preferences and upstream updater: test language persistence and isolate diagnostic profiles.

## Tasks
1. Core/catalog: add failing self-tests for `ru`, placeholder preservation and user-data-safe caption APIs; run red; implement Russian catalog loader, language support and defaults. Translate all existing catalog keys. Files: Localization*, LanguageAccessibilitySettings, SettingsLanguageAccessibility, LocalizationTests, SelfTests. Build and run tests green.
2. Settings/onboarding: audit UI/Settings plus first-run flows and settings previews; convert application captions to explicit localization, preserve option IDs, add Russian.Settings.json. Check Russian search and layout.
3. Widgets/application: audit UI/Island and Features except Assistant, application notifications/tray, installer/uninstaller. Add Russian.Features.json and explicit caption localization. Preserve all user text and IDs.
4. Assistant: audit Features/Assistant; localize owned captions, errors, statuses and voice settings; add Russian.Assistant.json. Keep provider/model IDs and assistant replies intact.
5. Integration: inspect diffs and missing strings; publish self-contained Windows x64 build, run full self-tests and settings scene checks, capture isolated Russian screens and review them. Independent final review, fix material findings, package portable build and source patch in outputs. Record GitHub fork/release/install status accurately.

## Execution decisions
The user approved the design and explicitly asked to proceed. Execute continuously without another plan approval. A fresh local clone and feature branch isolate changes from the installed app. Scoped parallel workers own separate source directories and JSON catalogs; root owns core integration and verification.

## Progress
- [x] Approved design, source audit, clean feature branch.
- [x] Core and Russian base catalog.
- [x] Settings/onboarding.
- [x] Widgets/application.
- [x] Assistant.
- [x] Build, UI inspection, independent review, delivery.
