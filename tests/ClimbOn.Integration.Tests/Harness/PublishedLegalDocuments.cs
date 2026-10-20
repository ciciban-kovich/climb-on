using Npgsql;

namespace ClimbOn.Integration.Tests.Harness;

// legal_documents is reference data the migration seeds with version 1 of each document and every
// reset keeps. Publishing a later version is an admin action (S114, M2), so until then a test that
// needs one inserts it here; disposing removes it, with the acknowledgements of it, so the tests
// after it see version 1 as current again.
public static class PublishedLegalDocuments
{
    public static async Task<IAsyncDisposable> PublishAsync(
        string connectionString, string document, int version, DateTimeOffset publishedAt, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO legal_documents (document, version, published_at, text_en, text_nb)
            VALUES (@document, @version, @published_at, @text_en, @text_nb)
            """,
            connection);
        command.Parameters.AddWithValue("document", document);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("published_at", publishedAt);
        command.Parameters.AddWithValue("text_en", $"{document} version {version}");
        command.Parameters.AddWithValue("text_nb", $"{document} versjon {version}");
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new Unpublish(connectionString, document, version);
    }

    private sealed class Unpublish(string connectionString, string document, int version) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand(
                """
                DELETE FROM legal_acknowledgements WHERE document = @document AND version = @version;
                DELETE FROM legal_documents WHERE document = @document AND version = @version;
                """,
                connection);
            command.Parameters.AddWithValue("document", document);
            command.Parameters.AddWithValue("version", version);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
