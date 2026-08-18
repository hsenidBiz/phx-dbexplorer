using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;

namespace PhxDbExplorer.Query;

public sealed record SqlValidationResult(bool IsValid, string? Error)
{
    public static SqlValidationResult Valid { get; } = new(true, null);
    public static SqlValidationResult Invalid(string error) => new(false, error);
}

/// <summary>
/// Guards the data-read tools against anything that is not a single read-only statement.
/// This is a defence-in-depth layer only — the providers additionally run every user-supplied
/// statement inside a transaction that is always rolled back (and, on PostgreSQL, a
/// READ ONLY transaction), so a miss here still cannot persist a write.
/// </summary>
public static partial class ReadOnlySqlValidator
{
    /// <summary>
    /// Whole-word tokens that can write, execute, or reach outside the current query.
    /// Matched against SQL with string literals, quoted identifiers, and comments blanked out,
    /// so a keyword used as a bracketed/quoted column name never trips the check.
    /// </summary>
    private static readonly string[] ForbiddenKeywords =
    [
        // Writes / DDL (both engines)
        "INSERT", "UPDATE", "DELETE", "MERGE", "UPSERT", "TRUNCATE",
        "CREATE", "ALTER", "DROP", "RENAME",
        // SELECT ... INTO materialises a new table on SQL Server
        "INTO",
        // Permissions / server control
        "GRANT", "REVOKE", "DENY", "SHUTDOWN", "RECONFIGURE", "KILL",
        "BACKUP", "RESTORE", "CHECKPOINT", "DBCC",
        // Procedural execution
        "EXEC", "EXECUTE", "CALL", "DO", "PERFORM",
        // Transaction / session control — the provider owns the transaction
        "COMMIT", "ROLLBACK", "SAVEPOINT", "BEGIN", "SET", "USE", "DECLARE", "WAITFOR",
        // External / file / maintenance access
        "OPENROWSET", "OPENDATASOURCE", "OPENQUERY", "OPENXML", "BULK",
        "COPY", "VACUUM", "REINDEX", "CLUSTER", "LISTEN", "NOTIFY", "LOCK",
    ];

    /// <summary>Function and procedure name prefixes that must never be reachable.</summary>
    private static readonly string[] ForbiddenPrefixes =
    [
        "sp_", "xp_", "pg_read_file", "pg_read_binary_file", "pg_ls_dir", "pg_sleep",
        "pg_terminate_backend", "pg_cancel_backend", "dblink", "lo_import", "lo_export",
    ];

    private static readonly SearchValues<char> TagChars = SearchValues.Create(
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_");

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_$]*", RegexOptions.Compiled)]
    private static partial Regex WordRegex();

    /// <summary>
    /// Validates a complete statement supplied to execute_query: it must be a single
    /// SELECT (or WITH … SELECT) statement containing no forbidden constructs.
    /// </summary>
    public static SqlValidationResult ValidateStatement(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return SqlValidationResult.Invalid("The query is empty.");

        var stripped = StripLiteralsAndComments(sql);

        if (string.IsNullOrWhiteSpace(stripped))
            return SqlValidationResult.Invalid("The query contains no executable SQL.");

        var statementCount = stripped.Split(';').Count(s => !string.IsNullOrWhiteSpace(s));
        if (statementCount > 1)
            return SqlValidationResult.Invalid(
                "Only a single statement is allowed. Remove the ';' separator and any extra statements.");

        var firstWord = WordRegex().Match(stripped);
        if (!firstWord.Success ||
            (!firstWord.Value.Equals("SELECT", StringComparison.OrdinalIgnoreCase) &&
             !firstWord.Value.Equals("WITH", StringComparison.OrdinalIgnoreCase)))
        {
            return SqlValidationResult.Invalid(
                "Only read-only queries are allowed — the statement must start with SELECT or WITH.");
        }

        return CheckForbidden(stripped, "query");
    }

