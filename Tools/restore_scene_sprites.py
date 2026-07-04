#!/usr/bin/env python3
"""Assign direct sprite refs from AddressableUIImageLoader keys, then remove loaders."""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCENE = ROOT / "Assets/_Project/Scenes/DehlaPakad.unity"
BACKUP = ROOT / "Assets/_Recovery/Recovery_Backups/0 (5).unity"
LOADER_GUID = "10a8ec7031834454bae678634968ed2f"
IMAGE_GUID = "fe87c0e1cc204ed48ad3b37840f39efc"


def parse_blocks(text: str) -> dict[str, str]:
    blocks: dict[str, str] = {}
    for m in re.finditer(r"(--- !u!\d+ &\d+\n.*?(?=\n--- !u!|\Z))", text, re.DOTALL):
        block = m.group(0)
        bid = re.search(r"--- !u!\d+ &(\d+)", block)
        if bid:
            blocks[bid.group(1)] = block
    return blocks


def parse_gameobjects(text: str) -> dict[str, tuple[str, list[str]]]:
    """go_id -> (name, [component_ids])"""
    result: dict[str, tuple[str, list[str]]] = {}
    for m in re.finditer(
        r"--- !u!1 &(\d+)\nGameObject:.*?m_Name: (.+)\n.*?m_Component:\n((?:  - component: \{fileID: \d+\}\n)+)",
        text,
        re.DOTALL,
    ):
        go_id = m.group(1)
        name = m.group(2).strip()
        comps = re.findall(r"component: \{fileID: (\d+)\}", m.group(3))
        result[go_id] = (name, comps)
    return result


def component_script_guid(block: str) -> str | None:
    m = re.search(r"m_Script: \{fileID: 11500000, guid: (\w+), type: 3\}", block)
    return m.group(1) if m else None


def sprite_line_for_asset(asset_path: str) -> str | None:
    rel = asset_path.replace("\\", "/")
    meta_path = ROOT / f"{rel}.meta"
    if not meta_path.exists():
        return None
    meta = meta_path.read_text(encoding="utf-8")
    guid_m = re.search(r"^guid: (\S+)", meta, re.M)
    if not guid_m:
        return None
    guid = guid_m.group(1)

    sheet_sprite = re.search(
        r"spriteSheet:\s*\n\s*serializedVersion: \d+\s*\n\s*sprites:\s*\n\s*- serializedVersion: \d+\s*\n(?:.*?\n)*?\s*internalID: (-?\d+)",
        meta,
        re.DOTALL,
    )
    if sheet_sprite:
        file_id = sheet_sprite.group(1)
        return f"  m_Sprite: {{fileID: {file_id}, guid: {guid}, type: 3}}"

    return f"  m_Sprite: {{fileID: 21300000, guid: {guid}, type: 3}}"


def parse_backup_sprites(text: str) -> dict[str, str]:
    sprites: dict[str, str] = {}
    for block in re.split(r"(?=--- !u!\d+ &\d+)", text):
        if IMAGE_GUID not in block:
            continue
        m_id = re.search(r"--- !u!114 &(\d+)", block)
        m_sprite = re.search(r"(\s+m_Sprite: .+)", block)
        if m_id and m_sprite and "{fileID: 0}" not in m_sprite.group(1):
            sprites[m_id.group(1)] = m_sprite.group(1)
    return sprites


def main() -> int:
    if not SCENE.exists():
        print(f"Scene not found: {SCENE}")
        return 1

    text = SCENE.read_text(encoding="utf-8")
    blocks = parse_blocks(text)
    gameobjects = parse_gameobjects(text)
    comp_to_go: dict[str, str] = {}
    for go_id, (_, comps) in gameobjects.items():
        for c in comps:
            comp_to_go[c] = go_id

    backup_sprites = {}
    if BACKUP.exists():
        backup_sprites = parse_backup_sprites(BACKUP.read_text(encoding="utf-8"))

    restored_key = 0
    restored_backup = 0
    failed: list[str] = []

    loader_ids: list[str] = []
    for cid, block in blocks.items():
        if LOADER_GUID not in block:
            continue
        loader_ids.append(cid)
        key_m = re.search(r"addressableKey: (.+)", block)
        if not key_m:
            continue
        key = key_m.group(1).strip()
        go_id = comp_to_go.get(cid)
        if not go_id:
            continue
        _, comps = gameobjects[go_id]
        image_cid = None
        for comp_id in comps:
            b = blocks.get(comp_id, "")
            if IMAGE_GUID in b:
                image_cid = comp_id
                break
        if not image_cid:
            continue

        image_block = blocks[image_cid]
        if "m_Sprite: {fileID: 0}" not in image_block and "m_Sprite: {fileID: 0," not in image_block:
            continue

        new_sprite = backup_sprites.get(image_cid)
        if new_sprite:
            restored_backup += 1
        else:
            line = sprite_line_for_asset(key)
            if not line:
                failed.append(key)
                continue
            new_sprite = line
            restored_key += 1

        new_image_block = re.sub(r"\s+m_Sprite: .+", new_sprite.strip(), image_block, count=1)
        text = text.replace(image_block, new_image_block)
        blocks[image_cid] = new_image_block

    loader_block_pat = (
        r"--- !u!114 &\d+\nMonoBehaviour:.*?Assembly-CSharp::AddressableUIImageLoader\n"
        r"  addressableKey:.*?\n"
    )
    removed = len(re.findall(loader_block_pat, text, flags=re.DOTALL))
    text = re.sub(loader_block_pat, "", text, flags=re.DOTALL)
    for lid in loader_ids:
        text = re.sub(rf"  - component: {{fileID: {lid}}}\n", "", text)

    SCENE.write_text(text, encoding="utf-8")
    print(f"Restored {restored_key} sprites from addressableKey paths.")
    print(f"Restored {restored_backup} sprites from recovery backup.")
    print(f"Removed {removed} AddressableUIImageLoader components.")
    if failed:
        print(f"Failed to resolve {len(failed)} keys (first 10):")
        for k in failed[:10]:
            print(f"  - {k}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
