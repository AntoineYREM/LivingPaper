using System.Text.Json;
using LivingPaper.Application;
using LivingPaper.Domain;
using LivingPaper.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LivingPaper.Web.Pages.Books;

// The chat with a character (PAPER-10). The page needs JavaScript: the conversation lives in the page and disappears when it is
// left (nothing is stored on the server), and each message is sent by fetch (POST) with the whole conversation; the reply
// comes back as NDJSON, one JSON object per line, so it can be shown while it is written:
//   {"type":"text","text":"..."}   a piece of the reply
//   {"type":"error","code":"..."}  the reply stopped (a ChatFailure, in camelCase); the page says why in its own words
//   {"type":"done"}                the reply is complete
// The text of the messages is never logged: only counts and codes.
[RequestSizeLimit(MaxBodyBytes)]
public partial class ChatModel(BookService books, CharacterChatService chat, ITextLocalizer texts, ILogger<ChatModel> logger) : PageModel
{
    // 100 messages of 4,000 characters, each up to 4 bytes in UTF-8, and the JSON around them.
    private const long MaxBodyBytes = 2_000_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public sealed record IncomingMessage(string? Role, string? Content);

    public sealed record IncomingConversation(List<IncomingMessage>? Messages);

    public Book Book { get; private set; } = null!;

    public Character Character { get; private set; } = null!;

    // False when no model is chosen for the chat (or its key can no longer be read): the page explains and offers no chat.
    public bool ChatAvailable { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? id, int position)
    {
        if (!await LoadAsync(id, position))
        {
            return NotFound();
        }

        ChatAvailable = await chat.IsAvailableAsync(HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnPostMessageAsync(string? id, int position)
    {
        var cancellationToken = HttpContext.RequestAborted;
        if (!await LoadAsync(id, position))
        {
            return NotFound();
        }

        IncomingConversation? incoming;
        try
        {
            incoming = await Request.ReadFromJsonAsync<IncomingConversation>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        var conversation = ToConversation(incoming);
        var problem = conversation is null ? ConversationProblem.UnknownRole : ConversationValidator.Check(conversation);
        if (problem != ConversationProblem.None)
        {
            LogRefused(logger, problem);
            return BadRequest();
        }

        if (!await chat.IsAvailableAsync(cancellationToken))
        {
            return StatusCode(StatusCodes.Status409Conflict);
        }

        await StreamReplyAsync(conversation!, cancellationToken);
        return new EmptyResult();
    }

    private async Task StreamReplyAsync(IReadOnlyList<ChatMessage> conversation, CancellationToken cancellationToken)
    {
        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Accel-Buffering"] = "no"; // A proxy in front must not hold the reply back.
        try
        {
            await foreach (var piece in chat.ReplyAsync(Book, Character, conversation, cancellationToken))
            {
                await WriteEventAsync(new { type = "text", text = piece }, cancellationToken);
            }

            await WriteEventAsync(new { type = "done" }, cancellationToken);
        }
        catch (ChatReplyException exception)
        {
            LogFailed(logger, exception.Failure);
            await WriteEventAsync(new { type = "error", code = CodeOf(exception.Failure) }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The reader left: nobody to tell.
        }
    }

    private async Task WriteEventAsync(object value, CancellationToken cancellationToken)
    {
        await Response.WriteAsync(JsonSerializer.Serialize(value, JsonOptions) + "\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    // The code the page knows, in camelCase: "modelUnreachable".
    private static string CodeOf(ChatFailure failure)
    {
        var name = failure.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static List<ChatMessage>? ToConversation(IncomingConversation? incoming)
    {
        if (incoming?.Messages is null)
        {
            return null;
        }

        var conversation = new List<ChatMessage>(incoming.Messages.Count);
        foreach (var message in incoming.Messages)
        {
            var role = message?.Role switch
            {
                "user" => ChatRole.User,
                "assistant" => ChatRole.Assistant,
                _ => (ChatRole?)null,
            };
            if (role is null || message?.Content is null)
            {
                return null;
            }

            conversation.Add(new ChatMessage(role.Value, message.Content));
        }

        return conversation;
    }

    private async Task<bool> LoadAsync(string? id, int position)
    {
        if (!BookId.TryParse(id, out var bookId))
        {
            return false;
        }

        var book = (await books.FindAsync(bookId, HttpContext.RequestAborted))?.Book; // The library, or else an example.
        var character = book?.Characters.Find(candidate => candidate.Position == position);
        if (book is null || character is null || book.Status != BookStatus.Done)
        {
            return false;
        }

        Book = book;
        Character = character;
        return true;
    }

    // The texts the script shows, as JSON in the page: it has no translation file of its own.
    public string ScriptTexts()
    {
        var name = Character.Name;
        return JsonSerializer.Serialize(new
        {
            you = texts["chat.you"],
            writing = texts.Format("chat.writing", new { name }),
            jump = texts["chat.jump"],
            name,
            maxMessages = ConversationLimits.MaxMessages,
            limitTitle = texts["chat.limit.title"],
            limit = texts.Format("chat.limit", new { max = ConversationLimits.MaxMessages }),
            errorTitle = texts["chat.error.title"],
            errors = new Dictionary<string, string>
            {
                ["noModel"] = texts["chat.failure.noModel"],
                ["modelUnreachable"] = texts["chat.failure.modelUnreachable"],
                ["modelRefused"] = texts["chat.failure.modelRefused"],
                ["contentFiltered"] = texts["chat.failure.contentFiltered"],
                ["invalidReply"] = texts["chat.failure.invalidReply"],
                ["promptFile"] = texts["chat.failure.promptFile"],
                ["unexpected"] = texts["chat.failure.unexpected"],
                ["refused"] = texts["chat.error.refused"],
                ["notFound"] = texts["chat.error.notFound"],
                ["network"] = texts["chat.error.network"],
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "A conversation was refused: {Problem}.")]
    private static partial void LogRefused(ILogger logger, ConversationProblem problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A reply of a character could not be written: {Failure}.")]
    private static partial void LogFailed(ILogger logger, ChatFailure failure);
}
