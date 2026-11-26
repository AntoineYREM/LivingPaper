namespace LivingPaper.Application;

// How the chat builds the text it searches the passages with (docs/specs/rag.md, section 7).
public enum QueryMode
{
    // The chat model writes a query that stands alone, from the recent messages.
    Rewrite,

    // The message of the reader and the ones before it.
    Recent,

    // The message of the reader alone.
    Last,
}

// The settings of the search of passages in the chat. The application reads the first two from the environment
// (RAG_QUERY_MODE, RAG_PASSAGES). The timeouts are short: the reader is waiting for the reply.
public sealed record RagOptions
{
    public const int MaxPassages = 10;

    public QueryMode Mode { get; init; } = QueryMode.Rewrite;

    // How many passages are given to the character.
    public int Passages { get; init; } = 3;

    public TimeSpan RewriteTimeout { get; init; } = TimeSpan.FromSeconds(30);

    // For the vector of the query and the search together.
    public TimeSpan SearchTimeout { get; init; } = TimeSpan.FromSeconds(30);

    // The traces then hold the text of the query: it comes from the conversation (LANGFUSE_CAPTURE_CONTENT).
    public bool CaptureContent { get; init; }
}

// What the reader is told when the passages could not be used as intended.
public enum ChatNotice
{
    // The search was made with the recent messages: the query could not be written.
    RewriteFailed,

    // The passages could not be searched: the reply uses the whole story.
    SearchFailed,
}
