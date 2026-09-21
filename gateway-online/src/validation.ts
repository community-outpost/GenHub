// Pure request validators. Error codes mirror GenHub.Core OnlineConstants.

export const MIN_NAME_LENGTH = 3;
export const MAX_NAME_LENGTH = 64;
export const MIN_PASSWORD_LENGTH = 4;
export const MAX_PASSWORD_LENGTH = 128;
export const MIN_SLOTS = 2;
export const MAX_SLOTS = 16;
export const MAX_TAGS = 8;
export const MAX_TAG_LENGTH = 32;
export const MAX_DESCRIPTION_LENGTH = 1024;
export const MAX_DISPLAY_NAME_LENGTH = 32;
export const MAX_PROFILE_NAME_LENGTH = 64;
export const MAX_FINGERPRINT_LENGTH = 256;
export const MAX_CONTENT_IDS = 32;
export const MAX_CONTENT_ID_LENGTH = 128;

export interface CreateNetworkInput {
  name: string;
  password: string;
  slotsMax: number;
  tags: string[];
  isPublic: boolean;
  description: string;
  displayName: string;
  expectedProfileId: string;
  expectedProfileFingerprint: string;
  expectedProfileName: string;
  expectedGameClientId: string;
  expectedContentIds: string[];
  profileFingerprint: string;
  profileName: string;
  preferRelay: boolean;
  endpoint: string;
}

const CONTROL_CHARS_REGEX = /\p{Cc}/gu;

export const sanitizeText = (value: string): string => value.replaceAll(CONTROL_CHARS_REGEX, "").trim();

const asString = (value: unknown, maxLength: number): string | null => {
  if (typeof value !== "string") {
    return null;
  }
  const trimmed = sanitizeText(value);
  if (trimmed.length > maxLength) {
    return null;
  }
  return trimmed;
};

const asStringList = (value: unknown): string[] | null => {
  if (!Array.isArray(value)) {
    return null;
  }
  if (value.length > MAX_TAGS) {
    return null;
  }
  const tags: string[] = [];
  for (const entry of value) {
    const tag = asString(entry, MAX_TAG_LENGTH);
    if (tag === null || tag.length === 0) {
      return null;
    }
    tags.push(tag);
  }
  return tags;
};

const asBoundedString = (value: unknown, maxLength: number): string | null => {
  if (value === undefined) {
    return "";
  }
  return asString(value, maxLength);
};

const asContentIdList = (value: unknown): string[] | null => {
  if (value === undefined) {
    return [];
  }
  if (!Array.isArray(value) || value.length > MAX_CONTENT_IDS) {
    return null;
  }
  const ids: string[] = [];
  for (const entry of value) {
    const id = asString(entry, MAX_CONTENT_ID_LENGTH);
    if (id === null || id.length === 0) {
      return null;
    }
    ids.push(id);
  }
  return ids;
};

export const parseCreateNetwork = (body: unknown): CreateNetworkInput | null => {
  if (body === null || typeof body !== "object") {
    return null;
  }
  const raw = body as Record<string, unknown>;

  const name = asString(raw.name, MAX_NAME_LENGTH);
  if (name === null || name.length < MIN_NAME_LENGTH) {
    return null;
  }
  if (typeof raw.password !== "string" || raw.password.length > MAX_PASSWORD_LENGTH) {
    return null;
  }
  if (typeof raw.slotsMax !== "number" || !Number.isInteger(raw.slotsMax) || raw.slotsMax < MIN_SLOTS || raw.slotsMax > MAX_SLOTS) {
    return null;
  }
  const tags = raw.tags === undefined ? [] : asStringList(raw.tags);
  if (tags === null) {
    return null;
  }
  const description = raw.description === undefined ? "" : asString(raw.description, MAX_DESCRIPTION_LENGTH);
  if (description === null) {
    return null;
  }
  const displayName = raw.displayName === undefined ? "" : asString(raw.displayName, MAX_DISPLAY_NAME_LENGTH);
  if (displayName === null) {
    return null;
  }
  const expectedProfileId = asBoundedString(raw.expectedProfileId, MAX_CONTENT_ID_LENGTH);
  const expectedProfileFingerprint = asBoundedString(raw.expectedProfileFingerprint, MAX_FINGERPRINT_LENGTH);
  const expectedProfileName = asBoundedString(raw.expectedProfileName, MAX_PROFILE_NAME_LENGTH);
  const expectedGameClientId = asBoundedString(raw.expectedGameClientId, MAX_CONTENT_ID_LENGTH);
  const expectedContentIds = asContentIdList(raw.expectedContentIds);
  const profileFingerprint = asBoundedString(raw.profileFingerprint, MAX_FINGERPRINT_LENGTH);
  const profileName = asBoundedString(raw.profileName, MAX_PROFILE_NAME_LENGTH);
  if (
    expectedProfileId === null ||
    expectedProfileFingerprint === null ||
    expectedProfileName === null ||
    expectedGameClientId === null ||
    expectedContentIds === null ||
    profileFingerprint === null ||
    profileName === null
  ) {
    return null;
  }

  return {
    name,
    password: raw.password,
    slotsMax: raw.slotsMax,
    tags,
    isPublic: raw.isPublic === true,
    description,
    displayName,
    expectedProfileId,
    expectedProfileFingerprint,
    expectedProfileName,
    expectedGameClientId,
    expectedContentIds,
    profileFingerprint,
    profileName,
    preferRelay: raw.preferRelay === true,
    endpoint: parseEndpoint(raw.endpoint),
  };
};

