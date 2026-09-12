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
    DISPLAY_NAME="Alpha ${BUILD_NUM}"
    DOWNLOAD_URL="https://github.com/community-outpost/GenHub/releases/download/${LATEST_TAG}/GenHub-win-Setup.exe"
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
