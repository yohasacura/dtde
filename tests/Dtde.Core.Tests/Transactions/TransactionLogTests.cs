using Dtde.Abstractions.Transactions;
using Dtde.Core.Transactions;

namespace Dtde.Core.Tests.Transactions;

public class TransactionLogTests
{
    [Fact]
    public async Task InMemoryLog_RecordsLifecycle_AndReportsInDoubt()
    {
        var log = new InMemoryTransactionLog();

        await log.RecordTransactionStartedAsync("tx-1", CrossShardTransactionOptions.Default);
        await log.RecordParticipantEnlistedAsync("tx-1", "EU");
        await log.RecordParticipantEnlistedAsync("tx-1", "US");
        await log.RecordParticipantPreparedAsync("tx-1", "EU");
        // tx-1 stays "started" — only one of two participants prepared.

        await log.RecordTransactionStartedAsync("tx-2", CrossShardTransactionOptions.Default);
        await log.RecordParticipantEnlistedAsync("tx-2", "EU");
        await log.RecordParticipantPreparedAsync("tx-2", "EU");
        // tx-2 has both halves of its single participant — fully prepared.

        await log.RecordTransactionStartedAsync("tx-3", CrossShardTransactionOptions.Default);
        await log.RecordTransactionCommittedAsync("tx-3");
        // tx-3 reached terminal state — not in doubt.

        var inDoubt = await log.GetInDoubtTransactionsAsync();

        Assert.Equal(2, inDoubt.Count);
        var byId = inDoubt.ToDictionary(e => e.TransactionId);

        Assert.False(byId["tx-1"].AllParticipantsPrepared);
        Assert.Equal(2, byId["tx-1"].EnlistedParticipants.Count);
        Assert.Single(byId["tx-1"].PreparedParticipants);

        Assert.True(byId["tx-2"].AllParticipantsPrepared);
        Assert.Single(byId["tx-2"].EnlistedParticipants);
        Assert.Single(byId["tx-2"].PreparedParticipants);
    }

    [Fact]
    public async Task FileBasedLog_PersistsAcrossInstances()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dtde-log-test-{Guid.NewGuid():N}.jsonl");

        try
        {
            using (var writer = new FileBasedTransactionLog(path))
            {
                await writer.RecordTransactionStartedAsync("tx-A", CrossShardTransactionOptions.Default);
                await writer.RecordParticipantEnlistedAsync("tx-A", "shard-1");
                await writer.RecordParticipantPreparedAsync("tx-A", "shard-1");
            }

            // Open a fresh instance reading the same file.
            using var reader = new FileBasedTransactionLog(path);
            var inDoubt = await reader.GetInDoubtTransactionsAsync();

            Assert.Single(inDoubt);
            Assert.Equal("tx-A", inDoubt[0].TransactionId);
            Assert.True(inDoubt[0].AllParticipantsPrepared);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task FileBasedLog_TerminalStateIsNotInDoubt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dtde-log-test-{Guid.NewGuid():N}.jsonl");

        try
        {
            using var log = new FileBasedTransactionLog(path);

            await log.RecordTransactionStartedAsync("tx-X", CrossShardTransactionOptions.Default);
            await log.RecordTransactionRolledBackAsync("tx-X");

            var inDoubt = await log.GetInDoubtTransactionsAsync();
            Assert.Empty(inDoubt);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task FileBasedLog_TolerantsCorruptLines()
    {
        // A real append-only log can have a partially-flushed final line
        // after a crash. The recovery routine should skip malformed lines
        // and keep going.
        var path = Path.Combine(Path.GetTempPath(), $"dtde-log-test-{Guid.NewGuid():N}.jsonl");

        try
        {
            using (var log = new FileBasedTransactionLog(path))
            {
                await log.RecordTransactionStartedAsync("tx-Y", CrossShardTransactionOptions.Default);
            }

            // Append a corrupted line directly.
            await File.AppendAllTextAsync(path, "{ this is not valid JSON\n");

            using var reader = new FileBasedTransactionLog(path);
            var inDoubt = await reader.GetInDoubtTransactionsAsync();

            Assert.Single(inDoubt);
            Assert.Equal("tx-Y", inDoubt[0].TransactionId);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
