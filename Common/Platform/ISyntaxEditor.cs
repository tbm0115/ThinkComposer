using System;
using System.Threading;
using System.Threading.Tasks;

namespace Instrumind.Common.Platform
{
    public interface ISyntaxEditor
    {
        event EventHandler TextChanged;

        string Text { get; set; }
        string SyntaxName { get; set; }
        bool IsReadOnly { get; set; }
        TextSelectionRange Selection { get; set; }

        Task FocusAsync(CancellationToken cancellationToken);
    }

    public interface ISyntaxEditorViewModel
    {
        string Text { get; set; }
        string SyntaxName { get; set; }
        TextSelectionRange Selection { get; set; }
    }

    public struct TextSelectionRange : IEquatable<TextSelectionRange>
    {
        public TextSelectionRange(int start, int length)
        {
            Start = start;
            Length = length;
        }

        public int Start { get; }
        public int Length { get; }

        public bool Equals(TextSelectionRange other)
        {
            return Start == other.Start && Length == other.Length;
        }

        public override bool Equals(object obj)
        {
            return obj is TextSelectionRange && Equals((TextSelectionRange)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Start * 397) ^ Length;
            }
        }
    }
}
