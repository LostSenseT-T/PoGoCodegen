using System.ComponentModel;
using System.Runtime.CompilerServices;
using PoGOQRCodesGenerator.Printing;

namespace PoGOQRCodesGenerator.Models;

public sealed class BatchInput : INotifyPropertyChanged
{
    public static BatchInput Shared { get; } = new();
    private string codes = string.Empty;
    private string urlTemplate = PromoCodes.DefaultUrlTemplate;
    public string Codes { get => codes; set => Set(ref codes, value ?? string.Empty); }
    public string UrlTemplate { get => urlTemplate; set => Set(ref urlTemplate, value ?? string.Empty); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
