using LivingPaper.Application;
using LivingPaper.Domain;
using Shouldly;

namespace LivingPaper.Application.Tests;

// Sending and deleting books (PAPER-7): the file is read at once, the book is created waiting for its analysis, and the
// refusals (empty, too large, unreadable, same title) change nothing.
public sealed class BookServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 20, 0, 0, TimeSpan.Zero);

    private readonly InMemoryBookRepository _books = new();
    private readonly InMemoryBookTextStore _texts = new();
    private readonly FakeBookTextExtractor _extractor = new();
    private readonly FakeAnalysisScheduler _scheduler = new();
    private readonly ManualTimeProvider _clock = new(Now);
    private readonly BookService _service;

    public BookServiceTests() => _service = new BookService(_books, _texts, _extractor, _scheduler, _clock, new AnalysisOptions());

    public void Dispose() => _service.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static MemoryStream Bytes(int length = 100) => new(new byte[length]);

    private Task<AddBookResult> AddAsync(string? title = null, bool replace = false, string fileName = "lighthouse.epub", Stream? content = null) =>
        _service.AddAsync(content ?? Bytes(), fileName, title, replace, Token);

    private static BookService NewService(InMemoryBookRepository books, InMemoryBookTextStore texts, FakeBookTextExtractor extractor) =>
        new(books, texts, extractor, new FakeAnalysisScheduler(), new FixedTimeProvider(Now), new AnalysisOptions());

    // ---- Sending a book ----

    [Fact]
    public async Task ABook_IsCreatedWaitingForItsAnalysis_WithItsTextKeptApart()
    {
        var result = await AddAsync();

        result.Succeeded.ShouldBeTrue();
        result.Replaced.ShouldBeFalse();
        var book = result.Added!;
        book.Title.ShouldBe("The Lighthouse");
        book.FileName.ShouldBe("lighthouse.epub");
        book.Status.ShouldBe(BookStatus.Waiting);
        book.SentAt.ShouldBe(Now);
        book.UpdatedAt.ShouldBe(Now);
        _books.Books[book.Id].ShouldBeSameAs(book);
        _texts.Texts[book.Id].ShouldBe("Lina climbs the steps.");
    }

    [Fact]
    public async Task TheTitleTyped_BeatsTheTitleOfTheFile()
    {
        var result = await AddAsync(title: "  My own title ");

        result.Added!.Title.ShouldBe("My own title");
    }

    [Theory]
    [InlineData(null, "lighthouse.epub", "lighthouse")]
    [InlineData("   ", "My Book.pdf", "My Book")]
    [InlineData(null, "no-extension", "no-extension")]
    public async Task WithNoTitleAnywhere_TheFileNameIsUsed(string? typed, string fileName, string expected)
    {
        var service = NewService(_books, _texts, new FakeBookTextExtractor(_ => new BookText(null, "Some words here.")));

        var result = await service.AddAsync(Bytes(), fileName, typed, replace: false, Token);

        result.Added!.Title.ShouldBe(expected);
        service.Dispose();
    }

    [Fact]
    public async Task WithNeitherTitleNorFileName_ABookIsStillCreated_UnderADefaultTitle()
    {
        var service = NewService(_books, _texts, new FakeBookTextExtractor(_ => new BookText(null, "Some words here.")));

        var result = await service.AddAsync(Bytes(), null, null, replace: false, Token);

        result.Added!.Title.ShouldBe(BookService.UntitledBook);
        result.Added.FileName.ShouldBe(string.Empty);
        service.Dispose();
    }

    [Fact]
    public async Task TheTitle_IsCleaned_ControlCharactersAndRepeatedSpaces()
    {
        var result = await AddAsync(title: "Line\none\tand   two\u0000\u0007");

        result.Added!.Title.ShouldBe("Line one and two");
    }

    [Fact]
    public async Task ATooLongTitle_IsCutAtThelimit_NotRefused()
    {
        var result = await AddAsync(title: string.Join(' ', Enumerable.Repeat("word", 200)));

        result.Succeeded.ShouldBeTrue();
        result.Added!.Title.Length.ShouldBeLessThanOrEqualTo(BookLimits.Title);
        result.Added.FindLimitViolations().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(@"C:\fakepath\Le Phare.epub", "Le Phare.epub")]
    [InlineData("/home/user/books/Le Phare.epub", "Le Phare.epub")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("bad\nname\u0000.epub", "badname.epub")]
    public async Task TheFileName_KeepsNoFolderAndNoControlCharacter(string sent, string expected)
    {
        var result = await AddAsync(fileName: sent);

        result.Added!.FileName.ShouldBe(expected);
    }

    [Fact]
    public async Task ATooLongFileName_IsCut_SoTheBookCanBeSaved()
    {
        var result = await AddAsync(fileName: new string('x', 400) + ".epub");

        result.Added!.FileName.Length.ShouldBeLessThanOrEqualTo(BookLimits.FileName);
        result.Added.FindLimitViolations().ShouldBeEmpty();
    }

    // ---- Refusals ----

    [Fact]
    public async Task NoFile_IsRefused_AsEmpty()
    {
        (await _service.AddAsync(null, "a.epub", null, replace: false, Token)).Rejection.ShouldBe(BookRejection.Empty);
        (await AddAsync(content: Bytes(0))).Rejection.ShouldBe(BookRejection.Empty);

        _extractor.Calls.ShouldBe(0);
        _books.Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task AFileOverTheLimit_IsRefused_WithoutBeingRead()
    {
        var result = await AddAsync(content: new RepeatingStream(BookService.MaxBookBytes + 1));

        result.Rejection.ShouldBe(BookRejection.TooLarge);
        _extractor.Calls.ShouldBe(0);
        _books.Books.ShouldBeEmpty();
        _texts.Texts.ShouldBeEmpty();
    }

    [Fact]
    public async Task AFileOfExactlyTheLimit_IsAccepted()
    {
        var result = await AddAsync(content: new RepeatingStream(BookService.MaxBookBytes));

        result.Succeeded.ShouldBeTrue();
        _extractor.LastLength.ShouldBe(BookService.MaxBookBytes);
    }

    [Fact]
    public async Task AnEnormousStream_IsNotReadToTheEnd()
    {
        var stream = new RepeatingStream(long.MaxValue / 2);

        var result = await AddAsync(content: stream);

        result.Rejection.ShouldBe(BookRejection.TooLarge);
        stream.BytesRead.ShouldBeLessThan(BookService.MaxBookBytes + 1_000_000);
    }

    [Theory]
    [InlineData(BookRejection.Unsupported)]
    [InlineData(BookRejection.ScannedPdf)]
    [InlineData(BookRejection.DamagedEpub)]
    [InlineData(BookRejection.DamagedPdf)]
    [InlineData(BookRejection.NoText)]
    public async Task AFileThatCannotBeRead_IsRefusedWithItsReason_AndNothingIsKept(BookRejection reason)
    {
        var service = NewService(_books, _texts, new FakeBookTextExtractor(_ => throw new BookReadException(reason)));

        var result = await service.AddAsync(Bytes(), "a.pdf", null, replace: false, Token);

        result.Rejection.ShouldBe(reason);
        result.Succeeded.ShouldBeFalse();
        _books.Books.ShouldBeEmpty();
        _texts.Texts.ShouldBeEmpty();
        service.Dispose();
    }

    [Fact]
    public async Task IfTheBookCannotBeSaved_ItsTextIsNotLeftBehind()
    {
        _books.SaveFailure = new IOException("disk full");

        await Should.ThrowAsync<IOException>(() => AddAsync());

        _texts.Texts.ShouldBeEmpty();
    }

    // ---- The same title twice ----

    [Fact]
    public async Task ABookWithTheTitleOfAnotherBook_IsRefused_AskingForReplace_IgnoringCaseAndSpaces()
    {
        var first = await AddAsync(title: "Le Malade imaginaire");

        var second = await AddAsync(title: "  le malade   IMAGINAIRE ");

        second.Rejection.ShouldBe(BookRejection.Duplicate);
        _books.Books.Keys.ShouldBe([first.Added!.Id]);
        _texts.Texts.Keys.ShouldBe([first.Added.Id]);
    }

    [Fact]
    public async Task Replace_PutsTheNewBookInPlaceOfTheOldOne_WithItsAnalysisAndText()
    {
        var first = (await AddAsync(title: "Le Malade imaginaire")).Added!;
        first.Status = BookStatus.Done;
        first.Characters.Add(new Character(1, "Argan", "A hypochondriac.", "Profile."));

        var second = await AddAsync(title: "Le Malade imaginaire", replace: true);

        second.Succeeded.ShouldBeTrue();
        second.Replaced.ShouldBeTrue();
        second.Added!.Id.ShouldNotBe(first.Id);
        second.Added.Status.ShouldBe(BookStatus.Waiting);
        second.Added.Characters.ShouldBeEmpty();
        _books.Books.Keys.ShouldBe([second.Added.Id]);
        _texts.Texts.Keys.ShouldBe([second.Added.Id]);
    }

    [Fact]
    public async Task Replace_WhenNothingIsThere_JustAddsTheBook()
    {
        var result = await AddAsync(replace: true);

        result.Succeeded.ShouldBeTrue();
        result.Replaced.ShouldBeFalse();
    }

    [Fact]
    public async Task ARefusedReplacement_KeepsTheOldBook()
    {
        var first = (await AddAsync(title: "Same")).Added!;
        var service = NewService(_books, _texts, new FakeBookTextExtractor(_ => throw new BookReadException(BookRejection.DamagedPdf)));

        var result = await service.AddAsync(Bytes(), "b.pdf", "Same", replace: true, Token);

        result.Rejection.ShouldBe(BookRejection.DamagedPdf);
        _books.Books.Keys.ShouldBe([first.Id]);
        service.Dispose();
    }

    [Fact]
    public async Task TwoSendsOfTheSameTitleAtOnce_AddOneBook_AndRefuseTheOther()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => AddAsync(title: "Race")));

        results.Count(result => result.Succeeded).ShouldBe(1);
        results.Count(result => result.Rejection == BookRejection.Duplicate).ShouldBe(4);
        _books.Books.Count.ShouldBe(1);
    }

    // ---- The list and the deletion ----

    [Fact]
    public async Task TheList_ShowsTheBooks_MostRecentFirst()
    {
        await AddAsync(title: "One");
        await AddAsync(title: "Two");

        (await _service.ListAsync(Token)).Books.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Deleting_RemovesTheBookAndItsText()
    {
        var book = (await AddAsync()).Added!;

        (await _service.DeleteAsync(book.Id, Token)).ShouldBeTrue();

        _books.Books.ShouldBeEmpty();
        _texts.Texts.ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_ABookThatIsNotThere_SaysSo_AndStillCleansItsText()
    {
        var id = BookId.New();
        _texts.Texts[id] = "orphan";

        (await _service.DeleteAsync(id, Token)).ShouldBeFalse();

        _texts.Texts.ShouldBeEmpty();
    }

    // ---- The analysis ----

    [Fact]
    public async Task ABookSent_IsPutInTheQueueOfTheAnalysis()
    {
        var book = (await AddAsync()).Added!;

        _scheduler.Queued.ShouldBe([book.Id]);
    }

    [Fact]
    public async Task ARefusedBook_IsNotQueued()
    {
        await AddAsync(content: Bytes(0));

        _scheduler.Queued.ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_StopsTheAnalysisOfTheBookFirst_SoNothingIsWrittenAfterwards()
    {
        var book = (await AddAsync()).Added!;

        await _service.DeleteAsync(book.Id, Token);

        _scheduler.Cancelled.ShouldBe([book.Id]);
        _scheduler.Queued.ShouldBeEmpty();
    }

    [Fact]
    public async Task Replace_StopsTheAnalysisOfTheOldBook_AndQueuesTheNewOne()
    {
        var first = (await AddAsync(title: "Same")).Added!;

        var second = (await AddAsync(title: "Same", replace: true)).Added!;

        _scheduler.Cancelled.ShouldBe([first.Id]);
        _scheduler.Queued.ShouldBe([second.Id]);
    }

    private async Task<Book> StoredBookAsync(BookStatus status, string? errorCode = null, bool withText = true)
    {
        var book = new Book(BookId.New(), "Stored " + Guid.NewGuid().ToString("N")[..6], "a.pdf", Now) { Status = status, ErrorCode = errorCode, ErrorDetail = errorCode is null ? null : "detail" };
        _books.Books[book.Id] = book;
        if (withText)
        {
            await _texts.SaveAsync(book.Id, "text", Token);
        }

        return book;
    }

    [Fact]
    public async Task Retry_PutsAFailedBookBackInTheQueue_WithoutItsError()
    {
        var book = await StoredBookAsync(BookStatus.Failed, "ModelUnreachable");
        _clock.Advance(TimeSpan.FromMinutes(3));

        (await _service.RetryAsync(book.Id, Token)).ShouldBeTrue();

        book.Status.ShouldBe(BookStatus.Waiting);
        book.ErrorCode.ShouldBeNull();
        book.ErrorDetail.ShouldBeNull();
        book.UpdatedAt.ShouldBe(Now.AddMinutes(3));
        _scheduler.Queued.ShouldBe([book.Id]);
    }

    [Fact]
    public async Task Retry_IsRefused_ForABookThatIsDone_WaitingInTheQueue_OrMissing()
    {
        var done = await StoredBookAsync(BookStatus.Done);
        var waiting = await StoredBookAsync(BookStatus.Waiting);
        _scheduler.Enqueue(waiting.Id);

        (await _service.RetryAsync(done.Id, Token)).ShouldBeFalse();
        (await _service.RetryAsync(waiting.Id, Token)).ShouldBeFalse();
        (await _service.RetryAsync(BookId.New(), Token)).ShouldBeFalse();

        done.Status.ShouldBe(BookStatus.Done);
    }

    [Fact]
    public async Task Retry_IsRefused_WhenTheTextOfTheBookIsGone_ItMustBeSentAgain()
    {
        var book = await StoredBookAsync(BookStatus.Failed, "TextMissing", withText: false);

        (await _service.RetryAsync(book.Id, Token)).ShouldBeFalse();

        book.Status.ShouldBe(BookStatus.Failed);
    }

    [Fact]
    public async Task ABookThatSaysItIsRunningButIsNotInTheQueue_IsInterrupted_AndCanBeRetried()
    {
        var book = await StoredBookAsync(BookStatus.Running);

        _service.IsInterrupted(book).ShouldBeTrue();
        (await _service.RetryAsync(book.Id, Token)).ShouldBeTrue();
        book.Status.ShouldBe(BookStatus.Waiting);
    }

    [Theory]
    [InlineData(BookStatus.Waiting, true, 60, false)] // Waiting behind another book, however long.
    [InlineData(BookStatus.Waiting, false, 0, true)] // Not in the queue any more.
    [InlineData(BookStatus.Running, true, 9, false)] // Moved 9 minutes ago.
    [InlineData(BookStatus.Running, true, 11, true)] // Not moved for 11 minutes: stuck.
    [InlineData(BookStatus.Running, false, 0, true)]
    [InlineData(BookStatus.Done, false, 90, false)]
    [InlineData(BookStatus.Failed, false, 90, false)]
    public async Task IsInterrupted_FollowsTheQueueAndTheTenMinuteRule(BookStatus status, bool queued, int minutesSinceUpdate, bool expected)
    {
        var book = await StoredBookAsync(status);
        if (queued)
        {
            _scheduler.Enqueue(book.Id);
        }

        _clock.Advance(TimeSpan.FromMinutes(minutesSinceUpdate));

        _service.IsInterrupted(book).ShouldBe(expected);
    }

    [Fact]
    public async Task Retry_OfAStuckAnalysis_StopsItFirst_ThenQueuesTheBookAgain()
    {
        var book = await StoredBookAsync(BookStatus.Running);
        _scheduler.Enqueue(book.Id);
        _clock.Advance(TimeSpan.FromMinutes(11));

        (await _service.RetryAsync(book.Id, Token)).ShouldBeTrue();

        _scheduler.Cancelled.ShouldBe([book.Id]);
        _scheduler.Queued.ShouldBe([book.Id]);
        book.Status.ShouldBe(BookStatus.Waiting);
    }

    [Fact]
    public async Task AtStartup_ARunningBookIsMarkedInterrupted_AndTheWaitingBooksAreQueuedOldestFirst()
    {
        var running = await StoredBookAsync(BookStatus.Running);
        var newer = await StoredBookAsync(BookStatus.Waiting);
        newer.SentAt = Now.AddMinutes(2);
        var older = await StoredBookAsync(BookStatus.Waiting);
        older.SentAt = Now.AddMinutes(1);
        var done = await StoredBookAsync(BookStatus.Done);
        var failed = await StoredBookAsync(BookStatus.Failed, "NoModel");

        await _service.RecoverAsync(Token);

        running.Status.ShouldBe(BookStatus.Failed);
        running.ErrorCode.ShouldBe("Interrupted");
        _scheduler.Queued.ShouldBe([older.Id, newer.Id]);
        done.Status.ShouldBe(BookStatus.Done);
        failed.ErrorCode.ShouldBe("NoModel");
    }

    // ---- Reading a book and changing a character sheet ----

    private async Task<Book> DoneBookAsync()
    {
        var book = await StoredBookAsync(BookStatus.Done);
        book.Characters.AddRange([new Character(1, "Argan", "A hypochondriac.", "I am Argan."), new Character(2, "Toinette", "A maid.", "I am Toinette.")]);
        return book;
    }

    [Fact]
    public async Task AStoredBook_CanBeReadById_AndAnUnknownOneIsNull()
    {
        var book = await DoneBookAsync();

        (await _service.GetAsync(book.Id, Token)).ShouldBeSameAs(book);
        (await _service.GetAsync(BookId.New(), Token)).ShouldBeNull();
    }

    [Fact]
    public async Task ChangingACharacter_SavesTheNewTexts_AndKeepsItsPosition()
    {
        var book = await DoneBookAsync();

        var result = await _service.UpdateCharacterAsync(book.Id, 2, new CharacterDraft("  Toinon ", "A sharp maid.", "I am Toinon.\r\nTruly."), Token);

        result.Outcome.ShouldBe(CharacterUpdateOutcome.Saved);
        result.Errors.ShouldBeEmpty();
        var saved = book.Characters.Single(character => character.Position == 2);
        (saved.Name, saved.ShortDescription, saved.ChatProfile).ShouldBe(("Toinon", "A sharp maid.", "I am Toinon.\nTruly."));
        result.Character.ShouldBe(saved);
        book.Characters.Select(character => character.Position).ShouldBe([1, 2]);
        book.Characters[0].Name.ShouldBe("Argan"); // The others are untouched.
    }

    [Fact]
    public async Task ChangingACharacter_KeepingItsOwnName_IsFine()
    {
        var book = await DoneBookAsync();

        (await _service.UpdateCharacterAsync(book.Id, 1, new CharacterDraft("argan", "A man.", "I am Argan."), Token)).Outcome.ShouldBe(CharacterUpdateOutcome.Saved);
    }

    [Fact]
    public async Task AnInvalidSheet_IsRefusedWithItsErrors_AndNothingChanges()
    {
        var book = await DoneBookAsync();

        var result = await _service.UpdateCharacterAsync(book.Id, 2, new CharacterDraft("ARGAN", string.Empty, new string('p', CharacterLimits.ChatProfile + 1)), Token);

        result.Outcome.ShouldBe(CharacterUpdateOutcome.Invalid);
        result.Errors.ShouldBe(
        [
            new FieldError("name", CharacterErrors.Taken),
            new FieldError("shortDescription", CharacterErrors.Required),
            new FieldError("chatProfile", CharacterErrors.TooLong),
        ]);
        book.Characters[1].Name.ShouldBe("Toinette");
    }

    [Theory]
    [InlineData(BookStatus.Waiting)]
    [InlineData(BookStatus.Running)]
    [InlineData(BookStatus.Failed)]
    public async Task ASheet_CanOnlyBeChanged_WhenTheAnalysisIsDone(BookStatus status)
    {
        var book = await DoneBookAsync();
        book.Status = status;

        (await _service.UpdateCharacterAsync(book.Id, 1, new CharacterDraft("Argan", "d", "p"), Token)).Outcome.ShouldBe(CharacterUpdateOutcome.NotEditable);
    }

    [Fact]
    public async Task ChangingACharacterThatIsNotThere_IsNotFound()
    {
        var book = await DoneBookAsync();

        (await _service.UpdateCharacterAsync(book.Id, 9, new CharacterDraft("A", "d", "p"), Token)).Outcome.ShouldBe(CharacterUpdateOutcome.NotFound);
        (await _service.UpdateCharacterAsync(book.Id, 0, new CharacterDraft("A", "d", "p"), Token)).Outcome.ShouldBe(CharacterUpdateOutcome.NotFound);
        (await _service.UpdateCharacterAsync(BookId.New(), 1, new CharacterDraft("A", "d", "p"), Token)).Outcome.ShouldBe(CharacterUpdateOutcome.NotFound);
    }

    [Fact]
    public async Task TwoChangesAtOnce_BothArrive_NeitherOverwritesTheOther()
    {
        var book = await DoneBookAsync();

        await Task.WhenAll(
            _service.UpdateCharacterAsync(book.Id, 1, new CharacterDraft("Argan the first", "d1", "p1"), Token),
            _service.UpdateCharacterAsync(book.Id, 2, new CharacterDraft("Toinette the second", "d2", "p2"), Token));

        book.Characters.Select(character => character.Name).ShouldBe(["Argan the first", "Toinette the second"]);
    }

    // ---- The examples ----

    private static Book Example(string title = "Alice in Wonderland")
    {
        var example = new Book(BookId.New(), title, "alice.epub", Now.AddYears(-1))
        {
            Status = BookStatus.Done,
            Summary = "England, 1865. A dream.",
            ProviderName = "OpenAI",
            Model = "gpt-5-nano",
            PartsDone = 3,
            PartsTotal = 3,
            ProfilesDone = 1,
            ProfilesTotal = 1,
            PartsSkipped = 1,
            InputTokens = 100,
            CachedInputTokens = 10,
            OutputTokens = 50,
            StartedAt = Now.AddYears(-1),
            FinishedAt = Now.AddYears(-1).AddMinutes(3),
        };
        example.Characters.Add(new Character(1, "Alice", "A curious girl.", "I am Alice."));
        return example;
    }

    private BookService WithExamples(params Book[] examples) =>
        new(_books, _texts, _extractor, _scheduler, _clock, new AnalysisOptions(), new InMemoryExampleCatalog(examples));

    [Fact]
    public async Task Find_LooksInTheLibraryFirst_ThenInTheExamples()
    {
        var example = Example();
        var mine = await DoneBookAsync();
        using var service = WithExamples(example);

        var inLibrary = await service.FindAsync(mine.Id, Token);
        var inExamples = await service.FindAsync(example.Id, Token);

        inLibrary!.IsExample.ShouldBeFalse();
        inLibrary.Book.ShouldBeSameAs(mine);
        inExamples!.IsExample.ShouldBeTrue();
        inExamples.Book.Title.ShouldBe("Alice in Wonderland");
        (await service.FindAsync(BookId.New(), Token)).ShouldBeNull();
    }

    [Fact]
    public async Task AnExampleWithTheIdOfABookOfTheLibrary_IsShadowedByTheLibrary()
    {
        var mine = await DoneBookAsync();
        var example = Example();
        using var service = new BookService(_books, _texts, _extractor, _scheduler, _clock, new AnalysisOptions(), new InMemoryExampleCatalog(new Book(mine.Id, "Imposter", "x", Now) { Status = BookStatus.Done }, example));

        (await service.FindAsync(mine.Id, Token))!.IsExample.ShouldBeFalse();
    }

    [Fact]
    public async Task WithoutACatalog_ThereAreNoExamples_AndFindOnlyLooksInTheLibrary()
    {
        var mine = await DoneBookAsync();

        (await _service.ListExamplesAsync(Token)).Books.ShouldBeEmpty();
        (await _service.FindAsync(mine.Id, Token))!.IsExample.ShouldBeFalse();
        (await _service.AddExampleAsync(BookId.New(), Token)).Rejection.ShouldBe(ExampleRejection.NotFound);
    }

    [Fact]
    public async Task TheExamplesAreListed_ByTitle()
    {
        using var service = WithExamples(Example("Zola"), Example("Alice"));

        (await service.ListExamplesAsync(Token)).Books.Select(book => book.Title).ShouldBe(["Alice", "Zola"]);
    }

    [Fact]
    public async Task AddingAnExample_CopiesItIntoTheLibrary_ADoneBookWithItsSheets_UnderANewId()
    {
        var example = Example();
        using var service = WithExamples(example);

        var result = await service.AddExampleAsync(example.Id, Token);

        result.Succeeded.ShouldBeTrue();
        var copy = result.Added!;
        copy.Id.ShouldNotBe(example.Id);
        _books.Books[copy.Id].ShouldBeSameAs(copy);
        (copy.Title, copy.FileName, copy.Summary, copy.Status).ShouldBe(("Alice in Wonderland", "alice.epub", "England, 1865. A dream.", BookStatus.Done));
        copy.Characters.ShouldBe(example.Characters);
        (copy.ProviderName, copy.Model, copy.InputTokens, copy.CachedInputTokens, copy.OutputTokens).ShouldBe(("OpenAI", "gpt-5-nano", 100, 10, 50));
        (copy.PartsDone, copy.PartsTotal, copy.ProfilesDone, copy.ProfilesTotal, copy.PartsSkipped).ShouldBe((3, 3, 1, 1, 1));
        copy.SentAt.ShouldBe(Now);
        copy.UpdatedAt.ShouldBe(Now);
        copy.StartedAt.ShouldBe(example.StartedAt);
        copy.FinishedAt.ShouldBe(example.FinishedAt);
        _scheduler.Queued.ShouldBeEmpty(); // Nothing to analyze.
        _texts.Texts.ShouldBeEmpty(); // Nor any text to keep.
    }

    [Fact]
    public async Task TheCopy_IsABookOfTheLibrary_ItsSheetsCanBeEdited_WithoutTouchingTheExample()
    {
        var example = Example();
        using var service = WithExamples(example);
        var copy = (await service.AddExampleAsync(example.Id, Token)).Added!;

        var edit = await service.UpdateCharacterAsync(copy.Id, 1, new CharacterDraft("Alice", "Edited.", "I am an edited Alice."), Token);
        var untouched = await service.UpdateCharacterAsync(example.Id, 1, new CharacterDraft("Alice", "Edited.", "I am an edited Alice."), Token);

        edit.Outcome.ShouldBe(CharacterUpdateOutcome.Saved);
        untouched.Outcome.ShouldBe(CharacterUpdateOutcome.NotFound); // An example cannot be changed.
        example.Characters[0].ChatProfile.ShouldBe("I am Alice.");
    }

    [Fact]
    public async Task AnExample_CannotBeDeleted_OrRetried()
    {
        var example = Example();
        using var service = WithExamples(example);

        (await service.DeleteAsync(example.Id, Token)).ShouldBeFalse();
        (await service.RetryAsync(example.Id, Token)).ShouldBeFalse();

        (await service.ListExamplesAsync(Token)).Books.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AddingAnExampleWhoseTitleIsAlreadyInTheLibrary_IsRefused_IgnoringCase()
    {
        var example = Example("Alice in Wonderland");
        using var service = WithExamples(example);
        await service.AddAsync(Bytes(), "mine.epub", "  alice IN wonderland ", replace: false, Token);

        var result = await service.AddExampleAsync(example.Id, Token);

        result.Rejection.ShouldBe(ExampleRejection.Duplicate);
        _books.Books.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AddingAnExampleThatDoesNotExist_IsNotFound()
    {
        using var service = WithExamples(Example());

        (await service.AddExampleAsync(BookId.New(), Token)).Rejection.ShouldBe(ExampleRejection.NotFound);
    }

    [Fact]
    public async Task AddingTheSameExampleTwiceAtOnce_AddsOneCopy()
    {
        var example = Example();
        using var service = WithExamples(example);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => service.AddExampleAsync(example.Id, Token)));

        results.Count(result => result.Succeeded).ShouldBe(1);
        results.Count(result => result.Rejection == ExampleRejection.Duplicate).ShouldBe(4);
    }

    // ---- Export and import ----

    private readonly FakeBookArchive _archive = new();

    private BookService WithArchive(params Book[] examples) =>
        new(_books, _texts, _extractor, _scheduler, _clock, new AnalysisOptions(), new InMemoryExampleCatalog(examples), _archive);

    private static MemoryStream Stream(byte[] bytes) => new(bytes);

    [Fact]
    public async Task ExportingAFinishedBook_GivesItsFile_NamedAfterItsTitle()
    {
        var book = await DoneBookAsync();
        book.Title = "Le Malade imaginaire";
        using var service = WithArchive();

        var file = await service.ExportAsync(book.Id, Token);

        file.ShouldNotBeNull();
        file.FileName.ShouldBe("le-malade-imaginaire.json");
        _archive.Parse(file.Content).Book!.Title.ShouldBe("Le Malade imaginaire");
    }

    [Theory]
    [InlineData("Le Malade imaginaire", "le-malade-imaginaire.json")]
    [InlineData("  L'Été à Paris !!  ", "l-ete-a-paris.json")]
    [InlineData("Œuvres: tome 1/2", "uvres-tome-1-2.json")]
    [InlineData("???", "book.json")]
    [InlineData("", "book.json")]
    [InlineData("../../etc/passwd", "etc-passwd.json")]
    [InlineData("a\"b\r\nc", "a-b-c.json")]
    public async Task TheFileName_IsAPlainAsciiSlug_NeverAnythingThatCouldBeAPathOrAHeader(string title, string expected)
    {
        var book = await DoneBookAsync();
        book.Title = title;
        using var service = WithArchive();

        (await service.ExportAsync(book.Id, Token))!.FileName.ShouldBe(expected);
    }

    [Fact]
    public async Task ATooLongTitle_GivesAShortFileName()
    {
        var book = await DoneBookAsync();
        book.Title = string.Join(' ', Enumerable.Repeat("word", 100));
        using var service = WithArchive();

        (await service.ExportAsync(book.Id, Token))!.FileName.Length.ShouldBeLessThanOrEqualTo(65);
    }

    [Theory]
    [InlineData(BookStatus.Waiting)]
    [InlineData(BookStatus.Running)]
    [InlineData(BookStatus.Failed)]
    public async Task ABookThatIsNotAnalyzed_CannotBeExported(BookStatus status)
    {
        var book = await DoneBookAsync();
        book.Status = status;
        using var service = WithArchive();

        (await service.ExportAsync(book.Id, Token)).ShouldBeNull();
    }

    [Fact]
    public async Task AnExample_CanBeExported_AndAnUnknownBookCannot()
    {
        var example = Example();
        using var service = WithArchive(example);

        (await service.ExportAsync(example.Id, Token))!.FileName.ShouldBe("alice-in-wonderland.json");
        (await service.ExportAsync(BookId.New(), Token)).ShouldBeNull();
    }

    private async Task<(BookService Service, Book Original, byte[] File)> ExportedAsync()
    {
        var original = await DoneBookAsync();
        var service = WithArchive();
        var file = (await service.ExportAsync(original.Id, Token))!.Content;
        return (service, original, file);
    }

    [Fact]
    public async Task ImportingAnExportInAnOtherLibrary_AddsTheSameBook_WithItsId_AndNothingToAnalyze()
    {
        var (_, original, file) = await ExportedAsync();
        var other = new InMemoryBookRepository();
        var otherTexts = new InMemoryBookTextStore();
        var otherScheduler = new FakeAnalysisScheduler();
        using var service = new BookService(other, otherTexts, _extractor, otherScheduler, _clock, new AnalysisOptions(), null, _archive);

        var result = await service.ImportAsync(Stream(file), replace: false, Token);

        result.Imported.ShouldNotBeNull();
        result.Rejection.ShouldBeNull();
        result.Replaced.ShouldBeFalse();
        var imported = other.Books.Values.Single();
        imported.Id.ShouldBe(original.Id);
        (imported.Title, imported.Status, imported.Characters.Count).ShouldBe((original.Title, BookStatus.Done, 2));
        imported.Characters.ShouldBe(original.Characters);
        otherScheduler.Queued.ShouldBeEmpty();
        otherTexts.Texts.ShouldBeEmpty();
    }

    [Fact]
    public async Task ImportingABookWhoseIdIsTaken_AsksToReplace_AndChangesNothing()
    {
        var (service, original, file) = await ExportedAsync();
        original.Title = "Renamed since the export";

        var result = await service.ImportAsync(Stream(file), replace: false, Token);

        result.Rejection.ShouldBe(ImportRejection.Conflict);
        result.ConflictTitles.ShouldBe(["Renamed since the export"]);
        _books.Books.Values.Single().ShouldBeSameAs(original);
        service.Dispose();
    }

    [Fact]
    public async Task ImportingABookWhoseTitleIsTaken_AsksToReplace_IgnoringCase()
    {
        var (service, original, file) = await ExportedAsync();
        _books.Books.Remove(original.Id);
        var sameTitle = await StoredBookAsync(BookStatus.Done);
        sameTitle.Title = "  " + original.Title.ToUpperInvariant() + " ";

        var result = await service.ImportAsync(Stream(file), replace: false, Token);

        result.Rejection.ShouldBe(ImportRejection.Conflict);
        _books.Books.Values.Single().ShouldBeSameAs(sameTitle);
        service.Dispose();
    }

    [Fact]
    public async Task Replace_PutsTheImportedBookInPlaceOfTheOne_WithTheSameId_AndStopsItsAnalysis()
    {
        var (service, original, file) = await ExportedAsync();
        original.Characters[0] = original.Characters[0] with { ChatProfile = "Edited after the export." };
        await _texts.SaveAsync(original.Id, "leftover text", Token);

        var result = await service.ImportAsync(Stream(file), replace: true, Token);

        result.Replaced.ShouldBeTrue();
        _books.Books.Values.Single().Characters[0].ChatProfile.ShouldBe("I am Argan.");
        _books.Books.Values.Single().ShouldNotBeSameAs(original);
        _scheduler.Cancelled.ShouldBe([original.Id]);
        _texts.Texts.ShouldNotContainKey(original.Id);
        service.Dispose();
    }

    [Fact]
    public async Task Replace_AlsoReplacesTheBookOfTheSameTitle_WhenTheIdIsFree()
    {
        var (service, original, file) = await ExportedAsync();
        _books.Books.Remove(original.Id);
        var sameTitle = await StoredBookAsync(BookStatus.Done);
        sameTitle.Title = original.Title;

        var result = await service.ImportAsync(Stream(file), replace: true, Token);

        result.Replaced.ShouldBeTrue();
        _books.Books.Keys.ShouldBe([original.Id]);
        service.Dispose();
    }

    [Fact]
    public async Task AnImportedBookWithTheIdOfAnExample_GetsANewId_SoTheExampleIsNotHidden()
    {
        var example = Example();
        using var service = WithArchive(example);
        var asExample = new Book(example.Id, "Imported under an example id", "x.json", Now) { Status = BookStatus.Done };
        asExample.Characters.Add(new Character(1, "Alice", "d", "p"));

        var result = await service.ImportAsync(Stream(_archive.As(new ArchiveParseResult(asExample, null))), replace: false, Token);

        result.Imported!.Id.ShouldNotBe(example.Id);
        (await service.FindAsync(example.Id, Token))!.IsExample.ShouldBeTrue();
    }

    [Fact]
    public async Task NothingSent_AnEmptyFile_AndATooLargeFile_AreRefused()
    {
        using var service = WithArchive();

        (await service.ImportAsync(null, false, Token)).Rejection.ShouldBe(ImportRejection.Empty);
        (await service.ImportAsync(Stream([]), false, Token)).Rejection.ShouldBe(ImportRejection.Empty);
        (await service.ImportAsync(new RepeatingStream(BookService.MaxImportBytes + 1), false, Token)).Rejection.ShouldBe(ImportRejection.TooLarge);
        _books.Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEnormousImportStream_IsNotReadToTheEnd()
    {
        using var service = WithArchive();
        var stream = new RepeatingStream(long.MaxValue / 2);

        (await service.ImportAsync(stream, false, Token)).Rejection.ShouldBe(ImportRejection.TooLarge);

        stream.BytesRead.ShouldBeLessThan(BookService.MaxImportBytes + 1_000_000);
    }

    [Theory]
    [InlineData(ImportRejection.NotJson)]
    [InlineData(ImportRejection.UnknownVersion)]
    [InlineData(ImportRejection.NewerVersion)]
    [InlineData(ImportRejection.Invalid)]
    public async Task WhatTheArchiveRefuses_IsRefused_WithItsReasonAndDetail_NothingIsSaved(ImportRejection reason)
    {
        using var service = WithArchive();

        var result = await service.ImportAsync(Stream(_archive.As(new ArchiveParseResult(null, reason, "characters[0].name"))), false, Token);

        result.Rejection.ShouldBe(reason);
        result.Detail.ShouldBe("characters[0].name");
        _books.Books.ShouldBeEmpty();
    }

    private async Task<ImportBookResult> ImportAsync(Book book)
    {
        using var service = WithArchive();
        return await service.ImportAsync(Stream(_archive.As(new ArchiveParseResult(book, null))), false, Token);
    }

    private static Book Imported(Action<Book>? change = null)
    {
        var book = new Book(BookId.New(), "Imported", "imported.json", Now) { Status = BookStatus.Done, Summary = "A summary." };
        book.Characters.AddRange([new Character(1, "Argan", "The invalid.", "I am Argan."), new Character(2, "Toinette", "The maid.", "I am Toinette.")]);
        change?.Invoke(book);
        return book;
    }

    [Theory]
    [InlineData(BookStatus.Waiting)]
    [InlineData(BookStatus.Running)]
    [InlineData(BookStatus.Failed)]
    public async Task OnlyAnAnalyzedBookCanBeImported(BookStatus status)
    {
        var result = await ImportAsync(Imported(book => book.Status = status));

        result.Rejection.ShouldBe(ImportRejection.NotAnalyzed);
        _books.Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task ABookWithNoCharacter_IsNotAnAnalyzedBook() =>
        (await ImportAsync(Imported(book => book.Characters.Clear()))).Rejection.ShouldBe(ImportRejection.NotAnalyzed);

    [Fact]
    public async Task TheCharactersOfAnImportedBook_FollowTheRulesOfASheet_NamesTheFieldNeverTheText()
    {
        var result = await ImportAsync(Imported(book => book.Characters[1] = new Character(2, "argan", string.Empty, "p\u0007")));

        result.Rejection.ShouldBe(ImportRejection.Invalid);
        result.Detail.ShouldNotBeNull().ShouldContain("characters[");
        result.Detail.ShouldNotContain("p\u0007");
        _books.Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task TwoCharactersWithTheSamePosition_AreRefused() =>
        (await ImportAsync(Imported(book => book.Characters[1] = new Character(1, "Toinette", "d", "p")))).Rejection.ShouldBe(ImportRejection.Invalid);

    [Theory]
    [InlineData("title", "Line\nbreak")]
    [InlineData("title", "   ")]
    [InlineData("model", "gpt\u0000")]
    [InlineData("providerName", "Open\rAI")]
    public async Task ControlCharactersInTheTexts_OrAnEmptyTitle_AreRefused(string field, string value)
    {
        var result = await ImportAsync(Imported(book =>
        {
            switch (field)
            {
                case "title": book.Title = value; break;
                case "model": book.Model = value; break;
                default: book.ProviderName = value; break;
            }
        }));

        result.Rejection.ShouldBe(ImportRejection.Invalid);
        result.Detail.ShouldBe(field);
    }

    [Fact]
    public async Task TheFileNameOfAnImportedBook_KeepsNoFolder()
    {
        var result = await ImportAsync(Imported(book => book.FileName = @"C:\Users\someone\Documents\secret-plans.pdf"));

        result.Imported!.FileName.ShouldBe("secret-plans.pdf");
    }

    [Fact]
    public async Task TheTextsOfImportedCharacters_AreCleaned_LikeThoseOfASheetSaved()
    {
        var result = await ImportAsync(Imported(book => book.Characters[0] = new Character(1, "  Argan ", " The invalid. ", "I am Argan.\r\nTruly.  ")));

        var saved = result.Imported!.Characters[0];
        (saved.Name, saved.ShortDescription, saved.ChatProfile).ShouldBe(("Argan", "The invalid.", "I am Argan.\nTruly."));
    }

    [Fact]
    public async Task AnImportedBook_KeepsItsDatesAndItsAnalysisFigures()
    {
        var result = await ImportAsync(Imported(book =>
        {
            book.InputTokens = 123;
            book.Model = "gpt-5-nano";
            book.FinishedAt = Now.AddHours(-1);
            book.UpdatedAt = Now.AddHours(-1);
            book.SentAt = Now.AddDays(-3);
        }));

        var imported = result.Imported!;
        (imported.InputTokens, imported.Model, imported.SentAt, imported.UpdatedAt, imported.FinishedAt).ShouldBe((123, "gpt-5-nano", Now.AddDays(-3), Now.AddHours(-1), Now.AddHours(-1)));
    }

    [Fact]
    public async Task TwoImportsOfTheSameFileAtOnce_AddOneBook()
    {
        var (service, _, file) = await ExportedAsync();
        _books.Books.Clear();

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => service.ImportAsync(Stream(file), replace: false, Token)));

        results.Count(result => result.Imported is not null).ShouldBe(1);
        results.Count(result => result.Rejection == ImportRejection.Conflict).ShouldBe(4);
        service.Dispose();
    }

    [Fact]
    public async Task WithoutAnArchive_ExportAndImportAreNotAvailable()
    {
        var book = await DoneBookAsync();

        await Should.ThrowAsync<InvalidOperationException>(() => _service.ExportAsync(book.Id, Token));
        await Should.ThrowAsync<InvalidOperationException>(() => _service.ImportAsync(Stream([1]), false, Token));
    }

    // A stream that never ends and counts what was read from it, to check that a huge upload is not swallowed.
    private sealed class RepeatingStream(long length) : Stream
    {
        private long _position;

        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = (int)Math.Min(count, length - _position);
            Array.Clear(buffer, offset, read);
            _position += read;
            BytesRead += read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
