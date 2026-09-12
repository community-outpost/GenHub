#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

cd "${ROOT_DIR}"

LATEST_TAG=$(gh release list --limit 1 --json tagName -q '.[0].tagName // ""' 2>/dev/null || echo "v0.0.3")
if [[ -z "${LATEST_TAG}" || "${LATEST_TAG}" = "null" ]]; then
    LATEST_TAG="v0.0.3"
fi

BUILD_NUM=$(echo "${LATEST_TAG}" | cut -d'.' -f3)
DISPLAY_NAME="Alpha ${BUILD_NUM}"
DOWNLOAD_URL="https://github.com/community-outpost/GenHub/releases/download/${LATEST_TAG}/GenHub-win-Setup.exe"

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

# Replace placeholders with live release info
sed -i "s|VERSION_PLACEHOLDER|${DISPLAY_NAME}|g" "${ROOT_DIR}/public/index.html"
sed -i "s|URL_PLACEHOLDER|${DOWNLOAD_URL}|g" "${ROOT_DIR}/public/index.html"

echo "Landing page built successfully in ${ROOT_DIR}/public"
