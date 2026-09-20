import { defineWorkersConfig } from "@cloudflare/vitest-pool-workers/config";

export default defineWorkersConfig({
  test: {
    poolOptions: {
      workers: {
        main: "./src/index.ts",
        isolatedStorage: false,
        miniflare: {
          compatibilityDate: "2025-01-01",
          compatibilityFlags: ["nodejs_compat"],
          durableObjects: {
            PRESENCE_ROOM: "PresenceRoom",
            DIRECTORY_INDEX: "DirectoryIndex",
          },
          bindings: {
            JWT_SIGNING_SECRET: "test-jwt-signing-secret",
            PASSWORD_PEPPER: "test-password-pepper",
            COTURN_SECRET: "test-coturn-secret",
            SESSION_TTL_SECONDS: "3600",
            JOIN_GRANT_TTL_SECONDS: "600",
            TURN_TTL_SECONDS: "1800",
            PRESENCE_TIMEOUT_SECONDS: "90",
            JOIN_RATE_LIMIT: "100",
            JOIN_RATE_WINDOW_SECONDS: "600",
            MAX_NETWORKS_PER_IP: "100",
            OVERLAY_SUBNET: "10.42.0.0/20",
            TURN_URIS: "turn:turn.example.invalid:3478",
          },
        },
      },
    },
  },
});
