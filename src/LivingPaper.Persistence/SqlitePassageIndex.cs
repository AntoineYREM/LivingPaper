using System.Runtime.InteropServices;
using System.Text.Json;
using LivingPaper.Application;
using Microsoft.Data.Sqlite;

namespace LivingPaper.Persistence;

// The passages and their vectors in one SQLite file (data/rag.db), compared with the sqlite-vec extension. The vectors are
// 32-bit floats in an ordinary table, so any size fits (a virtual table of sqlite-vec has a fixed size, and the size
// changes with the model). The file is created at the first save only: someone who does not use the feature has none.
// A file that cannot be used (damaged, from a later version, not ours) is reported and never removed or rewritten.
// SQLite calls block: they run on the thread pool.
public sealed class SqlitePassageIndex(string path, string extension = SqlitePassageIndex.VectorExtension) : IPassageIndex
{
    // The loadable library of the sqlite-vec package.
    public const string VectorExtension = "vec0";

    private const int SchemaVersion = 1;

    private const string Schema = """
        create table book (shelf text not null, id text not null, model text, dimensions integer, primary key (shelf, id));
        create table passage (shelf text not null, id text not null, number integer not null, text text not null,
            scenes text not null, vector blob, primary key (shelf, id, number));
        pragma user_version = 1;
        """;

    private const string OfTheBook = "shelf = $shelf and id = $id";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private volatile bool _extensionLoads;

