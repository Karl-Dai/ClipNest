"""Create a fresh synthetic runtime fixture. Never opens or replaces an existing database."""
import argparse
import hashlib
import json
from pathlib import Path
import sqlite3
import time
import uuid
from PIL import Image, ImageDraw

base = Path(argparse.ArgumentParser(description=__doc__).parse_known_args()[1][0]).resolve()
if base.exists():
    raise SystemExit("Choose a new fixture directory")
root = base / "ClipNest"
for folder in ("db", "blobs", "logs", "cache"):
    (root / folder).mkdir(parents=True)
(root / "appearance.json").write_text(json.dumps({"Theme": 1, "ShowPreview": True, "LayoutVersion": 2,
    "AutomaticUpdates": False, "Hotkey": {"Modifiers": 7, "Key": 121}}), encoding="utf-8")
(root / "diagnostic-fixture.json").write_text(json.dumps({"synthetic": True}), encoding="utf-8")
with sqlite3.connect(root / "db/clipnest.db") as connection:
    connection.executescript("""
        CREATE TABLE clipboard_entries(id TEXT PRIMARY KEY NOT NULL, created_at INTEGER NOT NULL,
          kind TEXT NOT NULL, content TEXT, blob_path TEXT, content_hash TEXT NOT NULL,
          source_app TEXT, is_favorite INTEGER NOT NULL DEFAULT 0);
        CREATE INDEX ix_entries_created_at ON clipboard_entries(created_at DESC);
        CREATE INDEX ix_entries_hash ON clipboard_entries(content_hash);
        CREATE TABLE app_settings(key TEXT PRIMARY KEY NOT NULL,value TEXT NOT NULL);
        INSERT INTO app_settings VALUES('history_limit','10000');
        PRAGMA user_version=2;
    """)
    timestamp = int(time.time() * 1000)
    for index in range(1200):
        content = f"Synthetic long text {index:04d}\n" + "x" * 65536 + f"TAIL-LONGTEXT-{index:04d}"
        connection.execute("INSERT INTO clipboard_entries VALUES (?,?,?,?,?,?,?,?)", (
            str(uuid.uuid4()), timestamp-index*1000, "text", content, None,
            hashlib.sha256(content.encode()).hexdigest(), "SyntheticText", int(index % 100 == 0)))
    for index in range(80):
        # Two actual 8K sources plus separate 4K files keep fixture creation inexpensive.
        width,height = (7680,4320) if index < 2 else (3840,2160)
        if index == 2: width,height = 320,16000
        image=Image.new("RGB", (width,height), (index*7%255,90,150))
        draw=ImageDraw.Draw(image)
        draw.rectangle((40,40,min(width-20,500),min(height-20,260)),fill=(250,235,205))
        draw.text((50,60),f"SYNTHETIC IMAGE {index:03d} {width}x{height}",fill=(10,70,55))
        name=f"diagnostic-{index:03d}.png"
        image.save(root / "blobs" / name)
        image.close()
        connection.execute("INSERT INTO clipboard_entries VALUES (?,?,?,?,?,?,?,?)", (
            str(uuid.uuid4()), timestamp+index*1000, "image", None, f"blobs/{name}",
            f"synthetic-image-hash-{index}", "SyntheticImages", int(index==0)))
print(f"Synthetic runtime fixture: {base}")
