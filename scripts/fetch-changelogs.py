#!/usr/bin/env python3
import json
import os
import sys
import time
import urllib.request

try:
    from bs4 import BeautifulSoup
except ImportError:
    BeautifulSoup = None

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ROOT_DIR = os.path.dirname(SCRIPT_DIR)
DATA_DIR = os.path.join(ROOT_DIR, "Landing-page", "assets", "data")
os.makedirs(DATA_DIR, exist_ok=True)

def fetch_github_releases():
    print("Fetching GitHub releases for community-outpost/GenHub...")
    url = "https://api.github.com/repos/community-outpost/GenHub/releases"
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "GenHub-LandingPage"})
        with urllib.request.urlopen(req, timeout=10) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            releases = []
            for r in data:
                releases.append({
                    "tag_name": r.get("tag_name"),
                    "name": r.get("name"),
                    "published_at": r.get("published_at"),
                    "html_url": r.get("html_url"),
                    "body": r.get("body", ""),
                    "prerelease": r.get("prerelease", False)
                })
            out_file = os.path.join(DATA_DIR, "genhub_releases.json")
            with open(out_file, "w", encoding="utf-8") as f:
                json.dump(releases, f, indent=2)
            print(f"Saved {len(releases)} releases to {out_file}")
    except Exception as e:
        print(f"Warning: Failed to fetch GitHub releases: {e}", file=sys.stderr)

def fetch_generals_online_patchnotes():
    if not BeautifulSoup:
        print("Warning: beautifulsoup4 not installed, skipping playgenerals.online scrape.")
        return
    print("Fetching patchnotes from playgenerals.online/patchnotes...")
    url = "https://www.playgenerals.online/patchnotes"
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"})
        with urllib.request.urlopen(req, timeout=10) as resp:
            html = resp.read().decode("utf-8")
        soup = BeautifulSoup(html, "html.parser")

        notes = []
        for post in soup.find_all("div", class_="post-text"):
            date_el = post.find("div", class_="d-date")
            date_str = date_el.get_text(strip=True) if date_el else ""
            h4 = post.find("h4")
            if not h4:
                continue
            a = h4.find("a")
            title = a.get_text(strip=True) if a else h4.get_text(strip=True)
            link = a["href"] if a and a.has_attr("href") else ""
            full_url = "https://www.playgenerals.online" + link if link.startswith("/") else link
            p = post.find("p")
            summary = p.get_text(strip=True) if p else ""

            details = []
            if full_url:
                try:
                    dreq = urllib.request.Request(full_url, headers={"User-Agent": "Mozilla/5.0"})
                    with urllib.request.urlopen(dreq, timeout=10) as dresp:
                        dhtml = dresp.read().decode("utf-8")
                    dsoup = BeautifulSoup(dhtml, "html.parser")
                    dpost = dsoup.find("div", class_="post-text")
                    if dpost:
                        ul = dpost.find("ul")
                        if ul:
                            for li in ul.find_all("li"):
                                t = li.get_text(strip=True)
                                if t:
                                    details.append(t)
                        else:
                            for dp in dpost.find_all("p"):
                                t = dp.get_text(strip=True)
                                if t and t != summary:
                                    details.append(t)
                except Exception as de:
                    print(f"Detail fetch error for {full_url}: {de}")

            notes.append({
                "date": date_str,
                "title": title,
                "url": full_url,
                "summary": summary,
                "details": details
            })
            time.sleep(0.05)

        out_file = os.path.join(DATA_DIR, "generals_online_patchnotes.json")
        with open(out_file, "w", encoding="utf-8") as f:
            json.dump(notes, f, indent=2)
        print(f"Saved {len(notes)} patch notes to {out_file}")
    except Exception as e:
        print(f"Warning: Failed to fetch patchnotes from playgenerals.online: {e}", file=sys.stderr)

if __name__ == "__main__":
    fetch_github_releases()
    fetch_generals_online_patchnotes()
