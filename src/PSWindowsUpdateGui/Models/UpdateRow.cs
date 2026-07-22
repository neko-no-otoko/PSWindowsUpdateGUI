using System;
using System.Management.Automation;

namespace PSWindowsUpdateGui.Models;

internal sealed class UpdateRow
{
    public bool IsSelected { get; set; }

    public string ComputerName { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string KB { get; set; } = string.Empty;

    public string Size { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string UpdateId { get; set; } = string.Empty;

    public string RawText { get; set; } = string.Empty;

    public static UpdateRow From(PSObject value)
    {
        return new UpdateRow
        {
            ComputerName = Read(value, "ComputerName"),
            Status = Read(value, "Status"),
            KB = FirstNonEmpty(Read(value, "KB"), Read(value, "KBArticleIDs"), Read(value, "KBArticleID")),
            Size = Read(value, "Size"),
            Title = Read(value, "Title"),
            UpdateId = FirstNonEmpty(Read(value, "UpdateId"), ReadNested(value, "Identity", "UpdateID")),
            RawText = value.ToString()
        };
    }

    private static string ReadNested(PSObject value, string property, string nestedProperty)
    {
        try
        {
            var raw = value.Properties[property]?.Value;
            return raw == null
                ? string.Empty
                : PSObject.AsPSObject(raw).Properties[nestedProperty]?.Value?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Read(PSObject value, string property)
    {
        try
        {
            var raw = value.Properties[property]?.Value;
            if (raw == null)
            {
                return string.Empty;
            }

            if (!(raw is string))
            {
                var enumerator = LanguagePrimitives.GetEnumerator(raw);
                if (enumerator != null)
                {
                    var values = new System.Collections.Generic.List<string>();
                    while (enumerator.MoveNext())
                    {
                        var text = enumerator.Current?.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            values.Add(text!);
                        }
                    }

                    return string.Join(", ", values);
                }
            }

            var rendered = raw.ToString() ?? string.Empty;
            return string.Equals(rendered, "System.__ComObject", StringComparison.Ordinal) ? string.Empty : rendered;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
