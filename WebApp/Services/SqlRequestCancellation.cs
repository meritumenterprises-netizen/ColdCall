using Microsoft.Data.SqlClient;

namespace ColdCall.Services;

public static class SqlRequestCancellation
{
    public static bool IsExpected(SqlException exception, CancellationToken requestAborted) =>
        requestAborted.IsCancellationRequested
        && exception.Errors.Count > 0
        && exception.Errors.Cast<SqlError>().All(error => (error.Number == 0 && error.Class <= 11)
            || (error.Number == 3621 && error.Class <= 16))
        && exception.Errors.Cast<SqlError>().Any(error => error.Number == 0 && error.State == 0 && error.Class == 11);
}