    public PassageIndexProblem? Check()
    {
        if (!ExtensionLoads())
        {
            return PassageIndexProblem.ExtensionMissing;
        }

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var connection = Connect(SqliteOpenMode.ReadOnly);
            var version = Version(connection, null);
            return version == SchemaVersion || (version == 0 && IsEmpty(connection, null)) ? null : PassageIndexProblem.FileUnusable;
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            return PassageIndexProblem.FileUnusable;
        }
    }

    public Task SavePassagesAsync(BookKey book, IReadOnlyList<StoredPassage> passages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(passages);
        if (passages.Any(passage => passage.Number < 1) || passages.Select(passage => passage.Number).Distinct().Count() != passages.Count)
        {
            throw new ArgumentException("The numbers of the passages must be positive and all different.", nameof(passages));
        }

        return RunAsync(
            () =>
            {
                using var connection = Open(create: passages.Count > 0);
                if (connection is null)
                {
                    return true;
                }

                using var transaction = connection.BeginTransaction();
                Remove(connection, transaction, book);
                if (passages.Count > 0)
                {
                    using var addBook = Command(connection, transaction, "insert into book (shelf, id) values ($shelf, $id)", book);
                    addBook.ExecuteNonQuery();
                }

                using var add = Command(
                    connection, transaction, "insert into passage (shelf, id, number, text, scenes) values ($shelf, $id, $number, $text, $scenes)", book);
                var number = add.Parameters.Add("$number", SqliteType.Integer);
                var text = add.Parameters.Add("$text", SqliteType.Text);
                var scenes = add.Parameters.Add("$scenes", SqliteType.Text);
                foreach (var passage in passages)
                {
                    cancellationToken.ThrowIfCancellationRequested(); // Nothing is committed: the book keeps what it had.
                    number.Value = passage.Number;
                    text.Value = passage.Text;
                    scenes.Value = JsonSerializer.Serialize(passage.Scenes, JsonOptions);
                    add.ExecuteNonQuery();
                }

                transaction.Commit();
                return true;
            },
            cancellationToken);
    }

    public Task SaveVectorsAsync(BookKey book, string embeddingModel, IReadOnlyList<float[]> vectors, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);
        ArgumentNullException.ThrowIfNull(vectors);
        if (vectors.Count == 0 || !vectors.All(CanBeCompared) || vectors.Any(vector => vector.Length != vectors[0].Length))
        {
            throw new ArgumentException("The vectors must all have the same size, finite numbers and a direction.", nameof(vectors));
        }

        return RunAsync(
            () =>
            {
                using var connection = Open(create: false) ?? throw NoMatch();
                using var transaction = connection.BeginTransaction();
                var numbers = new List<int>();
                using (var list = Command(connection, transaction, $"select number from passage where {OfTheBook} order by number", book))
                using (var reader = list.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        numbers.Add(reader.GetInt32(0));
                    }
                }

                if (numbers.Count != vectors.Count)
                {
                    throw NoMatch();
                }

                using var set = Command(connection, transaction, $"update passage set vector = $vector where {OfTheBook} and number = $number", book);
                var number = set.Parameters.Add("$number", SqliteType.Integer);
                var vector = set.Parameters.Add("$vector", SqliteType.Blob);
                for (var index = 0; index < numbers.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    number.Value = numbers[index];
                    vector.Value = Bytes(vectors[index]);
                    set.ExecuteNonQuery();
                }

                using var mark = Command(connection, transaction, $"update book set model = $model, dimensions = $dimensions where {OfTheBook}", book);
                mark.Parameters.AddWithValue("$model", embeddingModel);
                mark.Parameters.AddWithValue("$dimensions", vectors[0].Length);
                mark.ExecuteNonQuery();
                transaction.Commit();
                return true;
            },
            cancellationToken);

        static ArgumentException NoMatch() => new("There must be one vector for each passage of the book.", nameof(vectors));
    }

    public Task<PassageIndexState?> GetStateAsync(BookKey book, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        return RunAsync(
            () =>
            {
                using var connection = Open(create: false);
                return connection is null ? null : State(connection, book);
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<StoredPassage>> GetPassagesAsync(BookKey book, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        return RunAsync<IReadOnlyList<StoredPassage>>(
            () =>
            {
                using var connection = Open(create: false);
                if (connection is null)
                {
                    return [];
                }

                using var command = Command(connection, null, $"select number, text, scenes from passage where {OfTheBook} order by number", book);
                using var reader = command.ExecuteReader();
                var passages = new List<StoredPassage>();
                while (reader.Read())
                {
                    passages.Add(ReadPassage(reader));
                }

                return passages;
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<FoundPassage>> SearchAsync(BookKey book, float[] query, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        return RunAsync<IReadOnlyList<FoundPassage>>(
            () =>
            {
                if (!CanBeCompared(query))
                {
                    throw new PassageIndexException("The vector of the search cannot be compared with others.");
                }

                using var connection = Open(create: false);
                if (connection is null || State(connection, book) is not { EmbeddingModel: not null } state)
                {
                    return [];
                }

                if (state.VectorSize != query.Length)
                {
                    throw new PassageIndexException("The vector of the search does not have the size of those of the book.");
                }

                using var command = Command(
                    connection,
                    null,
                    $"select number, text, scenes, vec_distance_cosine(vector, $query) as distance from passage where {OfTheBook} and vector is not null order by distance, number limit $count",
                    book);
                command.Parameters.AddWithValue("$query", Bytes(query));
                command.Parameters.AddWithValue("$count", count);
                using var reader = command.ExecuteReader();
                var found = new List<FoundPassage>();
                while (reader.Read())
                {
                    found.Add(new FoundPassage(ReadPassage(reader), reader.GetDouble(3)));
                }

                return found;
            },
            cancellationToken);
    }

    public Task DeleteAsync(BookKey book, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        return RunAsync(
            () =>
            {
                using var connection = Open(create: false);
                if (connection is not null)
                {
                    using var transaction = connection.BeginTransaction();
                    Remove(connection, transaction, book);
                    transaction.Commit();
                }

                return true;
            },
            cancellationToken);
    }

    private static async Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(work, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException or JsonException)
        {
            // The message of SQLite names tables and columns, never the values written.
            throw new PassageIndexException("The index of the passages cannot be used.", exception);
        }
    }

    // A connection with the extension loaded and the tables there. Null when there is no file and none is wanted.
    private SqliteConnection? Open(bool create)
    {
        if (!ExtensionLoads())
        {
            throw new PassageIndexException("SQLite or its vector extension cannot be loaded.");
        }

        if (!File.Exists(path))
        {
            if (!create)
            {
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        }

        var connection = Connect(create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite);
        try
        {
            connection.LoadExtension(extension);
            var version = Version(connection, null);
            if (version == 0)
            {
                // A new file. Checked again under the lock of the transaction: another call may have just made the tables.
                using var transaction = connection.BeginTransaction();
                version = Version(connection, transaction);
                if (version == 0 && IsEmpty(connection, transaction))
                {
                    using var make = connection.CreateCommand();
                    make.Transaction = transaction;
                    make.CommandText = Schema;
                    make.ExecuteNonQuery();
                    transaction.Commit();
                    version = SchemaVersion;
                }
            }

            if (version != SchemaVersion)
            {
                throw new PassageIndexException("The file of the index is not one this version can use.");
            }

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    // No pool: a pooled connection would keep the file open after use, which stops it being moved or replaced.
    private SqliteConnection Connect(SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString());
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    // A success is remembered; a failure is checked again next time.
    private bool ExtensionLoads()
    {
        if (_extensionLoads)
        {
            return true;
        }

        try
        {
            using var connection = new SqliteConnection("Data Source=:memory:;Pooling=False");
            connection.Open();
            connection.LoadExtension(extension);
            _extensionLoads = true;
            return true;
        }
        catch (Exception exception) when (exception is SqliteException or DllNotFoundException or TypeInitializationException
            or BadImageFormatException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static long Version(SqliteConnection connection, SqliteTransaction? transaction) =>
        Number(connection, transaction, "pragma user_version");

    private static bool IsEmpty(SqliteConnection connection, SqliteTransaction? transaction) =>
        Number(connection, transaction, "select count(*) from sqlite_master") == 0;

    private static long Number(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    // The SQL is always a text of this class; what varies goes through parameters.
    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, BookKey book)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$shelf", book.Shelf == BookShelf.Examples ? "examples" : "library");
        command.Parameters.AddWithValue("$id", book.Id);
        return command;
    }

    private static void Remove(SqliteConnection connection, SqliteTransaction transaction, BookKey book)
    {
        using var command = Command(connection, transaction, $"delete from passage where {OfTheBook}; delete from book where {OfTheBook}", book);
        command.ExecuteNonQuery();
    }

    private static PassageIndexState? State(SqliteConnection connection, BookKey book)
    {
        using var command = Command(
            connection, null, $"select (select count(*) from passage where {OfTheBook}), model, dimensions from book where {OfTheBook}", book);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new PassageIndexState(reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? 0 : reader.GetInt32(2))
            : null;
    }

    private static StoredPassage ReadPassage(SqliteDataReader reader) =>
        new(reader.GetInt32(0), reader.GetString(1), JsonSerializer.Deserialize<List<PassageScene>>(reader.GetString(2), JsonOptions) ?? []);

    // A vector of zeros has no direction: its distance to another is not a number.
    private static bool CanBeCompared(float[] vector) =>
        vector is { Length: > 0 } && Array.TrueForAll(vector, float.IsFinite) && !Array.TrueForAll(vector, number => number == 0);

    // The format sqlite-vec reads: the floats one after the other (little-endian on every platform it is built for).
    private static byte[] Bytes(float[] vector) => MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();
}
