#!/usr/bin/env python3
"""
GameClient CRC Catalog Generator for GenHub Replay Manager.
Crawls TheSuperHackers (GitHub Releases) and GeneralsOnline (CDN) to build and update crc-mapping.json.
"""

import argparse
import concurrent.futures
import datetime
import hashlib
import io
import json
import os
import re
import sys
import urllib.request
import zipfile
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


def compute_buffer_crc(data: bytes) -> str:
    """Computes CRC-32 for raw binary data and formats as uppercase hex string."""
    return f"0x{zlib.crc32(data) & 0xFFFFFFFF:08X}"


def compute_buffer_sha256(data: bytes) -> str:
    """Computes SHA-256 for raw binary data."""
    return hashlib.sha256(data).hexdigest()


def check_url_exists(url: str, timeout: int = 5) -> bool:
    """Checks whether a remote URL exists using a HEAD request."""
    req = urllib.request.Request(url, method="HEAD", headers={"User-Agent": "GenHub-Replay-Crawler"})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return resp.status == 200
    except OSError:
        return False


def inspect_archive_binary(download_url: str, binary_patterns: list[str]) -> tuple[str, str, str]:
    """Downloads archive into memory and extracts CRC32, SHA256, and INI CRC."""
    req = urllib.request.Request(download_url, headers={"User-Agent": "GenHub-Replay-Crawler"})
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            archive_data = resp.read()

        with zipfile.ZipFile(io.BytesIO(archive_data)) as zf:
            exe_crc = ""
            sha256 = ""
            ini_crc = ""

            for name in zf.namelist():
                base_name = os.path.basename(name).lower()
                for pattern in binary_patterns:
                    if base_name == pattern.lower():
                        binary_bytes = zf.read(name)
                        exe_crc = compute_buffer_crc(binary_bytes)
                        sha256 = compute_buffer_sha256(binary_bytes)
                        break
                if not ini_crc and base_name in ("mapcachego.ini", "generals.ini"):
                    ini_bytes = zf.read(name)
                    ini_crc = compute_buffer_crc(ini_bytes)

            return exe_crc, sha256, ini_crc
    except (OSError, zipfile.BadZipFile) as e:
        print(f"Warning: could not inspect archive {download_url}: {e}", file=sys.stderr)
        return "", "", ""


def fetch_github_releases(repo: str, token: str | None = None) -> list[dict]:
    """Fetches all release records from a GitHub repository."""
    headers = {"User-Agent": "GenHub-Replay-Crawler"}
    if token:
        headers["Authorization"] = f"token {token}"

    releases = []
    page = 1
    while True:
        url = f"https://api.github.com/repos/{repo}/releases?per_page=100&page={page}"
        req = urllib.request.Request(url, headers=headers)
        try:
            with urllib.request.urlopen(req, timeout=15) as resp:
                page_data = json.loads(resp.read().decode("utf-8"))
                link_header = resp.headers.get("Link", "")
            if not page_data or not isinstance(page_data, list):
                break
            releases.extend(page_data)
            if 'rel="next"' not in link_header:
                break
            page += 1
        except (OSError, json.JSONDecodeError) as e:
            print(f"Warning: could not reach GitHub API page {page} ({e}). Falling back to cached catalog.", file=sys.stderr)
            break

    return releases


def parse_superhackers_asset(date_str: str, version_num: str, asset: dict, inspect_binaries: bool) -> dict | None:
    """Parses a single release asset into a mapping entry if it is a relevant gameclient zip."""
    name = asset.get("name", "")
    download_url = asset.get("browser_download_url", "")
    if not (name.endswith(".zip") and "generals" in name.lower()):
        return None

    game_type = "ZeroHour" if "zh" in name.lower() else "Generals"
    manifest_type = "zerohour" if game_type == "ZeroHour" else "generals"
    manifest_id = f"1.{version_num}.superhackers.gameclient.{manifest_type}"
    default_ini = "0x76B251A3" if game_type == "ZeroHour" else "0x323577BD"

    exe_crc = ""
    sha256 = ""
    ini_crc = default_ini

    if inspect_binaries and download_url:
        target_bin = ["generalszh.exe"] if game_type == "ZeroHour" else ["generals.exe"]
        c_exe, c_sha, c_ini = inspect_archive_binary(download_url, target_bin)
        if c_exe:
            exe_crc = c_exe
        if c_sha:
            sha256 = c_sha
        if c_ini:
            ini_crc = c_ini

    return {
        "exeCrc": exe_crc,
        "iniCrc": ini_crc,
        "sha256": sha256,
        "manifestId": manifest_id,
        "publisher": "superhackers",
        "gameType": game_type,
        "version": date_str,
        "buildDate": date_str,
        "description": f"TheSuperHackers {game_type} weekly {date_str}",
        "cdnUrl": download_url,
    }


