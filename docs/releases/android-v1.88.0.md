# Monolith for Android 1.88.0 — preview

Signed ARM64 APK for Android 8 or newer, built from the same source commit as desktop 1.88.0. The installed app is named Monolith and retains its green icon.

## Changes since 1.84.0

- Replies display Markdown images and HTML/CSS cards with source access.
- Create web app (/create-web-app) lets the model build an HTML/CSS/JavaScript app; tap its card to open the isolated fullscreen preview.
- Bundled Tailwind CSS 4.3.3 and Chart.js 4.5.1 have independent switches, enabled by default, and require no CDN.
- Complete HTML documents are kept in one card instead of fragmented Markdown, including messages already saved in history.

The fullscreen preview has no network, file access, native bridge or host API. HTTPS image downloads are separate and limited to public servers and 8 MiB raster files. Configure your own HTTPS provider; the APK contains no provider key or user data.

Install over a previous GitHub release signed with the same durable certificate to preserve private conversation data. A locally development-signed APK cannot be updated in place by this release; back up needed data before changing signing identities. SHA256SUMS-android.txt identifies the APK fingerprint.

The grouping/fullscreen behavior was checked on a Pixel 7 Pro in source build 1.86.1. Packaging of 1.88.0 verifies compilation and release identity; it does not validate every Android device or provider. Desktop-only chat settings remain separate from mobile.

[Android guide](https://github.com/loicdelaunay/Monolith-Harness/blob/v1.88.0/docs/android-portable.md) · [Rich replies](https://github.com/loicdelaunay/Monolith-Harness/blob/v1.88.0/docs/reponses-riches.md) · [GUI](https://github.com/loicdelaunay/Monolith-Harness/releases/tag/v1.88.0) · [CLI](https://github.com/loicdelaunay/Monolith-Harness/releases/tag/cli-v1.88.0)
