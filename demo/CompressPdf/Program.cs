using System.Text;
using SkiaSharp;
using Tetitulz.Pdf.Base;
using Tetitulz.Pdf.Base.Operations.Images;
using Tetitulz.Pdf.Plugins.Maintenance;

// Usage: CompressPdf [--input <file.pdf>] [--level lossless|balanced|aggressive] [output-directory]
//   --input  the PDF to compress. Without it, a bloated sample is made: three pages of large
//            photo-like PNG images, the first repeated on the last page.
//   --level  one preset only. Without it, all three run, each to its own file.
//
// The presets, from safest to smallest:
//   lossless    nothing a reader can see changes: streams recompressed, duplicates merged,
//               unused objects dropped, fonts subset, object streams.
//   balanced    lossless + thumbnails dropped, images re-encoded as JPEG at quality 75, 200 dpi.
//   aggressive  balanced + metadata dropped, images at quality 60, 150 dpi.
// None of them removes annotations, form fields or the structure tree.

Console.OutputEncoding = Encoding.UTF8;

string? inputArg = null, levelArg = null, outputArg = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--input" && i + 1 < args.Length) inputArg = args[++i];
    else if (args[i] == "--level" && i + 1 < args.Length) levelArg = args[++i].ToLowerInvariant();
    else outputArg = args[i];
}
var outputDir = Path.GetFullPath(outputArg ?? ".");
Directory.CreateDirectory(outputDir);

var presets = new (string Name, Func<OptimizationOptions> Options)[]
{
    ("lossless", () => OptimizationOptions.Lossless),
    ("balanced", () => OptimizationOptions.Balanced),
    ("aggressive", () => OptimizationOptions.Aggressive),
};
if (levelArg != null)
{
    presets = presets.Where(p => p.Name == levelArg).ToArray();
    if (presets.Length == 0)
    {
        Console.Error.WriteLine($"Unknown --level '{levelArg}': use lossless, balanced or aggressive.");
        return 2;
    }
}

var inputPath = inputArg != null ? Path.GetFullPath(inputArg) : MakeBloatedSample(Path.Combine(outputDir, "sample.pdf"));
var inputPages = PdfOperations.GetPageCount(inputPath);
var inputText = PdfOperations.ExtractText(inputPath);
Console.WriteLine($"Input     {inputPath}");
Console.WriteLine($"          {Size(new FileInfo(inputPath).Length)}, {inputPages} page{(inputPages == 1 ? "" : "s")}");
Console.WriteLine();
Console.WriteLine($"{"preset",-11} {"size",10} {"of input",9} {"images re-encoded",18} {"duplicates merged",18}  file");

var failures = 0;
foreach (var (name, options) in presets)
{
    var outputPath = Path.Combine(outputDir, $"{Path.GetFileNameWithoutExtension(inputPath)}-{name}.pdf");
    var result = PdfOptimizer.Optimize(inputPath, outputPath, options());

    Console.WriteLine($"{name,-11} {Size(result.OutputBytes),10} {result.CompressionRatio,9:P0} {result.ImagesOptimized,18} {result.DuplicatesRemoved,18}  {Path.GetFileName(outputPath)}");
    // NeverGrow (set by every preset): when the rewrite would be larger, the input is written as it was.
    if (result.FellBackToInput)
        Console.WriteLine("            already as small as this preset makes it: the input was kept unchanged");
    foreach (var (index, error) in result.ImageFailures ?? new Dictionary<int, Exception>())
        Console.WriteLine($"            image {index} left as it was: {error.Message}");

    // Check what was kept: the same pages, and for the lossless preset the same text.
    var pages = PdfOperations.GetPageCount(outputPath);
    if (pages != inputPages)
    {
        failures++;
        Console.WriteLine($"            ERROR: {pages} pages, the input has {inputPages}");
    }
    if (name == "lossless" && PdfOperations.ExtractText(outputPath) != inputText)
    {
        failures++;
        Console.WriteLine("            ERROR: the text differs from the input's");
    }
}

return failures == 0 ? 0 : 1;

static string Size(long bytes) => bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):F1} MB" : $"{bytes / 1024.0:F0} KB";

// Three A4 pages of photo-like PNG images, which a PDF stores losslessly (Flate) and so large; the
// first image appears again on page 3, embedded a second time. Real files bloat the same way:
// scans saved as PNG, and one logo or photo embedded on every page that shows it.
static string MakeBloatedSample(string path)
{
    var dir = Path.Combine(Path.GetDirectoryName(path)!, "sample-images");
    Directory.CreateDirectory(dir);
    var random = new Random(42);
    var colours = new (SKColor From, SKColor To)[]
    {
        (new SKColor(0x1E, 0x3C, 0x72), new SKColor(0x2A, 0x9D, 0x8F)),
        (new SKColor(0xE7, 0x6F, 0x51), new SKColor(0xF4, 0xA2, 0x61)),
    };

    var images = new List<string>();
    for (var i = 0; i < colours.Length; i++)
    {
        const int w = 2400, h = 1600;
        using var bitmap = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(bitmap))
        using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h), [colours[i].From, colours[i].To], SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader })
            canvas.DrawRect(0, 0, w, h, paint);

        // Grain, as a photo has: what makes a lossless encoding big and a JPEG small.
        var pixels = bitmap.GetPixelSpan().ToArray();
        for (var p = 0; p < pixels.Length; p += 4)
        {
            var grain = random.Next(-24, 25);
            for (var c = 0; c < 3; c++)
                pixels[p + c] = (byte)Math.Clamp(pixels[p + c] + grain, 0, 255);
        }
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);

        var imagePath = Path.Combine(dir, $"photo-{i + 1}.png");
        using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
            File.WriteAllBytes(imagePath, data.ToArray());
        images.Add(imagePath);
    }
    images.Add(images[0]);

    using (var output = File.Create(path))
        PdfOperations.CreatePdfFromImages(images, output, new ImageToPdfOptions { Sizing = ImagePageSizing.FitToPage, MarginPoints = 36 });
    return path;
}
