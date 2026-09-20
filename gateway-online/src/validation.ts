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

export interface CreateNetworkInput {
  name: string;
  password: string;
  slotsMax: number;
  tags: string[];
  isPublic: boolean;
  description: string;
  displayName: string;
  expectedProfileId: string;
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
  const expectedProfileId = raw.expectedProfileId === undefined ? "" : asString(raw.expectedProfileId, 128);
  if (expectedProfileId === null) {
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
    endpoint: parseEndpoint(raw.endpoint),
  };
};

export const parseJoinBody = (
  body: unknown
): { password: string; preferRelay: boolean; displayName: string; endpoint: string } | null => {
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
  return {
    password: raw.password,
    preferRelay: raw.preferRelay === true,
    displayName,
    endpoint: parseEndpoint(raw.endpoint),
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
