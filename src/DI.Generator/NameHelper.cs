using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace NkChinh.DI.Generator;

/// <summary>
/// Pure naming rules used by the generator: assembly-name sanitization for extension method
/// names and constructor parameter name derivation.
/// </summary>
internal static class NameHelper
{
    /// <summary>
    /// Converts an assembly name into a valid PascalCase C# identifier fragment.
    /// <c>MyCompany.Infrastructure</c> → <c>MyCompanyInfrastructure</c>.
    /// </summary>
    public static string SanitizeAssemblyIdentifier(string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return "Assembly";
        }

        var builder = new StringBuilder(assemblyName.Length);
        var startOfSegment = true;
        foreach (var ch in assemblyName)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(startOfSegment ? char.ToUpperInvariant(ch) : ch);
                startOfSegment = false;
            }
            else
            {
                startOfSegment = true;
            }
        }

        if (builder.Length == 0)
        {
            return "Assembly";
        }

        if (char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Derives a camelCase constructor parameter name for one [Inject] member.
    /// Prefers the member's type name (<c>IOrderRepository</c> → <c>orderRepository</c>);
    /// falls back to the member name when the type name is unusable (empty, keyword, invalid).
    /// </summary>
    public static string DeriveParameterName(string typeShortName, string memberName)
        => FromTypeName(typeShortName) ?? FallbackFromMemberName(memberName);

    /// <summary>
    /// Assigns unique parameter names for all members of one constructor. Members whose
    /// type-derived names collide fall back to member names; any remaining collisions get
    /// a numeric suffix. Order follows the input list (declaration order).
    /// </summary>
    public static string[] AssignParameterNames(IReadOnlyList<(string TypeShortName, string MemberName)> members)
    {
        var names = new string[members.Count];
        for (var i = 0; i < members.Count; i++)
        {
            names[i] = DeriveParameterName(members[i].TypeShortName, members[i].MemberName);
        }

        var duplicates = new HashSet<string>(
            names.GroupBy(static n => n).Where(static g => g.Count() > 1).Select(static g => g.Key));
        if (duplicates.Count > 0)
        {
            for (var i = 0; i < names.Length; i++)
            {
                if (duplicates.Contains(names[i]))
                {
                    names[i] = FallbackFromMemberName(members[i].MemberName);
                }
            }
        }

        var seen = new Dictionary<string, int>();
        for (var i = 0; i < names.Length; i++)
        {
            if (seen.TryGetValue(names[i], out var count))
            {
                seen[names[i]] = count + 1;
                names[i] += (count + 1).ToString();
            }
            else
            {
                seen[names[i]] = 1;
            }
        }

        return names;
    }

    private static string? FromTypeName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return null;
        }

        string candidate;
        if (IsAllUpper(typeName))
        {
            candidate = typeName.ToLowerInvariant();
        }
        else
        {
            var stripped = typeName.Length > 2 && typeName[0] == 'I' && char.IsUpper(typeName[1])
                ? typeName.Substring(1)
                : typeName;
            candidate = LowerFirst(stripped);
        }

        // Keywords (e.g. "string") are not usable; let the caller fall back to the member name.
        return SyntaxFacts.GetKeywordKind(candidate) == SyntaxKind.None ? candidate : null;
    }

    private static string FallbackFromMemberName(string memberName)
    {
        var name = memberName.TrimStart('_');
        if (name.Length == 0)
        {
            return "value";
        }

        name = IsAllUpper(name) ? name.ToLowerInvariant() : LowerFirst(name);
        return SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;
    }

    private static string LowerFirst(string value)
        => char.IsUpper(value[0]) ? char.ToLowerInvariant(value[0]) + value.Substring(1) : value;

    private static bool IsAllUpper(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsLetter(ch) && !char.IsUpper(ch))
            {
                return false;
            }
        }

        return true;
    }
}
