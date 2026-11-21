using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;
using Tetitulz.Pdf.Base;
using Tetitulz.Pdf.Base.Operations.Documents;
using Tetitulz.Pdf.Base.Operations.Images;
using Tetitulz.Pdf.Builder;
using Tetitulz.Pdf.Builder.Export;
using Tetitulz.Pdf.Builder.Model;

// Usage: PdfGallery [--images <folder>] [output-directory]
//   --images  the folder of images to use (JPEG, PNG, TIFF, BMP, GIF, WebP), in file-name order.
//             Without it, six sample images are drawn into <output-directory>/sample-images.
//
//   1. gallery.pdf                one image per A4 page, scaled to fit inside the margins, numbered,
//   2. gallery-original-size.pdf  one image per page, each page the image's own size,
//   3. contact-sheet.pdf          every image as a captioned thumbnail, three to a row.

Console.OutputEncoding = Encoding.UTF8;

string? imagesArg = null;
string? outputArg = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--images" && i + 1 < args.Length) imagesArg = args[++i];
    else outputArg = args[i];
}
var outputDir = Path.GetFullPath(outputArg ?? ".");
Directory.CreateDirectory(outputDir);

string[] extensions = [".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".gif", ".webp"];
var images = imagesArg != null
    ? Directory.GetFiles(Path.GetFullPath(imagesArg))
        .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToList()
    : DrawSampleImages(Path.Combine(outputDir, "sample-images"));

if (images.Count == 0)
{
    Console.Error.WriteLine($"No images in {imagesArg}.");
    return 1;
}
Console.WriteLine($"{images.Count} images");

// 1. One image per A4 page: FitToPage scales each image into the margins without distorting it,
//    centred. Then a footer with the page number on every page.
var galleryPath = Path.Combine(outputDir, "gallery.pdf");
ImageToPdfResult result;
using (var pages = new MemoryStream())
{
    result = PdfOperations.CreatePdfFromImages(images, pages, new ImageToPdfOptions
    {
        Sizing = ImagePageSizing.FitToPage,
        MarginPoints = 48,
    });
    pages.Position = 0;
    using var output = File.Create(galleryPath);
    PdfOperations.AddHeaderFooter(pages, output, new HeaderFooterOptions
    {
        FooterText = "{page}",
        HorizontalAlignment = HorizontalAlignment.Center,
        FontSize = 9,
        MarginPt = 20,
    });
}
Console.WriteLine($"1. Wrote   {galleryPath} ({result.PagesWritten} pages)");
// An image that cannot be read is skipped, not fatal, and said here. A warning names an image by
// its zero-based position in the list it was given.
foreach (var warning in result.Warnings)
{
    var match = Regex.Match(warning, @"^image (\d+)");
    var name = match.Success && int.Parse(match.Groups[1].Value) is var n && n < images.Count ? Path.GetFileName(images[n]) : "?";
    Console.WriteLine($"     skipped {name}: {warning}");
}

// 2. MatchImage: each page is the image's size at the given resolution, the default for scans.
var originalPath = Path.Combine(outputDir, "gallery-original-size.pdf");
using (var output = File.Create(originalPath))
    PdfOperations.CreatePdfFromImages(images, output, new ImageToPdfOptions { Sizing = ImagePageSizing.MatchImage, Dpi = 96 });
Console.WriteLine($"2. Wrote   {originalPath}");

// 3. A contact sheet: thumbnails in a three-column table, each above its file name. A4 is 595
//    points wide; less 36-point margins, each column is 174 points.
const int columns = 3;
const double cellWidth = 174, thumbWidth = 160, thumbHeight = 120;
var thumbs = images
    .Select(path => (Name: Path.GetFileName(path), Thumb: Thumbnail(path, thumbWidth, thumbHeight)))
    .Where(t => t.Thumb != null)
    .ToList();

var sheet = new DocumentBuilder()
    .WithTitle("Contact sheet")
    .AddSection(s =>
    {
        s.PageSize(595, 842).Margins(36, 36, 36, 36)
         .AddParagraph(p => p.AddText("Contact sheet").Bold().FontSize(18))
         .AddParagraph(p => p.SpacingAfter(12).AddText($"{thumbs.Count} images").FontSize(10).Color("#666666"));
        s.AddTable(t =>
        {
            t.ColumnWidths(Enumerable.Repeat(cellWidth, columns).ToArray()).BorderColor("#DDDDDD");
            foreach (var row in thumbs.Chunk(columns))
            {
                t.AddRow(r =>
                {
                    for (var c = 0; c < columns; c++)
                    {
                        if (c >= row.Length) { r.AddCell(""); continue; }
                        var (name, thumb) = row[c];
                        r.AddCell(cell => cell
                            .AddParagraph(p => p.Alignment(TextAlignment.Center).AddImage(thumb!.Value.Jpeg, thumb.Value.Width, thumb.Value.Height, name))
                            .AddParagraph(p => p.Alignment(TextAlignment.Center).AddText(name).FontSize(8)));
                    }
                });
            }
        });
    })
    .Build();

var sheetPath = Path.Combine(outputDir, "contact-sheet.pdf");
using (var output = File.Create(sheetPath))
    new PdfExporter().Export(sheet, output);
Console.WriteLine($"3. Wrote   {sheetPath} ({PdfOperations.GetPageCount(sheetPath)} pages)");
if (thumbs.Count < images.Count)
    Console.WriteLine($"     {images.Count - thumbs.Count} image(s) left out: SkiaSharp, which draws the thumbnails, cannot decode them (a TIFF, or a broken file).");

return result.PagesWritten > 0 ? 0 : 1;

// A JPEG thumbnail that fits the box, with its size in points; null when SkiaSharp cannot decode the file.
static (byte[] Jpeg, double Width, double Height)? Thumbnail(string path, double maxWidth, double maxHeight)
{
    using var bitmap = SKBitmap.Decode(path);
    if (bitmap == null) return null;
    var scale = Math.Min(maxWidth / bitmap.Width, maxHeight / bitmap.Height);
    double width = bitmap.Width * scale, height = bitmap.Height * scale;
    // Twice the size in pixels as in points, so the thumbnail stays sharp when the sheet is zoomed or printed.
    using var resized = bitmap.Resize(new SKImageInfo((int)(width * 2), (int)(height * 2)), new SKSamplingOptions(SKFilterMode.Linear));
    using var data = resized.Encode(SKEncodedImageFormat.Jpeg, 85);
    return (data.ToArray(), width, height);
}

// Six sample "photos": landscape and portrait, JPEG and PNG, each a gradient with its number on it.
static List<string> DrawSampleImages(string dir)
{
    Directory.CreateDirectory(dir);
    var samples = new (int W, int H, SKColor From, SKColor To, SKEncodedImageFormat Format)[]
    {
        (1600, 1066, new SKColor(0x1E, 0x3C, 0x72), new SKColor(0x2A, 0x9D, 0x8F), SKEncodedImageFormat.Jpeg),
        (1066, 1600, new SKColor(0xE7, 0x6F, 0x51), new SKColor(0xF4, 0xA2, 0x61), SKEncodedImageFormat.Jpeg),
        (1600, 900,  new SKColor(0x26, 0x46, 0x53), new SKColor(0xE9, 0xC4, 0x6A), SKEncodedImageFormat.Png),
        (1200, 1200, new SKColor(0x6A, 0x4C, 0x93), new SKColor(0xC0, 0x84, 0xFC), SKEncodedImageFormat.Png),
        (1600, 1066, new SKColor(0x0B, 0x3D, 0x2E), new SKColor(0x8A, 0xB1, 0x7D), SKEncodedImageFormat.Jpeg),
        (900,  1600, new SKColor(0x3D, 0x0B, 0x1E), new SKColor(0xF2, 0x84, 0x82), SKEncodedImageFormat.Jpeg),
    };

    var paths = new List<string>();
    for (var i = 0; i < samples.Length; i++)
    {
        var (w, h, from, to, format) = samples[i];
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        var canvas = surface.Canvas;
        using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h), [from, to], SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader })
            canvas.DrawRect(0, 0, w, h, paint);
        using (var font = new SKFont(SKTypeface.Default, Math.Min(w, h) / 3f))
        using (var paint = new SKPaint { Color = SKColors.White.WithAlpha(220), IsAntialias = true })
            canvas.DrawText($"{i + 1}", w / 2f, h / 2f + font.Size / 3, SKTextAlign.Center, font, paint);

        var path = Path.Combine(dir, $"photo-{i + 1:D2}.{(format == SKEncodedImageFormat.Png ? "png" : "jpg")}");
        using var image = surface.Snapshot();
        using var data = image.Encode(format, 90);
        File.WriteAllBytes(path, data.ToArray());
        paths.Add(path);
    }
    return paths;
}
