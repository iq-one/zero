namespace IQOne.Zero.Persistence;

/// <summary>
/// Hands one member of a <see cref="MappingAttribute"/> to a method of your own.
/// </summary>
/// <remarks>
/// <para>
/// The member that needs a decision is the common case, not the exception: an enum stored as
/// a byte, a name split into two columns, a value that has to be looked up. Without somewhere
/// to put that decision the whole mapping goes back to being written by hand, and the ninety
/// members that were never in question go with it.
/// </para>
/// <para>
/// This is the OPPOSITE of <see cref="MappingAttribute.Ignore"/>, and the difference is the
/// point. Ignore removes a member from the account — it says "not here" and nothing checks
/// that it is anywhere. MapMember keeps it in the account and says WHERE:
/// <code>
/// [Mapping]
/// [MapMember(nameof(BedModel.BedState), nameof(WriteBedState))]
/// private static partial void Apply(BedModel model, Bed bed);
///
/// private static void WriteBedState(BedModel model, Bed bed) =&gt; bed.StateId = (byte)model.BedState;
/// </code>
/// So reach for Ignore when a member genuinely must not be carried, and for this when it must
/// be carried differently. A member named by both is a contradiction and is reported.
/// </para>
/// <para>
/// WHICH MEMBER it names is the one the mapping is held to account for — the same list Ignore
/// draws from, so the two read as alternatives on the same set. Producing an object, that is a
/// member of the RESULT; writing onto one, a member of the SOURCE. Naming a member of the
/// other end is reported rather than quietly ignored, because it would leave the mapping
/// incomplete while looking as though it had been handled.
/// </para>
/// <para>
/// THE METHOD has the shape of the mapping itself, one member's worth:
/// <list type="bullet">
///   <item>
///     Producing — <c>static TMember Helper(TSource source)</c>. It returns the value, which
///     goes into the initialiser where the generated read would have gone.
///   </item>
///   <item>
///     Writing onto — <c>static void Helper(TSource source, TTarget target)</c>. It performs
///     the write, because the member it writes need not be the one it discharges: that is
///     usually the whole reason it exists.
///   </item>
/// </list>
/// It lives in the same type as the mapping, so it may be private; the generated body is
/// another part of that type. The signature is checked against the shape, and the mapping
/// method may not name itself.
/// </para>
/// <para>
/// A STATIC mapping can only call a static method, which is the limit worth knowing: the
/// moment a member needs something injected, drop <c>static</c> from the mapping too and the
/// helper can read the constructor's dependencies. See
/// <see cref="MappingAttribute"/> for what that turns the class into.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class MapMemberAttribute : Attribute
{
    /// <summary>Hands <paramref name="member"/> to <paramref name="method"/>.</summary>
    /// <param name="member">
    /// The member being accounted for — of the result when the mapping produces one, of the
    /// source when it writes onto one. Use <c>nameof</c>: a string that no longer matches
    /// after a rename is the failure this attribute exists to prevent.
    /// </param>
    /// <param name="method">
    /// The method in the same type that handles it. Use <c>nameof</c> here too.
    /// </param>
    public MapMemberAttribute(string member, string method)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        Member = member;
        Method = method;
    }

    /// <summary>The member this handles.</summary>
    public string Member { get; }

    /// <summary>The method that handles it.</summary>
    public string Method { get; }
}
