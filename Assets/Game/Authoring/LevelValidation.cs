using System.Collections.Generic;
using System.Text;

namespace GlimmerGrove.Content
{
    public enum LevelIssueSeverity
    {
        /// <summary>Worth a designer's attention, but the level is playable.</summary>
        Warning,

        /// <summary>The level is broken. It must not reach a player.</summary>
        Error,
    }

    public readonly struct LevelIssue
    {
        public readonly LevelIssueSeverity Severity;
        public readonly string Message;

        public LevelIssue(LevelIssueSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }

        public override string ToString() => $"{Severity}: {Message}";
    }

    /// <summary>Everything the validator found in one level.</summary>
    public sealed class LevelValidationReport
    {
        public readonly LevelId Id;
        public readonly IReadOnlyList<LevelIssue> Issues;

        /// <summary>Par the validator derived from the board, for comparison with the authored one.</summary>
        public readonly int ComputedPar;

        public LevelValidationReport(LevelId id, IReadOnlyList<LevelIssue> issues, int computedPar)
        {
            Id = id;
            Issues = issues;
            ComputedPar = computedPar;
        }

        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < Issues.Count; i++)
                    if (Issues[i].Severity == LevelIssueSeverity.Error) return true;
                return false;
            }
        }

        public bool IsClean => Issues.Count == 0;

        public string Describe()
        {
            if (IsClean) return $"{Id}: ok";
            var sb = new StringBuilder();
            sb.Append(Id).Append(':');
            foreach (var issue in Issues) sb.Append("\n  ").Append(issue);
            return sb.ToString();
        }
    }
}
