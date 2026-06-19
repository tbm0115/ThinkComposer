# Appendix A: Template Language

ThinkComposer Output Templates use Liquid markup plus ThinkComposer-specific control markup. Templates reference properties from the Composition Information Model and merge those values with template text to generate files.

The original Liquid language reference is available at:

<https://github.com/Shopify/liquid/wiki/Liquid-for-Designers>

## Control Markup

Control markup tells ThinkComposer what to do with generated text. It is prefixed with `%%:`.

| Control | Description |
|---|---|
| `%%:FileName=<Template-Text>` | Sets the generated file name. It must be the first line and consume the whole line. If omitted, generated files use the idea TechName and `.txt`. |
| `%%:[ExtensionPlace]` | Marks where text from templates that extend a base template should be inserted. If omitted, extension text is appended. |
| `%%:SubTemplate=<Identifier>` | Declares a reusable subtemplate from the next line until another subtemplate declaration or the end of the template. |
| `%%:TemplateRole=<Role>` | Declares the modern template role, such as `DocumentRoot`, `Fragment`, `SubTemplate`, `Diagnostic`, `NotApplicable`, or `Disabled`. |
| `%%:outputPostProcess.<option>=<value>` | Controls output cleanup such as trimming leading whitespace or normalizing line endings. |
| `%%:outputValidation=<Mode>` | Requests validation such as XML well-formedness. |

Example file-name control:

```liquid
%%:FileName=Idea-{{ TechName }}.txt
[MyDocStart]
My text
[MyDocEnd]
```

Example extension place:

```liquid
%%:FileName=Idea-{{ TechName }}.txt
[MyDocStart]
Base text
%%:[ExtensionPlace]
[MyDocEnd]
```

Example subtemplate:

```liquid
%%:FileName=Composition-{{ TechName }}.txt
[MyDocStart]
Root: {{ Name }}
{%- inject 'TreeNodeReader' with This -%}
[MyDocEnd]

%%:SubTemplate=TreeNodeReader
[NodeStart]
Node-Name: {{ Name }}
{% assign Depth = @@InjectionDepth %}
Nested-Level: {{ Depth }}
{%- for Idea in CompositeIdeas -%}
{%- inject 'TreeNodeReader' with Idea -%}
{%- endfor -%}
[NodeEnd]
```

## Output Markup

Output markup writes evaluated text to the generated output. It is enclosed between `{{` and `}}`.

An expression may contain:

- a property name
- an indexed lookup
- a literal value
- filters applied with `|`
- whitespace trimming markers using `-`

Examples:

```liquid
Title: {{ Name }}
{{ Name }} is a {{ Summary | Upcase }}.
"{{ Name }}" has {{ Name | Size }} characters.
```

Whitespace trimming:

```liquid
Name: {{- Name }}
Summary:
{{ Summary -}}
;
```

## Filters

Filters take input from the left side and return transformed output. Names are case-sensitive.

