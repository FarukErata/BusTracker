using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using BusTracker.Api.Tracing;
using Microsoft.Data.Sqlite;

namespace BusTracker.Api.Data;

public sealed class TracedConnection(SqliteConnection inner) : DbConnection
{
    internal SqliteConnection Inner => inner;

    [AllowNull]
    public override string ConnectionString
    {
        get => inner.ConnectionString;
        set => inner.ConnectionString = value;
    }

    public override string Database => inner.Database;
    public override string DataSource => inner.DataSource;
    public override string ServerVersion => inner.ServerVersion;
    public override ConnectionState State => inner.State;

    public override void ChangeDatabase(string databaseName) => inner.ChangeDatabase(databaseName);
    public override void Close() => inner.Close();
    public override void Open() => inner.Open();
    public override Task OpenAsync(CancellationToken cancellationToken) => inner.OpenAsync(cancellationToken);

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        using var span = Telemetry.Span("db", "begin");
        return new TracedTransaction(this, inner.BeginTransaction(isolationLevel));
    }

    protected override ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken) =>
        ValueTask.FromResult(BeginDbTransaction(isolationLevel));

    protected override DbCommand CreateDbCommand() => new TracedCommand(this, inner.CreateCommand());

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync();
        await base.DisposeAsync();
    }
}

public sealed class TracedTransaction(TracedConnection connection, SqliteTransaction inner) : DbTransaction
{
    internal SqliteTransaction Inner => inner;

    public override IsolationLevel IsolationLevel => inner.IsolationLevel;
    protected override DbConnection DbConnection => connection;

    public override void Commit()
    {
        using var span = Telemetry.Span("db", "commit");
        inner.Commit();
    }

    public override void Rollback() => inner.Rollback();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}

public sealed partial class TracedCommand(TracedConnection connection, SqliteCommand inner) : DbCommand
{
    private TracedConnection? _connection = connection;
    private DbTransaction? _transaction;

    [AllowNull]
    public override string CommandText
    {
        get => inner.CommandText;
        set => inner.CommandText = value;
    }

    public override int CommandTimeout
    {
        get => inner.CommandTimeout;
        set => inner.CommandTimeout = value;
    }

    public override CommandType CommandType
    {
        get => inner.CommandType;
        set => inner.CommandType = value;
    }

    public override bool DesignTimeVisible
    {
        get => inner.DesignTimeVisible;
        set => inner.DesignTimeVisible = value;
    }

    public override UpdateRowSource UpdatedRowSource
    {
        get => inner.UpdatedRowSource;
        set => inner.UpdatedRowSource = value;
    }

    protected override DbConnection? DbConnection
    {
        get => _connection;
        set
        {
            _connection = value as TracedConnection;
            inner.Connection = _connection?.Inner ?? value as SqliteConnection;
        }
    }

    protected override DbTransaction? DbTransaction
    {
        get => _transaction;
        set
        {
            _transaction = value;
            inner.Transaction = (value as TracedTransaction)?.Inner ?? value as SqliteTransaction;
        }
    }

    protected override DbParameterCollection DbParameterCollection => inner.Parameters;

    public override void Cancel() => inner.Cancel();
    public override void Prepare() => inner.Prepare();
    protected override DbParameter CreateDbParameter() => inner.CreateParameter();

    public override int ExecuteNonQuery()
    {
        using var span = Span();
        return inner.ExecuteNonQuery();
    }

    public override object? ExecuteScalar()
    {
        using var span = Span();
        return inner.ExecuteScalar();
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        using var span = Span();
        return inner.ExecuteReader(behavior);
    }

    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        using var span = Span();
        return await inner.ExecuteNonQueryAsync(cancellationToken);
    }

    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        using var span = Span();
        return await inner.ExecuteScalarAsync(cancellationToken);
    }

    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        using var span = Span();
        return await inner.ExecuteReaderAsync(behavior, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }

    private Activity? Span() => Telemetry.Span("db", Describe(inner.CommandText));

    private static string Describe(string sql)
    {
        var verb = Verb().Match(sql);
        var table = Table().Match(sql);
        return string.Join(' ', new[] { verb.Success ? verb.Value.ToLowerInvariant() : "sql", table.Success ? table.Groups[1].Value : null }.OfType<string>());
    }

    [GeneratedRegex(@"^\s*\w+")]
    private static partial Regex Verb();

    [GeneratedRegex(@"\b(?:FROM|INTO|UPDATE|JOIN)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase)]
    private static partial Regex Table();
}
