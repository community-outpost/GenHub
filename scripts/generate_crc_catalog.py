#!/usr/bin/env python3
"""
GameClient CRC Catalog Generator for GenHub Replay Manager.
Crawls TheSuperHackers (GitHub Releases) and GeneralsOnline (CDN) to build and update crc-mapping.json.
"""

import argparse
import datetime
import hashlib
import json
import os
import re
import sys
import urllib.request
import zlib

SUPERHACKERS_REPO = "TheSuperHackers/GeneralsGameCode"
GENERALSONLINE_CDN = "https://cdn.playgenerals.online"
DEFAULT_OUTPUT_PATH = os.path.join(
    os.path.dirname(__file__), "..", "GenHub", "GenHub", "Resources", "crc-mapping.json"
)

BASELINE_ENTRIES = [
    {
        "exeCrc": "0x401D89EA",
        "iniCrc": "0x76B251A3",
        "sha256": "7B075B9F0BAA9DF81651C0C9DD7D8C445454AE1B2452B928F4A1D9332E9CCECE",
        "manifestId": "1.104.steam.gameclient.zerohour",
        "publisher": "steam",
        "gameType": "ZeroHour",
        "version": "1.04",
        "buildDate": "2003-09-16",
        "description": "Official Steam Zero Hour 1.04",
        "cdnUrl": None,
    },
    {
        "exeCrc": "0x401D89EA",
        "iniCrc": "0x76B251A3",
        "sha256": "f37a4929f8d697104e99c2bcf46f8d833122c943afcd87fd077df641d344495b",
        "manifestId": "1.104.ea.gameclient.zerohour",
        "publisher": "ea",
        "gameType": "ZeroHour",
        "version": "1.04",
        "buildDate": "2003-09-16",
        "description": "Official Retail Zero Hour 1.04",
        "cdnUrl": None,
    },
    {
        "exeCrc": "0x89C1F821",
        "iniCrc": "0x323577BD",
        "sha256": "3B8580D1A1F93A96EBE430AA5D43048995393CE322DF20B2F2A1FD58DC0B3A79",
        "manifestId": "1.108.steam.gameclient.generals",
        "publisher": "steam",
        "gameType": "Generals",
        "version": "1.08",
        "buildDate": "2003-02-11",
        "description": "Official Steam Generals 1.08",
        "cdnUrl": None,
    },
]


def normalize_hex(val: str) -> str:
    """Normalizes a hex string to 0x uppercase format."""
    if not val:
        return ""
    val = val.strip()
    if val.startswith(("0x", "0X")):
        val = val[2:]
    return f"0x{val.upper()}"


def crawl_superhackers_releases(token: str | None = None) -> list[dict]:
    """Fetches and maps releases from TheSuperHackers repository on GitHub."""
    url = f"https://api.github.com/repos/{SUPERHACKERS_REPO}/releases?per_page=100"
    headers = {"User-Agent": "GenHub-Replay-Crawler"}
    if token:
        headers["Authorization"] = f"token {token}"

    req = urllib.request.Request(url, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=15) as resp:
            releases = json.loads(resp.read().decode("utf-8"))
    except Exception as e:
        print(f"Warning: could not reach GitHub API ({e}). Falling back to cached catalog.", file=sys.stderr)
        return []

    entries = []
    for rel in releases:
        tag = rel.get("tag_name", "")
        # Match weekly tags (e.g., weekly-2026-08-21)
        m = re.match(r"weekly-(\d{4}-\d{2}-\d{2})", tag)
        if not m:
            continue
        date_str = m.group(1)
        version_num = date_str.replace("-", "")

        for asset in rel.get("assets", []):
            name = asset.get("name", "")
            download_url = asset.get("browser_download_url", "")
            if name.endswith(".zip") and "generals" in name.lower():
                game_type = "ZeroHour" if "zh" in name.lower() else "Generals"
                manifest_type = "zerohour" if game_type == "ZeroHour" else "generals"
                manifest_id = f"1.{version_num}.superhackers.gameclient.{manifest_type}"

                entry = {
                    "exeCrc": "",
                    "iniCrc": "0x76B251A3" if game_type == "ZeroHour" else "0x323577BD",
                    "sha256": "",
                    "manifestId": manifest_id,
                    "publisher": "superhackers",
                    "gameType": game_type,
                    "version": date_str,
                    "buildDate": date_str,
                    "description": f"TheSuperHackers {game_type} weekly {date_str}",
                    "cdnUrl": download_url,
                }
                entries.append(entry)

    return entries


def compute_buffer_crc(data: bytes) -> str:
    """Computes CRC-32 for raw binary data and formats as hex string."""
    return f"0x{zlib.crc32(data) & 0xFFFFFFFF:08X}"


