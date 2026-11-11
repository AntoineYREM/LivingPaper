using System.Diagnostics.CodeAnalysis;

namespace LivingPaper.Domain;

// A book's identifier. It is the name of the book's file (data/books/{id}.json) and can come from a URL or an
// import, so the only accepted form is 32 hexadecimal characters: no path, no separator, no extension.
public readonly record struct BookId
{
    private readonly Guid _value;

    private BookId(Guid value) => _value = value;

    public static BookId New() => new(Guid.NewGuid());

    public static bool TryParse([NotNullWhen(true)] string? text, out BookId id)
    {
        id = default;
        if (text is not { Length: 32 } || !Guid.TryParseExact(text, "N", out var value) || value == Guid.Empty)
        {
            return false;
        }

        id = new BookId(value);
        return true;
    }

    public override string ToString() => _value.ToString("N");
}
