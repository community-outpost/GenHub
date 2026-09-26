# PostHog Product Analytics Dashboard Guide

This document describes the PostHog product analytics dashboards configured for GenHub telemetry, including dashboard layouts, HogQL queries, funnel definitions, breakdown attributes, and metric interpretations.

A machine-readable configuration template is located at [`docs/dev/posthog-dashboard-config.json`](./posthog-dashboard-config.json).

---

## 1. Dashboard 1: Executive Overview & Engagement

**Target Audience**: Product leads, core maintainers, release engineers.

### Tiles & Metrics

1. **Daily Active Installations (DAU)**
   - **Type**: Trends (`ActionsLineGraph`)
   - **Event**: `game_session_started`
   - **Math**: Unique anonymous installation IDs (`dau`)
   - **Purpose**: Tracks real daily active usage while preserving strict anonymity.

2. **Total Game Sessions Started**
   - **Type**: Single Metric / Bold Number
   - **Event**: `game_session_started`
   - **Math**: Total count (`total`)
   - **Window**: 30 days.

3. **Profile Launches: Launcher UI vs Shortcut / Pinned**
   - **Type**: Bar Chart (`ActionsBar`)
   - **Event**: `profile_launched`
   - **Breakdown**: `launch_source` (`launcher` vs `shortcut`)
   - **Purpose**: Evaluates how many players launch games directly from the GenHub client interface versus launching from desktop shortcuts and external links.

4. **Profiles Pinned to Desktop**
   - **Type**: Bold Number / Cumulative Trend
   - **Event**: `profile_pinned`
   - **Breakdown**: `shortcut_type` (`desktop`)
   - **Purpose**: Measures user commitment and convenience adoption.

5. **App Updates Served Funnel**
   - **Type**: Funnel Visualization (`FunnelViz`)
   - **Steps**:
     1. `app_update_checked`: Client contacts Velopack update server.
     2. `app_update_downloaded`: Client finishes acquiring the delta or full release package.
     3. `app_update_applied`: Client applies update and restarts into the new version.
   - **Purpose**: Identifies update drop-off and conversion rates across versions.

6. **Publisher Content Downloads Completed**
   - **Type**: Bold Number
   - **Event**: `content_download_completed`
   - **Math**: Total count
   - **Purpose**: Monitors content consumption across the publisher network.

7. **UploadThing Files Uploaded**
   - **Type**: Trends (Comparative Line / Bar)
   - **Events**: `uploadthing_upload_completed` vs `uploadthing_upload_failed`
   - **Purpose**: Tracks upload volume and gateway error rates.

---

## 2. Dashboard 2: Game Launch & Session Dynamics

**Target Audience**: Gameplay compatibility engineers, Linux/Wine maintainers.

### HogQL & Trends Queries

> **Note on Reporting Window**: By default, dashboard tiles apply a trailing 30-day window (`timestamp >= now() - INTERVAL 30 DAY`). The standalone HogQL snippets below can be run with or without the 30-day window constraint depending on whether cohort lifetime totals or trailing 30-day metrics are desired.

#### Game Sessions by Game Type
```sql
SELECT
    properties.game_type AS game,
    count() AS total_sessions
FROM events
WHERE event = 'game_session_started'
GROUP BY game
ORDER BY total_sessions DESC
```

#### Runner Environments (Proton / Wine / Native / Crossover)
```sql
SELECT
    properties.runner_environment AS runner,
    count() AS sessions
FROM events
WHERE event = 'game_session_started'
GROUP BY runner
```

#### Session Duration Distribution
```sql
SELECT
    round(avg(toFloat64OrNull(properties.duration_seconds)) / 60, 1) AS avg_duration_minutes,
    median(toFloat64OrNull(properties.duration_seconds)) / 60 AS median_duration_minutes
FROM events
WHERE event = 'game_session_ended'
```

#### Exit Gracefulness Rate
```sql
SELECT
    properties.was_graceful AS graceful,
    count() AS count
FROM events
WHERE event = 'game_session_ended'
GROUP BY graceful
```

---

## 3. Dashboard 3: Profile Sharing & Social Ecosystem

**Target Audience**: Community managers, UX designers.

### Tiles & Metrics

1. **Profile Sharing Volume by Format**
   - **Type**: Pie Chart
   - **Event**: `profile_shared`
   - **Breakdown**: `share_format` (`uri`, `file`, `json`)
   - **Insight**: Identifies whether players prefer 1-click shareable URIs or portable `.ghprofile` files.

