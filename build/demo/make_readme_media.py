"""Compose captioned GIFs from real ClipNest window captures (requires Pillow)."""
import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


SCENES = [
    ("01-history.png", "复制过的内容，随时找回来", "文字、图片与常用片段，收在一个悬浮面板里。", "Ctrl + Shift + V"),
    ("02-image.png", "上下选择，右侧即时预览", "连续浏览图片，不必反复打开，也不用先粘贴。", "↑ / ↓"),
    ("03-search.png", "输入关键词，缩小查找范围", "按内容或来源应用搜索，快速定位需要的记录。", "搜索：会议"),
    ("04-favorites.png", "常用内容，收藏留下", "链接、颜色与常用文字，下一次直接取用。", "收藏"),
    ("05-settings.png", "你的习惯，你来设置", "选择数据文件夹，调整外观与历史上限。", "设置"),
    ("06-shortcut.png", "直接按下组合键，即可录入", "录入后点击保存；保留冲突检查与恢复默认。", "Ctrl + Alt + B"),
]


def compose(source: Path, output: Path) -> None:
    output.mkdir(parents=True, exist_ok=True)
    font_path = "C:/Windows/Fonts/msyh.ttc"
    title_font = ImageFont.truetype(font_path, 30)
    body_font = ImageFont.truetype(font_path, 17)
    small_font = ImageFont.truetype(font_path, 13)
    key_font = ImageFont.truetype("C:/Windows/Fonts/consola.ttf", 17)
    scenes = []
    for index, (file, title, subtitle, key) in enumerate(SCENES):
        shot = Image.open(source / file).convert("RGB")
        canvas = Image.new("RGB", (960, 656), "#eff5f2")
        draw = ImageDraw.Draw(canvas)
        draw.text((46, 23), "ClipNest / 拾贴", font=small_font, fill="#08786e")
        draw.text((46, 48), title, font=title_font, fill="#153c38")
        draw.text((46, 94), subtitle, font=body_font, fill="#536f68")
        shot.thumbnail((866, 473), Image.Resampling.LANCZOS)
        canvas.paste(shot, ((960 - shot.width) // 2, 136))
        draw.text((47, 626), key, font=key_font, fill="#08786e")
        draw.text((725, 627), "实际界面 · 演示数据", font=small_font, fill="#536f68")
        for dot in range(len(SCENES)):
            x = 460 + dot * 12
            draw.ellipse((x, 631, x + 5, 636), fill="#08786e" if dot == index else "#b8cbc4")
        scenes.append(canvas)

    def save_animation(file: str, indexes: list[int]) -> None:
        frames, durations = [], []
        for position, index in enumerate(indexes):
            frame = scenes[index]
            frames.append(frame)
            durations.append(3200)
            following = scenes[indexes[(position + 1) % len(indexes)]]
            for step in range(1, 6):
                frames.append(Image.blend(frame, following, step / 6))
                durations.append(80)
        # One palette across all frames avoids palette flashing during transitions.
        atlas = Image.new("RGB", (240 * len(scenes), 164))
        for index, scene in enumerate(scenes):
            atlas.paste(scene.resize((240, 164)), (index * 240, 0))
        palette = atlas.quantize(colors=192, method=Image.Quantize.MEDIANCUT)
        indexed = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]
        indexed[0].save(output / file, save_all=True, append_images=indexed[1:],
                        duration=durations, loop=0, optimize=True, disposal=1)

    save_animation("overview.gif", list(range(len(scenes))))
    save_animation("shortcut.gif", [4, 5])
    Image.open(source / "02-image.png").save(output / "preview.png", optimize=True)
    scenes[1].save(output / "social-preview.png", optimize=True)
    for file in ("overview.gif", "shortcut.gif"):
        with Image.open(output / file) as gif:
            total = 0
            for frame in range(gif.n_frames):
                gif.seek(frame)
                total += gif.info.get("duration", 0)
            print(f"{file}: {gif.size}, {gif.n_frames} frames, {total / 1000:.1f}s, {(output / file).stat().st_size:,} bytes")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("captures", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    compose(args.captures, args.output)