def crawl_superhackers_releases(token: str | None = None, inspect_binaries: bool = False) -> list[dict]:
    """Fetches and maps releases from TheSuperHackers repository on GitHub across all pages (2025, 2026, etc.)."""
    releases = fetch_github_releases(SUPERHACKERS_REPO, token)
    if not releases:
        return []

    entries = []
    for rel in releases:
        tag = rel.get("tag_name", "")
        m = re.match(r"weekly-(\d{4}-\d{2}-\d{2})", tag)
        if not m:
            continue
        date_str = m.group(1)
        version_num = date_str.replace("-", "")

        for asset in rel.get("assets", []):
            entry = parse_superhackers_asset(date_str, version_num, asset, inspect_binaries)
            if entry:
                entries.append(entry)

    return entries


def generate_date_codes(start_year: int, end_year: int) -> set[str]:
    """Generates all valid MMDDYY date codes within the given year range."""
    date_set = {"021326", "032926", "042826", "060526", "062026", "081326"}
    try:
        curr_date = datetime.date(start_year, 1, 1)
        target_end = min(datetime.date(end_year, 12, 31), datetime.date.today() + datetime.timedelta(days=7))
        while curr_date <= target_end:
            date_set.add(curr_date.strftime("%m%d%y"))
            curr_date += datetime.timedelta(days=1)
    except (ValueError, OverflowError):
        pass
    return date_set


def _build_variant_candidate(date_code: str, qfe: int | None, is_eac: bool) -> tuple[str, str, str, str]:
    qfe_part = f"_QFE{qfe}" if qfe is not None else ""
    eac_part = "_EAC" if is_eac else ""
    suffix = f"{qfe_part}{eac_part}"
    zip_name = f"GeneralsOnline_portable_{date_code}{suffix}.zip"
    url = f"{GENERALSONLINE_CDN}/{zip_name}"
    manifest_type = "eac.zerohour" if is_eac else "zerohour"
    qfe_digit = str(qfe) if qfe is not None else "0"
    manifest_id = f"1.{date_code}{qfe_digit}.generalsonline.gameclient.{manifest_type}"
    version_str = f"{date_code}{suffix}"
    return (date_code, version_str, manifest_id, url)


def build_candidates_for_date(date_code: str) -> list[tuple[str, str, str, str]]:
    """Builds base and QFE candidate descriptors for a single date code."""
    candidates = [
        _build_variant_candidate(date_code, qfe=None, is_eac=False),
        _build_variant_candidate(date_code, qfe=None, is_eac=True),
    ]
    for qfe in range(1, 10):
        candidates.append(_build_variant_candidate(date_code, qfe=qfe, is_eac=False))
        candidates.append(_build_variant_candidate(date_code, qfe=qfe, is_eac=True))
    return candidates


def generate_generalsonline_candidates(start_year: int, end_year: int) -> list[tuple[str, str, str, str]]:
    """Generates candidate (date_code, version_str, manifest_id, url) tuples for GeneralsOnline."""
    date_set = generate_date_codes(start_year, end_year)
    candidates = []
    for date_code in sorted(date_set):
        candidates.extend(build_candidates_for_date(date_code))
    return candidates


def filter_available_candidates(candidates: list[tuple[str, str, str, str]]) -> list[tuple[str, str, str, str]]:
    """Probes candidate URLs concurrently and returns reachable ones."""
    valid_candidates = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=16) as executor:
        future_to_cand = {executor.submit(check_url_exists, cand[3]): cand for cand in candidates}
        for future in concurrent.futures.as_completed(future_to_cand):
            cand = future_to_cand[future]
            try:
                if future.result():
                    valid_candidates.append(cand)
            except OSError as e:
                print(f"Warning: error probing candidate {cand[3]}: {e}", file=sys.stderr)
    return valid_candidates


