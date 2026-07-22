using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management.Automation;
using System.Text.RegularExpressions;
using PSWindowsUpdateGui.Infrastructure;
using PSWindowsUpdateGui.Models;

namespace PSWindowsUpdateGui.ViewModels;

internal sealed class ParameterInputViewModel : ObservableObject
{
    private bool _isBound;
    private string _valueText = string.Empty;
    private PSCredential? _credential;

    public ParameterInputViewModel(ParameterDefinition definition)
    {
        Definition = definition;
        _isBound = definition.IsMandatory;
        if (definition.IsSwitch)
        {
            ValueText = "True";
        }
        else if (definition.ValidValues.Count > 0 && !definition.IsArray)
        {
            ValueText = definition.ValidValues[0];
        }
    }

    public ParameterDefinition Definition { get; }

    public string Name => Definition.Name;

    public string TypeLabel => Definition.TypeLabel;

    public bool IsRequired => Definition.IsMandatory;

    public bool UsesChoiceEditor => Definition.IsSwitch || (Definition.ValidValues.Count > 0 && !Definition.IsArray);

    public bool UsesTextEditor => !UsesChoiceEditor && !Definition.IsCredential;

    public bool UsesCredentialEditor => Definition.IsCredential;

    public IEnumerable<string> Choices => Definition.IsSwitch ? new[] { "True", "False" } : Definition.ValidValues;

    public string Constraint
    {
        get
        {
            if (Definition.Minimum != null || Definition.Maximum != null)
            {
                return $"Range: {Definition.Minimum}–{Definition.Maximum}";
            }

            if (Definition.IsArray)
            {
                return "Separate values with commas or new lines";
            }

            return string.Empty;
        }
    }

    public bool IsBound
    {
        get => _isBound;
        set
        {
            if (Definition.IsMandatory)
            {
                value = true;
            }

            SetProperty(ref _isBound, value);
        }
    }

    public string ValueText
    {
        get => _valueText;
        set => SetProperty(ref _valueText, value);
    }

    public PSCredential? Credential
    {
        get => _credential;
        set
        {
            if (SetProperty(ref _credential, value))
            {
                RaisePropertyChanged(nameof(CredentialSummary));
                IsBound = value != null;
            }
        }
    }

    public string CredentialSummary => Credential == null ? "Not set" : Credential.UserName;

    public object? ConvertValue()
    {
        if (!IsBound)
        {
            return null;
        }

        if (Definition.IsCredential)
        {
            return Credential ?? throw new FormatException($"-{Name} requires a credential.");
        }

        var type = Definition.ParameterType;
        if (type == typeof(SwitchParameter))
        {
            return new SwitchParameter(ParseBoolean(ValueText));
        }

        if (type == typeof(bool))
        {
            return ParseBoolean(ValueText);
        }

        if (type.IsArray)
        {
            var elementType = type.GetElementType() ?? typeof(string);
            var tokens = ValueText.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .ToArray();
            if (tokens.Length == 0 && IsRequired)
            {
                throw new FormatException($"-{Name} requires at least one value.");
            }

            var array = Array.CreateInstance(elementType, tokens.Length);
            for (var index = 0; index < tokens.Length; index++)
            {
                ValidateSet(tokens[index]);
                array.SetValue(ConvertScalar(tokens[index], elementType), index);
            }

            return array;
        }

        if (type == typeof(Hashtable))
        {
            return ParseHashtable(ValueText);
        }

        ValidateSet(ValueText);
        var value = ConvertScalar(ValueText, type);
        ValidateRange(value);
        ValidateRegexIfNeeded(value);
        return value;
    }

    private object ConvertScalar(string text, Type type)
    {
        if (type == typeof(string)) return text;
        if (type == typeof(int)) return int.Parse(text, NumberStyles.Integer, CultureInfo.CurrentCulture);
        if (type == typeof(long)) return long.Parse(text, NumberStyles.Integer, CultureInfo.CurrentCulture);
        if (type == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal);
        if (type == typeof(Guid)) return Guid.Parse(text);
        if (type.IsEnum) return Enum.Parse(type, text, true);
        return Convert.ChangeType(text, type, CultureInfo.CurrentCulture);
    }

    private static bool ParseBoolean(string text)
    {
        if (!bool.TryParse(text, out var value))
        {
            throw new FormatException("Boolean values must be True or False.");
        }

        return value;
    }

    private void ValidateSet(string text)
    {
        if (Definition.ValidValues.Count > 0 &&
            !Definition.ValidValues.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            throw new FormatException($"-{Name} must be one of: {string.Join(", ", Definition.ValidValues)}.");
        }
    }

    private void ValidateRange(object value)
    {
        if (Definition.Minimum == null && Definition.Maximum == null)
        {
            return;
        }

        var numeric = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        if (Definition.Minimum != null && numeric < Convert.ToDecimal(Definition.Minimum, CultureInfo.InvariantCulture) ||
            Definition.Maximum != null && numeric > Convert.ToDecimal(Definition.Maximum, CultureInfo.InvariantCulture))
        {
            throw new FormatException($"-{Name} must be in the range {Definition.Minimum}–{Definition.Maximum}.");
        }
    }

    private void ValidateRegexIfNeeded(object value)
    {
        if ((Name == "Title" || Name == "NotTitle") && value is string pattern && pattern.Length > 0)
        {
            _ = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
        }
    }

    private static Hashtable ParseHashtable(string text)
    {
        var table = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in text.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator < 1)
            {
                throw new FormatException("Hashtable values use Key=Value pairs separated by semicolons or new lines.");
            }

            var key = pair.Substring(0, separator).Trim();
            var value = pair.Substring(separator + 1).Trim();
            table[key] = value;
        }

        return table;
    }
}
