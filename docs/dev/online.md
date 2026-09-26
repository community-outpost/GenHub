# Online (virtual LAN)

The Online tab lets players browse lobbies, create lobbies (with a required
game profile), join them, and share one virtual LAN
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
3. **Overlay data plane** (see `docs/dev/overlay-spike.md`: userspace TUN +
   TURN-relayed UDP mesh). Runs as a sidecar process managed by
   `OverlaySidecarHost`; the edge reports `overlay: "pending-selection"`
   until the sidecar lands.

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
| `GENHUB_ONLINE_PRIMARY_URL` | Primary edge URL override (e.g. self-hosted VPS) |
| `GENHUB_ONLINE_FALLBACK_URL` | Backup edge URL for failover when primary is unreachable |
| `GENHUB_ONLINE_STUN_HOST` | STUN hostname (default `stun.cloudflare.com`) |
| `GENHUB_ONLINE_RELAY_HOST` | Relay hostname/IP override; wins over the server-advertised relay |
| `GENHUB_OVERLAY_BIN` | Overlay sidecar binary override (spike + power users) |

## Going live (maintainer checklist)

1. `cd gateway-online && npm run deploy`
2. Provision secrets (one command per secret):
   `wrangler secret put JWT_SIGNING_SECRET`
   `wrangler secret put PASSWORD_PEPPER`
   `wrangler secret put COTURN_SECRET`
3. Verify the public hostname matches `ApiConstants.DefaultOnlineEdgeBaseUrl`;
   update the constant if the `workers.dev` subdomain differs.
4. Point a coturn instance at `COTURN_SECRET` (`static-auth-secret`, sample in
   `gateway-online/coturn/turnserver.conf.sample`) and set `TURN_URIS` in
   `wrangler.jsonc`.
5. Redeploy after every edge change (`npm run deploy`): clients speaking to a
   stale edge see stale lobbies, missing password prompts, and
   blank expected profiles.

## Join flow

1. `POST /v1/sessions/anonymous` mints a short-lived session (no user database).
2. `GET /v1/networks` returns metadata only: no member lists, no endpoints.
   Non-members can never learn underlay IPs from the directory.
3. `POST /v1/networks/{id}/join` checks slots, bans, and the lobby password
   when one is set, then returns a grant, an overlay
   IP, an opaque adapter config, the initial roster, and the expected profile
   block. Joiners advertise their own profile fingerprint and player display
   name with the join.
4. The client brings the platform adapter up (while the edge reports
   `overlay: "pending-selection"` only the sidecar spawn is skipped and the
   client falls back to the in-process tunnel runner; the lobby stays usable
   either way),
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
   Play is blocked while the adapter is down: launching without tunneling
   only produces a game that cannot see the lobby, so the client toasts the
   concrete adapter diagnosis instead.

## Windows elevation

Creating the Wintun adapter requires administrator rights. On a non-elevated
launch the client spawns the bundled `genhub-overlay` sidecar through a UAC
prompt; accepting it brings tunneling up, while denying (or dismissing) it
leaves the lobby usable but without tunneling. A failed bring-up preserves
the adapter error (no teardown wipes it), the Online view maps the
elevation marker to remediation guidance, and Play stays blocked until the
user leaves, rejoins, and accepts the prompt (or restarts GenHub elevated).

## Linux provisioning

Creating `genhub0` needs `CAP_NET_ADMIN`, so the in-process runner provisions
it through iproute2 (`LinuxTunSetup`): `ip tuntap add`, `ip addr add` with the
member overlay IP, and `ip link set up`, escalated via polkit (`pkexec`) on
desktops or non-interactive `sudo -n` on headless hosts. The interface persists
across joins and is re-addressed per lobby; provisioning failure fails the
bring-up loudly with the exact manual `sudo ip ...` fallback instead of
starting the socket proxy, which cannot give the game an adapter address.

## Profile matching

Profile ids are machine-local, so lobbies match on the fingerprint
(`OnlineProfileMatcher`): the game client key plus a hash of the sorted
gameplay content ids (mods, patches).
Cosmetics (UI addons, skins, maps, media) never affect the match, mirroring
the exe/ini inputs that decide whether two setups can share a game. When the
engine CRC calculator resolves them, the fingerprint also embeds the iniCRC
and exeCRC (`opf4`); equal CRCs upgrade a same-client verdict to exact
because the setups provably produce the same game data, while differing or
missing CRCs leave the id-based verdict untouched. The host
picks a profile from their own library; joiners auto-match the closest local
profile (exact, then same-client overlap) and advertise it on join and on
every presence heartbeat, so the roster shows per-member match badges.
Heartbeat advertisements also carry the player display name, and the client
re-advertises whenever the nickname or the selected play profile changes, so
renames and profile edits reflect in the open lobby without rejoining. When
the host switches profile, the room broadcasts `profile-changed` and every
member gets a toast plus a re-match.

## Privacy model

Traffic routes through an IP-masking packet relay by default to protect player privacy;
real IP addresses are never shared with peers. The edge drops any endpoint sent by
a relay member on create, join, and heartbeat. Direct connection with STUN discovery
is supported at the service layer (`preferRelay: false`) but not yet surfaced as
an opt-in for self-hosters or custom configurations. Logs, toasts,
and diagnostics scrub IPs via `OnlineLogScrubber`; toasts show overlay IPs or relay state only.

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

Lobby passwords are optional: an empty password means an open lobby, while a
password of at least 4 characters (`MIN_PASSWORD_LENGTH`) protects the lobby.
The edge stores only a PBKDF2-SHA256 verifier salted with the server-side
`PASSWORD_PEPPER` (never the password itself), advertises
`requiresPassword` in the directory and detail responses, and rejects wrong
passwords with `online.wrong-password` (401). Display strings are
control-character sanitized; JSON bodies are capped at 8 KiB.

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

- **OQ1/D3**: the overlay spike picks the sidecar; see
  [overlay-spike.md](overlay-spike.md) for the recommended TUN + TURN-relayed
  design and validation plan. Until then the edge reports
  `overlay: "pending-selection"`, joins succeed lobby-only (roster +
  presence, no tunneling), and the UI says so instead of warning.
- **OQ4**: production hosting + TURN/relay funding and secret provisioning
  (one `wrangler secret put [KEY]` per secret: `JWT_SIGNING_SECRET`,
  `PASSWORD_PEPPER`, `COTURN_SECRET`).
- GameRanger interop stays "don't break it, don't depend on it".
