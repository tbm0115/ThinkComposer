using System.Collections.Generic;

namespace Instrumind.Common.Platform
{
    public interface ITemplateRenderer
    {
        TemplateRenderResult Render(TemplateRenderRequest request);
    }

    public sealed class TemplateRenderRequest
    {
        public TemplateRenderRequest()
        {
            Subtemplates = new Dictionary<string, string>();
            EnableThinkComposerCompatibility = true;
        }

        public string TemplateText { get; set; }
        public object Model { get; set; }
        public IDictionary<string, string> Subtemplates { get; private set; }
        public bool EnableThinkComposerCompatibility { get; set; }
    }

    public sealed class TemplateRenderResult
    {
        public TemplateRenderResult(string text)
            : this(text, new string[0])
        {
        }

        public TemplateRenderResult(string text, IReadOnlyList<string> diagnostics)
        {
            Text = text;
            Diagnostics = diagnostics ?? new string[0];
        }

        public string Text { get; }
        public IReadOnlyList<string> Diagnostics { get; }
    }
}
