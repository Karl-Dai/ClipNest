# ClipNest visual identity

Original artwork created for ClipNest in October 2026. Two notes rest above a
curved nest, expressing a home for copied content. The teal tile, cream notes,
and mint rear sheet are independent of the upstream VeloxClip artwork.

- `clipnest.svg`: editable vector master, 256-unit view box.
- `clipnest-512.png`: transparent 512-pixel export.
- `../build/Generate-BrandAssets.ps1`: reproducible source geometry and exporter,
  using Windows PowerShell and the built-in System.Drawing library.

The ICO contains 16, 20, 24, 32, 40, 48, 64, 128 and 256-pixel PNG frames.
At 16–32 pixels, a single note and a slightly heavier nest replace the stacked
notes and text strokes. This optical variant is used in the small header mark
and the corresponding ICO frames. All Windows tile and splash assets share
the same new artwork, with transparent margins.

Regenerate from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build/Generate-BrandAssets.ps1
```
