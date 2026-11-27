using System.Buffers;
using System.Text;

namespace LivingPaper.Infrastructure;

// Reads the lines of a UTF-8 stream as StreamReader.ReadLineAsync does ("\n", "\r\n" or "\r"), but gives up on a line
// longer than maxLineBytes: the built-in method keeps everything it reads until a line end comes, even if none ever does.
// A line is cut in the bytes, before being decoded: those two never appear inside another UTF-8 character.
internal sealed class BoundedLineReader(Stream stream, int maxLineBytes)
{
    private const byte LineFeed = (byte)'\n';
    private const byte CarriageReturn = (byte)'\r';

    private readonly byte[] _buffer = new byte[4096];
    private readonly ArrayBufferWriter<byte> _line = new();
    private int _start;
    private int _end;
    private bool _afterCarriageReturn;
    private bool _firstLine = true;

    // Null at the end of the stream. Throws LineTooLongException, after which the reader must not be used again.
    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        // Lines already received are given without reading the stream, so the token is looked at here too.
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            if (_start == _end)
            {
                _start = 0;
                _end = await stream.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
                if (_end == 0)
                {
                    return _line.WrittenCount > 0 ? TakeLine() : null;
                }
            }

            // The "\n" of a "\r\n": it may come in a later read than its "\r".
            if (_afterCarriageReturn)
            {
                _afterCarriageReturn = false;
                if (_buffer[_start] == LineFeed)
                {
                    _start++;
                    continue;
                }
            }

            var received = _buffer.AsSpan(_start, _end - _start);
            var lineEnd = received.IndexOfAny(LineFeed, CarriageReturn);
            var length = lineEnd < 0 ? received.Length : lineEnd;
            if (_line.WrittenCount + length > maxLineBytes)
            {
                throw new LineTooLongException();
            }

            _line.Write(received[..length]);
            if (lineEnd < 0)
            {
                _start = _end;
                continue;
            }

            _afterCarriageReturn = received[lineEnd] == CarriageReturn;
            _start += lineEnd + 1;
            return TakeLine();
        }
    }

    private string TakeLine()
    {
        var text = Encoding.UTF8.GetString(_line.WrittenSpan);
        _line.Clear();
        if (_firstLine)
        {
            // A byte order mark may open the stream.
            _firstLine = false;
            text = text.TrimStart('\uFEFF');
        }

        return text;
    }
}
