using CommunityToolkit.Mvvm.ComponentModel;
using LockscreenGif.Models.Diagnostics;
using Microsoft.UI.Xaml;

namespace LockscreenGif.ViewModels;

/// <summary>WinUI status and expansion state, keeping raw telemetry one click deeper.</summary>
public sealed class DiagnosticFindingViewModel : ObservableObject
{
    private bool _isExpanded;

    public DiagnosticFindingViewModel(DiagnosticFinding finding, bool isFile = false)
    {
        Title = isFile ? FileLabel(finding.Title) : finding.Title;
        Detail = finding.Detail;
        Children = finding.Children.Select(child => new DiagnosticFindingViewModel(child, isFile: true)).ToArray();
        var warning = finding.Severity is "Warning" or "Error" || Children.Any(child => child.WarningVisibility == Visibility.Visible);
        SuccessVisibility = !warning && finding.Severity == "Success" ? Visibility.Visible : Visibility.Collapsed;
        WarningVisibility = warning ? Visibility.Visible : Visibility.Collapsed;
        InformationVisibility = !warning && finding.Severity != "Success" ? Visibility.Visible : Visibility.Collapsed;

        BadgeText =
            warning ? "ATTENTION"
            : finding.Severity == "Success" ? "PASS"
            : "INFO";

        var status =
            warning ? "Needs attention"
            : finding.Severity == "Success" ? "Passed"
            : "Information";
        StatusDescription = finding.Confidence == "Observed" ? status : $"{status} · {finding.Confidence}";
        AccessibleStatus = $"{status}: {Title}";

        // Separate human-readable summary from raw technical telemetry (GUIDs, hashes, ETW dumps)
        if (string.IsNullOrWhiteSpace(finding.Detail))
        {
            SummaryText = status;
            TechnicalDetail = "";
        }
        else
        {
            var splitIdx = finding.Detail.IndexOfAny(['\r', '\n']);
            if (splitIdx > 0)
            {
                SummaryText = finding.Detail.Substring(0, splitIdx).Trim();
                TechnicalDetail = finding.Detail.Substring(splitIdx).Trim();
            }
            else
            {
                SummaryText = finding.Detail;
                // If it looks like technical telemetry (hashes, GUIDs, paths), offer it as collapsible raw detail
                TechnicalDetail =
                    finding.Detail.Length > 80 || finding.Detail.Contains('{') || finding.Detail.Contains('\\') ? finding.Detail : "";
            }
        }
    }

    public string Title { get; }
    public string Detail { get; }
    public string SummaryText { get; }
    public string TechnicalDetail { get; }
    public string BadgeText { get; }
    public string StatusDescription { get; }
    public string AccessibleStatus { get; }
    public IReadOnlyList<DiagnosticFindingViewModel> Children { get; }
    public Visibility SuccessVisibility { get; }
    public Visibility WarningVisibility { get; }
    public Visibility InformationVisibility { get; }
    public Visibility TechnicalDetailVisibility => string.IsNullOrWhiteSpace(TechnicalDetail) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility GroupVisibility => Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LeafVisibility => Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    private static string FileLabel(string path)
    {
        var directory = Path.GetFileName(Path.GetDirectoryName(path));
        return string.IsNullOrWhiteSpace(directory) ? Path.GetFileName(path) : $"{directory} / {Path.GetFileName(path)}";
    }
}
