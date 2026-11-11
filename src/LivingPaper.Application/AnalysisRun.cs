using System.Runtime.ExceptionServices;
using LivingPaper.Domain;

namespace LivingPaper.Application;

// One analysis of one book, from "waiting" to "done" or "failed". It keeps the book up to date as it goes (progress, tokens,
// and every step saved as soon as it succeeds), so that a retry resumes where this one stopped.
//   1. each part of the book is read; the model notes its characters (a few parts at the same time);
//   2. the notes are merged: one character under several names becomes one, the most important are kept;
//   3. a profile for each kept character, and the summary of the book (also a few at a time).
// Everything the model writes is cut to its limit; nothing the model writes is trusted to have the right shape.
internal sealed class AnalysisRun(
    Book book,
    IBookTextStore texts,
    IAnalysisStepStore steps,
    IBookRepository books,
    LlmSettingsService settings,
    IChatModelFactory models,
    IPromptProvider prompts,
    TimeProvider clock,
    AnalysisOptions options) : IDisposable
{
    private readonly SemaphoreSlim _bookLock = new(1, 1);
    private readonly SemaphoreSlim _calls = new(Math.Max(1, options.MaxParallelCalls));
    private readonly ChatUsageMeter _usage = new();
    private IChatModel _model = null!;
    private LlmProvider _provider = null!;

    public void Dispose()
    {
        _bookLock.Dispose();
        _calls.Dispose();
    }

    // Null when the analysis is done. Throws OperationCanceledException when stopped: the book then stays "running" and is
    // found interrupted when the application starts again, which is how a retry gets it back.
    public async Task<AnalysisFailure?> ExecuteAsync(CancellationToken cancellationToken)
    {
        await UpdateAsync(
            current =>
            {
                current.Status = BookStatus.Running;
                current.StartedAt = clock.GetUtcNow();
                current.FinishedAt = null;
                current.ErrorCode = null;
                current.ErrorDetail = null;
            },
            cancellationToken).ConfigureAwait(false);

        using var stopHeartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = BeatAsync(stopHeartbeat.Token);
        AnalysisException failure;
        try
        {
            await PipelineAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (AnalysisException exception)
        {
            failure = exception;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Only the kind of error: its message could hold a passage of the book.
            failure = new AnalysisException(AnalysisFailure.Unexpected, exception.GetType().Name);
        }
        finally
        {
            await stopHeartbeat.CancelAsync().ConfigureAwait(false);
            await heartbeat.ConfigureAwait(false);
        }

        await UpdateAsync(
            current =>
            {
                current.Status = BookStatus.Failed;
                current.ErrorCode = failure.Failure.ToString();
                current.ErrorDetail = failure.Detail is null ? null : CharacterLimits.Clip(failure.Detail, BookLimits.ErrorDetail);
                AddUsage(current);
            },
            cancellationToken).ConfigureAwait(false);
        return failure.Failure;
    }

    private async Task PipelineAsync(CancellationToken cancellationToken)
    {
        await PrepareModelAsync(cancellationToken).ConfigureAwait(false);
        var text = await texts.ReadAsync(book.Id, cancellationToken).ConfigureAwait(false);
        var parts = text is null ? [] : BookSplitter.Split(text, options.PagesPerPart, options.CharactersPerPage);
        if (parts.Count == 0)
        {
            throw new AnalysisException(AnalysisFailure.TextMissing);
        }

        var saved = new Dictionary<string, string>(await steps.GetAllAsync(book.Id, cancellationToken).ConfigureAwait(false), StringComparer.Ordinal);

        // 1. The parts. What a previous try saved is read again, not asked again.
        var notes = new PartNotes?[parts.Count];
        for (var index = 0; index < parts.Count; index++)
        {
            notes[index] = saved.TryGetValue(PartKey(index), out var stored) ? AnalysisNotes.Deserialize<PartNotes>(stored) : null;
        }

        await UpdateAsync(
            current =>
            {
                current.PartsTotal = parts.Count;
                current.PartsDone = notes.Count(note => note is not null);
                current.PartsSkipped = notes.Count(note => note is { Skipped: true });
                current.ProfilesDone = 0;
                current.ProfilesTotal = 0;
            },
            cancellationToken).ConfigureAwait(false);
        await RunAllAsync(
            Enumerable.Range(0, parts.Count).Where(index => notes[index] is null)
                .Select<int, Func<CancellationToken, Task>>(index => async token => notes[index] = await ReadPartAsync(index, parts[index], token).ConfigureAwait(false)),
            cancellationToken).ConfigureAwait(false);

        // 2. The merge.
        var allNotes = AnalysisNotes.Combine([.. notes.Select(note => note!)], options.MaxCharactersPerPart, options.MaxNotesLetters);
        var chosen = await MergeAsync(saved, allNotes, cancellationToken).ConfigureAwait(false);

        // 3. The profiles and the summary.
        var profiles = new string?[chosen.Count];
        for (var index = 0; index < chosen.Count; index++)
        {
            profiles[index] = saved.GetValueOrDefault(ProfileKey(index)) is { Length: > 0 } stored ? stored : null;
        }

        string? summary = saved.GetValueOrDefault(SummaryKey) is { Length: > 0 } storedSummary ? storedSummary : null;
        await UpdateAsync(
            current =>
            {
                current.ProfilesTotal = chosen.Count;
                current.ProfilesDone = profiles.Count(profile => profile is not null);
            },
            cancellationToken).ConfigureAwait(false);
        var work = Enumerable.Range(0, chosen.Count).Where(index => profiles[index] is null)
            .Select<int, Func<CancellationToken, Task>>(index => async token => profiles[index] = await WriteProfileAsync(index, chosen[index], allNotes, token).ConfigureAwait(false))
            .ToList();
        if (summary is null)
        {
            work.Add(async token => summary = await WriteSummaryAsync(allNotes, token).ConfigureAwait(false));
        }

        await RunAllAsync(work, cancellationToken).ConfigureAwait(false);

        await FinishAsync(chosen, profiles!, summary!, cancellationToken).ConfigureAwait(false);
    }

    private async Task PrepareModelAsync(CancellationToken cancellationToken)
    {
        LlmProvider? provider;
        try
        {
            provider = await settings.GetAnalysisProviderAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SettingsFileException)
        {
            throw new AnalysisException(AnalysisFailure.NoModel, "settings.json");
        }

        _provider = provider ?? throw new AnalysisException(AnalysisFailure.NoModel);
        try
        {
            _model = models.Create(_provider);
        }
        catch (ArgumentException)
        {
            throw new AnalysisException(AnalysisFailure.ModelUnreachable, "invalid address");
        }
    }

    // ---- The parts ----

    private async Task<PartNotes> ReadPartAsync(int index, string part, CancellationToken cancellationToken)
    {
        var variables = Variables((PromptVariables.BookTitle, book.Title));
        List<CharacterNote>? read = null;
        foreach (var key in new[] { PromptKeys.ReadPart, PromptKeys.ReadPartSimple })
        {
            try
            {
                var system = await RenderAsync(key, variables, cancellationToken).ConfigureAwait(false);
                read = await AskAsync(system, part, (string reply, out List<CharacterNote> notes) => AnalysisNotes.TryReadPart(reply, out notes), cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (ContentFilteredException)
            {
                // Rich notes ask about personalities, which a filter may refuse for a dark scene: the simple notes are
                // tried, then the part is left out. The filter is random: the same part may pass another time.
            }
        }

        // What the model has answered was paid for: it is saved even if a neighbouring step has just failed and stopped the
        // others, so the retry does not pay for it again.
        var notes = new PartNotes(index + 1, Skipped: read is null, read ?? []);
        await steps.SaveAsync(book.Id, PartKey(index), AnalysisNotes.Serialize(notes), CancellationToken.None).ConfigureAwait(false);
        await UpdateAsync(
            current =>
            {
                current.PartsDone++;
                if (notes.Skipped)
                {
                    current.PartsSkipped++;
                }
            },
            CancellationToken.None).ConfigureAwait(false);
        return notes;
    }

    // ---- The merge ----

    private async Task<List<ChosenCharacter>> MergeAsync(Dictionary<string, string> saved, string allNotes, CancellationToken cancellationToken)
    {
        if (saved.TryGetValue(MergeKey, out var stored) && AnalysisNotes.TryReadMerge(stored, options.MaxCharacters, out var already) && already.Count > 0)
        {
            return already;
        }

        var system = await RenderAsync(
            PromptKeys.MergeCharacters,
            Variables((PromptVariables.BookTitle, book.Title), (PromptVariables.MaxCharacters, options.MaxCharacters.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            cancellationToken).ConfigureAwait(false);
        var chosen = await AskStepAsync(system, allNotes, (string reply, out List<ChosenCharacter> list) => AnalysisNotes.TryReadMerge(reply, options.MaxCharacters, out list), cancellationToken).ConfigureAwait(false);
        if (chosen.Count == 0)
        {
            throw new AnalysisException(AnalysisFailure.NoCharacters);
        }

        await steps.SaveAsync(book.Id, MergeKey, AnalysisNotes.Serialize(new { characters = chosen }), CancellationToken.None).ConfigureAwait(false);
        return chosen;
    }

    // ---- The profiles and the summary ----

    private async Task<string> WriteProfileAsync(int index, ChosenCharacter character, string allNotes, CancellationToken cancellationToken)
    {
        var system = await RenderAsync(
            PromptKeys.CharacterProfile,
            Variables((PromptVariables.BookTitle, book.Title), (PromptVariables.CharacterName, character.Name), (PromptVariables.ShortDescription, character.ShortDescription)),
            cancellationToken).ConfigureAwait(false);
        var profile = CharacterLimits.Clip(await AskTextStepAsync(system, allNotes, cancellationToken).ConfigureAwait(false), CharacterLimits.ChatProfile);
        await steps.SaveAsync(book.Id, ProfileKey(index), profile, CancellationToken.None).ConfigureAwait(false);
        await UpdateAsync(current => current.ProfilesDone++, CancellationToken.None).ConfigureAwait(false);
        return profile;
    }

    private async Task<string> WriteSummaryAsync(string allNotes, CancellationToken cancellationToken)
    {
        var system = await RenderAsync(PromptKeys.BookSummary, Variables((PromptVariables.BookTitle, book.Title)), cancellationToken).ConfigureAwait(false);
        var summary = CharacterLimits.Clip(await AskTextStepAsync(system, allNotes, cancellationToken).ConfigureAwait(false), CharacterLimits.Summary);
        await steps.SaveAsync(book.Id, SummaryKey, summary, CancellationToken.None).ConfigureAwait(false);
        return summary;
    }

    // ---- The end ----

    private async Task FinishAsync(List<ChosenCharacter> chosen, string[] profiles, string summary, CancellationToken cancellationToken)
    {
        await UpdateAsync(
            current =>
            {
                current.Characters.Clear();
                current.Characters.AddRange(chosen.Select((character, index) => new Character(index + 1, character.Name, character.ShortDescription, profiles[index])));
                current.Summary = summary;
                current.ProviderName = CharacterLimits.Clip(_provider.Name, BookLimits.ProviderName);
                current.Model = CharacterLimits.Clip(_provider.Model, BookLimits.ModelName);
                current.Status = BookStatus.Done;
                current.ErrorCode = null;
                current.ErrorDetail = null;
                current.PartsDone = current.PartsTotal;
                current.ProfilesDone = current.ProfilesTotal;
                current.FinishedAt = clock.GetUtcNow();
                AddUsage(current);
            },
            cancellationToken).ConfigureAwait(false);

        // The book is saved as done first. What is left over (the steps, the text) is cleaned up after: if that fails the
        // book is still done, and deleting the book deletes the rest.
        try
        {
            await steps.DeleteAllAsync(book.Id, cancellationToken).ConfigureAwait(false);
            await texts.DeleteAsync(book.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void AddUsage(Book current)
    {
        current.InputTokens += _usage.InputTokens;
        current.CachedInputTokens += _usage.CachedInputTokens;
        current.OutputTokens += _usage.OutputTokens;
    }

    // ---- Asking the model ----

    private delegate bool Reader<T>(string reply, out T result);

    // Asks until the reply can be read: the first try and InvalidReplyRetries more. An unreadable reply is never replaced by
    // a default value that would hide it.
    private async Task<T> AskAsync<T>(string system, string user, Reader<T> read, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var reply = await CompleteAsync(system, user, cancellationToken).ConfigureAwait(false);
            if (read(reply, out var result))
            {
                return result;
            }

            if (attempt >= options.InvalidReplyRetries)
            {
                // No text of the reply: it comes from the book.
                throw new AnalysisException(AnalysisFailure.InvalidReply, $"the reply could not be read after {attempt + 1} tries");
            }
        }
    }

    // The same for a step with no simpler version: a content filter then stops the analysis.
    private async Task<T> AskStepAsync<T>(string system, string user, Reader<T> read, CancellationToken cancellationToken)
    {
        try
        {
            return await AskAsync(system, user, read, cancellationToken).ConfigureAwait(false);
        }
        catch (ContentFilteredException)
        {
            throw new AnalysisException(AnalysisFailure.ContentFiltered);
        }
    }

    private Task<string> AskTextStepAsync(string system, string user, CancellationToken cancellationToken) =>
        AskStepAsync(
            system,
            user,
            (string reply, out string text) =>
            {
                text = reply.Trim();
                return text.Length > 0;
            },
            cancellationToken);

    // One call, at most MaxParallelCalls at the same time. The client has already retried the passing failures; what comes
    // out here is final.
    private async Task<string> CompleteAsync(string system, string user, CancellationToken cancellationToken)
    {
        await _calls.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _model.CompleteAsync([new ChatMessage(ChatRole.System, system), new ChatMessage(ChatRole.User, user)], _usage, cancellationToken).ConfigureAwait(false);
        }
        catch (ContentFilteredException)
        {
            throw;
        }
        catch (ChatProviderException exception)
        {
            throw Classify(exception);
        }
        finally
        {
            _calls.Release();
        }
    }

    // Never the message of the client: only the kind of failure or the HTTP status.
    private static AnalysisException Classify(ChatProviderException exception) => exception.Kind switch
    {
        ChatFailureKind.Http when exception.StatusCode is 400 or 401 or 403 or 404 => new AnalysisException(AnalysisFailure.ModelRefused, $"HTTP {exception.StatusCode}"),
        ChatFailureKind.Http => new AnalysisException(AnalysisFailure.ModelUnreachable, $"HTTP {exception.StatusCode}"),
        ChatFailureKind.InvalidResponse or ChatFailureKind.EmptyReply => new AnalysisException(AnalysisFailure.InvalidReply, exception.Kind.ToString()),
        _ => new AnalysisException(AnalysisFailure.ModelUnreachable, exception.Kind.ToString()),
    };

    private async Task<string> RenderAsync(string key, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken)
    {
        try
        {
            return await prompts.RenderAsync(key, variables, cancellationToken).ConfigureAwait(false);
        }
        catch (PromptFileException exception)
        {
            throw new AnalysisException(AnalysisFailure.PromptFile, exception.Message);
        }
    }

    private static Dictionary<string, string> Variables(params (string Name, string Value)[] values) =>
        values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

    // ---- Running the steps together ----

    // Runs the steps, a few at a time (the limit is in CompleteAsync). When one fails, the others stop at once and the first
    // real failure is the one reported, not the cancellation of its neighbours.
    private static async Task RunAllAsync(IEnumerable<Func<CancellationToken, Task>> work, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Exception? first = null;

        async Task GuardAsync(Func<CancellationToken, Task> step)
        {
            try
            {
                await step(stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // A neighbour failed, or the application is stopping.
            }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(ref first, exception, null);
                await stop.CancelAsync().ConfigureAwait(false);
            }
        }

        await Task.WhenAll(work.Select(GuardAsync)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (first is not null)
        {
            ExceptionDispatchInfo.Capture(first).Throw();
        }
    }

    // ---- The book ----

    // Changes the book and writes it, one at a time: the steps that run together report their progress through here.
    private async Task UpdateAsync(Action<Book> change, CancellationToken cancellationToken)
    {
        await _bookLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            change(book);
            book.UpdatedAt = clock.GetUtcNow();
            await books.SaveAsync(book, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _bookLock.Release();
        }
    }

    // Writes the book now and then, so a long call is not taken for a stopped analysis. Progress is only for display: a
    // failure to write it must not stop the analysis.
    private async Task BeatAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(options.HeartbeatEvery, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false))
            {
                try
                {
                    await UpdateAsync(_ => { }, stop).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The analysis is over.
        }
    }

    private static string PartKey(int index) => $"part:{index + 1}";

    private static string ProfileKey(int index) => $"profile:{index + 1}";

    private const string MergeKey = "merge";

    private const string SummaryKey = "summary";
}
