# Overlay spike (tunneling)

Parent: [Online (virtual LAN)](online.md) (open question OQ1/D3).
Status: design spike, no implementation yet.

## Problem

The Online tab ships the lobby layer today: directory, roster, presence,
profile match badges. No game packets flow between machines, so the in-game
LAN lobby stays empty. Joining reports `overlay: "pending-selection"` and
runs lobby-only by design. This spike picks the tunneling overlay that
carries actual Generals / Zero Hour LAN traffic.

## Constraints

- Relay is always on (product decision): no direct peer-to-peer UDP
  hole-punching. Peer traffic must traverse operator infrastructure.
- Game traffic is UDP on the LAN segment (community-documented ZH ports:
  UDP 4321, 27900, 16000; lobby discovery expects LAN broadcast semantics,
  which is why Tunngle/Radmin style virtual LANs work and plain port
  forwarding is fragile).
- Cross-platform: Windows, Linux, macOS, with Wine/Proton in the mix. Any
  TUN bring-up must survive that matrix.
- Reuse what exists: the edge already mints ephemeral coturn credentials
  (`COTURN_SECRET`, `/v1/turn-config`), and the client already has the
  `IOverlaySidecarLocator` / `OverlaySidecarHost` seam plus opaque adapter
  configs for peer maps.

## Options considered

### A. Userspace TUN (L3) + TURN-relayed UDP mesh (recommended)

Each client brings up a TUN interface with its member overlay IP
(10.42.x.y). A sidecar packet pump reads IP packets from the TUN device,
wraps them in UDP, and sends them through the member's TURN allocation to
each peer's allocation; incoming relayed datagrams are unwrapped and
written back to the TUN device. LAN broadcast discovery is emulated by a
userspace broadcast relay: packets to 255.255.255.255 (or the subnet
broadcast) are fanned out to every peer, since L3 TUNs do not forward
broadcasts themselves.

- Pros: no new server infrastructure (coturn already planned); relay-only
  by construction, matching the privacy model; game sees a normal LAN
  interface with the overlay IP Play already preselects.
- Cons: per-OS TUN bring-up (see matrix); broadcast relay must be validated
  against real ZH discovery traffic with packet captures.

### B. WireGuard sidecar

Ship `wireguard-go` (userspace) per platform, configured star/mesh through
the edge. Strong crypto and roaming, but peer endpoints still need a relay
story under always-relay (WireGuard over TURN TCP is poor), and key
distribution plus mesh reconfiguration add a control plane the lobby does
not have. Revisit if TURN UDP proves unusable.

### C. TAP (L2) virtual ethernet

True broadcast semantics for free, but TAP-Windows6 is deprecated, needs
admin driver installs, and has no macOS story. Rejected.

### D. No interface (proxy helper only)

Does not work: the game binds and browses on adapter addresses. There must
be an interface with the overlay IP.

## Platform matrix (to validate in the spike)

| OS | Interface | Privilege | Notes |
|----|-----------|-----------|-------|
| Linux | `/dev/net/tun` | `CAP_NET_ADMIN` or root helper | Prototype target; containers need `NET_ADMIN`. |
| Windows | WinTun | Admin once (driver install), then user | Ship `wintun.dll`; firewall rules already exist for UDP 16000/16001. |
| macOS | utun | Root helper or NetworkExtension entitlement | Biggest risk: entitlement needs Apple Developer signing; root-helper prompt is the fallback. |

## Validation plan

1. Two Linux nodes with `NET_ADMIN`: TUN + minimal UDP relay pump, static
   peer map, ping across overlay IPs.
2. Packet capture of real ZH LAN discovery + 2-player skirmish over the
   prototype; confirm which broadcast/ports must be relayed.
3. Same capture through TURN allocations (not direct UDP) to prove the
   relay-only path; record latency/loss budget.
4. WinTun bring-up on a Windows VM; utun check on macOS (go/no-go on the
   entitlement question).
5. Success criteria: 2-player LAN skirmish over relay-only transport on
   Linux + Windows, discovery working, no relay bypass.

## Implementation shape (after the spike)

- `IOverlaySidecarLocator` implementations per platform resolve the packet
  pump sidecar; the edge adapter config graduates from `pending-selection`
  to `{ overlay: "tun-turn/1", peers: [...], turn: {...} }`.
- `SharedVirtualLanAdapter.BringUpAsync` starts the sidecar and reports Up;
  the lobby-only path stays as the graceful fallback.
- New milestone, new PR: estimator 2-4 weeks including the Windows driver
  flow and macOS entitlement investigation.
