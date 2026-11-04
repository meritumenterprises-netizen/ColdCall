using System.Data;
using System.Security.Cryptography;
using System.Text;
using ColdCall.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ColdCall.Services;

public sealed record FeedbackReservation(Guid? Id, int RetryAfterSeconds);

public sealed class FeedbackSubmissionLimiter(ColdCallDbContext db)
{
    public async Task<FeedbackReservation> ReserveAsync(string email, CancellationToken token, string category = "feedback")
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var key = category switch
        {
            "feedback" => normalizedEmail,
            "support" => "support:" + normalizedEmail,
            "rodo" => "rodo:" + normalizedEmail,
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource=N'FeedbackSubmissionSchema',
                @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 50001, 'Feedback schema lock unavailable', 1;
            IF OBJECT_ID(N'ColdCall.FeedbackSubmissions', N'U') IS NULL
            BEGIN
                CREATE TABLE [ColdCall].[FeedbackSubmissions] (
                    [SubmissionId] uniqueidentifier NOT NULL CONSTRAINT [PK_FeedbackSubmissions] PRIMARY KEY,
                    [EmailHash] char(64) NOT NULL,
                    [CreatedAtUtc] datetime2 NOT NULL
                );
                CREATE INDEX [IX_FeedbackSubmissions_EmailHash_CreatedAtUtc]
                    ON [ColdCall].[FeedbackSubmissions] ([EmailHash], [CreatedAtUtc]);
            END;
            """, token);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @now datetime2 = SYSUTCDATETIME(), @count int, @oldest datetime2, @result int;
            DECLARE @resource nvarchar(255) = N'FeedbackEmail:' + @hash;
            EXEC @result = sys.sp_getapplock @Resource=@resource,
                @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 50001, 'Feedback email lock unavailable', 1;
            SELECT @count=COUNT(*), @oldest=MIN([CreatedAtUtc])
            FROM [ColdCall].[FeedbackSubmissions]
            WHERE [EmailHash]=@hash AND [CreatedAtUtc] > DATEADD(hour,-24,@now);
            IF @count >= 2
                SELECT CAST(NULL AS uniqueidentifier),
                    CAST(CEILING(DATEDIFF_BIG(millisecond,@now,DATEADD(hour,24,@oldest))/1000.0) AS int);
            ELSE
            BEGIN
                INSERT INTO [ColdCall].[FeedbackSubmissions] ([SubmissionId],[EmailHash],[CreatedAtUtc])
                VALUES (@id,@hash,@now);
                SELECT @id,0;
            END;
            """;
        var hashParameter = command.CreateParameter();
        hashParameter.ParameterName = "@hash";
        hashParameter.DbType = DbType.AnsiStringFixedLength;
        hashParameter.Size = 64;
        hashParameter.Value = hash;
        command.Parameters.Add(hashParameter);
        var idParameter = command.CreateParameter();
        idParameter.ParameterName = "@id";
        idParameter.DbType = DbType.Guid;
        idParameter.Value = Guid.NewGuid();
        command.Parameters.Add(idParameter);
        FeedbackReservation result;
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new InvalidOperationException("Missing feedback quota result.");
            result = new(reader.IsDBNull(0) ? null : reader.GetGuid(0), reader.GetInt32(1));
        }
        await transaction.CommitAsync(token);
        return result;
    }

    public Task ReleaseAsync(Guid reservationId, CancellationToken token) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [ColdCall].[FeedbackSubmissions] WHERE [SubmissionId]={reservationId};", token);
}
