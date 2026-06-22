using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Instrumind.Common.Portable;

namespace Instrumind.Common.Platform
{
    public interface IPdfReportExporter
    {
        Task ExportAsync(PdfReportDocument document, Stream output, CancellationToken cancellationToken);
    }

    public sealed class PdfReportDocument
    {
        public PdfReportDocument()
        {
            Sections = new List<PdfReportSection>();
            Metadata = new Dictionary<string, string>();
        }

        public string Title { get; set; }
        public IList<PdfReportSection> Sections { get; private set; }
        public IDictionary<string, string> Metadata { get; private set; }
    }

    public sealed class PdfReportSection
    {
        public PdfReportSection()
        {
            Blocks = new List<PdfReportBlock>();
        }

        public string Heading { get; set; }
        public IList<PdfReportBlock> Blocks { get; private set; }
    }

    public abstract class PdfReportBlock
    {
    }

    public sealed class PdfParagraphBlock : PdfReportBlock
    {
        public string Text { get; set; }
        public TcTextFormat Format { get; set; }
    }

    public sealed class PdfImageBlock : PdfReportBlock
    {
        public byte[] ImageBytes { get; set; }
        public string ContentType { get; set; }
        public TcSize Size { get; set; }
    }
}
