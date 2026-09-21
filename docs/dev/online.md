# Online (virtual LAN)

The Online tab lets players browse lobbies, create lobbies (with an optional
password and an optional game profile), join them, and share one virtual LAN
so Generals / Zero Hour LAN lobbies work without manual port forwarding. It
replaces the RadminVPN/Hamachi + GameRanger combination with one integrated
flow. Anyone can join any lobby; profile setup only controls match badges,
never access.

## Architecture

Three layers, each independently testable:

1. **Edge control plane** (`gateway-online/`, Cloudflare Worker + Durable Objects).
   Trust and control only: anonymous sessions, public directory, join grants,
   presence fan-out. It never relays game traffic.
2. **Client transport** (`GenHub.Core` interfaces + `GenHub/Features/Online/`
   services + per-OS adapter bring-up). Join/leave, adapter lifecycle, presence
   channel, launch integration. All fallible operations return
   `OperationResult<T>`; all long-running work takes a `CancellationToken`.
3. **Overlay data plane** (selected by the Phase 0 spike: Nebula, ZeroTier, or
   NetBird). Runs as a sidecar process managed by `OverlaySidecarHost`; the edge
   reports `overlay: "pending-selection"` until the spike lands.

## Feature flag

The Online tab is enabled by default so downloaded builds can connect out of
the box. Set `GENHUB_ONLINE_ENABLED=0` (or `false`, any casing) to hide the
tab: the nav button disappears, a persisted Online selection falls back to
Game Profiles, `OnlineViewModel` is never initialized, and directory refreshes
short-circuit before any HTTP call, so the launch path is byte-identical. The
services stay registered for DI but receive no calls while disabled. Until the
edge is deployed, the tab shows its failed state with a retry action instead
of failing silently.

## Environment overrides

| Variable | Purpose |
|----------|---------|
| `GENHUB_ONLINE_ENABLED` | `0`/`false` disables the tab (default: enabled) |
| `GENHUB_ONLINE_EDGE_URL` | Edge base URL (local dev: `http://127.0.0.1:8787`) |
| `GENHUB_ONLINE_STUN_HOST` | STUN hostname (default `stun.cloudflare.com`) |
| `GENHUB_OVERLAY_BIN` | Overlay sidecar binary override (spike + power users) |

## Going live (maintainer checklist)

1. `cd gateway-online && npm run deploy`
2. Provision secrets (one command per secret):
   `wrangler secret put JWT_SIGNING_SECRET`
   `wrangler secret put PASSWORD_PEPPER`
   `wrangler secret put COTURN_SECRET`
3. Verify the public hostname matches `ApiConstants.DefaultOnlineEdgeBaseUrl`;
   update the constant if the `workers.dev` subdomain differs.
4. Point a coturn instance at `COTURN_SECRET` (`static-auth-secret`) and set
   `TURN_URIS` in `wrangler.jsonc`.

## Join flow

1. `POST /v1/sessions/anonymous` mints a short-lived session (no user database).
2. `GET /v1/networks` returns metadata only: no member lists, no endpoints.
   Non-members can never learn underlay IPs from the directory.
3. `POST /v1/networks/{id}/join` checks the password (PBKDF2 verifier + pepper,
   when the lobby has one), slots, and bans, then returns a grant, an overlay
   IP, an opaque adapter config, the initial roster, and the expected profile
   block. Joiners advertise their own profile fingerprint with the join.
4. The client brings the platform adapter up (skipped while the edge reports
   `overlay: "pending-selection"`; the lobby stays usable without tunneling),
   opens the grant-scoped presence socket
   (`/v1/networks/{id}/presence?ticket=`), and shows live roster updates.
   Reconnect uses exponential backoff; eviction/ban closes the socket (code
   4001) and the client auto-leaves with a toast. Credentials renew
   transparently: sessions retry once after a 401 and grants re-mint via
   `/cert` ahead of expiry, so long sessions never decay into ghost joins.
5. Play launches the locally matched profile via `IProfileLauncherFacade`
   (existing reconciliation) after preselecting the member overlay IP in
   `options.ini` (`GameSpyIPAddress`), so no manual adapter choice is needed.
   With no usable local profile, a warning shows instead of failing silently.

## Profile matching

Profile ids are machine-local, so lobbies match on the fingerprint
(`OnlineProfileMatcher`): the game client key plus a hash of the sorted
gameplay content ids (mods, patches).
Cosmetics (UI addons, skins, maps, media) never affect the match, mirroring
the exe/ini inputs that decide whether two setups can share a game. The host
picks a profile from their own library; joiners auto-match the closest local
profile (exact, then same-client overlap) and advertise it on join and on
every presence heartbeat, so the roster shows per-member match badges. When
the host switches profile, the room broadcasts `profile-changed` and every
member gets a toast plus a re-match.

## Privacy model

Everyone sees servers; only joined members exchange packets. Relay is always
on: members publish no public endpoint and no STUN traffic runs, so the real
IP address is never shared with lobby members. The edge drops any endpoint
sent by a relay member on create, join, and heartbeat. Logs, toasts, and
diagnostics scrub IPs via `OnlineLogScrubber`; toasts show overlay IPs or
relay state only.

## Lifecycle

A network with zero members gets a 5-minute grace window (`EMPTY_NETWORK_TTL_SECONDS`)
so accidental leaves can rejoin; rejoining revives the room and the first
member back becomes host. After the window the room deletes itself and drops
its directory entry. Idle members are evicted after the presence timeout, so
abandoned lobbies drain and then expire on their own.

## Abuse

Members can be reported (`POST .../report`, host is notified over presence);
hosts can ban (`POST .../ban`), which adds a deny entry, evicts the slot, and
closes the member socket. Join attempts are rate-limited per network + IP,
directory reads per IP per minute, creation per IP per hour.

Bans bind to both the session identity and the last known client IP, so a
banned player who mints a fresh session from the same address stays banned.
Sessions are still anonymous, so a determined player on a new address can
return under a new identity. Treat edge bans as strong friction, not identity
enforcement, until an optional account layer exists.

## Password policy

Passwords are optional everywhere: public lobbies may run open, and private
lobbies stay unlisted. A password that is set must be at least 4 characters.
Display strings are control-character sanitized; JSON bodies are capped at
8 KiB.

## Local development

```bash
cd gateway-online
npm install
npm run types
npm test
npx wrangler dev --port 8787
```

```bash
GENHUB_ONLINE_ENABLED=1 GENHUB_ONLINE_EDGE_URL=http://127.0.0.1:8787 \
  dotnet run --project GenHub/GenHub.Linux
```

## What is still gated

- **OQ1/D3**: the overlay spike (two machines, real NATs, packet capture) picks
  the sidecar. Until then the edge reports `overlay: "pending-selection"`,
  joins succeed lobby-only (roster + presence, no tunneling), and the UI says
  so instead of warning.
- **OQ4**: production hosting + TURN/relay funding and secret provisioning
  (one `wrangler secret put [KEY]` per secret: `JWT_SIGNING_SECRET`,
  `PASSWORD_PEPPER`, `COTURN_SECRET`).
- GameRanger interop stays "don't break it, don't depend on it".
