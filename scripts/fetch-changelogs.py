#!/usr/bin/env python3
import json
import os
import sys
import time
import urllib.request
from typing import Any, Dict, List, Optional

try:
    from bs4 import BeautifulSoup
except ImportError:
    BeautifulSoup = None

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ROOT_DIR = os.path.dirname(SCRIPT_DIR)
DEFAULT_DATA_DIR = os.path.join(ROOT_DIR, "public", "assets", "data")


def build_asset_entry(asset: Dict[str, Any]) -> Dict[str, Any]:
    return {
        "name": asset.get("name"),
        "download_count": asset.get("download_count", 0),
        "browser_download_url": asset.get("browser_download_url", "")
    }


def build_release_entry(release: Dict[str, Any]) -> Dict[str, Any]:
    return {
        "tag_name": release.get("tag_name"),
        "name": release.get("name"),
        "published_at": release.get("published_at"),
        "html_url": release.get("html_url"),
        "body": release.get("body", ""),
        "prerelease": release.get("prerelease", False),
        "draft": release.get("draft", False),
        "assets": [build_asset_entry(a) for a in release.get("assets", [])]
    }


def fetch_releases_page(page: int) -> List[Any]:
    url = f"https://api.github.com/repos/community-outpost/GenHub/releases?per_page=100&page={page}"
    req = urllib.request.Request(url, headers={"User-Agent": "GenHub-LandingPage"})
    with urllib.request.urlopen(req, timeout=10) as resp:
        data = json.loads(resp.read().decode("utf-8"))
    return data if isinstance(data, list) else []


def collect_github_releases() -> List[Dict[str, Any]]:
    releases: List[Dict[str, Any]] = []
    page = 1
    max_pages = 10  # Cap at 1000 releases

    while page <= max_pages:
        try:
            data = fetch_releases_page(page)
        except Exception as e:
            print(f"Warning: Failed to fetch GitHub releases page {page}: {e}", file=sys.stderr)
            break
        if not data:
            break
        for r in data:
            # Drafts (e.g. the release-drafter staging draft) have no
            # assets and must never surface on the website.
            if r.get("draft", False):
                continue
            releases.append(build_release_entry(r))
        if len(data) < 100:
            break
        page += 1

    return releases


def fetch_github_releases(data_dir: str):
    print("Fetching GitHub releases for community-outpost/GenHub...")
    releases = collect_github_releases()

    out_file = os.path.join(data_dir, "genhub_releases.json")
    try:
        with open(out_file, "w", encoding="utf-8") as f:
            json.dump(releases, f, indent=2)
        print(f"Saved {len(releases)} releases to {out_file}")
    except OSError as e:
        print(f"Error saving releases: {e}", file=sys.stderr)


def fetch_html(url: str, user_agent: str = "Mozilla/5.0") -> str:
    req = urllib.request.Request(url, headers={"User-Agent": user_agent})
    with urllib.request.urlopen(req, timeout=10) as resp:
        return resp.read().decode("utf-8")


def extract_list_items(post_element) -> List[str]:
    details = []
    ul = post_element.find("ul")
    if ul:
        for li in ul.find_all("li"):
            text = li.get_text(strip=True)
            if text:
                details.append(text)
    return details


def extract_paragraph_items(post_element, summary: str) -> List[str]:
    details = []
    for dp in post_element.find_all("p"):
        text = dp.get_text(strip=True)
        if text and text != summary:
            details.append(text)
    return details


def extract_patch_details(full_url: str, summary: str) -> List[str]:
    if not full_url or not BeautifulSoup:
        return []
    try:
        dhtml = fetch_html(full_url)
        dsoup = BeautifulSoup(dhtml, "html.parser")
        dpost = dsoup.find("div", class_="post-text")
        if not dpost:
            return []
        items = extract_list_items(dpost)
        if items:
            return items
        return extract_paragraph_items(dpost, summary)
    except Exception as e:
        print(f"Detail fetch error for {full_url}: {e}", file=sys.stderr)
        return []


def parse_patch_post(post) -> Optional[Dict[str, Any]]:
    date_el = post.find("div", class_="d-date")
    date_str = date_el.get_text(strip=True) if date_el else ""

    h4 = post.find("h4")
    if not h4:
        return None

    a = h4.find("a")
    title = a.get_text(strip=True) if a else h4.get_text(strip=True)
    link = a["href"] if a and a.has_attr("href") else ""
    full_url = ("https://www.playgenerals.online" + link) if link.startswith("/") else link

    p = post.find("p")
    summary = p.get_text(strip=True) if p else ""
    details = extract_patch_details(full_url, summary)

    return {
        "date": date_str,
        "title": title,
        "url": full_url,
        "summary": summary,
        "details": details
    }


def fetch_generals_online_patchnotes(data_dir: str):
    if not BeautifulSoup:
        print("Warning: beautifulsoup4 not installed, skipping playgenerals.online scrape.")
        return

    print("Fetching patchnotes from playgenerals.online/patchnotes...")
    try:
        html = fetch_html("https://www.playgenerals.online/patchnotes")
        soup = BeautifulSoup(html, "html.parser")
        notes = []

        for post in soup.find_all("div", class_="post-text"):
            item = parse_patch_post(post)
            if item:
                notes.append(item)
                time.sleep(0.05)

        out_file = os.path.join(data_dir, "generals_online_patchnotes.json")
        with open(out_file, "w", encoding="utf-8") as f:
            json.dump(notes, f, indent=2)
        print(f"Saved {len(notes)} patch notes to {out_file}")
    except Exception as e:
        print(f"Warning: Failed to fetch patchnotes from playgenerals.online: {e}", file=sys.stderr)


def main():
    target_dir = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_DATA_DIR
    os.makedirs(target_dir, exist_ok=True)
    fetch_github_releases(target_dir)
    fetch_generals_online_patchnotes(target_dir)


if __name__ == "__main__":
    main()
