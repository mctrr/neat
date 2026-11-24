"""Prepare filter manifest variants for the installer.

The app's FilterDir.Verify() requires every file listed in manifest.json to
exist with a matching hash. The installer lets users pick filter sets, so we
pre-generate one manifest per combination and copy the right one at install
time.
"""
import json
import os
import shutil

BASE = r"D:\neat-rebuild\ads-dl\filters"
OUT = r"D:\neat-installer-src\filters"
os.makedirs(OUT, exist_ok=True)

with open(os.path.join(BASE, "manifest.json"), encoding="utf-8") as f:
    full = json.load(f)

BASE_FILES = [
    "easylist.txt", "easyprivacy.txt", "ublock-filters.txt", "ublock-quick-fixes.txt",
    "ublock-unbreak.txt", "ublock-badware.txt", "urlhaus-1.txt", "resources.json",
    "ubo-resource-abuse.txt", "fanboy-thirdparty_social.txt", "popups.txt",
    "easylist-notifications.txt", "easylist-chat.txt",
]
CN_FILES = ["chn-0.txt"]
PRIVACY_FILES = [
    "fanboy-cookiemonster.txt", "adguard-cookies.txt", "ublock-privacy.txt",
    "hagezi-pro.txt", "easylist-newsletters.txt", "documents.txt",
    "ublock-cookies-adguard.txt", "easylist-annoyances.txt",
    "adguard-other-annoyances.txt", "ublock-annoyances.txt",
]

all_files = {f["name"] for f in full["files"]}
base, cn, priv = set(BASE_FILES), set(CN_FILES), set(PRIVACY_FILES)
assert base <= all_files, base - all_files
assert cn <= all_files, cn - all_files
assert priv <= all_files, priv - all_files

variants = {
    "base": base,
    "base_cn": base | cn,
    "base_privacy": base | priv,
    "base_cn_privacy": base | cn | priv,
    "all": all_files,
}

for name, keep in variants.items():
    d = dict(full)
    d["files"] = [f for f in full["files"] if f["name"] in keep]
    with open(os.path.join(OUT, f"_manifest_{name}.json"), "w", encoding="utf-8") as f:
        json.dump(d, f, ensure_ascii=False, indent=2)
    lists = sum(1 for f in d["files"] if f["kind"] == "list")
    print(f"{name}: {len(d['files'])} files, {lists} lists")

# Copy every rule file into the installer source. Component selection in the
# .iss decides which ones actually reach the destination.
for f in full["files"]:
    shutil.copy2(os.path.join(BASE, f["name"]), os.path.join(OUT, f["name"]))
for extra in ("optional.json", "optional-adult.txt"):
    src = os.path.join(BASE, extra)
    if os.path.exists(src):
        shutil.copy2(src, os.path.join(OUT, extra))

print("copied", len(os.listdir(OUT)), "files to", OUT)
