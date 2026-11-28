"""Keep the download header on demo releases pointed at the newest complete build."""

import json
import os
import re
import subprocess


TAG = re.compile(r"v(\d+)\.(\d+)\.(\d+)-demo\.(\d+)")
START = "<!-- xcity-latest-downloads:start -->"
END = "<!-- xcity-latest-downloads:end -->"


def version(release):
    return tuple(map(int, TAG.fullmatch(release["tag_name"]).groups()))


def download_pair(release):
    assets = {
        asset["name"]: asset["browser_download_url"]
        for asset in release["assets"]
        if asset["state"] == "uploaded"
    }
    tag = release["tag_name"]
    apk = assets.get(f"XCity-{tag}.apk")
    ipa = assets.get(f"XCity-{tag}-unsigned.ipa")
    if tag == "v0.1.0-demo.1":
        apk = apk or assets.get("XCity.apk")
        ipa = ipa or assets.get("XCity-unsigned.ipa")
    return (apk, ipa) if apk and ipa else None


def gh_api(*args):
    return subprocess.run(
        ["gh", "api", *args], check=True, text=True, stdout=subprocess.PIPE
    ).stdout


def header(tag, apk, ipa):
    return (
        f"{START}\n"
        f"## Tải demo mới nhất · {tag}\n\n"
        f"[![Tải APK cho Android](https://img.shields.io/badge/Android-T%E1%BA%A3i_APK-2E7D32?style=for-the-badge)]({apk}) "
        f"[![Tải IPA cho iPhone](https://img.shields.io/badge/iPhone-T%E1%BA%A3i_IPA-1565C0?style=for-the-badge)]({ipa})\n\n"
        f"[APK Android]({apk}) · [IPA iPhone]({ipa})\n\n"
        "IPA cần được ký trước khi cài lên iPhone.\n"
        f"{END}"
    )


def main():
    endpoint = f"repos/{os.environ['GITHUB_REPOSITORY']}/releases"
    releases = gh_api("--paginate", f"{endpoint}?per_page=100", "--jq", ".[]")
    demos = [
        release
        for release in map(json.loads, releases.splitlines())
        if not release["draft"] and release["published_at"] and TAG.fullmatch(release["tag_name"])
    ]
    complete = [(release, pair) for release in demos if (pair := download_pair(release))]
    if not complete:
        print("No published demo has both APK and IPA assets.")
        return

    latest, (apk, ipa) = max(complete, key=lambda item: version(item[0]))
    block = header(latest["tag_name"], apk, ipa)
    pattern = re.compile(re.escape(START) + r".*?" + re.escape(END), re.DOTALL)
    for release in demos:
        original = release["body"] or ""
        if pattern.search(original):
            updated = pattern.sub(lambda _: block, original, count=1)
        else:
            updated = block + "\n\n" + original
        if updated == original:
            continue
        gh_api("--method", "PATCH", f"{endpoint}/{release['id']}", "-f", f"body={updated}")
        print(f"Updated {release['tag_name']} → {latest['tag_name']}")


if __name__ == "__main__":
    main()
