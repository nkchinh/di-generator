using System.Collections;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace NkChinh.DI.Generator;

/// <summary>
/// Immutable array with structural equality, required for incremental pipeline caching.
/// Never flow <see cref="ISymbol"/> or syntax nodes through the pipeline — only value data.
/// </summary>
internal readonly struct EquatableArray<T>(T[] array) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new([]);

    private readonly T[]? _array = array;

    public int Count => _array?.Length ?? 0;

    public T this[int index] => (_array ?? Array.Empty<T>())[index];

    public T[] ToArray() => _array ?? [];

    public bool Equals(EquatableArray<T> other)
    {
        var left = _array ?? [];
        var right = other._array ?? [];
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var item in _array ?? [])
            {
                hash = (hash * 31) + item.GetHashCode();
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_array ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Value-equatable stand-in for <see cref="Location"/> so diagnostics can flow through the pipeline.</summary>
internal readonly record struct LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationInfo? CreateFrom(SyntaxNode node)
    {
        var location = node.GetLocation();
        return location.SourceTree is null
            ? null
            : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }
}

/// <summary>A diagnostic captured during the transform stage, reported later in the output stage.</summary>
internal sealed record DiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    LocationInfo? Location,
    EquatableArray<string> MessageArgs)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, LocationInfo? location, params string[] args)
        => new(descriptor, location, new EquatableArray<string>(args));

    public Diagnostic ToDiagnostic()
        => Diagnostic.Create(
            Descriptor,
            Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None,
            MessageArgs.ToArray().Cast<object>().ToArray());
}

/// <summary>One service registration extracted from a lifetime attribute.</summary>
/// <param name="Lifetime">
/// Null when <paramref name="IsAutoScope"/> is true and not yet resolved against locked-scope rules.
/// </param>
/// <param name="IsAutoScope">True for <c>[Service&lt;T&gt;]</c>, whose lifetime is resolved from T's locked scope.</param>
/// <param name="LockedLifetime">
/// The lifetime <c>ServiceFqn</c> is locked to via its own <c>[RequiredScope]</c>, if any. Resolved eagerly
/// because it only depends on the service type's symbol, not on the whole compilation.
/// </param>
internal sealed record ServiceInfo(
    string ImplementationFqn,
    string? ServiceFqn,
    string? Lifetime,
    string? Key,
    bool IsHostedService,
    LocationInfo? Location,
    bool IsAutoScope,
    string? LockedLifetime);

/// <summary>A valid service registration after scope resolution; lifetime is guaranteed non-null.</summary>
internal sealed record ResolvedServiceInfo(
    string ImplementationFqn,
    string? ServiceFqn,
    string Lifetime,
    string? Key,
    bool IsHostedService,
    LocationInfo? Location);

/// <summary>Transform result for the service pipeline: either a registration or a diagnostic.</summary>
internal sealed record ServiceResult(ServiceInfo? Service, DiagnosticInfo? Diagnostic);

/// <summary>One link in the containing-type chain of an [Inject] member (outermost first).</summary>
internal readonly record struct TypeShell(string Keyword, string DisplayName);

/// <summary>The partial-class shell a generated constructor is emitted into.</summary>
internal sealed record InjectClassShell(
    string GroupKey,
    string? Namespace,
    string ConstructorName,
    EquatableArray<TypeShell> TypeChain,
    bool HasUserConstructor,
    string HintName);

/// <summary>One [Inject] member; ordering fields preserve declaration order across partial files.</summary>
internal sealed record InjectMemberInfo(
    string MemberName,
    string TypeFqn,
    string TypeShortName,
    bool IsProperty,
    string? Key,
    bool IsOptional,
    string FilePath,
    int SpanStart,
    LocationInfo? Location);

/// <summary>Metadata for constructor-injected services, used to emit factory delegates when a class has both a generated constructor and user-defined constructors.</summary>
internal sealed record InjectConstructorMeta(
    EquatableArray<string> ParamTypeFqns,
    EquatableArray<string> ParamKeys,
    EquatableArray<bool> ParamOptionals);

/// <summary>Transform result for the [Inject] pipeline.</summary>
internal sealed record InjectResult(
    InjectClassShell? Shell,
    InjectMemberInfo? Member,
    string? GroupKey,
    DiagnosticInfo? Diagnostic);

/// <summary>
/// A published service definition read from a referenced assembly's <c>[assembly: ServiceDefinition]</c>
/// attributes (or built for the current assembly). Value data only (strings) so it flows through the
/// incremental pipeline and caches by content; member arrays are index-aligned and in the exact
/// order the derived class's generated constructor uses.
/// </summary>
internal sealed record ServiceDefinitionData(
    string OwnerAssemblyName,
    string ServiceTypeFqn,
    string ImplementationTypeFqn,
    string Lifetime,
    string? Key,
    bool IsHostedService,
    bool RequiresFactory,
    EquatableArray<string> MemberNames,
    EquatableArray<string> MemberTypeFqns,
    EquatableArray<string> MemberKeys,
    EquatableArray<bool> MemberOptionals,
    bool IsAccessibleToConsumer);

/// <summary>Published definitions and MEDI registration modules available to this compilation.</summary>
internal sealed record ReferencedServices(
    EquatableArray<ServiceDefinitionData> Definitions,
    EquatableArray<string> RegistrationModuleIdentifiers);

/// <summary>Combined input for the registration emitter (own services, name, scope locks, MEDI
/// availability, [Inject] metadata and published definitions from referenced assemblies).</summary>
internal sealed record RegistrationPipelineInput(
    EquatableArray<ServiceResult> Services,
    string AssemblyName,
    ExternalScopeRules ExternalScopeRules,
    bool HasServiceLifetime,
    IReadOnlyDictionary<string, (bool HasUserCtor, EquatableArray<InjectMemberInfo> Members)> InjectMeta,
    ReferencedServices ReferencedServices);

/// <summary>A locked lifetime for a type, read from an <c>[assembly: RequiredExternalScope]</c> declaration.</summary>
internal readonly record struct ExternalScopeRule(string TypeFqn, string Lifetime);

/// <summary>External-scope rules resolved for the current compilation, plus any conflicts found while merging them.</summary>
internal sealed record ExternalScopeRules(
    EquatableArray<ExternalScopeRule> Rules,
    EquatableArray<DiagnosticInfo> Diagnostics);
