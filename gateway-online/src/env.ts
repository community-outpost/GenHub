import type { DurableObjectNamespace } from "@cloudflare/workers-types";

export interface OnlineEnv {
  PRESENCE_ROOM: DurableObjectNamespace;
  DIRECTORY_INDEX: DurableObjectNamespace;
  JWT_SIGNING_SECRET: string;
  PASSWORD_PEPPER: string;
  COTURN_SECRET: string;
  SESSION_TTL_SECONDS?: string;
  JOIN_GRANT_TTL_SECONDS?: string;
  TURN_TTL_SECONDS?: string;
  PRESENCE_TIMEOUT_SECONDS?: string;
  EMPTY_NETWORK_TTL_SECONDS?: string;
  JOIN_RATE_LIMIT?: string;
  JOIN_RATE_WINDOW_SECONDS?: string;
  MAX_NETWORKS_PER_IP?: string;
  DIRECTORY_RATE_PER_MIN?: string;
  REPORT_RATE_LIMIT?: string;
  REPORT_RATE_WINDOW_SECONDS?: string;
  OVERLAY_SUBNET?: string;
  TURN_URIS?: string;
  SESSION_RATE_LIMIT?: string;
  SESSION_RATE_WINDOW_SECONDS?: string;
}

export interface RoomMember {
  sub: string;
  displayName: string;
  overlayIp: string;
  quality: number;
  isHost: boolean;
  lastSeen: number;
  endpoint: string;
  lastIp: string;
  profileFingerprint: string;
  profileName: string;
}

export interface PublicMember {
  displayName: string;
  overlayIp: string;
  quality: number;
  isHost: boolean;
  endpoint: string;
  profileFingerprint: string;
  profileName: string;
}

export interface NetworkSummary {
  id: string;
  name: string;
  tags: string[];
  slotsUsed: number;
  slotsMax: number;
  region: string;
  hostDisplayName: string;
  quality: number;
  requiresPassword: boolean;
  isPublic: boolean;
  lastHeartbeatUtc: string;
}

export interface NetworkDetail {
  id: string;
  name: string;
  description: string;
  tags: string[];
  slotsUsed: number;
  slotsMax: number;
  expectedProfileId: string;
  expectedProfileFingerprint: string;
  expectedProfileName: string;
  expectedGameClientId: string;
  expectedContentIds: string[];
  requiresPassword: boolean;
  hostPresent: boolean;
}

export interface RoomMeta {
  id: string;
  name: string;
  description: string;
  tags: string[];
  slotsMax: number;
  isPublic: boolean;
  region: string;
  hostDisplayName: string;
  expectedProfileId: string;
  expectedProfileFingerprint: string;
  expectedProfileName: string;
  expectedGameClientId: string;
  expectedContentIds: string[];
  verifier: string;
  nextSlot: number;
  createdAt: string;
  emptiedUtc: number;
}

export const QUALITY_UNKNOWN = 0;
export const QUALITY_DIRECT = 1;
export const QUALITY_RELAY = 2;
