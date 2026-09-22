import dgram from "node:dgram";

const PORT = Number.parseInt(process.env.RELAY_PORT || "8088", 10);
const server = dgram.createSocket("udp4");

// Map: networkId -> Map(sourceIpStr -> { address, port, lastSeen })
const rooms = new Map();

server.on("error", (err) => {
  console.error("Relay server error:", err);
});

server.on("message", (msg, rinfo) => {
  // Minimum framing: 16-byte networkId, 4-byte targetIp, 4-byte sourceIp, + payload
  if (msg.length < 24) return;

  const networkId = msg.subarray(0, 16).toString("hex");
  const targetIp = `${msg[16]}.${msg[17]}.${msg[18]}.${msg[19]}`;
  const sourceIp = `${msg[20]}.${msg[21]}.${msg[22]}.${msg[23]}`;

  let room = rooms.get(networkId);
  if (!room) {
    room = new Map();
    rooms.set(networkId, room);
  }

  // Track sender endpoint
  room.set(sourceIp, {
    address: rinfo.address,
    port: rinfo.port,
    lastSeen: Date.now(),
  });

  // Keep-alive ping (length exactly 24 bytes with target 0.0.0.0)
  if (msg.length === 24 && targetIp === "0.0.0.0") {
    return;
  }

  const isBroadcast =
    msg[16] === 255 ||
    (msg[16] === 10 && msg[17] === 42 && (msg[18] === 255 || msg[19] === 255));

  if (isBroadcast) {
    // Fan out broadcast packet to all other members in the room
    for (const [peerIp, peer] of room.entries()) {
      if (peerIp !== sourceIp) {
        server.send(msg, peer.port, peer.address);
      }
    }
  } else {
    // Unicast to target peer
    const target = room.get(targetIp);
    if (target) {
      server.send(msg, target.port, target.address);
    }
  }
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
