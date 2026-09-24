# Telemetry & Product Analytics Architecture

GenHub features an opt-in, privacy-preserving telemetry and error-reporting architecture built on top of **Sentry** (for crash diagnostics and exception traces) and **PostHog** (for privacy-respecting product analytics and event telemetry).

---

## 1. Privacy & Consent Model

Telemetry strictly honors user choice and local regulations:

1. **Explicit Preference**: Users configure their preference in **Settings > Telemetry & Diagnostics**.
   - `Disabled (0)`: Completely disables all telemetry and error tracking. No network requests are made.
   - `AnonymousErrors (1)`: Sends anonymized crash reports and exceptions via Sentry.
   - `AnonymousMetrics (2)`: Sends anonymous operational and product usage events via PostHog in addition to anonymous error reports.
2. **Anonymous Identification**: Users are identified solely by a randomly generated installation GUID (`AnonymousInstallationId`). No IP addresses, usernames, personal directory paths, or hardware serials are ever transmitted.
3. **Data Sanitization**: All event payloads, stack traces, and error messages pass through [`TelemetrySanitizer`](file:///home/ubuntu/workspaces/cc1-GenHub/GenHub/GenHub.Core/Utilities/TelemetrySanitizer.cs) prior to dispatching:
   - Scrubbing Windows user profiles (`C:\Users\<user>\...` -> `C:\Users\***\...`) and Unix home paths (`/home/<user>/...` -> `/home/***/...`).
   - Redacting API keys, bearer tokens, passwords, and sensitive query strings.
   - Normalizing file system paths into canonical formats.

---

## 2. Tracked Product Analytics Events

| Event Name | Constant | Emitted When | Key Properties |
|---|---|---|---|
| `profile_launched` | `Events.ProfileLaunched` | A game profile is launched | `profile_id`, `profile_name`, `game_type`, `launch_source` ("launcher" \| "shortcut") |
| `profile_launched_from_shortcut` | `Events.ProfileLaunchedFromShortcut` | A game profile is launched via OS shortcut or IPC URI | `profile_id` |
| `profile_pinned` | `Events.ProfilePinned` | A game profile is pinned to desktop or launcher shortcuts | `profile_id`, `profile_name`, `game_type`, `shortcut_type` ("desktop") |
| `profile_shared` | `Events.ProfileShared` | A profile is exported/shared to URI, JSON, or `.ghprofile` file | `profile_id`, `share_format` ("uri" \| "file" \| "json"), `file_size_bytes` |
| `profile_imported` | `Events.ProfileImported` | A shared profile package is imported | `profile_id`, `profile_name`, `game_type`, `success`, `file_count`, `error_message` |
| `game_session_started` | `Events.GameSessionStarted` | Game executable process starts | `game_type`, `runner_environment`, `installation_type`, `is_custom_runner`, `is_direct_play` |
| `game_session_ended` | `Events.GameSessionEnded` | Game executable process exits | `game_type`, `runner_environment`, `session_duration_seconds`, `exit_code`, `was_graceful` |
| `game_session_heartbeat` | `Events.GameSessionHeartbeat` | Periodic alive signal while in-game (5 min) | `game_type`, `session_duration_seconds` |
| `app_update_checked` | `Events.AppUpdateChecked` | Velopack checks for application updates | `current_version`, `channel` |
| `app_update_downloaded` | `Events.AppUpdateDownloaded` | Velopack finishes downloading an update package | `from_version`, `to_version` |
| `app_update_applied` | `Events.AppUpdateApplied` | Application update is applied and app restarts | `from_version`, `to_version` |
| `content_download_completed` | `Events.ContentDownloadCompleted` | Content download finishes | `content_id`, `content_name`, `publisher_id`, `content_type`, `size_mb`, `speed_mbps`, `duration_seconds` |
| `uploadthing_upload_completed` | `Events.UploadThingUploadCompleted` | User upload to UploadThing gateway succeeds | `file_name`, `size_mb`, `file_size_bytes`, `duration_seconds` |
| `uploadthing_upload_failed` | `Events.UploadThingUploadFailed` | User upload to UploadThing gateway fails | `file_name`, `size_mb`, `duration_seconds`, `error_message` |
| `genpatcher_fix_applied` | `Events.GenPatcherFixApplied` | A GenPatcher compatibility or registry fix is executed | `fix_id`, `fix_name`, `game_type`, `is_crucial`, `success`, `error_message` |
| `modbuilder_project_created` | `Events.ModProjectCreated` | A new ModBuilder project is initialized | `project_name`, `content_type` |
| `modbuilder_mod_built` | `Events.ModBuilt` | A ModBuilder build pipeline finishes | `project_name`, `build_steps`, `success`, `file_count`, `duration_seconds`, `error_message` |

---

## 3. Architecture & Sinks

The telemetry pipeline coordinates through the [`ITelemetryService`](file:///home/ubuntu/workspaces/cc1-GenHub/GenHub/GenHub.Core/Interfaces/Telemetry/ITelemetryService.cs) interface:

- **[`TelemetryService`](file:///home/ubuntu/workspaces/cc1-GenHub/GenHub/GenHub/Features/Telemetry/Services/TelemetryService.cs)**:
  - Validates user consent from `IUserSettingsService`.
  - Enriches events with OS platform, architecture, app version, and anonymous installation GUID.
  - Sanitizes properties via `TelemetrySanitizer`.
  - Dispatches concurrently to registered sinks.
- **[`PostHogTelemetrySink`](file:///home/ubuntu/workspaces/cc1-GenHub/GenHub/GenHub/Features/Telemetry/Sinks/PostHogTelemetrySink.cs)**:
  - Batches events and flushes over HTTP to PostHog EU or Cloud endpoint.
  - Formats payloads in standard PostHog batch event format.
- **[`SentryTelemetrySink`](file:///home/ubuntu/workspaces/cc1-GenHub/GenHub/GenHub/Features/Telemetry/Sinks/SentryTelemetrySink.cs)**:
  - Captures unhandled exceptions and error-level logs.
  - Attaches breadcrumbs of recent user actions.
- **[`LoggingTelemetrySink`](file:///home/ubuntu/workspaces/cc1-GenHub/GenHub/GenHub/Features/Telemetry/Sinks/LoggingTelemetrySink.cs)**:
  - Outputs telemetry debug logs in development builds when configured.