    /// <summary>
    /// Validates a raw SQL fragment (a WHERE predicate or an ORDER BY list) that is spliced into
    /// a generated SELECT. Fragments may not close the statement or start a new one.
    /// </summary>
    public static SqlValidationResult ValidateFragment(string? fragment, string fragmentName)
    {
        if (string.IsNullOrWhiteSpace(fragment))
            return SqlValidationResult.Valid;

        var stripped = StripLiteralsAndComments(fragment);

        if (stripped.Contains(';'))
            return SqlValidationResult.Invalid($"The {fragmentName} may not contain ';'.");

        if (CountUnbalancedParens(stripped) != 0)
            return SqlValidationResult.Invalid($"The {fragmentName} has unbalanced parentheses.");

        return CheckForbidden(stripped, fragmentName);
    }

    private static SqlValidationResult CheckForbidden(string stripped, string what)
    {
        foreach (Match word in WordRegex().Matches(stripped))
        {
            foreach (var keyword in ForbiddenKeywords)
            {
                if (word.Value.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                    return SqlValidationResult.Invalid(
                        $"The {what} contains the disallowed keyword '{keyword}'. Only read-only SELECT logic is permitted.");
            }

            foreach (var prefix in ForbiddenPrefixes)
            {
                if (word.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return SqlValidationResult.Invalid(
                        $"The {what} references '{word.Value}', which is not allowed.");
            }
        }

        return SqlValidationResult.Valid;
    }

    private static int CountUnbalancedParens(string stripped)
    {
        var depth = 0;
        foreach (var c in stripped)
        {
            if (c == '(') depth++;
            else if (c == ')') depth--;
            if (depth < 0) return depth;
        }
        return depth;
    }

    /// <summary>
    /// Blanks out string literals, quoted/bracketed identifiers, and comments while preserving the
    /// length of the input, so the remaining text can be keyword-scanned without false positives
    /// (a column named [Update]) or false negatives (DR/*x*/OP style smuggling — the comment is
    /// removed, which leaves the keyword contiguous and detectable).
    /// </summary>
    internal static string StripLiteralsAndComments(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        var i = 0;

        while (i < sql.Length)
        {
            var c = sql[i];

            // Line comment
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') { sb.Append(' '); i++; }
                continue;
            }

            // Block comment (nested, as both engines allow)
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var depth = 0;
                while (i < sql.Length)
                {
                    if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*') { depth++; sb.Append("  "); i += 2; }
                    else if (sql[i] == '*' && i + 1 < sql.Length && sql[i + 1] == '/')
                    {
                        depth--; sb.Append("  "); i += 2;
                        if (depth == 0) break;
                    }
                    else { sb.Append(' '); i++; }
                }
                continue;
            }

            // Single-quoted string literal ('' escapes a quote)
            if (c == '\'') { i = SkipDelimited(sql, i, '\'', sb); continue; }

            // Double-quoted identifier ("" escapes a quote)
            if (c == '"') { i = SkipDelimited(sql, i, '"', sb); continue; }

            // SQL Server bracketed identifier (]] escapes a bracket)
            if (c == '[') { i = SkipDelimited(sql, i, ']', sb); continue; }

            // PostgreSQL dollar-quoted string ($$ … $$ or $tag$ … $tag$)
            if (c == '$')
            {
                var close = sql.IndexOf('$', i + 1);
                if (close > i && !sql.AsSpan(i + 1, close - i - 1).ContainsAnyExcept(TagChars))
                {
                    var tag = sql[i..(close + 1)];
                    var end = sql.IndexOf(tag, close + 1, StringComparison.Ordinal);
                    var stop = end < 0 ? sql.Length : end + tag.Length;
                    sb.Append(' ', stop - i);
                    i = stop;
                    continue;
                }
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Blanks a delimited run starting at the opening character, treating a doubled closing
    /// character as an escape. Returns the index just past the closing delimiter (or the end of
    /// the input when the delimiter is never closed).
    /// </summary>
    private static int SkipDelimited(string sql, int start, char closing, StringBuilder sb)
    {
        sb.Append(' ');
        var i = start + 1;
        while (i < sql.Length)
        {
            if (sql[i] == closing)
            {
                if (i + 1 < sql.Length && sql[i + 1] == closing) { sb.Append("  "); i += 2; continue; }
                sb.Append(' ');
                return i + 1;
            }
            sb.Append(' ');
            i++;
        }
        return i;
    }
}
