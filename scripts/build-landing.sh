#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

cd "${ROOT_DIR}"

LATEST_TAG=$(gh release list --limit 1 --json tagName -q '.[0].tagName // ""' 2>/dev/null || true)
if [[ -z "${LATEST_TAG}" || "${LATEST_TAG}" = "null" ]]; then
    DISPLAY_NAME="Alpha"
    DOWNLOAD_URL="https://github.com/community-outpost/GenHub/releases"
else
    BUILD_NUM=$(echo "${LATEST_TAG}" | cut -d'.' -f3)
    if [[ -n "${BUILD_NUM}" && "${BUILD_NUM}" != "${LATEST_TAG}" ]]; then
        DISPLAY_NAME="Alpha ${BUILD_NUM}"
    else
        CLEAN_TAG="${LATEST_TAG#v}"
        DISPLAY_NAME="Alpha ${CLEAN_TAG:-Preview}"
    fi

    # Check if Windows installer asset exists in the release, fallback to release page if absent
    HAS_EXE=$(gh release view "${LATEST_TAG}" --json assets -q '.assets[] | select(.name == "GenHub-win-Setup.exe") | .name' 2>/dev/null || true)
    if [[ -n "${HAS_EXE}" ]]; then
        DOWNLOAD_URL="https://github.com/community-outpost/GenHub/releases/download/${LATEST_TAG}/GenHub-win-Setup.exe"
    else
        DOWNLOAD_URL="https://github.com/community-outpost/GenHub/releases/tag/${LATEST_TAG}"
    fi
fi

echo "Building Landing Page..."
echo "  Tag: ${LATEST_TAG}"
echo "  Display Name: ${DISPLAY_NAME}"
echo "  Download URL: ${DOWNLOAD_URL}"

rm -rf "${ROOT_DIR}/public"
mkdir -p "${ROOT_DIR}/public"
cp "${ROOT_DIR}/Landing-page/index.html" "${ROOT_DIR}/public/index.html"

if [[ -d "${ROOT_DIR}/Landing-page/assets" ]]; then
    cp -r "${ROOT_DIR}/Landing-page/assets" "${ROOT_DIR}/public/assets"
fi

mkdir -p "${ROOT_DIR}/public/assets/data"

if [[ -f "${SCRIPT_DIR}/fetch-changelogs.py" ]]; then
    echo "  Syncing changelogs from GitHub & playgenerals.online to public/assets/data..."
    python3 "${SCRIPT_DIR}/fetch-changelogs.py" "${ROOT_DIR}/public/assets/data" 2>/dev/null || true
fi

# Create client-side redirect for legacy /wiki/ path
mkdir -p "${ROOT_DIR}/public/wiki"
cat << 'EOF_WIKI' > "${ROOT_DIR}/public/wiki/index.html"
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <title>Redirecting to GeneralsHub Wiki...</title>
  <link rel="canonical" href="https://wiki.generalshub.com/">
  <meta http-equiv="refresh" content="0; url=https://wiki.generalshub.com/">
  <script>window.location.replace("https://wiki.generalshub.com/" + window.location.search + window.location.hash);</script>
</head>
<body>
  <p>Redirecting to <a href="https://wiki.generalshub.com/">https://wiki.generalshub.com/</a>...</p>
</body>
</html>
EOF_WIKI

# Create client-side 404 handler (redirects legacy /wiki/* or displays 404 page)
cat << 'EOF_404' > "${ROOT_DIR}/public/404.html"
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <title>GeneralsHub</title>
  <script>
    if (window.location.pathname.startsWith('/wiki/')) {
      var target = 'https://wiki.generalshub.com/' + window.location.pathname.replace(/^\/wiki\//, '') + window.location.search + window.location.hash;
      window.location.replace(target);
    }
  </script>
  <style>
    body { font-family: system-ui, -apple-system, sans-serif; background: #0a0612; color: #f0f4ff; display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; text-align: center; }
    .box { max-width: 480px; padding: 32px; }
    h1 { font-size: 3rem; margin: 0 0 12px; color: #a78bfa; }
    p { color: #c7d2fe; font-size: 1.1rem; line-height: 1.6; }
    a { color: #a855f7; text-decoration: none; }
    a:hover { text-decoration: underline; color: #c084fc; }
  </style>
</head>
<body>
  <div class="box" id="message">
    <h1>404</h1>
    <p>Page not found. Looking for the <a href="https://wiki.generalshub.com/">Wiki</a> or want to return <a href="/">Home</a>?</p>
  </div>
  <script>
    if (window.location.pathname.startsWith('/wiki/')) {
      document.getElementById('message').innerHTML = '<p>Redirecting to <a href="https://wiki.generalshub.com/">GeneralsHub Wiki</a>...</p>';
    }
  </script>
</body>
</html>
EOF_404

# Replace placeholders with live release info safely and portably
python3 -c '
import html, sys

display_name = html.escape(sys.argv[1])
download_url = html.escape(sys.argv[2], quote=True)
target_file = sys.argv[3]

with open(target_file, "r", encoding="utf-8") as f:
    text = f.read()

text = text.replace("VERSION_PLACEHOLDER", display_name)
text = text.replace("URL_PLACEHOLDER", download_url)

with open(target_file, "w", encoding="utf-8") as f:
    f.write(text)
' "${DISPLAY_NAME}" "${DOWNLOAD_URL}" "${ROOT_DIR}/public/index.html"

echo "Landing page built successfully in ${ROOT_DIR}/public"
