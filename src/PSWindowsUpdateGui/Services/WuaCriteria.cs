using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PSWindowsUpdateGui.Models;

namespace PSWindowsUpdateGui.Services;

internal static class WuaCriteria
{
    private static readonly Regex SafeCharacters = new Regex(
        "^[A-Za-z0-9_{}'\\-\\s().=<>!]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PropertyToken = new Regex(
        @"(?i)\b([A-Za-z][A-Za-z0-9_]*)\s*(?:=|!=|<=|>=|<|>)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly ISet<string> AllowedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AutoSelectOnWebSites", "BrowseOnly", "CategoryIDs", "DeploymentAction", "IsAssigned",
        "IsHidden", "IsInstalled", "IsPresent", "IsUninstallable", "RebootRequired", "RevisionNumber",
        "Type", "UpdateID"
    };

    public static string Build(ScanRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (!string.IsNullOrWhiteSpace(request.Criteria))
        {
            Validate(request.Criteria);
            return request.Criteria.Trim();
        }

        var parts = new List<string>();
        if (!request.IncludeInstalled) parts.Add("IsInstalled=0");
        if (!request.IncludeHidden) parts.Add("IsHidden=0");
        if (request.Type == UpdateKind.Software) parts.Add("Type='Software'");
        if (request.Type == UpdateKind.Driver) parts.Add("Type='Driver'");
        return parts.Count == 0 ? "Type='Software' or Type='Driver'" : string.Join(" and ", parts);
    }

    public static void Validate(string criteria)
    {
        if (string.IsNullOrWhiteSpace(criteria)) throw new FormatException("Criteria cannot be empty.");
        if (criteria.Length > 2048) throw new FormatException("Criteria cannot exceed 2,048 characters.");
        if (criteria.IndexOfAny(new[] { '\0', '\r', '\n', ';', '`' }) >= 0 || !SafeCharacters.IsMatch(criteria))
            throw new FormatException("Criteria contains unsupported characters.");

        var properties = PropertyToken.Matches(criteria).Cast<Match>().Select(match => match.Groups[1].Value).ToList();
        if (properties.Count == 0) throw new FormatException("Criteria must contain at least one supported comparison.");
        var unsupported = properties.FirstOrDefault(property => !AllowedProperties.Contains(property));
        if (unsupported != null) throw new FormatException($"Unsupported WUA criteria property: {unsupported}.");

        var scrubbed = PropertyToken.Replace(criteria, string.Empty);
        scrubbed = Regex.Replace(scrubbed, "(?i)\\b(and|or)\\b", string.Empty);
        scrubbed = Regex.Replace(scrubbed, "'[A-Za-z0-9_{}\\-. ]*'|[0-9]+|[()\\s]", string.Empty);
        if (scrubbed.Length != 0) throw new FormatException("Criteria contains an unsupported token or operator.");
    }

    public static void ValidateRequest(ScanRequest request)
    {
        if (!Enum.IsDefined(typeof(UpdateSourceKind), request.Source) || !Enum.IsDefined(typeof(UpdateKind), request.Type))
            throw new FormatException("The update source or type is invalid.");
        if (request.TimeoutSeconds < 5 || request.TimeoutSeconds > 86400)
            throw new FormatException("Timeout must be between 5 and 86,400 seconds.");
        if ((request.Source == UpdateSourceKind.Service || request.Source == UpdateSourceKind.Offline) &&
            string.IsNullOrWhiteSpace(request.ServiceId) && request.Source == UpdateSourceKind.Service)
            throw new FormatException("A service ID is required for the selected source.");
        if (request.Source == UpdateSourceKind.Offline && string.IsNullOrWhiteSpace(request.OfflineCabPath))
            throw new FormatException("An offline scan CAB path is required.");
        if (request.TitlePattern.Length > 512) throw new FormatException("The title pattern cannot exceed 512 characters.");
        if (!string.IsNullOrWhiteSpace(request.TitlePattern))
            _ = new Regex(request.TitlePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        _ = Build(request);
    }
}
