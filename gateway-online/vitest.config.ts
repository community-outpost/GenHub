import { defineWorkersConfig } from "@cloudflare/vitest-pool-workers/config";

export default defineWorkersConfig({
  test: {
    poolOptions: {
      workers: {
        main: "./src/index.ts",
        // Stays false: vitest-pool-workers 0.8 aborts with "Isolated storage
        // failed" when DO storage stacks between suites. Tests share one IP
        // inside the file, so the creation cap stays above the file's total
        // creates; join attempts are per-room and run at the production limit.
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
            JOIN_RATE_LIMIT: "10",
            JOIN_RATE_WINDOW_SECONDS: "600",
            SESSION_RATE_LIMIT: "5",
            SESSION_RATE_WINDOW_SECONDS: "60",
            // Stays above the file's total creates: without an explicit
            // CF-Connecting-IP every create here shares one client IP, so the
            // cap must clear the whole suite. The per-IP limiter itself is
            // proven below with a dedicated quota IP driven to this exact cap.
            MAX_NETWORKS_PER_IP: "100",
            DIRECTORY_RATE_PER_MIN: "600",
            EMPTY_NETWORK_TTL_SECONDS: "300",
            OVERLAY_SUBNET: "10.42.0.0/20", // NOSONAR - private test overlay range
            TURN_URIS: "turn:turn.example.invalid:3478",
          },
        },
      },
    },
  },
});
