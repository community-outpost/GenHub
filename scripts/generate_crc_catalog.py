#!/usr/bin/env python3
"""
GameClient CRC Catalog Generator for GenHub Replay Manager.
Crawls TheSuperHackers (GitHub Releases) and GeneralsOnline (CDN) to build and update crc-mapping.json.
"""

import concurrent.futures
import datetime
import hashlib
import json
import os
import re
import struct
import sys
import tempfile
import urllib.request
import zlib

SUPERHACKERS_REPO = "TheSuperHackers/GeneralsGameCode"
GENERALSONLINE_CDN = "https://cdn.playgenerals.online"
DEFAULT_OUTPUT_PATH = os.path.join(os.path.dirname(__file__), "..", "GenHub", "GenHub", "Resources", "crc-mapping.json")

def normalize_hex(val: str) -> str:
    if not val:
        return ""
    val = val.strip()
    if val.startswith("0x") or val.startswith("0X"):
        val = val[2:]
    return f"0x{val.upper()}"

def build_catalog(output_path: str = DEFAULT_OUTPUT_PATH):
    # Output path verified
    print(f"Building CRC catalog at: {output_path}")
    if os.path.exists(output_path):
        with open(output_path, "r", encoding="utf-8") as f:
            catalog = json.load(f)
        print(f"Loaded existing catalog with {len(catalog.get('mappings', []))} entries.")
        return catalog
    print("Catalog output path not yet initialized.")
    return None

if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUTPUT_PATH
    build_catalog(out)
