"""Create a new, isolated ClipNest data set for documentation captures."""
import argparse
import hashlib
import json
from pathlib import Path
import sqlite3
import time
import uuid

from PIL import Image, ImageDraw, ImageFont


def seed(base: Path) -> None:
    root = base / "ClipNest"
    database = root / "db" / "clipnest.db"
    if database.exists():
        raise SystemExit("Refusing to overwrite an existing data set; choose a new demo directory.")
    for name in ("db", "blobs", "cache", "logs"):
        (root / name).mkdir(parents=True, exist_ok=True)
    (root / "appearance.json").write_text(json.dumps({
        "Theme": 1, "ShowPreview": True, "LayoutVersion": 2,
        "Hotkey": {"Modifiers": 6, "Key": 86},
    }), encoding="utf-8")
    (root / "settings.json").write_text("{}", encoding="utf-8")

    image = Image.new("RGB", (960, 600), "#f5f0e5")
    draw = ImageDraw.Draw(image)
    font_path = "C:/Windows/Fonts/msyh.ttc"
    title = ImageFont.truetype(font_path, 64)
    subtitle = ImageFont.truetype(font_path, 27)
    draw.rounded_rectangle((50, 48, 910, 550), 40, fill="#08786e")
    draw.rounded_rectangle((112, 112, 190, 194), 14, fill="#fff8e8")
    draw.line((130, 137, 171, 137), fill="#168b7c", width=7)
    draw.line((130, 158, 159, 158), fill="#168b7c", width=7)
    draw.text((112, 248), "灵感，随手拾起。", font=title, fill="#fff8e8")
    draw.text((115, 365), "ClipNest · 拾贴  /  Windows 11", font=subtitle, fill="#92e3cd")
    draw.text((115, 421), "文字、图片、常用内容，都有一个小巢。", font=subtitle, fill="#fff8e8")
    image.save(root / "blobs" / "demo-poster.png")

    records = [
        ("text", "本周发布清单\n\n✓ 完成 Windows 11 适配\n✓ 检查图片与文字预览\n✓ 收藏常用链接\n\n下次需要时，按 Ctrl + Shift + V。", None, "Notepad", False),
        ("image", None, "blobs/demo-poster.png", "Photos", False),
        ("text", "会议纪要 · 产品讨论\n\n时间：周五 14:00\n主题：让复制过的内容更容易找回来\n\n待办：整理反馈、更新文档、准备下一版。", None, "Notepad", False),
        ("text", "https://github.com/Karl-Dai/ClipNest", None, "Chrome", True),
        ("color", "#08786E", None, "Figma", True),
        ("text", "git clone https://github.com/Karl-Dai/ClipNest.git", None, "Terminal", False),
    ]
    with sqlite3.connect(database) as connection:
        connection.executescript("""
            CREATE TABLE clipboard_entries (
                id TEXT PRIMARY KEY NOT NULL, created_at INTEGER NOT NULL,
                kind TEXT NOT NULL, content TEXT, blob_path TEXT,
                content_hash TEXT NOT NULL, source_app TEXT,
                is_favorite INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX ix_entries_created_at ON clipboard_entries(created_at DESC);
            CREATE INDEX ix_entries_hash ON clipboard_entries(content_hash);
            CREATE TABLE app_settings (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL);
            PRAGMA user_version = 2;
        """)
        for index, (kind, content, blob, app, favorite) in enumerate(records):
            digest = hashlib.sha256((content or blob).encode()).hexdigest()
            connection.execute("INSERT INTO clipboard_entries VALUES (?,?,?,?,?,?,?,?)", (
                str(uuid.uuid4()), int(time.time() * 1000) - index * 60000,
                kind, content, blob, digest, app, int(favorite),
            ))
    print(f"Demo data ready: {root}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("base", type=Path)
    seed(parser.parse_args().base.resolve())
