import dgram from "node:dgram";

const PORT = Number.parseInt(process.env.RELAY_PORT || "8088", 10);
const server = dgram.createSocket("udp4");

// Map: networkId -> Map(sourceIpStr -> { address, port, lastSeen })
// Entries are keyed by the *claimed* source IP inside the frame, so a
// rotating sender can mint them without bound. Both levels are capped;
// beyond the cap new senders are dropped until cleanup reclaims entries.
const rooms = new Map();
const MAX_ROOMS = Number.parseInt(process.env.RELAY_MAX_ROOMS ?? "", 10) || 10000;
const MAX_PEERS_PER_ROOM = Number.parseInt(process.env.RELAY_MAX_PEERS_PER_ROOM ?? "", 10) || 64;

server.on("error", (err) => {
  console.error("Relay server error:", err);
  try {
    server.close();
  } catch {
    // Ignore close errors during fatal error handling
  }
  process.exit(1);
});

// Minimum framing: 16-byte networkId, 4-byte targetIp, 4-byte sourceIp, + payload
const parseFrame = (msg) => {
  if (msg.length < 24) {
    return null;
  }
  const targetIp = `${msg[16]}.${msg[17]}.${msg[18]}.${msg[19]}`;
  return {
    networkId: msg.subarray(0, 16).toString("hex"),
    targetIp,
    sourceIp: `${msg[20]}.${msg[21]}.${msg[22]}.${msg[23]}`,
    isKeepAlive: msg.length === 24 && targetIp === "0.0.0.0",
    isBroadcast:
      msg[16] === 255 ||
      (msg[16] === 10 && msg[17] === 42 && (msg[18] === 255 || msg[19] === 255)),
  };
};

const getOrCreateRoom = (networkId) => {
  let room = rooms.get(networkId);
  if (!room) {
    if (rooms.size >= MAX_ROOMS) {
      return null;
    }
    room = new Map();
    rooms.set(networkId, room);
  }
  return room;
};

// Track sender endpoint
const trackSender = (room, sourceIp, rinfo) => {
  if (!room.has(sourceIp) && room.size >= MAX_PEERS_PER_ROOM) {
    return false;
  }
  room.set(sourceIp, {
    address: rinfo.address,
    port: rinfo.port,
    lastSeen: Date.now(),
  });
  return true;
};

const forwardPacket = (room, frame, msg) => {
  if (frame.isBroadcast) {
    // Fan out broadcast packet to all other members in the room
    for (const [peerIp, peer] of room.entries()) {
      if (peerIp !== frame.sourceIp) {
        server.send(msg, peer.port, peer.address);
      }
    }
    return;
  }
  // Unicast to target peer
  const target = room.get(frame.targetIp);
  if (target) {
    server.send(msg, target.port, target.address);
  }
};

server.on("message", (msg, rinfo) => {
  const frame = parseFrame(msg);
  if (frame === null) {
    return;
  }
  const room = getOrCreateRoom(frame.networkId);
  if (room === null || !trackSender(room, frame.sourceIp, rinfo)) {
    return;
  }
  // Keep-alive ping (length exactly 24 bytes with target 0.0.0.0)
  if (frame.isKeepAlive) {
    return;
  }
  forwardPacket(room, frame, msg);
});

// Periodic cleanup of stale endpoints (> 60 seconds of inactivity)
setInterval(() => {
  const now = Date.now();
  for (const [netId, room] of rooms.entries()) {
    for (const [ip, peer] of room.entries()) {
      if (now - peer.lastSeen > 60000) {
        room.delete(ip);
      }
    }
    if (room.size === 0) {
      rooms.delete(netId);
    }
  }
}, 10000);

server.bind(PORT, "0.0.0.0", () => {
  console.log(`GenHub UDP Packet Relay listening on 0.0.0.0:${PORT}`);
});
