"""Builds tools/LethalCraft.mrpack: a Modrinth App instance (Minecraft 26.3, Fabric Loader 0.19.5,
Fabric API) with the SkyCraft guest jar and its config. Import it in the Modrinth App with
"Create instance -> From file". Run after `gradlew build` in fabric/."""
import glob, hashlib, json, os, sys, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
JARS = [p for p in glob.glob(os.path.join(ROOT, "fabric", "build", "libs", "skycraft-*.jar")) if not p.endswith("-sources.jar")]
if not JARS:
    sys.exit("build the Fabric mod first: cd fabric && gradlew build")
JAR = max(JARS, key=os.path.getmtime)
OUT = os.path.join(ROOT, "tools", "LethalCraft.mrpack")

# Fabric API for 26.3 (https://api.modrinth.com/v2/project/fabric-api/version?game_versions=["26.3"]&loaders=["fabric"]).
FABRIC_API = {
    "path": "mods/fabric-api-0.161.0+26.3.jar",
    "hashes": {
        "sha1": "53f9ee02c3702734d90520aa8c9719bf0e1c8d4f",
        "sha512": "ed6b2586d6fde11fde8472f5a527c51e99b67026e46f94d4bfd85e7e28ce5ee299173ee16ad576ceb51f39f98d30a811086a6deb1a86a524859cc16e12da109d",
    },
    "env": {"client": "required", "server": "required"},
    "downloads": ["https://cdn.modrinth.com/data/P7dR8mSH/versions/bNnaTiuM/fabric-api-0.161.0%2B26.3.jar"],
    "fileSize": 2620967,
}

index = {
    "formatVersion": 1,
    "game": "minecraft",
    "versionId": "0.1.0",
    "name": "LethalCraft",
    "summary": "Minecraft inside Lethal Company: the hidden guest half of LethalCraft (a SkyCraft port).",
    "files": [FABRIC_API],
    "dependencies": {"minecraft": "26.3", "fabric-loader": "0.19.5"},
}

config = "\n".join([
    "# LethalCraft guest settings (see docs/DESIGN.md). Digging Lethal Company's level yields blocks.",
    "destruction=true",
    "",
])

with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as z:
    z.writestr("modrinth.index.json", json.dumps(index, indent=2))
    z.write(JAR, "overrides/mods/" + os.path.basename(JAR))
    z.writestr("overrides/config/skycraft.properties", config)
print(f"wrote {OUT} with {os.path.basename(JAR)}")
