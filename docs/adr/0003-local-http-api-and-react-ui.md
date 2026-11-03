# 0003: Local HTTP API and React UI instead of a native UI

- **Status:** Accepted
- **Date:** 2026-11-02

## Context

The tracker itself only needs a tray icon. The part people look at is the report: day
summary, timeline, drill-down by window title, week overview. That UI is mostly charts and
tables.

Options considered:

- **Native UI** in WinForms, WPF or WinUI inside the tray app.
- **Embedded web view** (WebView2) hosting HTML.
- **Local HTTP API plus a React app** opened in the user's browser.

## Decision

Expose a **local HTTP API** from the app (ASP.NET Core on `127.0.0.1`) and build the UI as a
**React + TypeScript** single-page app with Vite. In production the app serves the built
frontend itself, so it is still one deliverable (#30).

- Charting and layout are far easier in the web ecosystem than in any of the native toolkits.
- The UI can be developed and tested on any OS with hot reload and Vitest + MSW, without the
  Windows host running.
- The API is a clean boundary: it is the only way the UI reads data, it has an OpenAPI
  description (#27), and other tools (scripts, a future CLI) can use it too.
- The tray app stays small: tracking, storage, and serving files and JSON.

## Consequences

- A local HTTP server is an attack surface. It listens on loopback only, validates the `Host`
  header (against DNS rebinding) and requires an access token (#29).
- Two toolchains (.NET and Node) to install and keep up to date. CI runs both; Dependabot
  updates both.
- The UI opens in a browser tab rather than a native window. If that turns out to matter, a
  WebView2 shell can host the same frontend without changing the API.
- The port has to be chosen and discovered; the dev proxy defaults to 5381 and can be
  overridden with `TIMEGLASS_API_PORT`.
