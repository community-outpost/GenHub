## Goal

Add a GenHub **Online tab** where players can discover networks, create password-protected networks, join them, and end up on the same virtual LAN so Generals / Zero Hour LAN lobbies "just work" — replacing the RadminVPN/Hamachi (virtual LAN) plus GameRanger (browser/lobbies) combination with one integrated flow: open GenHub, pick a network, join the in-game LAN lobby.

## Success Criteria

- A player opens GenHub, selects the Online tab, sees a browsable network/server list, creates or joins a password-protected network, and appears on the same virtual network as the other members with no manual port forwarding on common home NATs.
- Two or more joined players each launch the same game profile and see each other's lobby in the game's LAN browser, then start and play a match.
- This works on Windows, Linux (Wine/Proton), and macOS, following GenHub's platform-composition rules.
- Failure states (wrong password, full network, relay-only connection, adapter install blocked) surface as clear toast notifications, never silent breakage.
- No regressions to existing launch, reconciliation, CAS, or profile flows when the Online feature is disabled or uninstalled.

## Context And Current Facts

GitHub issues (all OPEN, repo `community-outpost/GenHub`):

- #21 `Make Gameranger work with the launcher` — the GameRanger-compat ask; this plan treats it as "interop where cheap, replace where not" (see OQ5).
- #91 `Feature: Quick Match & Integrated Lobby System` — matchmaking queue with preferences (match type, maps, armies, game version, mods), automatic GameProfile preparation, connection via GeneralsOnline or GenHub-facilitated LAN (IP exchange, UPnP, manual-forwarding instructions), with a stretch goal of a P2P relay or lightweight integrated virtual LAN backed by a central GenHub server, explicitly noting STUN/TURN for NAT traversal.
- #94 `Core: Client-Side P2P Connection Service & NAT Traversal` — proposes `IP2PConnectionService` with UPnP (names `Open.NAT`), STUN queries, `StartListening(port)` / `ConnectToPeer(ipAddress, port)` / `GetLocalAndPublicEndpoints()`, and observable connection-status events.
- #98 `Backend Task: Deploy & Configure P2P Connection Facilitator (STUN/TURN)` — deploy standard STUN and optionally TURN (names `coturn` explicitly: no custom server code, infrastructure + monitoring work).
- #95 (epic) / #96 / #97 — backend matchmaking, lobby, and session services: anonymous short-lived JWT sessions (`POST /v1/sessions/anonymous`, scopes, TTL, no user database) in Phase 1 per #96, and SignalR/WebSocket queue + lobby logic with Redis-backed state per #96/#97.
- #92 (live replay/spectator) is a downstream consumer of whatever real-time transport this plan builds, not part of it.

Codebase facts (verified by inspection this session):

- Avalonia MVVM shell: `GenHub/GenHub/Common/ViewModels/MainViewModel.cs` composes one ViewModel per tab (profiles, downloads, tools, settings, info, notifications). A new Online tab follows the same pattern: `GenHub/GenHub/Features/Online/` (ViewModels/Views/Services) registered in `GenHub/GenHub/Infrastructure/DependencyInjection/`, consumed by `AppServices.ConfigureApplicationServices`.
- Platform split: `GenHub.Windows` / `GenHub.Linux` / `GenHub.MacOS` each own a services module and `Program.cs` composition root — virtual-adapter bring-up must live behind Core interfaces with per-OS implementations.
- No P2P, VPN, STUN/TURN, or networking client code exists yet; `grep` for those terms only hits `IGeneralsOnlineUpdateService` / `IGeneralsOnlineProfileReconciler` (GeneralsOnline content-update services, a different concern).
- Existing backend precedent: `gateway/` is a Cloudflare Worker upload proxy that keeps master secrets off the client and uses HMAC receipts — the same posture (thin trusted edge, untrusted client) applies to session issuance and TURN credentials.
- Hard project rules apply: `OperationResult<T>` for fallible operations (`docs/dev/result-pattern.md`), centralized constants (`docs/dev/constants.md`), `Strings.resx` + strict 1:1 `ar`/`ru` parity for every user-facing string, theme tokens via `{DynamicResource}`, `INotificationService` toasts instead of status labels, `CancellationToken` on all long-running work.
- GenHub license is GPLv3 (verified `LICENSE` header this session). Any bundled overlay/adapter component must be GPLv3-compatible. This sharpens OQ2: ZeroTier controller code is source-available/nonfree in current releases (only the 1.14.2-era controller crossed to Apache-2.0 via BSL expiry, per community grafts) — do not bundle a current ZeroTier controller without legal review. Prefer MIT/BSD/MPL components (Nebula MIT, NetBird client BSD-3, WireGuard MIT/Apache) for anything shipped in-process.
- `gateway/` verified shape (`gateway/README.md`, `gateway/src/index.ts`, `gateway/wrangler.jsonc`): Worker `genhub-upload-gateway`, secrets `UPLOADTHING_TOKEN` + `GATEWAY_HMAC_SECRET` server-side only, stateless HMAC-SHA256 delete receipts (`fileKey:timestamp.signature`, base64url, 14-day default TTL, clock-skew guard), file guardrails. This is the template for all Online-tab edge trust (see Cloudflare plan below).

