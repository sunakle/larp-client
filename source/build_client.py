"""Package an existing local Fabric installation without changing its mod JARs.

Offline, deterministic file selection; no credentials, launcher files or downloads.
Requires Python 3.12 on the build machine, not on the target machine.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import zipfile


def digest(path: Path, algorithm: str = "sha1") -> str:
    h = hashlib.new(algorithm)
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def allowed(rules: list[dict] | None) -> bool:
    if not rules:
        return True
    result = False
    for rule in rules:
        os_rule = rule.get("os", {})
        if os_rule.get("name", "windows") != "windows":
            continue
        if "arch" in os_rule and not re.fullmatch(os_rule["arch"], "amd64"):
            continue
        if "version" in os_rule and not re.search(os_rule["version"], "10.0"):
            continue
        if any(value is not False for value in rule.get("features", {}).values()):
            continue
        result = rule["action"] == "allow"
    return result


def maven_path(name: str) -> str:
    group, artifact, version, *classifier = name.split(":")
    suffix = "-" + classifier[0] if classifier else ""
    return f"{group.replace('.', '/')}/{artifact}/{version}/{artifact}-{version}{suffix}.jar"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--install", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    install, output = args.install.resolve(), args.output.resolve()
    game = install / "game"
    version_dir = game / "versions" / "Fabric 1.21.11"
    meta = json.loads((version_dir / "Fabric 1.21.11.json").read_text(encoding="utf-8-sig"))
    if meta.get("inheritsFrom"):
        raise ValueError("This builder expects a fully resolved version manifest")
    output.mkdir(parents=True, exist_ok=True)
    report = {"minecraft": "1.21.11", "fabricLoader": "0.20.1", "platform": "windows-x64", "files": []}

    def copy(src: Path, relative: str, expected_hash: str | None = None) -> Path:
        dst = (output / relative).resolve()
        if not dst.is_relative_to(output):
            raise ValueError(f"Path escapes package: {relative}")
        if not src.is_file():
            raise FileNotFoundError(src)
        if expected_hash and digest(src) != expected_hash:
            raise ValueError(f"Hash mismatch: {relative}")
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst)
        report["files"].append({"path": relative, "bytes": dst.stat().st_size})
        return dst

    classpath = []
    native_archives = []
    for lib in meta["libraries"]:
        name = lib["name"]
        if name.startswith(("ru.legacylauncher:", "org.tlauncher:")) or not allowed(lib.get("rules")):
            continue
        classifier = name.split(":")[3:]
        if classifier and classifier[0].startswith("natives-") and classifier[0] != "natives-windows":
            continue
        artifact = lib.get("downloads", {}).get("artifact", {})
        path = artifact.get("path") or maven_path(name)
        relative = "game/libraries/" + path
        dst = copy(game / "libraries" / path, relative, artifact.get("sha1"))
        classpath.append(relative)
        if classifier and classifier[0] == "natives-windows":
            native_archives.append(dst)

    client_hash = meta["downloads"]["client"]["sha1"]
    copy(version_dir / "Fabric 1.21.11.jar", "game/versions/1.21.11/1.21.11.jar", client_hash)
    classpath.append("game/versions/1.21.11/1.21.11.jar")

    # Extract only x64 Windows DLLs from the selected, verified native artifacts.
    natives = output / "game/natives"
    natives.mkdir(parents=True, exist_ok=True)
    for archive in native_archives:
        with zipfile.ZipFile(archive) as zf:
            for info in zf.infolist():
                if info.filename.lower().endswith(".dll"):
                    filename = Path(info.filename).name
                    (natives / filename).write_bytes(zf.read(info))

    asset_id = meta["assetIndex"]["id"]
    index_relative = f"game/assets/indexes/{asset_id}.json"
    index = copy(game / "assets/indexes" / f"{asset_id}.json", index_relative, meta["assetIndex"]["sha1"])
    objects = json.loads(index.read_text(encoding="utf-8"))["objects"]
    for sha1 in sorted({obj["hash"] for obj in objects.values()}):
        relative = f"assets/objects/{sha1[:2]}/{sha1}"
        copy(game / relative, "game/" + relative, sha1)
    print(f"Verified and copied {len(classpath)} classpath entries and {len(objects)} assets", flush=True)

    mod_hashes = []
    for mod_name in ("fabric-api-0.141.6+1.21.11.jar", "LARP-CRACKED-BY-MITTKEE.jar"):
        src = game / "mods" / mod_name
        dst = copy(src, "game/mods/" + mod_name)
        source_hash, copied_hash = digest(src, "sha256"), digest(dst, "sha256")
        if source_hash != copied_hash:
            raise ValueError("Mod was changed during copying")
        mod_hashes.append({"file": mod_name, "sha256": copied_hash})

    config_root = game / "config/larp-client"
    for src in config_root.rglob("*"):
        relative = src.relative_to(config_root)
        if not src.is_file() or any(part.lower() in {"accounts", "tokens", "auth", "credentials"} for part in relative.parts):
            continue
        copy(src, "game/config/larp-client/" + relative.as_posix())
    # Keep keyboard, graphics, sound and mod options, without any launcher/account state.
    for filename in ("options.txt",):
        if (game / filename).exists():
            copy(game / filename, "game/" + filename)
    for directory in ("saves", "resourcepacks", "screenshots", "logs"):
        (output / "game" / directory).mkdir(parents=True, exist_ok=True)

    runtime = install / "jre/java-runtime-delta/windows-x64/java-runtime-delta"
    for src in runtime.rglob("*"):
        if src.is_file():
            copy(src, "runtime/" + src.relative_to(runtime).as_posix())
    print("Bundled Java 21 and unchanged mod JARs", flush=True)

    config = {
        "title": "LARP Client",
        "version": "1.21.11",
        "java": "runtime/bin/javaw.exe",
        "classpath": classpath,
        "jvmArgs": [
            "-Dfile.encoding=UTF-8", "-Xss1M",
            "-Djava.library.path=${natives_directory}",
            "-Djna.tmpdir=${natives_directory}",
            "-Dorg.lwjgl.system.SharedLibraryExtractPath=${natives_directory}",
            "-Dio.netty.native.workdir=${natives_directory}",
            "-Dminecraft.launcher.brand=LARPClient",
            "-Dminecraft.launcher.version=1.0",
            "-cp", "${classpath}",
        ],
        "mainClass": meta["mainClass"],
        "gameArgs": [
            "--username", "${auth_player_name}",
            "--version", "1.21.11",
            "--gameDir", "${game_directory}",
            "--assetsDir", "${assets_root}",
            "--assetIndex", asset_id,
            "--uuid", "${auth_uuid}",
            "--accessToken", "0",
            "--userType", "legacy",
            "--versionType", "release",
        ],
    }
    (output / "launch-config.json").write_text(json.dumps(config, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    report["mods"] = mod_hashes
    report["clientJarSha1"] = client_hash
    report["assetIndex"] = asset_id
    report["assetObjectCount"] = len({obj["hash"] for obj in objects.values()})
    report["totalCopiedBytes"] = sum(entry["bytes"] for entry in report["files"])
    report["copiedFileCount"] = len(report["files"])
    # No original installation path is written into the distributable.
    (output / "build-info.json").write_text(json.dumps({k: v for k, v in report.items() if k != "files"}, indent=2) + "\n", encoding="utf-8")
    (Path(__file__).parent / "build-file-manifest.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"Complete: {report['copiedFileCount']} files, {report['totalCopiedBytes'] / 1024**2:.1f} MiB", flush=True)


if __name__ == "__main__":
    main()
