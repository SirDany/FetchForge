using System.Text;

namespace FetchForge.Core.Models;

public sealed record CommandBuildResult(
    string Executable,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<OptionValidationIssue> Issues)
{
    public bool HasErrors =>
        Issues.Any(issue => issue.Severity == OptionValidationSeverity.Error);

    public string FormattedCommandLine
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(QuoteArgument(Executable));

            foreach (var argument in Arguments)
            {
                sb.Append(' ');
                sb.Append(QuoteArgument(argument));
            }

            return sb.ToString();
        }
    }

    private static string QuoteArgument(string value)
    {
        if (value.Length == 0)
            return "\"\"";

        if (!value.Any(ch => char.IsWhiteSpace(ch) || ch == '"'))
            return value;

        return $"\"{value.Replace("\"", "\\\"")}\"";
    }
}
