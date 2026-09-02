namespace IQOne.Zero.Events;

/// <summary>
/// Something that has already happened, told to whoever cares.
/// </summary>
/// <remarks>
/// <para>
/// The distinction from a request is direction, not shape.
/// A request is an instruction with exactly one handler and an answer the caller waits for;
/// an event is a statement of fact with any number of subscribers and no answer at all. That
/// is why an event is named in the past tense — <c>InvoicePaid</c>, not <c>PayInvoice</c> —
/// and why nothing about it may be conditional on who is listening.
/// </para>
/// <para>
/// An event is a value: it carries what happened and nothing that can be changed. Subscribers
/// run one after another over the same instance, so a settable property is a private channel
/// between two of them, and the order they use it in is not defined. <c>ZERO501</c> reports it.
/// </para>
/// <para>
/// There is no <c>IEvent&lt;TResponse&gt;</c> and there will not be one. An event that returns
/// something has a single caller waiting for it, which makes it a request.
/// </para>
/// </remarks>
public interface IEvent;
