namespace Instrumind.Common.Portable
{
    public enum TcTextAlignment
    {
        Left,
        Center,
        Right,
        Justify
    }

    public sealed class TcTextFormat
    {
        public TcTextFormat()
        {
            FontFamily = "Segoe UI";
            FontSize = 12;
            Foreground = TcColor.FromRgb(0, 0, 0);
            Alignment = TcTextAlignment.Left;
        }

        public string FontFamily { get; set; }
        public double FontSize { get; set; }
        public TcColor Foreground { get; set; }
        public TcTextAlignment Alignment { get; set; }
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public bool IsUnderline { get; set; }
        public bool IsStrikethrough { get; set; }
    }
}
