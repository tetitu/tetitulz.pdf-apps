using System.Text;
using Tetitulz.Pdf.Base;
using Tetitulz.Pdf.Base.Operations.Forms;
using Tetitulz.Pdf.Builder;
using Tetitulz.Pdf.Builder.Export;
using Tetitulz.Pdf.Plugins.Fonts;

// Usage: FormFill [output-directory]
//
//   1. form.pdf      a page with a text field, a Hebrew text field, a drop-down and a check box,
//   2. (listed)      the fields read back: the names FillForm takes,
//   3. filled.pdf    the fields filled, still editable in any viewer,
//   4. flattened.pdf the filled values drawn into the page, no longer a form.
//
// To fill a form you already have, skip step 1 and start from step 2 with your own file.

Console.OutputEncoding = Encoding.UTF8;

var outputDir = Path.GetFullPath(args.FirstOrDefault() ?? ".");
Directory.CreateDirectory(outputDir);

var blankPath = Path.Combine(outputDir, "blank.pdf");
var formPath = Path.Combine(outputDir, "form.pdf");
var filledPath = Path.Combine(outputDir, "filled.pdf");
var flattenedPath = Path.Combine(outputDir, "flattened.pdf");

// 1. A page with labels, then the fields placed beside them. Field positions are in points from
//    the page's lower-left corner; the page is A4 (595 x 842).
var page = new DocumentBuilder()
    .WithTitle("Registration")
    .AddSection(s => s
        .PageSize(595, 842)
        .Margins(72, 72, 72, 72)
        .AddParagraph(p => p.AddText("Registration").Bold().FontSize(20))
        .AddParagraph(p => p.AddText("Name").SpacingBefore(24))
        .AddParagraph(p => p.AddText("Name in Hebrew").SpacingBefore(14))
        .AddParagraph(p => p.AddText("Plan").SpacingBefore(14))
        .AddParagraph(p => p.AddText("Subscribe to the newsletter").SpacingBefore(14)))
    .Build();
using (var output = File.Create(blankPath))
    new PdfExporter().Export(page, output);

var fields = new List<FormFieldBase>
{
    new FormTextField  { FieldName = "name",       X = 240, Y = 702, Width = 220, Height = 20 },
    new FormTextField  { FieldName = "nameHebrew", X = 240, Y = 668, Width = 220, Height = 20 },
    new FormComboField
    {
        FieldName = "plan", X = 240, Y = 633, Width = 220, Height = 20,
        Options = { new ChoiceOption("Free"), new ChoiceOption("Pro"), new ChoiceOption("Enterprise") },
    },
    new FormCheckboxField { FieldName = "newsletter", X = 240, Y = 602, Width = 16, Height = 16 },
};
using (var input = File.OpenRead(blankPath))
using (var output = File.Create(formPath))
    PdfFormFieldBuilder.AddFields(input, output, fields);
Console.WriteLine($"1. Wrote   {formPath}");

// 2. List the fields: FullyQualifiedName is the key FillForm takes; a check box says which
//    state names turn it on.
Console.WriteLine("2. Fields:");
foreach (var f in PdfOperations.GetFormFields(formPath))
{
    var states = f.OnStates.Count > 0 ? $"  on-states: {string.Join(", ", f.OnStates)}" : "";
    Console.WriteLine($"     {f.FullyQualifiedName,-12} {f.FieldType}{states}");
}

// 3. Fill. A Standard-14 font cannot draw Hebrew, so the fill asks FontProvider for a font program
//    for such text; here, the Noto Sans Hebrew the Fonts package carries. With no provider the value
//    is still stored, and the result says it could not be drawn.
var fontDir = BundledFonts.Register();
var hebrewFont = File.ReadAllBytes(Path.Combine(fontDir, "NotoSansHebrew-Regular.ttf"));

var values = new Dictionary<string, string>
{
    ["name"] = "Dana Levi",
    ["nameHebrew"] = "דנה לוי",
    ["plan"] = "Pro",
    ["newsletter"] = "true",
};

FormFillResult result;
using (var input = File.OpenRead(formPath))
using (var output = File.Create(filledPath))
    result = PdfOperations.FillForm(input, output, values, new FormFillOptions { FontProvider = _ => hebrewFont });

Console.WriteLine($"3. Wrote   {filledPath}");
Console.WriteLine($"     filled:    {string.Join(", ", result.FilledFields)}");
if (result.UnmatchedFields.Count > 0)
    Console.WriteLine($"     unmatched: {string.Join(", ", result.UnmatchedFields)}");
foreach (var warning in result.Warnings)
    Console.WriteLine($"     warning:   {warning}");

// 4. Flatten: the appearances become page content and the fields are removed.
using (var input = File.OpenRead(filledPath))
using (var output = File.Create(flattenedPath))
    PdfOperations.FlattenForm(input, output);
Console.WriteLine($"4. Wrote   {flattenedPath} ({PdfOperations.GetFormFields(flattenedPath).Count} fields left)");

File.Delete(blankPath);
return result.UnmatchedFields.Count == 0 ? 0 : 1;
