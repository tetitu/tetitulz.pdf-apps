using System.Text;
using Tetitulz.Pdf.Base;
using Tetitulz.Pdf.Builder;
using Tetitulz.Pdf.Builder.Export;
using Tetitulz.Pdf.Builder.Model;
using Tetitulz.Pdf.Plugins.PageEdits;

// Usage: QuickStart [output-directory]
//
// The first steps with the library, one after another:
//   1. build a report in code and write it as a PDF,
//   2. render its first page to PNG,
//   3. extract its text,
//   4. merge it with a second document into one PDF.

Console.OutputEncoding = Encoding.UTF8;

var outputDir = Path.GetFullPath(args.FirstOrDefault() ?? ".");
Directory.CreateDirectory(outputDir);

// 1. Build a document. A4 is 595 x 842 points; a margin of 72 points is one inch.
var reportPath = Path.Combine(outputDir, "report.pdf");
var report = new DocumentBuilder()
    .WithTitle("Quarterly Report")
    .AddSection(s => s
        .PageSize(595, 842)
        .Margins(72, 72, 72, 72)
        .Header(h => h.AddParagraph(p => p.AddText("Contoso Ltd.").Bold().FontSize(10)))
        .AddParagraph(p => p
            .Alignment(TextAlignment.Center)
            .AddText("Quarterly Report").Bold().FontSize(24).Color("#0078D4"))
        .AddParagraph(p => p.AddText("Revenue grew ").AddText("12%").Bold().AddText(" this quarter."))
        .AddTable(t => t
            .AddRow(r => r.AddCell("Item").Bold().AddCell("Price").Bold())
            .AddRow(r => r.AddCell("Widget").AddCell("$10"))
            .AddRow(r => r.AddCell("Gadget").AddCell("$25")))
        .AddList(l => l.Ordered()
            .AddItem("Ship the new viewer")
            .AddItem("Open two more offices")))
    .Build();

using (var output = File.Create(reportPath))
    new PdfExporter().Export(report, output);
Console.WriteLine($"1. Wrote   {reportPath}");

// 2. Render page 1 (pages are zero-based) at 150 dpi.
var pngPath = Path.Combine(outputDir, "report-page1.png");
using (var input = File.OpenRead(reportPath))
    File.WriteAllBytes(pngPath, PdfOperations.RenderPageToPng(input, pageIndex: 0, dpi: 150));
Console.WriteLine($"2. Wrote   {pngPath}");

// 3. Extract the text of every page.
string text;
using (var input = File.OpenRead(reportPath))
    text = PdfOperations.ExtractText(input);
Console.WriteLine("3. Text:");
foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    Console.WriteLine($"     {line}");

// 4. Build a one-page appendix and merge it after the report.
var appendixPath = Path.Combine(outputDir, "appendix.pdf");
var appendix = new DocumentBuilder()
    .WithTitle("Appendix")
    .AddSection(s => s
        .PageSize(595, 842)
        .Margins(72, 72, 72, 72)
        .AddParagraph(p => p.AddText("Appendix A").Bold().FontSize(18))
        .AddParagraph(p => p.AddText("Figures are unaudited.")))
    .Build();
using (var output = File.Create(appendixPath))
    new PdfExporter().Export(appendix, output);

var mergedPath = Path.Combine(outputDir, "merged.pdf");
PdfMerger.Merge(mergedPath, reportPath, appendixPath);
Console.WriteLine($"4. Wrote   {mergedPath} ({PdfOperations.GetPageCount(mergedPath)} pages)");

return 0;
