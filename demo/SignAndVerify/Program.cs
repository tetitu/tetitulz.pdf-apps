using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Tetitulz.Pdf.Builder;
using Tetitulz.Pdf.Builder.Export;
using Tetitulz.Pdf.Signing;
using Tetitulz.Pdf.Signing.Models;

// Usage: SignAndVerify [--tsa <url>] [output-directory]
//   --tsa  an RFC 3161 timestamp authority, which raises the signature from PAdES B-B to B-T
//          (for example http://timestamp.digicert.com). Without it nothing goes over the network.
//
//   1. contract.pdf          the document to sign,
//   2. signer.pfx            a self-signed test certificate, made here (use your own in production),
//   3. contract-signed.pdf   the signed document,
//   4. (verified)            twice: with no trust anchor, then trusting the test certificate,
//   5. contract-tampered.pdf the title changed after signing, and the verification that catches it.

Console.OutputEncoding = Encoding.UTF8;

string? tsaUrl = null;
string? outputArg = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--tsa" && i + 1 < args.Length) tsaUrl = args[++i];
    else outputArg = args[i];
}
var outputDir = Path.GetFullPath(outputArg ?? ".");
Directory.CreateDirectory(outputDir);

var contractPath = Path.Combine(outputDir, "contract.pdf");
var pfxPath = Path.Combine(outputDir, "signer.pfx");
var signedPath = Path.Combine(outputDir, "contract-signed.pdf");
var tamperedPath = Path.Combine(outputDir, "contract-tampered.pdf");
const string pfxPassword = "demo";

// 1. The document.
var contract = new DocumentBuilder()
    .WithTitle("Service Agreement")
    .AddSection(s => s
        .PageSize(595, 842)
        .Margins(72, 72, 72, 72)
        .AddParagraph(p => p.AddText("Service Agreement").Bold().FontSize(20))
        .AddParagraph(p => p.AddText("The supplier will deliver 100 units for a total of $5,000.")))
    .Build();
using (var output = File.Create(contractPath))
    new PdfExporter().Export(contract, output);
Console.WriteLine($"1. Wrote   {contractPath}");

// 2. A self-signed certificate. A signing certificate needs the digitalSignature or
//    nonRepudiation key usage; the verifier checks it (RequireSignerKeyPurpose).
using (var rsa = RSA.Create(2048))
{
    var request = new CertificateRequest("CN=Demo Signer, O=Contoso", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    request.CertificateExtensions.Add(new X509KeyUsageExtension(
        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));
    using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    File.WriteAllBytes(pfxPath, certificate.Export(X509ContentType.Pfx, pfxPassword));
}
Console.WriteLine($"2. Wrote   {pfxPath}");

// 3. Sign. ApplyPadesBaseline sets the ETSI sub-filter and, from B-T up, the timestamp.
var parameters = new SignatureParameters
{
    CertificatePath = pfxPath,
    CertificatePassword = pfxPassword,
    Reason = "Approved",
    Location = "Tel Aviv",
}.ApplyPadesBaseline(tsaUrl == null ? PadesBaseline.B_B : PadesBaseline.B_T, tsaUrl);

using (var input = File.OpenRead(contractPath))
using (var output = File.Create(signedPath))
    new PdfSigner().Sign(input, output, parameters);
Console.WriteLine($"3. Wrote   {signedPath} (PAdES {parameters.PadesBaselineLevel})");

// 4. Verify. IsValid says the bytes match the signature: integrity only. Trust needs anchors,
//    the certificates you trust; IsFullyTrusted is the verdict to show a user.
var anchor = new Org.BouncyCastle.X509.X509Certificate(X509CertificateLoader.LoadPkcs12FromFile(pfxPath, pfxPassword).RawData);

Console.WriteLine("4. Verify with no trust anchor:");
Report(Verify(signedPath, null));
Console.WriteLine("   Verify trusting the test certificate:");
Report(Verify(signedPath, new SignatureVerificationOptions { TrustAnchors = [anchor] }));

// 5. Edit the signed bytes: the document title, which the file stores as plain text, goes from
//    "Service Agreement" to "Revised Agreement", and the signature no longer matches.
var bytes = File.ReadAllBytes(signedPath);
var at = IndexOf(bytes, Encoding.ASCII.GetBytes("(Service Agreement)"));
if (at < 0)
{
    Console.WriteLine("5. Skipped: the title is not stored as plain text in this file.");
    return 0;
}
Encoding.ASCII.GetBytes("(Revised").CopyTo(bytes, at);
File.WriteAllBytes(tamperedPath, bytes);
Console.WriteLine($"5. Wrote   {tamperedPath}");
var tampered = Verify(tamperedPath, new SignatureVerificationOptions { TrustAnchors = [anchor] });
Report(tampered);

// The demo succeeds when the tampered copy is refused.
return tampered.All(r => !r.IsValid) ? 0 : 1;

static IReadOnlyList<SignatureVerificationResult> Verify(string path, SignatureVerificationOptions? options)
{
    using var input = File.OpenRead(path);
    return PdfSigningOperations.VerifySignatures(input, options);
}

static void Report(IReadOnlyList<SignatureVerificationResult> results)
{
    foreach (var r in results)
    {
        Console.WriteLine($"     {r.FieldName}: signed by {r.SignerCommonName}");
        Console.WriteLine($"       IsValid (integrity)    {r.IsValid}");
        Console.WriteLine($"       ChainStatus            {r.ChainStatus}");
        Console.WriteLine($"       IsFullyTrusted         {r.IsFullyTrusted}");
        Console.WriteLine($"       CoversEntireDocument   {r.CoversEntireDocument}");
        foreach (var e in r.ValidationErrors) Console.WriteLine($"       error:   {e}");
        foreach (var w in r.Warnings) Console.WriteLine($"       warning: {w}");
    }
}

static int IndexOf(byte[] haystack, byte[] needle)
{
    for (var i = 0; i <= haystack.Length - needle.Length; i++)
        if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            return i;
    return -1;
}
