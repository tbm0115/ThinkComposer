using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;
using DotLiquid;
using DotLiquid.NamingConventions;
using Instrumind.Common.Platform;

namespace Instrumind.ThinkComposer.Services;

public sealed class DotLiquidTemplateRenderer : ITemplateRenderer
{
    private const int MaximumInjectionDepth = 16;

    private static readonly Regex LiteralShorthand =
        new(@"^\s*\{\{\{\s?(?<body>.*?)\s*\}\}\}\s*$", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex CommentShorthand =
        new(@"^\s*\{\s?#\s?(?<body>.*?)\s?#\s?\}\s*$", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex InjectTag =
        new(@"(?<indent>^[ \t]*)\{%-?\s*inject\s+['""](?<name>[^'""]+)['""]\s+with\s+(?<model>[A-Za-z_][\w\.]*)\s*(?<modifier>noindent|keepindent)?\s*-?%\}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public TemplateRenderResult Render(TemplateRenderRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        Template.NamingConvention = new CSharpNamingConvention();

        try
        {
            var text = RenderInternal(request.TemplateText ?? string.Empty, request.Model, request.Subtemplates, 0);
            return new TemplateRenderResult(text);
        }
        catch (Exception exception)
        {
            return new TemplateRenderResult(string.Empty, new[] { exception.Message });
        }
    }

    private static string RenderInternal(
        string templateText,
        object? model,
        IDictionary<string, string> subtemplates,
        int injectionDepth)
    {
        if (injectionDepth > MaximumInjectionDepth)
            throw new InvalidOperationException("Template injection depth exceeded.");

        var normalized = NormalizeShorthand(templateText);
        normalized = ExpandInjectTags(normalized, model, subtemplates, injectionDepth);

        var template = Template.Parse(normalized);
        return template.Render(Hash.FromAnonymousObject(model ?? new { }));
    }

    private static string NormalizeShorthand(string templateText)
    {
        var literal = LiteralShorthand.Match(templateText);
        if (literal.Success)
            return "{% raw %}" + literal.Groups["body"].Value + "{% endraw %}";

        var comment = CommentShorthand.Match(templateText);
        if (comment.Success)
            return "{% comment %}" + comment.Groups["body"].Value + "{% endcomment %}";

        return templateText;
    }

    private static string ExpandInjectTags(
        string templateText,
        object? model,
        IDictionary<string, string> subtemplates,
        int injectionDepth)
    {
        return InjectTag.Replace(templateText, match =>
        {
            var templateName = match.Groups["name"].Value;
            if (!subtemplates.TryGetValue(templateName, out var subtemplate))
                return string.Empty;

            var childModel = ResolveModelValue(model, match.Groups["model"].Value);
            var rendered = RenderInternal(subtemplate, childModel, subtemplates, injectionDepth + 1);
            var modifier = match.Groups["modifier"].Value;

            if (string.Equals(modifier, "keepindent", StringComparison.OrdinalIgnoreCase))
                return rendered;

            if (string.Equals(modifier, "noindent", StringComparison.OrdinalIgnoreCase))
                return PrefixLines(rendered, match.Groups["indent"].Value);

            return PrefixLines(rendered, match.Groups["indent"].Value + "    ");
        });
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Template compatibility intentionally reads public properties from caller-provided safe models.")]
    private static object? ResolveModelValue(object? model, string variableName)
    {
        if (model == null
            || string.IsNullOrWhiteSpace(variableName)
            || string.Equals(variableName, "This", StringComparison.OrdinalIgnoreCase))
            return model;

        object? current = model;
        foreach (var segment in variableName.Split('.'))
        {
            if (current == null)
                return null;

            if (current is IDictionary<string, object> dictionary
                && dictionary.TryGetValue(segment, out var dictionaryValue))
            {
                current = dictionaryValue;
                continue;
            }

            var property = current.GetType().GetTypeInfo().GetDeclaredProperty(segment);
            current = property == null ? null : property.GetValue(current);
        }

        return current;
    }

    private static string PrefixLines(string text, string prefix)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(prefix))
            return text;

        return Regex.Replace(text, "^(?=.)", prefix, RegexOptions.Multiline);
    }
}
