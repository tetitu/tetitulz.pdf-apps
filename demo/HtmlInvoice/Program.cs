using System.Globalization;
using System.Text;
using Tetitulz.Pdf.Core.Plugins;
using Tetitulz.Pdf.HtmlToPdf;
using Tetitulz.Pdf.Plugins.HtmlBrowser;

// Usage: HtmlInvoice [--install] [output-directory]
//   --install  downloads the Chromium build Playwright drives (once per machine).
//
// Renders two invoices - a short English one and a long right-to-left Hebrew one that runs over
// two pages - with each of two engines: the managed one (native, no browser) and Chromium.

var install = args.Contains("--install");
var outputDir = args.FirstOrDefault(a => a != "--install") ?? ".";
Directory.CreateDirectory(outputDir);

if (install)
{
    PlaywrightHtmlBrowserPlugin.InstallBrowsers(BrowserEngine.Chromium);
}

// The browser engine is a plugin; the managed engine needs none.
var browser = new PlaywrightHtmlBrowserPlugin();
browser.Initialize();
PdfPluginHost.Instance.Register(browser);

var invoices = new (string Name, string Html)[]
{
    ("Invoice", EnglishInvoice()),
    ("InvoiceHebrew", HebrewInvoice()),
};

var engines = new (string Suffix, HtmlRenderEngine Engine)[]
{
    ("native", HtmlRenderEngine.Managed),
    ("chrome", HtmlRenderEngine.Browser),
};

var failures = 0;
foreach (var (name, html) in invoices)
{
    foreach (var (suffix, engine) in engines)
    {
        var path = Path.GetFullPath(Path.Combine(outputDir, $"{name}.{suffix}.pdf"));
        var options = new HtmlToPdfOptions
        {
            RenderEngine = engine,
            // A centered footer at the foot of every page, the same for each engine.
            // An empty header, or Chromium prints its default date-and-title one.
            DisplayHeaderFooter = true,
            HeaderTemplate = "<span></span>",
            FooterTemplate = "<div style='font-size:10px; text-align:center; width:100%;'>Produced By Tetitulz PDF</div>",
        };

        try
        {
            HtmlToPdfConverter.ConvertHtml(html, path, options);
            Console.WriteLine($"Wrote {path}");
        }
        catch (Exception ex)
        {
            failures++;
            Console.Error.WriteLine($"{Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
            for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                Console.Error.WriteLine($"  -> {inner.GetType().Name}: {inner.Message.Split('\n')[0]}");
        }
    }
}

if (failures > 0 && !install)
    Console.Error.WriteLine("If a browser engine failed for want of its browser, run once with --install.");

return failures == 0 ? 0 : 1;

static string EnglishInvoice()
{
    var date = DateTime.Today.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

    return $$"""
        <html>
        <head>
            <style>
                h1 { color: #3333ff; font-family: Arial; }
                .date { text-align: right; font-family: Arial; }
                .invoice-table { width: 100%; border-collapse: collapse; }
                .invoice-table td { padding: 11px; border: 1px solid #999999; }
            </style>
        </head>
        <body>
            <p class='date'>Date: {{date}}</p>
            <h1>Invoice #INV-2027-001</h1>
            <table class='invoice-table'>
                <tr><td>Service</td><td>Consulting</td></tr>
                <tr><td>Amount</td><td>$12,000.00</td></tr>
            </table>
        </body>
        </html>
        """;
}

// Right-to-left Hebrew with English terms mixed in, and enough line items to run over two pages.
static string HebrewInvoice()
{
    var date = DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    (string Item, decimal Rate)[] services =
    [
        ("ייעוץ Cloud Architecture", 450m),
        ("פיתוח REST API ב-.NET 10", 380m),
        ("בדיקות QA אוטומטיות עם Playwright", 320m),
        ("הטמעת CI/CD ב-Azure DevOps", 400m),
        ("אופטימיזציה של SQL Server", 420m),
        ("עיצוב UI/UX למסכי Dashboard", 300m),
        ("הדרכת צוות על Git ו-Code Review", 350m),
        ("תחזוקת שרתי Linux ו-Docker", 330m),
        ("סקירת אבטחה (Security Review) לפי OWASP", 480m),
        ("המרת מסמכים ל-PDF/A לארכיון", 290m),
    ];

    var rows = new StringBuilder();
    decimal subtotal = 0;
    const int lineCount = 50;
    for (var i = 0; i < lineCount; i++)
    {
        var (item, rate) = services[i % services.Length];
        var hours = 2 + (i * 7 % 11);
        var amount = hours * rate;
        subtotal += amount;
        rows.Append($"""
                <tr>
                    <td class='num'>{i + 1}</td>
                    <td>{item} - שלב {i / services.Length + 1}</td>
                    <td class='num'>{hours}</td>
                    <td class='num'>{Money(rate)}</td>
                    <td class='num'>{Money(amount)}</td>
                </tr>
        """).Append('\n');
    }

    const decimal vatRate = 0.18m;
    var vat = subtotal * vatRate;
    var total = subtotal + vat;

    return $$"""
        <html lang='he' dir='rtl'>
        <head>
            <meta charset='utf-8'>
            <style>
                body { font-family: Arial; }
                h1 { color: #3333ff; }
                .date { text-align: right; }
                .invoice-table { width: 100%; border-collapse: collapse; }
                .invoice-table th { background: #3333ff; color: #ffffff; padding: 8px; text-align: right; }
                .invoice-table td { padding: 11px; border: 1px solid #999999; }
                .num { text-align: left; }
                .totals td { font-weight: bold; }
            </style>
        </head>
        <body>
            <p class='date'>תאריך: {{date}}</p>
            <h1>חשבונית מס #INV-2027-002</h1>
            <p>לכבוד: חברת Acme Software Ltd., רחוב הרצל 10, תל אביב</p>
            <p>עבור שירותי פיתוח וייעוץ (Development &amp; Consulting) לפרויקט Tetitulz PDF.</p>
            <table class='invoice-table'>
                <thead>
                    <tr><th>#</th><th>שירות (Service)</th><th>שעות</th><th>תעריף לשעה</th><th>סכום</th></tr>
                </thead>
                <tbody>
        {{rows}}
                    <!-- In the body, not a <tfoot>: a browser repeats a <tfoot> on every page. -->
                    <tr class='totals'><td colspan='4'>סכום ביניים (Subtotal)</td><td class='num'>{{Money(subtotal)}}</td></tr>
                    <tr class='totals'><td colspan='4'>מע"מ (VAT) 18%</td><td class='num'>{{Money(vat)}}</td></tr>
                    <tr class='totals'><td colspan='4'>סה"כ לתשלום (Total)</td><td class='num'>{{Money(total)}}</td></tr>
                </tbody>
            </table>
            <p>תנאי תשלום: שוטף + 30 (Net 30). העברה בנקאית לחשבון 12-345-678901, Bank Hapoalim.</p>
            <p>תודה על שיתוף הפעולה! Thank you for your business.</p>
        </body>
        </html>
        """;

    static string Money(decimal value) => "₪" + value.ToString("N2", CultureInfo.InvariantCulture);
}
