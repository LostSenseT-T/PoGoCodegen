# PoGoCodegen

.NET MAUI application for generating Pokémon GO QR codes and printable PDF cards for duplex printing.

## Usage

* **QR images**: paste promo codes separated by commas, spaces, or line breaks and save them as individual PNG files.
* **Print cards**: paste the codes, set the expiration date, and upload an image of **one** front side and one back side of the card. The code list and URL template are shared between both tabs.
* The back background should contain the permanent design, but no outdated QR code or date. PNG/JPEG images are imported without stretching while preserving their aspect ratio. Cards use a vertical 3:4 aspect ratio.
* QR and date areas are defined as percentages of the card dimensions. Drag the selected area in the preview; the QR size can be adjusted using the slider or a numeric value. The QR code is always square and includes a white quiet zone.
* For the date, you can configure the format, prefix, alignment, color, size, and a custom TTF/OTF font. If the text does not fit, the font size is automatically reduced to fit within the defined area. The built-in Open Sans font is used by default.
* **Save template** stores the design, images, custom font, element positions, and print settings locally. Load it from the **Saved templates** list. **New template** starts a new design; current unsaved settings will be reset.

## Duplex Printing

Default settings: A4 (210 × 297 mm), 4 × 4 grid, 45 × 60 mm cards, 2 mm spacing. The entire grid is centered. You can change the number of rows and columns, card width, and spacing; the card height is automatically calculated to preserve the 3:4 aspect ratio. The application verifies that the grid fits on the page with the required margins.

* **Long edge**: card backs are mirrored horizontally. For each card, `backX = 210 − frontX − cardWidth`; Y remains unchanged.
* **Short edge**: card backs are mirrored vertically. `backY = 297 − frontY − cardHeight`; X remains unchanged.
* Only the **positions** are mirrored, not the images, QR codes, or text themselves. The order and mapping of codes remain unchanged.
* **Back shift X/Y**: an adjustment in millimeters applied after mirroring. Positive values move the back side to the right/down when viewing the PDF page. With zero adjustments, front/back pairs are geometrically aligned.
* Crop marks are placed outside the entire grid rather than on top of the cards.

### Export Modes

**Duplex PDF** creates `cards.pdf`: front side of the first sheet, its back side, front side of the second sheet, its back side, and so on. For 15/16 codes, the PDF contains 2 pages; for 17/30/32 codes, it contains 4 pages. Unused positions remain empty on **both** sides at the corresponding locations.

**Separate** creates two files: `front.pdf` containing the complete shared front page and `backs.pdf` containing the required number of mirrored back pages. Print as many copies of `front.pdf` as there are pages in `backs.pdf`. On the final sheet, extra front cards will not have QR codes on the back and should be discarded.

In the printer dialog, select **A4**, **100% / Actual size**, and **the same flip edge** as configured in the application. Do not enable additional mirroring or “multiple pages per sheet” layout in the printer driver. First, print one test sheet, verify alignment, and scan the QR codes. X/Y correction compensates for a consistent printer offset, but not for paper feed skew.

The PDF contains lossless raster backgrounds, vector QR codes, and vector date text. The recommended print resolution is 300 dpi or higher; raster QR readability validation is performed at 300 dpi. This is a standard print-ready PDF without PDF/X preparation, CMYK conversion, or bleed.

## Build and Verification

On Windows, the project loads only the Windows target by default. `global.json` selects SDK 9.0.3xx (9.0.300 or a newer patch in that series), which is compatible with Rider 2025.1; the application target remains .NET 8. The .NET 8 Runtime is required to run the application. The Windows build runs as a regular x64 application without MSIX deployment.

```powershell
dotnet build PoGOQRCodesGenerator.sln
dotnet run --project PoGOQRCodesGenerator/PoGOQRCodesGenerator.csproj -f net8.0-windows10.0.19041.0
dotnet test PoGOQRCodesGenerator.Printing.Tests/PoGOQRCodesGenerator.Printing.Tests.csproj
```

In Rider, open `PoGOQRCodesGenerator.sln` from the repository root. After changing the SDK, reload the solution if the IDE does not do so automatically. Under **Run → Edit Configurations → .NET Launch Settings Profile**, select **PoGOQRCodesGenerator: Windows Machine**. It uses the Windows target and `Windows Machine (Project)` from `launchSettings.json`.

The old **UWP** profile looks for an MSIX package and fails in this mode with the `Could not load appxrecipe` error.

If the IDE uses a different SDK, check **Settings → Build, Execution, Deployment → Toolset and Build**:

* `.NET CLI` — `C:\Program Files\dotnet\dotnet.exe`
* MSBuild — from the SDK selected by `global.json`

To restore mobile targets on Windows, set `-p:BuildWindowsOnly=false`; the corresponding .NET MAUI workloads and platform SDKs are required. On other operating systems, these targets remain enabled by default.

`BuildWindowsOnly` limits only the application targets; the printing library remains a standard .NET 8 library. Packaged deployment can be enabled using `-p:WindowsPackageType=MSIX` with the corresponding launch profile.

Structure:

* `PoGOQRCodesGenerator` — UI, file importing, and system PDF saving.
* `PoGOQRCodesGenerator.Printing` — code parsing, templates, mirrored geometry, and a shared renderer used for both preview and PDF generation (QRCoder + SkiaSharp).
* `PoGOQRCodesGenerator.Printing.Tests` — tests for page pairs, partially filled sheets, boundaries, calibration, QR readability, physical PDF dimensions, and template persistence.

To generate test output files, set `POGO_PRINT_TEST_OUTPUT` before running the tests. The tests use a synthetic design and `SAMPLE…` codes rather than actual promo codes:

```powershell
$env:POGO_PRINT_TEST_OUTPUT = "$PWD/artifacts/print-validation"
dotnet test PoGOQRCodesGenerator.Printing.Tests/PoGOQRCodesGenerator.Printing.Tests.csproj
pdftoppm -r 300 -png artifacts/print-validation/duplex-long.pdf artifacts/print-validation/rendered-duplex-long
pdftoppm -r 300 -png artifacts/print-validation/duplex-short.pdf artifacts/print-validation/rendered-duplex-short
$env:POGO_PRINT_TEST_OUTPUT = $null
$env:POGO_VERIFY_RENDERED_PDFS = "$PWD/artifacts/print-validation"
dotnet test PoGOQRCodesGenerator.Printing.Tests/PoGOQRCodesGenerator.Printing.Tests.csproj
```

The final verification step decodes all 30 QR codes at their expected physical positions in each duplex PDF variant and verifies all empty positions. Without prepared Poppler-rendered images, this test is marked as skipped; all other tests run without Poppler.