| Filter | Description |
|---|---|
| `Size` | Gets the size of an array, string, or collection. Also usable as a property. |
| `Any` | Indicates whether a collection or string has content. Also usable as a property. |
| `AsChar` | Converts `tab`, `newline`, or a UTF-16 code to a character. |
| `ToBase64` | Gets binary content as Base64 text. |
| `ToUnformattedText` | Converts rich text such as `Description` to unformatted text. |
| `ToPlainText` | Converts an arbitrary object such as detail content to simple text. |
| `Capitalize` | Capitalizes words in the input sentence. |
| `Downcase` | Converts a string to lowercase. |
| `Upcase` | Converts a string to uppercase. |
| `First` | Gets the first element of an array or collection. |
| `Last` | Gets the last element of an array or collection. |
| `Join` | Joins array elements with a separator. |
| `Sort` | Sorts array elements. |
| `Map` | Maps a collection to a property. |
| `Escape` | Escapes a string. |
| `EscapeOnce` | Escapes HTML without affecting existing escaped entities. |
| `StripHtml` | Removes HTML tags. |
| `StripNewlines` | Removes newline characters. |
| `NewlineToBr` | Replaces newlines with HTML line breaks. |
| `Replace` | Replaces each occurrence. |
| `ReplaceFirst` | Replaces the first occurrence. |
| `Remove` | Removes each occurrence. |
| `RemoveFirst` | Removes the first occurrence. |
| `Truncate` | Truncates a string to a character count. |
| `Truncatewords` | Truncates a string to a word count. |
| `Prepend` | Prepends a string. |
| `Append` | Appends a string. |
| `Minus` | Numeric subtraction. |
| `Plus` | Numeric addition, or string concatenation when values are strings. |
| `Times` | Numeric multiplication. |
| `DividedBy` | Numeric division. |
| `Split` | Splits a string on a matching pattern. |
| `Modulo` | Numeric remainder. |
| `Get` | Gets a property from a source object by TechName. |
| `GetIdeasDefinedAs` | Filters ideas by Idea Definition TechName. |
| `GetElements` | Filters identifiable elements by TechName. |
| `GetLinksByVariant` | Filters role-based links by role variant TechName. |
| `SelectMany` | Flattens nested collections from a named collection property. |

Examples:

```liquid
Items = {{ Source | Size }}
{{ 'da vinci' | Capitalize }}
{{ 'foofoo' | replace:'foo','bar' }}
{{ 5 | times:4 }}
{% assign Arachnids = Animals | GetIdeasDefinedAs:'Spider;Scorpion' %}
{% assign AllSolarSystemMoons = Planets | SelectMany:'Moons' %}
```

## Safe Modern Filters

Current generation also supports safer helpers for XML, JSON, identifiers, and detail lookup:

| Filter | Use |
|---|---|
| `EscapeXmlAttribute` | Escape text for XML attributes. |
| `EscapeXmlText` | Escape text for XML element content. |
| `NormalizeTechName` | Normalize text into a technical identifier. |
| `DefaultIfEmpty` | Provide fallback text when a value is blank. |
| `DetailValue` | Read a simple field value from a detail/table-like object. |
| `JsonString` | Escape text for JSON string values. |

Example:

```liquid
<device id="{{ TechName | NormalizeTechName | EscapeXmlAttribute }}"
        name="{{ Name | DefaultIfEmpty: 'Unnamed' | EscapeXmlAttribute }}" />
```

## Tag Markup

Tag markup declares processing instructions. Tags are enclosed between `{%` and `%}` and do not directly emit text unless the tag body emits text.

| Tag | Description |
|---|---|
| `assign` | Assigns a value to a variable. |
| `capture` | Captures generated text into a variable. |
| `case` | Standard `case` / `when` / `else` block. |
| `comment` | Comments out a block. |
| `for` | Iterates through a collection. |
| `if` | Conditional block. |
| `inject` | Inserts a subtemplate with a new information context. |
| `raw` | Temporarily disables tag processing. |
| `unless` | Inverse conditional block. |

Example `for` block:

```liquid
{% for Detail in Details %}
This is a detail of kind {{ Detail.Kind.Name }}
{% if forloop.last %}
This is the last element.
{% endif %}
{% endfor %}
```

Example `if` block:

```liquid
{% if Details.Size < 1 %}
empty
{% else %}
There are {{ Details.Size }} details.
{% endif %}
```

Example `inject`:

```liquid
{% for Child in CompositeIdeas %}
{% inject 'ChildrenTemplate' with Child %}
{% endfor %}

{% inject 'MyTemplate' with Data keepindent %}
{% inject 'MyTemplate' with Remarks noindent %}
```

## Operators And Escaping

Logical operators include:

- `==`
- `!=`
- `and`
- `or`

Collection operators include:

- `contains`
- `empty`

Separate operators and values with spaces. For example, `a == b` is valid, while `a==b` should be avoided.

Backslashes in function parameters must be escaped by writing them twice:

```liquid
{{ TechName | replace:'\\','.' }}
```
