namespace IQOne.Zero.Resilience;

/// <summary>
/// A command that may be sent again without doing its work a second time.
/// </summary>
/// <remarks>
/// <para>
/// A query is retried without being asked, because a query is defined as a request that
/// changes nothing — <see cref="Messaging.IQuery{TResponse}"/> says so. A command is not,
/// because the cost of being wrong is not a wasted round trip: it is a customer charged
/// twice, an email sent twice, a stock movement booked twice. So the safe case is the
/// default and the dangerous one has to be written down.
/// </para>
/// <para>
/// The claim this interface makes is specific. It is not "this command is important" or
/// "this command usually works". It is: <em>if the same command is handled twice, the state
/// afterwards is the one a single handling would have left, and the second answer is as good
/// as the first.</em> In practice that means the command carries the identity of what it
/// creates or changes — a reference the caller chose, a version it expects — so the handler
/// can recognise work it has already done. A command whose handler generates that identity
/// itself is not idempotent, however carefully it is written.
/// </para>
/// <para>
/// Marking a query is allowed but says nothing, since a query is already retried. Marking
/// anything that is not a request does nothing at all.
/// </para>
/// </remarks>
public interface IIdempotent;
