namespace IQOne.Zero;

/// <summary>
/// Generate nothing for this; the code is yours to write.
/// </summary>
/// <remarks>
/// <para>
/// Generated code CANNOT BE EDITED. It is written during compilation and there is no file the
/// compiler reads back, so a generator that is wrong about something would otherwise stop a
/// build with nothing its author could change. This is the way out, and it is meant to be
/// used: on the type the generator gets wrong, on the one it has no rule for, on the one a
/// hand-written version does better.
/// </para>
/// <para>
/// It says the same thing everywhere, which is why there is one attribute rather than a switch
/// per generator: whatever would have been written for this target is not written, and the
/// members it would have supplied are yours. Expect the compiler to name them — an unimplemented
/// abstract member, a partial method with no body — which is the point: it tells you exactly
/// what you have taken on.
/// </para>
/// <code>
/// // The generator has no rule for this shape, so the selector is written out.
/// [NoGenerate]
/// public sealed class LegacyRowMap : Map&lt;Row, RowModel&gt;
/// {
///     public override Expression&lt;Func&lt;Row, RowModel&gt;&gt; Selector =&gt; r =&gt; new RowModel(r.A, r.B);
/// }
/// </code>
/// <para>
/// ON THE ASSEMBLY it turns generation off for the whole project — every module registration,
/// every projection, every map. That is the setting for a project that wants none of it, and it
/// leaves the packages in place: the abstractions, the runtime and the analyzers all keep
/// working, and only the writing stops. Removing the analyzer package does the same thing more
/// bluntly and takes the diagnostics with it.
/// </para>
/// <code>
/// [assembly: NoGenerate]
/// </code>
/// <para>
/// A generator standing down is SILENT. There is nothing to report: the hand-written member is
/// in the same file as the attribute, and a diagnostic saying "you asked for this" is noise.
/// </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct
    | AttributeTargets.Interface | AttributeTargets.Method,
    Inherited = false)]
public sealed class NoGenerateAttribute : Attribute;
