using System.IO.Compression;
using System.Text;
using System.Xml;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Files;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Import;
using DocumentFormat.OpenXml.Packaging;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Documents;

internal sealed partial class FormDocumentReader
{
    // What a Word's body may cost, whatever it holds: its XML is read up to MaximumWordBytes, MaximumWordDepth levels of nesting and
    // MaximumWordElements elements. Past one of them the text read so far stays, marked truncated, or the Word is unreadable without
    // any text.
    internal const int MaximumWordBytes = 4 * 1024 * 1024;
    internal const int MaximumWordDepth = 128;
    internal const int MaximumWordElements = 500_000;
    private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string CompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    // OpenXml only opens the package and finds its main part: its reader loads the content of a text element as a tree, recursing
    // once per level, so the XML of the body is read node by node with the BCL, without DTD or resolver.
    private static readonly XmlReaderSettings BodySettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true, IgnoreProcessingInstructions = true,
        CloseInput = true,
    };

    // A Word gives the paragraphs and tables of its body in document order, once its manifest declares its main part as the private
    // files require and after the ZIP preflight of the import (every entry once, sizes declared and delivered). Headers, footers,
    // comments and notes are other parts and are never read.
    private static FormDocumentContent ReadWord(byte[] content, CancellationToken ct)
    {
        try
        {
            if (!DocumentSignature.IsOpenXml(content, MainWordPart, WordContentType)) return Unreadable;
            using var stream = new MemoryStream(content, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (FormArchivePreflight.Check(archive, ct).IsFailure) return Unreadable;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or XmlException)
        {
            return Unreadable;
        }
        using var package = new MemoryStream(content, writable: false);
        using var body = WordBody.Open(package);
        if (body is null) return Unreadable;
        var text = new WordText();
        try
        {
            text.Read(body.Reader, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The XML ended where MaximumWordBytes cut it: what was read stays. Anything else the package, the archive or the XML
            // throw while reading means a Word that does not read.
            if (!body.Xml.Cut) return Unreadable;
            text.StopShort();
        }
        return text.Result();
    }

    // The package, its main part and the reader over its XML, opened inside the boundary of the library: a part that is missing,
    // relationships that are cut or bring a DTD, or anything else it throws, except cancellation, mean a Word that does not open.
    private sealed class WordBody : IDisposable
    {
        private readonly WordprocessingDocument _document;

        private WordBody(WordprocessingDocument document, CappedStream xml, XmlReader reader) =>
            (_document, Xml, Reader) = (document, xml, reader);

        public CappedStream Xml { get; }
        public XmlReader Reader { get; }

        public static WordBody? Open(Stream package)
        {
            WordprocessingDocument? document = null;
            CappedStream? xml = null;
            try
            {
                document = WordprocessingDocument.Open(package, false, new OpenSettings { AutoSave = false });
                if (document.MainDocumentPart is not { } main)
                {
                    document.Dispose();
                    return null;
                }
                xml = new CappedStream(main.GetStream(FileMode.Open, FileAccess.Read), MaximumWordBytes);
                return new WordBody(document, xml, XmlReader.Create(xml, BodySettings));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                xml?.Dispose();
                document?.Dispose();
                return null;
            }
        }

        public void Dispose()
        {
            Reader.Dispose();
            Xml.Dispose();
            _document.Dispose();
        }
    }

    // The XML of the main part read only up to a number of bytes: past it the stream ends, and Cut says it ended there because more
    // was left, not because the part was over.
    private sealed class CappedStream(Stream inner, long capacity) : Stream
    {
        private long _remaining = capacity;

        public bool Cut { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining == 0)
            {
                Cut |= inner.Read(new byte[1], 0, 1) > 0;
                return 0;
            }
            var read = inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
            _remaining -= read;
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    // The body as text, streamed node by node and recognized by name and namespace: each paragraph on its line and each row of a
    // table that is not inside a paragraph as a Markdown row («| a | b |»), with the paragraphs of a cell joined and a pipe escaped; a
    // table inside a cell or a paragraph is flattened into it, and a paragraph inside another one (a text box) ends with a space. Only
    // the text inside a w:t counts, wherever it is (a smart tag among them); empty paragraphs and rows are left out and runs of
    // spaces become one. Deleted text, field codes and math are other elements; the origin of a tracked move repeats its
    // destination, and of an alternate content only the first choice counts, so the rest is skipped. Reading stops once the text
    // kept passes StructuredAttachmentLimits.MaximumDocumentCharacters or the Word passes one of its limits, also inside a text.
    private sealed class WordText
    {
        private readonly StringBuilder _text = new();
        private readonly Stack<bool> _alternates = new();
        private int _paragraphs;
        private int _tables;
        private int _texts;
        private int _line = -1;
        private int _cells;
        private int _elements;
        private bool _written;
        private bool _stoppedShort;

        // What the text keeps so far: a line that has no text yet would be taken back with its line break.
        private int Kept => _line >= 0 && !_written ? _line : _text.Length;

        public void Read(XmlReader reader, CancellationToken ct)
        {
            var more = reader.Read();
            while (more)
            {
                ct.ThrowIfCancellationRequested();
                if (Kept > StructuredAttachmentLimits.MaximumDocumentCharacters)
                {
                    StopShort();
                    return;
                }
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        if (++_elements > MaximumWordElements || reader.Depth > MaximumWordDepth)
                        {
                            StopShort();
                            return;
                        }
                        if (Skips(reader))
                        {
                            // Skip leaves the reader on the node after the element, which the next turn takes as it is.
                            reader.Skip();
                            more = !reader.EOF;
                            continue;
                        }
                        // An empty element has no end of its own.
                        var empty = reader.IsEmptyElement;
                        Start(reader);
                        if (empty) End(reader);
                        break;
                    case XmlNodeType.EndElement:
                        End(reader);
                        break;
                    case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace when _texts > 0:
                        Append(reader.Value);
                        break;
                }
                more = reader.Read();
            }
        }

        public void StopShort() => _stoppedShort = true;

        // Text read, marked truncated when reading stopped short; without any text, a Word that stopped short is unreadable and one
        // read whole is read with an empty text.
        public FormDocumentContent Result()
        {
            if (_line >= 0 && !_written) _text.Length = _line;
            if (_text.Length == 0) return _stoppedShort ? Unreadable : new(FormDocumentReadStatus.Read, Text: "");
            return new(FormDocumentReadStatus.Read, Text: _text.ToString(), Truncated: _stoppedShort);
        }

        private bool Skips(XmlReader reader)
        {
            if (reader.NamespaceURI == WordNamespace) return reader.LocalName == "moveFrom";
            if (reader.NamespaceURI != CompatibilityNamespace) return false;
            if (reader.LocalName == "Fallback") return true;
            if (reader.LocalName != "Choice" || _alternates.Count == 0) return false;
            if (_alternates.Pop())
            {
                _alternates.Push(true);
                return true;
            }
            _alternates.Push(true);
            return false;
        }

        private void Start(XmlReader reader)
        {
            if (reader.NamespaceURI == CompatibilityNamespace)
            {
                if (reader.LocalName == "AlternateContent") _alternates.Push(false);
                return;
            }
            if (reader.NamespaceURI != WordNamespace) return;
            switch (reader.LocalName)
            {
                case "tbl":
                    if (_tables > 0) _tables++;
                    else if (_paragraphs == 0) _tables = 1;
                    break;
                case "tr" when _tables == 1:
                    Open();
                    _text.Append('|');
                    _cells = 0;
                    break;
                case "tc" when _tables == 1:
                    if (_cells++ > 0) TrimLine();
                    _text.Append(_cells == 1 ? " " : " | ");
                    break;
                case "p":
                    if (_paragraphs++ == 0 && _tables == 0) Open();
                    else Space();
                    break;
                case "t":
                    _texts++;
                    break;
                case "tab" or "ptab" or "br" or "cr":
                    Space();
                    break;
                case "noBreakHyphen":
                    Append("-");
                    break;
            }
        }

        private void End(XmlReader reader)
        {
            if (reader.NamespaceURI == CompatibilityNamespace)
            {
                if (reader.LocalName == "AlternateContent" && _alternates.Count > 0) _alternates.Pop();
                return;
            }
            if (reader.NamespaceURI != WordNamespace) return;
            switch (reader.LocalName)
            {
                case "tbl":
                    if (_tables > 0) _tables--;
                    break;
                case "tr" when _tables == 1:
                    Close(" |");
                    break;
                case "p" when _paragraphs > 0:
                    if (--_paragraphs == 0 && _tables == 0) Close("");
                    else Space();
                    break;
                case "t" when _texts > 0:
                    _texts--;
                    break;
            }
        }

        // A line starts where the previous one ends; an empty one is taken back, with its line break.
        private void Open()
        {
            _line = _text.Length;
            if (_line > 0) _text.Append('\n');
            _written = false;
        }

        private void Close(string end)
        {
            if (_line < 0) return;
            if (!_written) _text.Length = _line;
            else
            {
                TrimLine();
                _text.Append(end);
            }
            _line = -1;
        }

        private void TrimLine()
        {
            while (_text.Length > _line + 1 && _text[^1] == ' ') _text.Length--;
        }

        private void Space()
        {
            if (_line >= 0 && _text.Length > 0 && _text[^1] is not (' ' or '\n')) _text.Append(' ');
        }

        private void Append(string value)
        {
            if (_line < 0) return;
            foreach (var character in value)
            {
                if (Kept > StructuredAttachmentLimits.MaximumDocumentCharacters) return;
                if (char.IsWhiteSpace(character))
                {
                    Space();
                    continue;
                }
                if (character == '|' && _tables > 0) _text.Append('\\');
                _text.Append(character);
                _written = true;
            }
        }
    }
}