def compute_buffer_sha256(data: bytes) -> str:
    """Computes SHA-256 for raw binary data."""
    return hashlib.sha256(data).hexdigest()


def merge_catalogs(existing: list[dict], crawled: list[dict]) -> list[dict]:
    """Merges new crawled entries into existing catalog, preserving known CRCs and hashes."""
    by_manifest = {entry["manifestId"]: dict(entry) for entry in existing if "manifestId" in entry}

    for item in crawled:
        m_id = item.get("manifestId")
        if not m_id:
            continue
        if m_id in by_manifest:
            existing_entry = by_manifest[m_id]
            if item.get("cdnUrl"):
                existing_entry["cdnUrl"] = item["cdnUrl"]
            if not existing_entry.get("exeCrc") and item.get("exeCrc"):
                existing_entry["exeCrc"] = normalize_hex(item["exeCrc"])
            if not existing_entry.get("sha256") and item.get("sha256"):
                existing_entry["sha256"] = item["sha256"]
        else:
            by_manifest[m_id] = item

    return list(by_manifest.values())


def validate_catalog(catalog: dict) -> bool:
    """Validates the structure and entries of the CRC mapping catalog."""
    if not isinstance(catalog, dict):
        print("Validation error: catalog must be a JSON object", file=sys.stderr)
        return False

    if "mappings" not in catalog or not isinstance(catalog["mappings"], list):
        print("Validation error: 'mappings' array missing", file=sys.stderr)
        return False

    valid = True
    seen_manifest_ids = set()
    for idx, entry in enumerate(catalog["mappings"]):
        m_id = entry.get("manifestId")
        if not m_id:
            print(f"Validation error at mapping index {idx}: missing manifestId", file=sys.stderr)
            valid = False
        elif m_id in seen_manifest_ids:
            print(f"Validation warning: duplicate manifestId {m_id}", file=sys.stderr)
        seen_manifest_ids.add(m_id)

        if not entry.get("publisher"):
            print(f"Validation error at {m_id}: missing publisher", file=sys.stderr)
            valid = False

        if not entry.get("gameType") or entry["gameType"] not in ("Generals", "ZeroHour"):
            print(f"Validation error at {m_id}: invalid gameType {entry.get('gameType')}", file=sys.stderr)
            valid = False

        exe_crc = entry.get("exeCrc")
        if exe_crc and not re.match(r"^0x[0-9A-Fa-f]{8}$", exe_crc):
            print(f"Validation error at {m_id}: invalid exeCrc format '{exe_crc}'", file=sys.stderr)
            valid = False

    return valid


def build_catalog(output_path: str = DEFAULT_OUTPUT_PATH, crawl: bool = False) -> dict:
    """Builds and writes the complete CRC catalog."""
    existing_mappings = list(BASELINE_ENTRIES)

    if os.path.exists(output_path):
        try:
            with open(output_path, "r", encoding="utf-8") as f:
                loaded = json.load(f)
                if isinstance(loaded, dict) and "mappings" in loaded:
                    existing_mappings = merge_catalogs(existing_mappings, loaded["mappings"])
        except Exception as e:
            print(f"Warning: could not read existing catalog: {e}", file=sys.stderr)

    if crawl:
        crawled = crawl_superhackers_releases()
        if crawled:
            existing_mappings = merge_catalogs(existing_mappings, crawled)

    catalog = {
        "schemaVersion": 1,
        "lastUpdated": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "totalEntries": len(existing_mappings),
        "mappings": existing_mappings,
    }

    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(catalog, f, indent=2)
        f.write("\n")

    print(f"Successfully generated CRC mapping catalog at {output_path} with {len(existing_mappings)} entries.")
    return catalog


def main():
    parser = argparse.ArgumentParser(description="GenHub GameClient CRC Catalog Generator")
    parser.add_argument("--output", "-o", default=DEFAULT_OUTPUT_PATH, help="Output path for crc-mapping.json")
    parser.add_argument("--crawl", action="store_true", help="Crawl upstream sources for latest releases")
    parser.add_argument("--validate", action="store_true", help="Validate existing catalog")

    args = parser.parse_args()

    if args.validate:
        if not os.path.exists(args.output):
            print(f"Catalog file not found: {args.output}", file=sys.stderr)
            sys.exit(1)
        with open(args.output, "r", encoding="utf-8") as f:
            cat = json.load(f)
        if validate_catalog(cat):
            print(f"Catalog at {args.output} is valid with {len(cat.get('mappings', []))} entries.")
            sys.exit(0)
        else:
            sys.exit(1)

    build_catalog(output_path=args.output, crawl=args.crawl)


if __name__ == "__main__":
    main()
