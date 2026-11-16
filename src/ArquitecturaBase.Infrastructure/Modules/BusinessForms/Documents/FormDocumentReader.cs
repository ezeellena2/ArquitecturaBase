using System.IO.Compression;
using System.Xml;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Files;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Documents;

// The reading of documents (Parte L): what an attached document is, by its bytes, and the little that is read here, without
// rendering a page, decoding pixels or running anything. PdfPig opens a PDF only to count its pages; SkiaSharp reads the header of
// a photo; a Word .docx gives its body as text, read node by node from the main part OpenXml finds, after the ZIP preflight of the
// import. A PDF or a Word is accepted
// only as the private files accept it (DocumentSignature), so what is prepared here can later be stored with a response. The reader
// logs only that a PDF ran out of time or memory: names, hashes and contents never leave it.
internal sealed partial class FormDocumentReader : IFormDocumentReader
{
    // PdfPig follows the page tree and chains of references recursively: a PDF with more indirect objects than this is unreadable
    // before PdfPig sees it, which bounds every chain, and PdfPig runs on a thread of its own with a large stack, a few at a time.
    internal const int MaximumPdfObjects = 20_000;
    // What all the compressed streams of a PDF may inflate to, in all, and how long a PDF may take to be read (F6).
    internal const long MaximumPdfInflatedBytes = 64L * 1024 * 1024;
    internal static readonly TimeSpan PdfReadTimeout = TimeSpan.FromSeconds(20);
    private const int PdfStackBytes = 128 * 1024 * 1024;
    private const int MaximumConcurrentPdfs = 2;
    // How many readings that went past their time may still be running: with that many alive, a new PDF is unreadable without opening.
    internal const int MaximumAbandonedPdfs = 2;
    private const int MaximumManifestBytes = 64 * 1024;
    private const string MainWordPart = "word/document.xml";
    private const string WordContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
    private static readonly FormDocumentContent Unreadable = new(FormDocumentReadStatus.Unreadable);
    private static readonly SemaphoreSlim PdfGate = new(MaximumConcurrentPdfs, MaximumConcurrentPdfs);

    private readonly ILogger<FormDocumentReader> logger;
    private readonly TimeSpan pdfTimeout;
    private readonly Func<byte[], FormDocumentContent> openPdf;
    private int abandonedPdfs;

    public FormDocumentReader(ILogger<FormDocumentReader> logger) : this(logger, PdfReadTimeout, null)
    {
    }

    // The way to open a PDF and its time are replaceable only for the tests of the timeout.
    internal FormDocumentReader(ILogger<FormDocumentReader> logger, TimeSpan pdfTimeout, Func<byte[], FormDocumentContent>? openPdf)
    {
        this.logger = logger;
        this.pdfTimeout = pdfTimeout;
        this.openPdf = openPdf ?? OpenPdf;
    }

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // What the bytes are, never the name: a PDF by its header, a photo by its signature (SkiaSharp decides when reading), an OLE2
    // container with a «WordDocument» stream is a Word older than 2007, and a ZIP is a Word by its OOXML manifest. Anything else is
    // not a document: an OLE2 with a password does not say whether it was a Word or an Excel, so the spreadsheet reader takes it.
    public FormDocumentFormat DetectFormat(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var bytes = content.AsSpan();
        if (bytes.StartsWith("%PDF-"u8)) return FormDocumentFormat.Pdf;
        if (bytes.StartsWith(JpegSignature)) return FormDocumentFormat.Jpeg;
        if (bytes.StartsWith(PngSignature)) return FormDocumentFormat.Png;
        if (CompoundDocument.IsCompound(bytes)) return CompoundFormat(content);
        return bytes.StartsWith("PK\u0003\u0004"u8) ? PackageFormat(content) : FormDocumentFormat.Unknown;
    }

    public async Task<FormDocumentContent> ReadAsync(byte[] content, FormDocumentFormat format, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();
        return format switch
        {
            FormDocumentFormat.Pdf => await ReadPdfAsync(content, cancellationToken),
            FormDocumentFormat.Jpeg => ReadPhoto(content, SKEncodedImageFormat.Jpeg, logger),
            FormDocumentFormat.Png => ReadPhoto(content, SKEncodedImageFormat.Png, logger),
            FormDocumentFormat.Docx => ReadWord(content, cancellationToken),
            _ => Unreadable,
        };
    }

    private static FormDocumentFormat CompoundFormat(byte[] content)
    {
        try
        {
            return CompoundDocument.HasStream(content, "WordDocument") ? FormDocumentFormat.WordLegacy : FormDocumentFormat.Unknown;
        }
        catch (Exception exception) when (exception is IOException or FormatException or InvalidDataException or ArgumentException
            or InvalidOperationException)
        {
            return FormDocumentFormat.Unknown;
        }
    }

