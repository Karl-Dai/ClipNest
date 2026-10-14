# Original ClipNest artwork. This script is the editable source of every brand asset.
# Run with Windows PowerShell; uses only the built-in System.Drawing assembly.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

public static class ClipNestBrand
{
    const string Tile = "M 68 8 L 188 8 C 221 8 248 35 248 68 L 248 188 C 248 221 221 248 188 248 L 68 248 C 35 248 8 221 8 188 L 8 68 C 8 35 35 8 68 8 Z";
    const string Back = "M 128 54 L 177 67 C 184 69 188 76 186 83 L 166 157 L 104 140 L 117 63 C 118 56 122 52 128 54 Z";
    const string Paper = "M 87 72 L 139 72 C 146 72 152 78 152 85 L 152 155 L 74 155 L 74 85 C 74 78 80 72 87 72 Z";
    const string Nest = "M 59 153 C 64 180 88 197 128 197 C 168 197 192 180 197 153";
    const string Ink = "M 94 103 L 132 103 M 94 122 L 120 122";
    const string SimplePaper = "M 95 65 L 153 65 C 161 65 166 70 166 78 L 166 148 L 82 148 L 82 78 C 82 70 87 65 95 65 Z";

    static GraphicsPath PathOf(string data)
    {
        var tokens = Regex.Matches(data, @"[MLCZ]|-?\d+(?:\.\d+)?");
        var path = new GraphicsPath();
        float x = 0, y = 0;
        int i = 0;
        Func<float> read = () => float.Parse(tokens[i++].Value, CultureInfo.InvariantCulture);
        while (i < tokens.Count)
        {
            string command = tokens[i++].Value;
            if (command == "M") { path.StartFigure(); x = read(); y = read(); }
            else if (command == "L") { float nx = read(), ny = read(); path.AddLine(x, y, nx, ny); x = nx; y = ny; }
            else if (command == "C") { float a = read(), b = read(), c = read(), d = read(), nx = read(), ny = read(); path.AddBezier(x, y, a, b, c, d, nx, ny); x = nx; y = ny; }
            else if (command == "Z") path.CloseFigure();
        }
        return path;
    }
    static Color ColorOf(string hex) { return ColorTranslator.FromHtml(hex); }
    static void Fill(Graphics g, string path, string color)
    { using (var p = PathOf(path)) using (var b = new SolidBrush(ColorOf(color))) g.FillPath(b, p); }
    static void Stroke(Graphics g, string path, string color, float width)
    {
        using (var p = PathOf(path)) using (var pen = new Pen(ColorOf(color), width))
        { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round; g.DrawPath(pen, p); }
    }
    public static Bitmap Render(int size)
    {
        using (var large = new Bitmap(1024, 1024, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(large))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(4, 4);
                using (var p = PathOf(Tile))
                using (var b = new LinearGradientBrush(new Point(40, 8), new Point(216, 248), ColorOf("#20B7A3"), ColorOf("#08786E"))) g.FillPath(b, p);
                if (size <= 32) Fill(g, SimplePaper, "#FFF8E8");
                else { Fill(g, Back, "#92E3CD"); Fill(g, Paper, "#FFF8E8"); Stroke(g, Ink, "#168B7C", 7); }
                Stroke(g, Nest, "#FFF8E8", size <= 32 ? 22 : 18);
            }
            var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(large, new Rectangle(0, 0, size, size)); }
            return result;
        }
    }
    static byte[] Png(int size)
    { using (var b = Render(size)) using (var stream = new MemoryStream()) { b.Save(stream, ImageFormat.Png); return stream.ToArray(); } }
    public static void SavePng(string file, int width, int height, int iconSize)
    {
        using (var canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(canvas)) using (var icon = Render(iconSize))
        { g.DrawImageUnscaled(icon, (width - iconSize) / 2, (height - iconSize) / 2); canvas.Save(file, ImageFormat.Png); }
    }
    public static void SaveIco(string file)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var frames = Array.ConvertAll(sizes, Png);
        using (var writer = new BinaryWriter(File.Create(file)))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
            }
            foreach (var bytes in frames) writer.Write(bytes);
        }
    }
    public static void SaveSvg(string file)
    {
        string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\" role=\"img\" aria-label=\"ClipNest: notes resting in a nest\">\n" +
            "  <defs><linearGradient id=\"tile\" gradientUnits=\"userSpaceOnUse\" x1=\"40\" y1=\"8\" x2=\"216\" y2=\"248\"><stop stop-color=\"#20B7A3\"/><stop offset=\"1\" stop-color=\"#08786E\"/></linearGradient></defs>\n" +
            "  <path fill=\"url(#tile)\" d=\"" + Tile + "\"/>\n" +
            "  <path fill=\"#92E3CD\" d=\"" + Back + "\"/>\n" +
            "  <path fill=\"#FFF8E8\" d=\"" + Paper + "\"/>\n" +
            "  <path fill=\"none\" stroke=\"#168B7C\" stroke-width=\"7\" stroke-linecap=\"round\" d=\"" + Ink + "\"/>\n" +
            "  <path fill=\"none\" stroke=\"#FFF8E8\" stroke-width=\"18\" stroke-linecap=\"round\" d=\"" + Nest + "\"/>\n</svg>\n";
        File.WriteAllText(file, svg, new UTF8Encoding(false));
    }
}
'@
$root = (Resolve-Path "$PSScriptRoot/..").Path
$assets = Join-Path $root 'src/ClipNest.App/Assets'
$brand = Join-Path $root 'design'
New-Item -ItemType Directory -Path $brand -Force | Out-Null
[ClipNestBrand]::SaveSvg("$brand/clipnest.svg")
[ClipNestBrand]::SavePng("$brand/clipnest-512.png", 512, 512, 512)
[ClipNestBrand]::SaveIco("$assets/app.ico")
Copy-Item "$assets/app.ico" "$PSScriptRoot/icons/app.ico" -Force
[ClipNestBrand]::SavePng("$assets/BrandMark.png", 128, 128, 128)
[ClipNestBrand]::SavePng("$assets/BrandMarkSmall.png", 32, 32, 32)
[ClipNestBrand]::SavePng("$assets/Square44x44Logo.png", 44, 44, 44)
[ClipNestBrand]::SavePng("$assets/Square150x150Logo.png", 150, 150, 150)
[ClipNestBrand]::SavePng("$assets/StoreLogo.png", 50, 50, 50)
[ClipNestBrand]::SavePng("$assets/Wide310x150Logo.png", 310, 150, 130)
[ClipNestBrand]::SavePng("$assets/SplashScreen.png", 620, 300, 200)
Write-Host 'Generated original ClipNest SVG, PNG assets and multi-resolution Windows icons.'
