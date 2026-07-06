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

    public T this[int index] => _array![index];

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
internal sealed record ServiceInfo(
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
    string HintName);

/// <summary>One [Inject] member; ordering fields preserve declaration order across partial files.</summary>
internal sealed record InjectMemberInfo(
    string MemberName,
    string TypeFqn,
    string TypeShortName,
    bool IsProperty,
    string FilePath,
    int SpanStart);

/// <summary>Transform result for the [Inject] pipeline.</summary>
internal sealed record InjectResult(
    InjectClassShell? Shell,
    InjectMemberInfo? Member,
    string? GroupKey,
    DiagnosticInfo? Diagnostic);

/// <summary>A referenced assembly's generated registration module, read from its assembly-level marker.</summary>
internal readonly record struct ModuleInfo(string MethodName, string ExtensionsTypeName);