    // A ZIP is a Word by the ContentType of any Override or Default of its OOXML manifest, with the XML bounded and without DTD; the
    // main part of a Word enabled for macros (.docm, .dotm) wins over a document one. Any other ZIP, a workbook among them, is not one.
    private static FormDocumentFormat PackageFormat(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var manifest = archive.GetEntry("[Content_Types].xml");
            if (manifest is null || manifest.Length > MaximumManifestBytes) return FormDocumentFormat.Unknown;
            using var source = manifest.Open();
            using var xml = XmlReader.Create(source, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumManifestBytes,
            });
            var format = FormDocumentFormat.Unknown;
            while (xml.Read())
            {
                if (xml.NodeType != XmlNodeType.Element || xml.LocalName is not ("Override" or "Default")) continue;
                var type = xml.GetAttribute("ContentType");
                if (type is null) continue;
                if (type.StartsWith("application/vnd.ms-word.", StringComparison.Ordinal) && type.Contains("macroEnabled", StringComparison.Ordinal)
                    && type.EndsWith(".main+xml", StringComparison.Ordinal))
                    return FormDocumentFormat.WordMacroEnabled;
                if (type == WordContentType) format = FormDocumentFormat.Docx;
            }
            return format;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or XmlException or NotSupportedException)
        {
            return FormDocumentFormat.Unknown;
        }
    }

    // The private files accept a PDF only with «%%EOF» in its last KiB, and its objects are counted before PdfPig opens it. Then it
    // opens on a thread of its own with a large stack, at most MaximumConcurrentPdfs at a time, after its compressed streams were
    // inflated into nothing against a budget. PdfPig cannot be cancelled, so a reading that takes more than the timeout is unreadable
    // and gives its place back while the thread, which is in the background, ends or not on its own.
    private async Task<FormDocumentContent> ReadPdfAsync(byte[] content, CancellationToken ct)
    {
        if (!DocumentSignature.IsPdf(content) || FormPdfObjects.Count(content, MaximumPdfObjects) > MaximumPdfObjects) return Unreadable;
        // Its streams are ciphertext until PdfPig decrypts them: with the empty password they are decrypted to be measured, and a file
        // that does not open with it, or whose handler is not the standard one, is a PDF with a password.
        var security = FormPdfSecurity.Resolve(content);
        if (security.Outcome == FormPdfSecurity.Outcome.Protected) return new(FormDocumentReadStatus.Protected);
        if (Volatile.Read(ref abandonedPdfs) >= MaximumAbandonedPdfs)
        {
            LogTooManyAbandoned(logger);
            return Unreadable;
        }
        await PdfGate.WaitAsync(ct);
        try
        {
            // 0 running, 1 abandoned by the time, 2 finished: the thread and the wait settle it once, with no lock.
            var state = 0;
            var opened = new TaskCompletionSource<FormDocumentContent>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                // Nothing may escape a thread of its own: it would end the process.
                try
                {
                    opened.SetResult(FormPdfStreams.Fits(content, MaximumPdfInflatedBytes, MaximumPdfObjects, security.Decryptor) ? openPdf(content) : Unreadable);
                }
                catch (OutOfMemoryException exception)
                {
                    // Not the file's fault: it is logged, so it is not mistaken for one, and answered as unreadable.
                    LogOutOfMemory(logger, exception);
                    opened.SetResult(Unreadable);
                }
                catch (Exception exception) { opened.SetException(exception); }
                finally
                {
                    if (Interlocked.CompareExchange(ref state, 2, 0) == 1) Interlocked.Decrement(ref abandonedPdfs);
                }
            }, PdfStackBytes) { IsBackground = true, Name = "FormDocumentReader.Pdf" };
            thread.Start();
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var finished = await Task.WhenAny(opened.Task, Task.Delay(pdfTimeout, waiting.Token));
            await waiting.CancelAsync();
            if (finished == opened.Task) return await opened.Task;
            ct.ThrowIfCancellationRequested();
            // Whatever the abandoned thread ends with is of no one.
            _ = opened.Task.ContinueWith(task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            if (Interlocked.CompareExchange(ref state, 1, 0) == 0) Interlocked.Increment(ref abandonedPdfs);
            LogPdfTimedOut(logger);
            return Unreadable;
        }
        finally
        {
            PdfGate.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading a PDF took longer than its time and was abandoned as unreadable")]
    private static partial void LogPdfTimedOut(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Readings of PDFs that went past their time are still running: a new PDF is answered as unreadable without opening it")]
    private static partial void LogTooManyAbandoned(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "The memory ran out reading a document; it is answered as unreadable")]
    private static partial void LogOutOfMemory(ILogger logger, Exception exception);

    // A PDF opens only to count the leaves of its page tree, never to render them: one that asks for a password to open is
    // Protected, and one that opens without it and only restricts permissions is read. Unknown failures of the library are contained
    // only at this boundary and make it unreadable; cancellation is not one of them, and a lack of memory goes on to be logged.
    private static FormDocumentContent OpenPdf(byte[] content)
    {
        try
        {
            using var document = PdfDocument.Open(content);
            return new(FormDocumentReadStatus.Read, Pages: document.NumberOfPages);
        }
        catch (PdfDocumentEncryptedException)
        {
            return new(FormDocumentReadStatus.Protected);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            return Unreadable;
        }
    }

    // SkiaSharp decides the format from the bytes and reads only the header: the size the photo declares, never its pixels.
    private static FormDocumentContent ReadPhoto(byte[] content, SKEncodedImageFormat format, ILogger logger)
    {
        try
        {
            using var stream = new SKMemoryStream(content);
            using var codec = SKCodec.Create(stream);
            if (codec is null || codec.EncodedFormat != format) return Unreadable;
            var info = codec.Info;
            return new(FormDocumentReadStatus.Read, Width: info.Width, Height: info.Height);
        }
        catch (OutOfMemoryException exception)
        {
            LogOutOfMemory(logger, exception);
            return Unreadable;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            return Unreadable;
        }
    }
}
