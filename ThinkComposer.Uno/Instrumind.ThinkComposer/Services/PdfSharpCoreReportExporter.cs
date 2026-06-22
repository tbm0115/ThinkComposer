using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Instrumind.Common.Platform;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace Instrumind.ThinkComposer.Services;

public sealed class PdfSharpCoreReportExporter : IPdfReportExporter
{
    public Task ExportAsync(PdfReportDocument document, Stream output, CancellationToken cancellationToken)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));
        if (output == null)
            throw new ArgumentNullException(nameof(output));

        cancellationToken.ThrowIfCancellationRequested();

        using var pdf = new PdfDocument();
        pdf.Info.Title = string.IsNullOrWhiteSpace(document.Title) ? "ThinkComposer Report" : document.Title;

        var page = pdf.AddPage();
        var graphics = XGraphics.FromPdfPage(page);
        var titleFont = new XFont("Arial", 18, XFontStyle.Bold);
        var headingFont = new XFont("Arial", 13, XFontStyle.Bold);
        var bodyFont = new XFont("Arial", 11, XFontStyle.Regular);
        var y = 48.0;

        graphics.DrawString(pdf.Info.Title, titleFont, XBrushes.Black, new XRect(48, y, page.Width - 96, 28));
        y += 42;

        foreach (var section in document.Sections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(section.Heading))
            {
                graphics.DrawString(section.Heading, headingFont, XBrushes.Black, new XRect(48, y, page.Width - 96, 22));
                y += 26;
            }

            foreach (var block in section.Blocks)
            {
                if (block is PdfParagraphBlock paragraph && !string.IsNullOrWhiteSpace(paragraph.Text))
                {
                    graphics.DrawString(paragraph.Text, bodyFont, XBrushes.Black, new XRect(48, y, page.Width - 96, 18));
                    y += 22;
                }
            }

            y += 12;
        }

        pdf.Save(output, false);
        return Task.CompletedTask;
    }
}
