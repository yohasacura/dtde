namespace Dtde.Abstractions.Transactions;

/// <summary>
/// Persistent log of cross-shard transaction lifecycle events. Used to
/// recover in-doubt transactions after a coordinator crash: by replaying
/// the log, the recovery routine can identify any transaction that
/// completed the prepare phase (and therefore must commit) versus any that
/// hadn't (and must abort).
/// </summary>
/// <remarks>
/// <para>
/// DTDE ships two implementations:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     <c>InMemoryTransactionLog</c> — the default. No persistence, no
///     recovery; suitable for development and single-process tests.
///     </description>
///   </item>
///   <item>
///     <description>
///     <c>FileBasedTransactionLog</c> — JSON-lines append-only file. Useful
///     for integration tests and single-node deployments. Survives process
///     restarts.
///     </description>
///   </item>
/// </list>
/// <para>
/// For production deployments where the coordinator runs across multiple
/// nodes, plug in a real durable store (Postgres, Redis with persistence,
/// etc.) by implementing this interface.
/// </para>
/// </remarks>
public interface ITransactionLog
{
    /// <summary>
    /// Records the start of a new cross-shard transaction.
    /// </summary>
    /// <param name="transactionId">The transaction id.</param>
    /// <param name="options">The transaction's options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task RecordTransactionStartedAsync(
        string transactionId,
        CrossShardTransactionOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a shard was enlisted in the transaction.
    /// </summary>
    /// <param name="transactionId">The transaction id.</param>
    /// <param name="participantId">The participant's qualified shard id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task RecordParticipantEnlistedAsync(
        string transactionId,
        string participantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a participant has voted "prepared" (2PC phase 1
    /// success). After every participant has been recorded as prepared, the
    /// transaction is committed; if the coordinator dies after this point,
    /// the recovery routine knows the transaction must be committed (the
    /// participants' local transactions are still open at this stage).
    /// </summary>
    /// <param name="transactionId">The transaction id.</param>
    /// <param name="participantId">The participant's qualified shard id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task RecordParticipantPreparedAsync(
        string transactionId,
        string participantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the transaction has committed across all participants.
    /// Recovery can ignore transactions in this state.
    /// </summary>
    /// <param name="transactionId">The transaction id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task RecordTransactionCommittedAsync(
        string transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the transaction has rolled back. Recovery can ignore
    /// transactions in this state.
    /// </summary>
    /// <param name="transactionId">The transaction id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task RecordTransactionRolledBackAsync(
        string transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns transactions that started but never reached a terminal state
    /// (committed or rolled-back). The recovery routine inspects each one
    /// and either drives it to commit (if all participants were prepared)
    /// or rolls it back (otherwise).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list of in-doubt transactions, in start order.</returns>
    public Task<IReadOnlyList<TransactionLogEntry>> GetInDoubtTransactionsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A snapshot of a transaction's logged history, used during recovery.
/// </summary>
public sealed class TransactionLogEntry
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="transactionId">The transaction id.</param>
    /// <param name="startedAt">When the transaction started.</param>
    /// <param name="enlistedParticipants">Qualified ids of every enlisted shard.</param>
    /// <param name="preparedParticipants">Qualified ids of every shard that voted prepared.</param>
    /// <param name="state">The latest known state.</param>
    public TransactionLogEntry(
        string transactionId,
        DateTime startedAt,
        IReadOnlyList<string> enlistedParticipants,
        IReadOnlyList<string> preparedParticipants,
        TransactionLogState state)
    {
        TransactionId = transactionId ?? throw new ArgumentNullException(nameof(transactionId));
        StartedAt = startedAt;
        EnlistedParticipants = enlistedParticipants ?? throw new ArgumentNullException(nameof(enlistedParticipants));
        PreparedParticipants = preparedParticipants ?? throw new ArgumentNullException(nameof(preparedParticipants));
        State = state;
    }

    /// <summary>The transaction id.</summary>
    public string TransactionId { get; }

    /// <summary>When the transaction started.</summary>
    public DateTime StartedAt { get; }

    /// <summary>Qualified ids of every enlisted participant.</summary>
    public IReadOnlyList<string> EnlistedParticipants { get; }

    /// <summary>Qualified ids of participants that recorded a "prepared" vote.</summary>
    public IReadOnlyList<string> PreparedParticipants { get; }

    /// <summary>The latest known state.</summary>
    public TransactionLogState State { get; }

    /// <summary>
    /// Whether every enlisted participant has been recorded as prepared. If
    /// true and the transaction is still in-doubt, recovery should commit it.
    /// </summary>
    public bool AllParticipantsPrepared
        => EnlistedParticipants.Count > 0
           && EnlistedParticipants.Count == PreparedParticipants.Count;
}

/// <summary>
/// The persistent state of a logged transaction.
/// </summary>
public enum TransactionLogState
{
    /// <summary>
    /// The transaction was started but its outcome wasn't recorded.
    /// Recovery has to inspect the participants' votes (or roll back).
    /// </summary>
    Started,

    /// <summary>
    /// The transaction was successfully committed.
    /// </summary>
    Committed,

    /// <summary>
    /// The transaction was rolled back.
    /// </summary>
    RolledBack,
}
