using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Infrastructure;

// Records every call to a language model as an Activity of LlmTelemetry.Source, in the vocabulary of Langfuse ("generation",
// model, tokens, level), so an exporter can show the calls. It changes nothing in the call: same reply, same exceptions, same
// tokens added to the caller's meter. It sits below RetryingChatModel, so every try is a generation of its own and a try that
// failed for a passing reason shows as an error.
// What is recorded: the model and the name of the provider, the tokens, the time of the first piece of a streamed reply, and
// on failure the kind and HTTP status (never the message). The messages and the reply only when CaptureContent is on.
// Without a listener (tracing not configured) the calls go straight to the model.
public sealed class TracingChatModel(IChatModel inner, LlmProvider provider, LlmTracingOptions options, TimeProvider? clock = null) : IChatModel
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    // The analysis asks for whole replies; the chat streams: the names tell them apart in Langfuse.
    private const string CompleteName = "llm-call";
    private const string StreamName = "chat-reply";

    public async Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (!LlmTelemetry.Source.HasListeners())
        {
            return await inner.CompleteAsync(messages, usage, cancellationToken).ConfigureAwait(false);
        }

        using var activity = Start(CompleteName, messages);
        var meter = new ChatUsageMeter();
        try
        {
            var reply = await inner.CompleteAsync(messages, meter, cancellationToken).ConfigureAwait(false);
            if (options.CaptureContent)
            {
                activity?.SetTag("langfuse.observation.output", reply);
            }

            return reply;
        }
        catch (Exception exception)
        {
            Fail(activity, exception);
            throw;
        }
        finally
        {
            Report(activity, meter, usage);
        }
    }

    public async IAsyncEnumerable<string> StreamReplyAsync(
        IReadOnlyList<ChatMessage> messages, ChatUsageMeter? usage, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (!LlmTelemetry.Source.HasListeners())
        {
            await foreach (var piece in inner.StreamReplyAsync(messages, usage, cancellationToken).ConfigureAwait(false))
            {
                yield return piece;
            }

            yield break;
        }

        // Started here, ended in the finally: the reader may stop at any piece. Activity.Current is put back each time, because
        // between two pieces the code that reads (the endpoint of the chat) runs in the same context as this method.
        var activity = Start(StreamName, messages);
        var meter = new ChatUsageMeter();
        var reply = options.CaptureContent ? new StringBuilder() : null;
        var enumerator = inner.StreamReplyAsync(messages, meter, cancellationToken).GetAsyncEnumerator(cancellationToken);
        Exception? failure = null;
        var finished = false;
        try
        {
            var first = true;
            while (true)
            {
                string piece;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        finished = true;
                        break;
                    }

                    piece = enumerator.Current;
                }
                catch (Exception exception)
                {
                    failure = exception;
                    break;
                }

                if (first)
                {
                    first = false;
                    activity?.SetTag("langfuse.observation.completion_start_time", _clock.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
                }

                reply?.Append(piece);
                yield return piece;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
            if (activity is not null)
            {
                if (failure is not null)
                {
                    Fail(activity, failure);
                }
                else if (!finished)
                {
                    Warn(activity, "stopped before the end");
                }

                if (reply is not null)
                {
                    activity.SetTag("langfuse.observation.output", reply.ToString());
                }

                Report(activity, meter, usage);
                activity.Dispose();
            }
            else
            {
                Report(null, meter, usage);
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private Activity? Start(string name, IReadOnlyList<ChatMessage> messages)
    {
        var parent = Activity.Current;
        var activity = LlmTelemetry.Source.StartActivity(name, ActivityKind.Client);
        Activity.Current = parent;
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("langfuse.observation.type", "generation");
        activity.SetTag("langfuse.observation.model.name", provider.Model);
        activity.SetTag("langfuse.observation.metadata.provider", provider.Name);
        if (options.CaptureContent)
        {
            activity.SetTag("langfuse.observation.input", JsonSerializer.Serialize(
                messages.Select(message => new { role = message.Role.ToString().ToLowerInvariant(), content = message.Content })));
        }

        return activity;
    }

    // The tokens of this call go to the meter of the caller, as when the model is called directly, and to the activity.
    private static void Report(Activity? activity, ChatUsageMeter meter, ChatUsageMeter? usage)
    {
        usage?.Add(meter.InputTokens, meter.CachedInputTokens, meter.OutputTokens);
        if (activity is not null && (meter.InputTokens > 0 || meter.OutputTokens > 0))
        {
            activity.SetTag("langfuse.observation.usage_details", $$"""{"input":{{meter.InputTokens}},"output":{{meter.OutputTokens}}}""");
        }
    }

    // Only the kind of the failure and the HTTP status: the exception of the client holds no text of a book, but anything else could.
    private static void Fail(Activity? activity, Exception exception)
    {
        if (activity is null)
        {
            return;
        }

        if (exception is OperationCanceledException)
        {
            Warn(activity, "cancelled");
            return;
        }

        var message = exception is ChatProviderException provider
            ? provider.StatusCode is { } status ? $"{provider.Kind} (HTTP {status})" : provider.Kind.ToString()
            : exception.GetType().Name;
        activity.SetTag("langfuse.observation.level", "ERROR");
        activity.SetTag("langfuse.observation.status_message", message);
        activity.SetStatus(ActivityStatusCode.Error, message);
    }

    private static void Warn(Activity activity, string message)
    {
        activity.SetTag("langfuse.observation.level", "WARNING");
        activity.SetTag("langfuse.observation.status_message", message);
    }
}