Protocol facts (inspected source):

- Community port guides for Generals list TCP 6667, 28910, 29900, 29920 and UDP 4321, 27900 (portforward.com page, fetched and verified this session). These cover legacy online + LAN traffic; exact LAN-discovery mechanics (broadcast vs subnet-scoped beacons) are NOT confirmed and must be captured in Phase 0 (OQ1).
- The decisive technical point for this plan: legacy LAN play assumes peers are mutually visible on one local network (discovery + direct UDP). STUN/TURN alone only solve "learn my public endpoint / relay my packets to a known peer" — they do not put peers on one subnet and do not forward LAN discovery. So the data plane must be a **virtual LAN overlay** (virtual NIC per member, all members addressed on one overlay network); STUN/TURN then serve the overlay's NAT traversal and relay fallback, exactly as #91/#94/#98 anticipate.
- ZH LAN lore (forums/revora threads, re-verified this session): LAN and Online IP selectors in game options, Hamachi/Radmin IPs selected as the LAN IP, UDP 4321 + 27900 repeatedly cited for gameplay/discovery. Still does not answer broadcast-vs-unicast — OQ1 stands.

## Constraints And Non-goals

- Target `development` branch; no signature changes to `ICasService`, `IProfileContentService`, `IContentReconciliationService` without caller-chain review.
- All user-facing strings localized (`Strings.resx` + `ar`/`ru` parity); all toasts via `INotificationService`; `OperationResult<T>` for fallible ops; `CancellationToken` throughout; no hardcoded paths/URLs (centralize endpoints in `ApiConstants` with env-var overrides, per project rules).
- Every phase ships behind a feature flag and leaves the existing launch path untouched when disabled.
- Non-goals for this plan: ranked/skill matchmaking, anti-cheat, voice chat, dedicated game servers, mobile clients, spectator mode (#92 stays a follow-up consumer).

## Key Decisions

- **D1 — STUN/TURN via self-hosted coturn, no custom server.** coturn is a free open-source STUN+TURN implementation (inspected README). This matches #98 exactly. STUN for endpoint discovery, TURN relay strictly as fallback for restrictive/symmetric NATs.
- **D2 — Data plane is a WireGuard-protocol mesh overlay, not UPnP-only or TURN-proxied game traffic.** UPnP fails on CGNAT and managed routers; proxying game UDP through TURN would require protocol surgery on discovery the game expects to "just see". A virtual NIC avoids touching game protocol at all. WireGuard is a cross-platform general-purpose VPN protocol (inspected wireguard.com); embeddable userspace implementations exist for managed bring-up: wireguard-go (inspected README) and boringtun, whose library form exists precisely to build WireGuard clients per platform (inspected README).
- **D3 — Overlay controller shortlist: Nebula vs ZeroTier, gated by a Phase 0 spike.** Nebula is a portable (Linux/OSX/Windows) scalable overlay with certificate-based trust and self-hosted lighthouses (inspected README). ZeroTier combines a P2P network layer with an Ethernet-emulation layer so devices communicate as if on the same physical switch — the closest literal match to "same LAN", with end-to-end encryption and P2P-first relay-fallback behavior (inspected README). Default to Nebula if the Phase 0 capture shows ZH LAN discovery works over routed L3 + directed broadcast relay; choose ZeroTier if the game needs true L2 adjacency. A Tailscale-compatible self-hosted control plane (headscale: open-source self-hosted Tailscale control server; Tailscale itself a WireGuard-based overlay with NAT traversal — both per inspected headscale README) is the documented fallback if both finalists fail on an OS. ZeroTier controller self-host licensing for bundled use must be verified before committing (OQ2).
- **D4 — Backend stays inside the #95/#96/#97 shape.** ASP.NET Core API + SignalR/WebSocket, anonymous short-lived JWT sessions with Redis TTL state in Phase 1 (all per those issues), plus coturn and the mesh control plane (lighthouse/controller) as adjacent infrastructure. Reuse the `gateway/` operating posture: secrets server-side, ephemeral credentials to clients.
- **D5 — Client is a new `Features/Online` slice behind Core interfaces** (`IOnlineNetworkService`, `IP2PConnectionService` as specified in #94, `IVirtualLanAdapter`), with adapter bring-up implemented per platform host (Windows service/driver install with elevation UX; Linux tun/device or userspace; macOS packet-tunnel path subject to signing/notarization constraints already tracked in #322). Joining maps to an expected game profile: reconcile workspace, then launch — so #91's "dynamic GameProfile preparation" falls out of existing reconciliation instead of new machinery.
- **D6 — Passwords are control-plane authorization, not overlay PSKs.** Networks carry an access grant issued after password check; mesh certificates are short-lived and per-member; TURN credentials are time-limited; nothing secret is logged. Rate limiting and abuse controls follow #96.

## Recommended Approach

Layered architecture, each layer independently testable:

1. **Signaling/control API** (new backend service): anonymous sessions, network CRUD (name, password verifier, slot cap, game/mod tags, public/private), presence, match intents; SignalR channels for lobby/queue events (consumes #96/#97 designs).
2. **Overlay control plane**: mesh lighthouse/controller issuing short-lived member certificates per network; network join = password check → grant → certificate → adapter config.
3. **Client transport** (`GenHub.Core` interfaces + `Features/Online` services + per-OS adapter bring-up): join/leave, adapter lifecycle, endpoint health (direct vs relayed), reconnect with backoff, all cancellable, all `OperationResult<T>`.
4. **Online tab UI**: network list with search/filter, create dialog (name/password/slots/game tags), join flow (password prompt), member/presence roster, connection-quality indicator (direct/relay), host/launch affordances, all strings localized, all outcomes toasted.
5. **Launch integration**: on "Play", resolve the network's expected profile → reconcile workspace via existing services → launch with LAN parameters → user opens the in-game LAN browser and sees peers. No game-protocol code inside GenHub.
6. **Ops**: coturn + control plane deployed with uptime monitoring (per #98 acceptance criteria), TURN bandwidth metered with budget alerts, abuse tooling (report/block, network bans).

---

## Enrichment Round 2 — Security, Alternatives, Browse-vs-Join, Cloudflare (2026-09-20)

This section enriches the PRD without removing any context above. It answers the follow-up ask: IP-leak risks, other open-source options, public-server-visibility with join-gated connectivity, and what GenHub must run on Cloudflare (Workers, keys, STUN/TURN).

### S1 — Threat Model And IP-Leak Analysis (What Leaks, To Whom, When)

Principle adopted from the follow-up: **everyone can see servers; only joined members exchange packets.** Browsing must never disclose underlay IPs. Joining discloses the minimum needed for P2P, and only to fellow members of the same network/room.

#### S1.1 — What "leaking IP addresses" concretely means here

- **Overlay IP (e.g. 10.42.x.x)**: assigned by GenHub, visible to fellow members by design — same as seeing a LAN peer's 192.168.x.x. Not a leak; display it freely in the member roster. Never reuse a player's real LAN IP as the overlay IP.
- **Underlay public IP:port (reflexive candidate)**: the player's real home IP as seen after NAT. Disclosing it to non-members is the leak to avoid. Disclosing it to joined members is inherent to P2P hole-punching (same as Hamachi/Radmin/GameRanger do today) and must be consent-gated by joining.
- **Relayed transport**: hides underlay IPs from peers (peers see only the relay), but moves visibility to the relay operator. Useful as a privacy mode at the cost of latency/bandwidth.
- **Control-plane metadata**: lighthouse/signal/controller inevitably learns which overlay identity maps to which current underlay endpoint while a member is joined (needed for hole-punch rendezvous). It must not retain or expose this after leave, and must never serve it to non-members.

#### S1.2 — Leak vectors and mitigations (matrix)

| # | Vector | Who learns what | When | Mitigation (required) |
|---|--------|-----------------|------|------------------------|
| V1 | Network directory / browse list | Must learn nothing about member underlay IPs | Always (anonymous browsing) | Directory returns only metadata: name, game/tags, slots used/max, region, host display-name, aggregate health. No endpoints, no candidates, no overlay-IP-to-underlay mapping. Enforce in API serialization tests. |
| V2 | Presence / roster | Member-only: display-names + overlay IPs + direct/relay quality | Only after grant, only inside joined room channel | Scope SignalR/DO room messages by grant; server rejects non-member subscriptions. Non-members see counts only. |
| V3 | Endpoint exchange for hole-punch (signal) | Joined peers learn each other's public IP:port | Only after password check + grant | Join-gated candidate exchange; no pre-join trickle; per-network short-lived grant scopes it. Document in UI: "joining shares your public endpoint with room members for direct connection (like Hamachi)". |
| V4 | STUN binding | STUN operator sees requestor's public IP (inherent) | On join / re-punch only, never while browsing | Use GenHub-operated or Cloudflare STUN (`stun.cloudflare.com`, documented free/unlimited) instead of random public STUN; no STUN while browsing; cache reflexive endpoint per session. |
| V5 | TURN relay | Relay operator sees relayed bytes + both peers' relay allocations | Only when direct fails and member opts/accepts relay | Ephemeral TURN credentials per member per network (TTL 15–60 min, minted server-side like the gateway HMAC pattern); relay-preferred "Protect IP" toggle (WhatsApp-style) forces relay and skips direct candidates; meter + budget-alert relay GB. |
| V6 | Lighthouse/controller rendezvous store | Control plane learns overlay→underlay mapping while joined | While joined | Short TTL, delete on leave/disconnect, no IP logs; treat as ephemeral rendezvous, not a user database (consistent with #96 anonymous sessions). |
| V7 | Logs, crash dumps, toasts, diagnostics | Support tooling could capture IPs | On errors | Never log endpoints/tokens/candidates at info level; scrub IPv4/IPv6 + ports in log pipeline and in "copy diagnostics" output; toasts show overlay IP or "direct/relay", never public IP. Add a log-scrub unit test. |
| V8 | Password handling | Brute-force / replay to join rooms | On join attempts | Server-side Argon2id/bcrypt verifier + per-account pepper, constant-time compare, rate-limit + exponential backoff per network+IP, short-lived join grant (not the password) unlocks the overlay cert. |
| V9 | Stale membership (leave/ban without revocation) | Ex-member keeps connectivity or keeps seeing roster | On leave/kick/ban | Short-lived mesh certs (hours, not days) + explicit deny-list pushed on kick/ban; adapter config torn down on leave; presence evicts on heartbeat timeout. |
| V10 | Game-level exposure | In-game lobby/direct-connect may show overlay IP only | In match | Verify in Phase 0 that the game binds the overlay adapter and never needs the underlay; game options should point at the overlay IP. |

What Radmin/Hamachi teach us: both use a central broker for rendezvous plus P2P with relay fallback (Hamachi via its mediation servers, Radmin via its backend), AES-256 on the tunnel, and a virtual adapter on a private range. Their privacy weakness is the same central mapping store plus closed clients. GenHub matches their connectivity model but improves on it: self-hosted open-source control plane, minimal metadata, ephemeral credentials, no full-traffic VPN (only the overlay subnet is routed — never default-route a player's internet through GenHub).

### S2 — Open-Source Alternatives Re-Assessment (Delta To D3)

D3's Nebula-vs-ZeroTier spike stands, but the shortlist widens to three finalists plus two fallbacks, informed by license, L2 behavior, relay story, and .NET embeddability.

| Candidate | Network model | NAT traversal / relay | License (bundling relevance, GenHub=GPLv3) | Platform / embed notes | Verdict |
|-----------|---------------|----------------------|--------------------------------------------|------------------------|---------|
| **Nebula** (Slack, MIT) | L3 routed overlay, Noise, groups = firewall rules, lighthouses = directory (not data path) | UDP hole-punch via lighthouse; relay support exists (`relay` peers) | MIT — safe to embed/ship | Portable Go, TUN; run as sidecar process managed by GenHub or via library; no L2 broadcast natively — needs directed-broadcast/relay config | **Finalist A.** Default if OQ1 shows ZH works over L3 unicast + directed broadcast. |
| **ZeroTier** (node MPL/GPLv3, controller source-available/nonfree in current line) | True L2 Ethernet emulation — closest literal "same switch" | P2P with root/relay fallback (planet/roots) | Node OK; **controller bundling is the risk** (OQ2). Options: use hosted API, self-host 1.14.2-era Apache controller graft, or ZTNET (GPLv3) — each needs legal + ops sign-off | Mature Win/Linux/macOS TAP; needs elevation; L2 broadcast works | **Finalist B.** Choose if OQ1 proves true L2 adjacency is required. Do not bundle current controller code. |
| **NetBird** (client BSD-3, server mixed BSD/AGPL — verify exact tag before bundling) | WireGuard L3 mesh + management/signal/relay/coturn as one self-hosted stack | Signal (ICE) + relay + bundled coturn STUN/TURN; QUIC relay fallback | Client license friendly; server self-host OK but confirm AGPL obligations for modified server code (running unmodified images is the safe path) | Native Win/Mac/Linux clients exist; one deployment covers control+signal+relay (less assembly than Nebula+lighthouse+coturn) | **Finalist C (new).** Best ops story if L3 suffices. Spike must test: sidecar vs library embed from C#, per-OS elevation, relay bandwidth. |
| headscale + Tailscale client | WireGuard L3 + DERP relay (TCP-friendly fallback) | DERP relay excellent on nasty NATs | headscale MIT/Apache-style; Tailscale client BSD — workable | Same L2 caveat as Nebula/NetBird | **Fallback 1** if Finalists A/C fail on an OS. |
| Plain WireGuard (boringtun / wireguard-go) + custom controller | L3 point-to-mesh you build yourself | You build discovery + relay | WireGuard-side licenses friendly | Maximal control, maximal work | **Fallback 2.** Only if no finalist survives Phase 0. |
| OpenVPN | TUN (L3) or TAP (L2) but star topology, no P2P mesh | Central server relays everything — bandwidth $$$, latency | GPLv2 — GPLv3-compat review needed; TAP drivers per OS | Easy to deploy, wrong shape for gaming mesh | Rejected for data plane (keep only as mental baseline). |
| Hamachi / Radmin / GameRanger (proprietary) | Reference behavior only | Broker + P2P + relay | Closed — cannot bundle | Windows-centric (Radmin), Hamachi cross-platform but closed | Baselines for UX/parity, not candidates. |

Spike delta (amends Phase 0.2): prototype **three** overlays (Nebula, ZeroTier, NetBird) unmodified with ZH across Win/Linux/macOS; record (a) which discovery modes work, (b) privileges per OS, (c) relay path + latency, (d) sidecar-process vs library embed effort from .NET, (e) license disposition. Keep headscale as documented fallback. Success bar unchanged: two machines behind different home NATs see each other's LAN lobby.

GPL note: because GenHub is GPLv3, preferred shipped components are MIT/BSD/MPL/Apache-2.0 or GPLv3-compatible with no additional restrictions. Anything AGPL-licensed stays server-side unmodified (no client bundling). Record the exact version + license text of the chosen overlay in the Phase 0 report.

### S3 — Browse-vs-Join Design (The Requested Visibility Model)

This makes "everyone sees servers, only members connect" a testable contract:

1. **Directory (anonymous, unauthenticated-browse).** `GET /v1/networks` returns public networks only: id, name, game/tags, slots used/max, region, host display-name, created/heartbeat age, aggregate quality (e.g. "mostly direct"). No member list, no IPs, no endpoints. Private networks are unlisted; join via invite code. Search/filter is server-side on metadata only.
2. **Pre-join detail (still no endpoints).** Selecting a network shows description, rules, slot count, expected game profileplural, host presence — still no member IPs.
3. **Join (password → grant → cert).** `POST /v1/networks/{id}/join` with password → server verifies verifier, checks slots/bans → returns (a) short-lived join grant JWT (scope `network:join`, TTL minutes), (b) overlay config + short-lived mesh cert/key, (c) ephemeral TURN credentials if relay may be needed. Only now does the client bring up the adapter and subscribe to the member-only presence room.
4. **Member room (grant-scoped).** SignalR/DO channel carries roster (display-name, overlay IP, direct/relay state), host/launch intents, kick/ban events. Server rejects non-grant subscriptions; heartbeat evicts stale members; leave tears down cert + adapter routes.
5. **Launch.** Same as before: resolve expected profile → reconcile → launch with LAN parameters bound to the overlay adapter. Mismatched profile → download prompt (per #91 §B).
6. **Privacy toggle.** Settings option "Prefer relay (hide my direct endpoint from peers)" — when on, client offers only relay candidates (like WhatsApp "Protect IP address in calls"). Slower but hides underlay IP from peers (relay operator still sees it). Default off; surfaced in join dialog + settings with localized strings.
7. **Abuse.** Report/block per member, network ban = cert deny-list + grant refusal; rate-limits from #96 apply to directory + join endpoints separately (directory generous, join strict).

Test contracts: (i) unauthenticated directory response contains zero IPv4/IPv6 endpoint fields (schema test); (ii) non-member WebSocket subscription to a room is rejected; (iii) packet capture while browsing shows no STUN/punch traffic, only HTTPS to control plane; (iv) post-join capture shows hole-punch only to fellow members (or relay only in privacy mode).

### S4 — What GenHub Must Run On Cloudflare (Workers, Keys, STUN/TURN)

#### S4.1 — Do Workers relay game traffic? No.

Cloudflare Workers speak HTTP/WebSocket at the edge; Cloudflare Tunnel does not proxy UDP game traffic; Workers cannot forward arbitrary WireGuard/Nebula/ZeroTier UDP. So the split is strict:

- **Cloudflare edge = trust + control only**: session issuance, directory, password-check/grants, ephemeral credential minting, presence fan-out.
- **Data plane = P2P overlay (+ its native relay)**: Nebula relay / ZT roots / NetBird relay / DERP. Budget relay bandwidth there, not on Workers.

#### S4.2 — Recommended Cloudflare-first control plane (extends `gateway/`)

Reuse the verified `gateway/` posture (master secrets never leave Workers, stateless HMAC receipts, `wrangler secret put`) for a second Worker, e.g. `genhub-online-edge`:

| Endpoint | Purpose | Secrets touched (server-side only) |
|----------|---------|-------------------------------------|
| `POST /v1/sessions/anonymous` | Mint anonymous short-lived JWT (scopes, TTL, no user DB — per #96) | `JWT_SIGNING_SECRET` |
| `GET /v1/networks` | Public directory (metadata only, §S3.1) | none (validates session only) |
| `POST /v1/networks` | Create network (name, verifier, slots, tags, public/private) | `PASSWORD_PEPPER` (verifier = Argon2id(password, pepper)) |
| `POST /v1/networks/{id}/join` | Password check → join grant + overlay cert + TURN creds | `PASSWORD_PEPPER`, `MESH_CA_KEY`, TURN minting key |
| `POST /v1/turn/credentials` | Ephemeral TURN credentials (TTL 15–60 min) | `TURN_API_TOKEN` or `COTURN_SECRET` |
| `GET /v1/overlay/cert` | Short-lived mesh cert/key rotation | `MESH_CA_KEY` |
| `WS /v1/networks/{id}/presence` | Grant-scoped roster + intents (one Durable Object per network/room) | validates grant JWT only |

State: one Durable Object per network/room holds roster + heartbeat + message queue; no Redis needed for MVP. If the project keeps the ASP.NET + Redis shape from #95/#96/#97, Workers still own the edge trust (issuance + minting) and proxy to it — secrets never ship in the Avalonia client. Decide Workers+DO vs ASP.NET+Redis explicitly in Phase 1 (new OQ8 below); either way the endpoint + secret inventory above is unchanged.

Client config: all edge URLs + STUN hostnames centralized in `GenHub.Core.Constants.ApiConstants` with `GENHUB_*` env-var overrides (per project rules); no hardcoded URLs.

#### S4.3 — STUN/TURN: Cloudflare vs self-hosted coturn (corrected guidance)

- **STUN**: use `stun.cloudflare.com` (documented free/unlimited) or self-hosted coturn STUN. Either is trivial cost. No custom server code.
- **TURN for WebRTC-style ICE**: Cloudflare Calls TURN (`turn.cloudflare.com`, credentials via `generate-ice-servers` API, ~$0.05/GB outbound, free while attached to Calls SFU usage) is the zero-ops option — but only if the client stack consumes TURN (WebRTC ICE / NetBird-style signal). Mint per-session server-side, never embed the API token in the client.
- **TURN for Nebula/ZeroTier/WireGuard-native overlays**: these overlays do NOT consume generic TURN allocations — they have their own relay (Nebula relay peers, ZT roots/planet, Tailscale DERP, NetBird relay). A Phase 1 test must confirm whether the chosen overlay can use an external TURN server at all; if not, remove "Cloudflare TURN relays game traffic" from the plan and budget the overlay's native relay instead. coturn remains relevant only where the stack actually speaks ICE/TURN (NetBird bundle includes coturn; custom WebRTC transport would use Cloudflare TURN).
- **Ops**: whichever relay is used, it is fallback-only, metered, budgeted, and alerted (per #98 acceptance criteria). STUN + direct P2P cost ~nothing.

Secret inventory (all `wrangler secret put`, never in repo/client): existing `UPLOADTHING_TOKEN`, `GATEWAY_HMAC_SECRET`, plus new `JWT_SIGNING_SECRET`, `PASSWORD_PEPPER`, `MESH_CA_KEY` (offline-generated CA, only public halves + short-lived member certs leave the edge), `TURN_API_TOKEN` or `COTURN_SECRET`, optional `CLOUDFLARE_API_TOKEN` for Calls TURN minting. Rotate with dual-secret support; document rotation runbook in Phase 5.

### S5 — Work-Plan Deltas (Amendments, Not Replacements)

- **Phase 0.1 (unchanged)**: two-machine pcap; add explicit capture gates: broadcast vs directed-unicast, ports, subnet assumption, which adapter IP the game binds, whether overlay IP appears in lobby.
- **Phase 0.2 (amended)**: three-way spike — Nebula, ZeroTier, **NetBird** — plus headscale fallback note. Record per-OS privilege, embed effort (.NET sidecar vs library), relay path, license disposition (version + license text).
- **Phase 0.3 (amended)**: cost both relay paths — (a) overlay-native relay bandwidth, (b) TURN bandwidth (Cloudflare $/GB vs self-hosted VPS egress) — and confirm whether the chosen overlay consumes external TURN at all.
- **Phase 1 (amended)**: add edge-trust endpoints + secret inventory (§S4.2), directory-schema test (no endpoints to non-members), grant-scoped presence, Argon2id verifiers, rate limits (directory generous / join strict), and the Workers+DO vs ASP.NET+Redis decision (OQ8).
- **Phase 2 (amended)**: add `PreferRelay` privacy toggle, log-scrub + no-IP-in-toast rules, cert teardown on leave, reconnect/backoff that re-mints (never reuses expired creds).
- **Phase 3 (amended)**: directory shows metadata only; roster is member-only; join dialog discloses endpoint-sharing + offers relay-preferred toggle; full resx + ar/ru parity for every new string.
- **Phase 5 (amended)**: add privacy tests (§S3 test contracts), relay-cost dashboards, secret-rotation runbook, abuse tooling (report/block/ban via deny-list).

### S6 — Open Questions (OQ1–OQ5 Retained, OQ6–OQ9 Added)

Retained: OQ1 (ZH discovery mechanics — gates D3), OQ2 (ZeroTier controller license — now widened to full license matrix incl. NetBird server AGPL review + wintun/wireguard-windows review), OQ3 (macOS packet-tunnel + #322), OQ4 (who operates backend + TURN/relay funding — gates Phase 1), OQ5 (#21 GameRanger interop — Phase 4, don't break/don't depend).

- **OQ6**: Relay privacy vs latency default — is relay-preferred opt-in (default direct) acceptable to players who asked about IP leaks, or must public rooms default to relay? Product decision, informs S3.7 toggle default.
- **OQ7**: Directory moderation — who can list public networks, name-squatting / offensive names, takedown SLA? Needed before open directory launch.
- **OQ8**: Control-plane hosting — Cloudflare Workers + Durable Objects vs ASP.NET Core + SignalR + Redis (#95/#96/#97). Recommending Workers+DO for MVP edge trust (zero servers, matches `gateway/`), but needs maintainer sign-off before Phase 1.
- **OQ9**: Log/diagnostics retention — scrubbed-by-default logs, opt-in full capture for bug reports with explicit IP warning? Needed for Phase 5 runbooks.
- None of OQ6–OQ9 block Phase 0; OQ4 + OQ8 gate Phase 1; OQ1 gates D3.

## Work Plan

- **Phase 0 — Spike (gates D3).** 0.1: capture ZH LAN discovery on a real LAN (two machines, packet capture; document beacon ports, broadcast vs unicast, subnet assumptions). 0.2: prototype Nebula and ZeroTier overlays across Windows/Linux/macOS with the game unmodified; record which discovery modes work and what privileges each OS requires. 0.3: coturn costing/ops estimate (STUN ≈ trivial, TURN = bandwidth-metered).
- **Phase 1 — Backend MVP.** 1.1: API skeleton + anonymous session issuance/validation per #96. 1.2: network CRUD + presence over SignalR per #97 (queue logic itself can stay minimal: list/create/join/leave first, matchmaking second). 1.3: coturn deploy + ephemeral-credential endpoint + monitoring. 1.4: mesh control plane deploy (lighthouse/controller) + certificate issuance endpoint.
- **Phase 2 — Client transport.** 2.1: Core interfaces + result types (`IOnlineNetworkService`, `IP2PConnectionService` per #94, `IVirtualLanAdapter`) with unit tests. 2.2–2.4: per-OS bring-up (Windows, Linux, macOS) each with installer/elevation UX and a userspace fallback where viable; each OS is its own reviewable unit.
- **Phase 3 — Online tab UI.** 3.1: list/search/create/join/roster/quality-indicator bound to Phase 2 services. 3.2: full localization (resx + ar/ru parity) and toast coverage; culture-switch refresh for dynamic lists.
- **Phase 4 — Launch integration.** Resolve network → expected profile → reconcile → launch; handle missing-profile download prompt (per #91 §B). Assess #21 GameRanger interop (OQ5) — at minimum, do not break GameRanger-detected installs.
- **Phase 5 — Hardening/ops.** TURN-fallback E2E tests, symmetric-NAT lab, abuse tooling, metrics/dashboards, runbooks, docs. Quick-match ranking and #92 spectator remain follow-ups on top of this transport.

## Validation Plan

- `dotnet build GenHub/GenHub.sln -c Release`; targeted suites only: Core tests for services, plus the matching platform suite per OS unit (Windows/Linux/MacOS test projects).
- New tests per unit: session issuance/validation, network grant logic, adapter lifecycle state machine, reconnect/backoff; no tests asserting game behavior itself.
- Manual E2E matrix (required, per phase 2–4): two machines behind different home NATs → join same password network → both launch same profile → each sees the other's lobby → match starts. Repeat with UDP-P2P blocked to prove TURN-relay fallback, and with mismatched profiles to prove the reconcile prompt.
- Highest-risk validation: macOS adapter bring-up under signing/notarization constraints (#322), and the symmetric-NAT relay path (cost + latency).

## Risks / Rollback

- **Elevation/drivers, especially macOS** (system-extension approval, notarization): mitigate with userspace fallback and explicit install UX; rollback is clean because all adapter code sits behind new interfaces and the Online tab is feature-flagged — disable the flag and the launch path is byte-identical to today.
- **TURN bandwidth cost**: relay is fallback-only, metered, budgeted, alerted; STUN and P2P cost ~nothing.
- **L2-vs-L3 discovery miss**: Phase 0 spike exists precisely to retire this before any product code; D3 defaults are conditional on it.
- **Abuse/moderation**: private-by-default networks, report/block, revocation via short-lived certs, rate limits from #96.
- **Scope creep**: non-goals are listed above; quick match stays minimal (browse/join first, smart matching later).

## Open Questions

- **OQ1**: Exact ZH LAN discovery mechanism — answered only by the Phase 0 capture; everything in D2/D3 that depends on broadcast behavior is conditional until then.
- **OQ2**: ZeroTier controller self-host/bundling license terms — verify before D3 final selection.
- **OQ3**: Minimum supported macOS version and packet-tunnel approach given #322 signing/notarization constraints.
- **OQ4**: Who operates the backend (community infra, funding for TURN bandwidth) — product/ops decision, needed before Phase 1.
- **OQ5**: #21 GameRanger interop vs replace — decide in Phase 4; default is "don't break it, don't depend on it".
- **OQ6**: Relay privacy vs latency default — opt-in relay-preferred or default-relay for public rooms? Product decision, informs privacy toggle default.
- **OQ7**: Directory moderation — listing policy, name-squatting, takedown SLA before open directory launch.
- **OQ8**: Control-plane hosting — Cloudflare Workers + Durable Objects vs ASP.NET Core + SignalR + Redis. Recommends Workers+DO for MVP edge trust; needs maintainer sign-off before Phase 1.
- **OQ9**: Log/diagnostics retention — scrubbed-by-default logs, opt-in full capture with explicit IP warning for bug reports.
- None of the above block Phase 0; OQ1 gates D3, OQ4 gates Phase 1.

## Sources

- https://raw.githubusercontent.com/coturn/coturn/master/README.md
- https://www.wireguard.com/
- https://raw.githubusercontent.com/WireGuard/wireguard-go/master/README.md
- https://raw.githubusercontent.com/cloudflare/boringtun/master/README.md
- https://raw.githubusercontent.com/slackhq/nebula/master/README.md
- https://raw.githubusercontent.com/zerotier/ZeroTierOne/master/README.md
- https://raw.githubusercontent.com/juanfont/headscale/main/README.md
- https://portforward.com/command-and-conquer-generals/
- Enrichment round 2 (2026-09-20): Cloudflare Calls TURN + Realtime docs (turn.cloudflare.com, stun.cloudflare.com free/unlimited, generate-ice-servers minting, ~$0.05/GB outbound); Cloudflare Workers + Durable Objects WebSocket/presence patterns (per-room DO, short-lived ticket auth); NetBird self-hosted docs (management+signal+relay+coturn combined server, BSD-3 client); ZeroTier licensing threads (node MPL/GPLv3, current controller source-available — 1.14.2-era controller Apache-2.0 via BSL expiry grafts, ZTNET GPLv3); Nebula lighthouse/directory model; WebRTC/STUN IP-exposure mitigations (relay-preferred "protect IP" pattern); Hamachi/Radmin virtual-LAN behavior baselines; GameReplays/revora ZH port + LAN/Online-IP threads.