2. **Profile Import Success Rate**
   - **Type**: Bar Chart
   - **Event**: `profile_imported`
   - **Breakdown**: `success` (`true` vs `false`)
   - **Filter**: When `success = false`, group by `error_message` to detect missing installation errors, corrupt packages, or signature mismatches.

3. **Shortcut Adoption (Pinned vs Launched from Shortcut)**
   - **Type**: Trends Graph
   - **Events**: `profile_pinned` vs `profile_launched_from_shortcut`
   - **Insight**: Confirms that players who pin shortcuts actively use them to launch games.

---

## 4. Dashboard 4: Content Distribution & Publishers

**Target Audience**: Content team, infrastructure and bandwidth planners.

### HogQL & Trends Queries

#### Top Content Downloads by Publisher
```sql
SELECT
    properties.publisher_id AS publisher,
    count() AS total_downloads,
    round(sum(toFloat64OrNull(properties.size_mb)) / 1024, 2) AS total_gigabytes
FROM events
WHERE event = 'content_download_completed'
GROUP BY publisher
ORDER BY total_downloads DESC
```

#### Download Speeds by Source Provider
```sql
SELECT
    properties.source_provider AS provider,
    round(avg(toFloat64OrNull(properties.speed_mbps)), 2) AS avg_mbps,
    round(quantile(0.95)(toFloat64OrNull(properties.speed_mbps)), 2) AS p95_mbps
FROM events
WHERE event = 'content_download_completed'
GROUP BY provider
```

#### Content Updates Applied by Publisher & Version
```sql
SELECT
    properties.publisher_id AS publisher,
    properties.to_version AS version,
    properties.strategy AS strategy,
    count() AS total_updates,
    sum(toInt32OrNull(properties.profiles_updated)) AS total_profiles_updated
FROM events
WHERE event = 'content_update_applied'
GROUP BY publisher, version, strategy
ORDER BY total_updates DESC
```

#### Specific Content & Mod Downloads Clean Tracking
```sql
SELECT
    properties.publisher_id AS publisher,
    properties.content_name AS content,
    properties.file_name AS file,
    count() AS download_count,
    round(sum(toFloat64OrNull(properties.size_mb)) / 1024, 2) AS total_gb
FROM events
WHERE event = 'content_download_completed'
GROUP BY publisher, content, file
ORDER BY download_count DESC
LIMIT 50
```

#### Download Failures & Errors
```sql
SELECT
    properties.publisher_id AS publisher,
    properties.content_name AS content,
    properties.error_message AS error,
    count() AS failure_count
FROM events
WHERE event = 'content_download_failed'
GROUP BY publisher, content, error
ORDER BY failure_count DESC
```

---

## 5. Dashboard 5: GenPatcher Fixes & ModBuilder Usage

**Target Audience**: Modders, tools team, QA.

### Tiles & Metrics

1. **GenPatcher Fixes Applied by Fix Name**
   - **Event**: `genpatcher_fix_applied`
   - **Breakdown**: `fix_name`
   - **Display**: Table showing total attempts and success rates.

2. **GenPatcher Crucial Fix Reliability**
   - **Event**: `genpatcher_fix_applied`
   - **Filter**: `properties.is_crucial = true`
   - **Metric**: Success percentage. Immediate Sentry alert if failure rate exceeds 1%.

3. **ModBuilder Projects Created by Content Type**
   - **Event**: `modbuilder_project_created`
   - **Breakdown**: `content_type` (`Mod`, `Patch`, `Map`, `Addon`, etc.)

4. **ModBuilder Mod Builds**
   - **Event**: `modbuilder_mod_built`
   - **Breakdown**: `success`
   - **Secondary Metric**: Average `duration_seconds` grouped by `build_steps`.

---

## 6. How to Import and Deploy

1. In your PostHog instance (e.g. `https://us.posthog.com` or self-hosted), navigate to **Dashboards > New Dashboard**.
2. Either create the dashboard manually using the queries above, or use the PostHog REST API to batch-import [`docs/dev/posthog-dashboard-config.json`](./posthog-dashboard-config.json):
   ```bash
   curl -X POST "https://us.posthog.com/api/projects/<project_id>/dashboards/" \
     -H "Authorization: Bearer <personal_api_key>" \
     -H "Content-Type: application/json" \
     -d @docs/dev/posthog-dashboard-config.json
   ```
3. Set alert thresholds for:
   - `uploadthing_upload_failed` spike (>5% in 15 minutes)
   - `genpatcher_fix_applied` failure on crucial fixes
   - `app_update_downloaded` -> `app_update_applied` conversion dropping below 80%.