export const parseJoinBody = (
  body: unknown
): {
  password: string;
  preferRelay: boolean;
  displayName: string;
  endpoint: string;
  profileFingerprint: string;
  profileName: string;
} | null => {
  if (body === null || typeof body !== "object") {
    return null;
  }
  const raw = body as Record<string, unknown>;
  if (typeof raw.password !== "string" || raw.password.length > MAX_PASSWORD_LENGTH) {
    return null;
  }
  const displayName = raw.displayName === undefined ? "" : asString(raw.displayName, MAX_DISPLAY_NAME_LENGTH);
  if (displayName === null) {
    return null;
  }
  const profileFingerprint = asBoundedString(raw.profileFingerprint, MAX_FINGERPRINT_LENGTH);
  const profileName = asBoundedString(raw.profileName, MAX_PROFILE_NAME_LENGTH);
  if (profileFingerprint === null || profileName === null) {
    return null;
  }
  return {
    password: raw.password,
    preferRelay: raw.preferRelay === true,
    displayName,
    endpoint: parseEndpoint(raw.endpoint),
    profileFingerprint,
    profileName,
  };
};

const ENDPOINT_REGEX = /^(?:\d{1,3}(?:\.\d{1,3}){3}|\[[0-9a-fA-F:.]+\]):\d{1,5}$/;
const MAX_ENDPOINT_LENGTH = 64;

export const parseEndpoint = (value: unknown): string => {
  if (typeof value !== "string" || value.length === 0 || value.length > MAX_ENDPOINT_LENGTH) {
    return "";
  }
  const trimmed = value.trim();
  if (!ENDPOINT_REGEX.test(trimmed)) {
    return "";
  }
  const port = Number(trimmed.substring(trimmed.lastIndexOf(":") + 1));
  if (!Number.isSafeInteger(port) || port < 1 || port > 65535) {
    return "";
  }
  return trimmed;
};

export const defaultDisplayName = (sub: string): string => `Player-${sub.replaceAll("-", "").substring(0, 6)}`;

export const OUTCOME_DIRECT = "direct";
export const OUTCOME_RELAY = "relay";
export const OUTCOME_FAILED = "failed";
const MAX_OVERLAY_IP_LENGTH = 64;

export const parseOutcomeBody = (
  body: unknown
): { targetIp: string; direct: boolean; outcome: string } | null => {
  if (body === null || typeof body !== "object") {
    return null;
  }
  const raw = body as Record<string, unknown>;
  if (typeof raw.targetIp !== "string" || raw.targetIp.length === 0 || raw.targetIp.length > MAX_OVERLAY_IP_LENGTH) {
    return null;
  }
  if (raw.outcome !== OUTCOME_DIRECT && raw.outcome !== OUTCOME_RELAY && raw.outcome !== OUTCOME_FAILED) {
    return null;
  }
  return { targetIp: raw.targetIp, direct: raw.direct === true, outcome: raw.outcome };
};