def build_generalsonline_entry(cand: tuple[str, str, str, str], inspect_binaries: bool) -> dict | None:
    """Builds a single catalog entry from a verified GeneralsOnline release candidate."""
    date_code, version_str, manifest_id, url = cand
    exe_crc = ""
    sha256 = ""
    ini_crc = "0x5CB7992C"

    if inspect_binaries:
        c_exe, c_sha, c_ini = inspect_archive_binary(url, ["generalsonlinezh_60.exe", "generalsonlinezh.exe"])
        if not c_exe:
            return None
        exe_crc = c_exe
        sha256 = c_sha
        if c_ini:
            ini_crc = c_ini

    year = f"20{date_code[4:6]}"
    month = date_code[0:2]
    day = date_code[2:4]
    build_date = f"{year}-{month}-{day}"

    return {
        "exeCrc": exe_crc,
        "iniCrc": ini_crc,
        "sha256": sha256,
        "manifestId": manifest_id,
        "publisher": "generalsonline",
        "gameType": "ZeroHour",
        "version": version_str,
        "buildDate": build_date,
        "description": f"GeneralsOnline {version_str} portable release",
        "cdnUrl": url,
    }


def crawl_generalsonline_releases(inspect_binaries: bool = False, start_year: int = 2025, end_year: int = 2026) -> list[dict]:
    """Probes and maps portable releases from the GeneralsOnline CDN across 2025, 2026, and all QFE variants."""
    candidates = generate_generalsonline_candidates(start_year, end_year)
    valid_candidates = filter_available_candidates(candidates)

    entries = []
    for cand in valid_candidates:
        entry = build_generalsonline_entry(cand, inspect_binaries)
        if entry:
            entries.append(entry)

    return entries


def _update_existing_entry(existing: dict, incoming: dict) -> None:
    """Updates missing metadata and checksums on an existing catalog entry."""
    if incoming.get("cdnUrl"):
        existing["cdnUrl"] = incoming["cdnUrl"]
    for key in ("exeCrc", "iniCrc"):
        if not existing.get(key) and incoming.get(key):
            existing[key] = normalize_hex(incoming[key])
    if not existing.get("sha256") and incoming.get("sha256"):
        existing["sha256"] = incoming["sha256"]


def merge_catalogs(existing: list[dict], crawled: list[dict]) -> list[dict]:
    """Merges new crawled entries into existing catalog, preserving known CRCs and hashes."""
    by_manifest = {entry["manifestId"]: dict(entry) for entry in existing if "manifestId" in entry}

    for item in crawled:
        m_id = item.get("manifestId")
        if not m_id:
            continue
        if m_id in by_manifest:
            _update_existing_entry(by_manifest[m_id], item)
        elif item.get("exeCrc"):
            by_manifest[m_id] = dict(item)

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


def build_catalog(output_path: str = DEFAULT_OUTPUT_PATH, crawl: bool = False, inspect_binaries: bool = False) -> dict:
    """Builds and writes the complete CRC catalog."""
    existing_mappings = list(BASELINE_ENTRIES)

    if os.path.exists(output_path):
        try:
            with open(output_path, "r", encoding="utf-8") as f:
                loaded = json.load(f)
                if isinstance(loaded, dict) and "mappings" in loaded:
                    existing_mappings = merge_catalogs(existing_mappings, loaded["mappings"])
        except (OSError, json.JSONDecodeError) as e:
            print(f"Warning: could not read existing catalog: {e}", file=sys.stderr)

    if crawl:
        sh_crawled = crawl_superhackers_releases(inspect_binaries=inspect_binaries)
        if sh_crawled:
            existing_mappings = merge_catalogs(existing_mappings, sh_crawled)

        go_crawled = crawl_generalsonline_releases(inspect_binaries=inspect_binaries)
        if go_crawled:
            existing_mappings = merge_catalogs(existing_mappings, go_crawled)

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
    parser.add_argument("--inspect-binaries", action="store_true", help="Download archives and calculate CRC32/SHA256 from binaries")
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

    build_catalog(output_path=args.output, crawl=args.crawl, inspect_binaries=args.inspect_binaries)


if __name__ == "__main__":
    main()
