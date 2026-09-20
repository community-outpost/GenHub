# genhub-online-edge

GenHub Online edge control plane (Cloudflare Worker + Durable Objects). Trust and
control only: sessions, directory, join grants, presence fan-out. It never relays
game traffic — gameplay stays on the P2P overlay.

## Endpoints

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| GET | `/v1/health` | none | Liveness probe |
| POST | `/v1/sessions/anonymous` | none | Mint short-lived session JWT |
| GET | `/v1/networks?search=` | session | Public directory, metadata only |
| POST | `/v1/networks` | session | Create network, join as host |
| GET | `/v1/networks/{id}` | session | Pre-join detail, no endpoints |
| PATCH | `/v1/networks/{id}` | host grant | Update description / expected profile |
| POST | `/v1/networks/{id}/join` | session | Password check, grant, overlay config |
| POST | `/v1/networks/{id}/leave` | grant | Leave, free slot |
| POST | `/v1/networks/{id}/heartbeat` | grant | Keep membership, fetch roster |
| GET | `/v1/networks/{id}/members` | grant | Member roster (poll fallback) |
| POST | `/v1/networks/{id}/report` | grant | Report a member (abuse) |
| POST | `/v1/networks/{id}/ban` | host grant | Ban member, revoke slot |
| GET | `/v1/networks/{id}/cert` | grant | Refresh grant + TURN credentials |
| POST | `/v1/turn/credentials` | session | Ephemeral coturn REST credentials |
| WS | `/v1/networks/{id}/presence?ticket=` | grant | Roster fan-out socket |

## Deploy

```bash
npm install
npx wrangler deploy
wrangler secret put JWT_SIGNING_SECRET
wrangler secret put PASSWORD_PEPPER
wrangler secret put COTURN_SECRET
```

Then verify the public hostname matches `ApiConstants.DefaultOnlineEdgeBaseUrl`
in the client, and set `TURN_URIS` to the production coturn hosts.

## Secrets (never in repo or client)

`COTURN_SECRET` must match the coturn `static-auth-secret`. Rotate with dual-secret
support on the coturn side; tokens are short-lived so rotation converges in minutes.

## Vars

See `wrangler.jsonc`: TTLs, presence timeout, join rate limit, per-IP creation cap,
directory reads per minute, overlay subnet, TURN URIs.

## Policies

- Public networks require a password (min 4 chars); private networks may be
  password-less and stay unlisted.
- Display strings are control-character sanitized; JSON bodies are capped at
  8 KiB; directory reads are rate-limited per IP.
- Bans bind to the anonymous session identity (see `docs/dev/online.md`).
- Joined members may publish a reflexive `endpoint` (`host:port`, validated)
  on create/join/heartbeat. Endpoints are visible only to grant holders via
  the roster; the directory and pre-join detail never carry them. Relay-mode
  members publish nothing, and the edge drops any endpoint they send.

## Develop and test

```bash
npm install
npm run types   # tsc --noEmit
npm test        # vitest-pool-workers integration suite
npx wrangler dev
```

## Notes

- Passwords are PBKDF2-SHA256 verifiers with a server-side pepper; join rate
  limiting is per network + IP inside the room object.
- `adapterConfig.overlay` is `"pending-selection"` until the Phase 0 overlay spike
  picks Nebula / ZeroTier / NetBird. The payload is versioned (`v: 0`) so the
  client can keep treating it as opaque.
