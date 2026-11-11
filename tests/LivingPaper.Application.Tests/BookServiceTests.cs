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
