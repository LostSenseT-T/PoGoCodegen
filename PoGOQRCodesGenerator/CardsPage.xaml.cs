using System.Globalization;
using CommunityToolkit.Maui.Storage;
using PoGOQRCodesGenerator.Models;
using PoGOQRCodesGenerator.Printing;
using SkiaSharp;

namespace PoGOQRCodesGenerator;

public partial class CardsPage : ContentPage
{
    private readonly TemplateStore store = new(Path.Combine(FileSystem.AppDataDirectory, "card-templates"));
    private CardTemplate template = new();
    private byte[] defaultFont = [];
    private bool ready, updating, busy, active;
    private CancellationTokenSource? previewCancellation;
    private CancellationTokenSource? exportCancellation;
    private double panX, panY, panWidth, panHeight;

    public CardsPage()
    {
        InitializeComponent();
        BindingContext = BatchInput.Shared;
        ExportModePicker.SelectedIndex = 0;
        CardSidePicker.SelectedIndex = 1;
        DragTargetPicker.SelectedIndex = 0;
        ApplyTemplate(template);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        active = true;
        try
        {
            if (!ready)
            {
                using Stream stream = await FileSystem.OpenAppPackageFileAsync("PrintFont.ttf");
                using var bytes = new MemoryStream();
                await stream.CopyToAsync(bytes);
                defaultFont = bytes.ToArray();
                await ReloadTemplatesAsync();
                ready = true;
            }
            OnPageSizeChanged(this, EventArgs.Empty);
            QueuePreview();
        }
        catch (Exception ex) { await DisplayAlert("Could not load print settings", ex.Message, "OK"); }
    }

    protected override void OnDisappearing()
    {
        active = false;
        previewCancellation?.Cancel();
        base.OnDisappearing();
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (!ready) return;
        bool narrow = Width < 850;
        WorkspaceGrid.ColumnSpacing = narrow ? 0 : 24;
        WorkspaceGrid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : GridLength.Star;
        Grid.SetColumn(PreviewPanel, narrow ? 0 : 1);
        Grid.SetRow(PreviewPanel, narrow ? 1 : 0);
        double width = Math.Clamp(Width - 64, 160, 330);
        CardPreviewSurface.WidthRequest = width;
        CardPreviewSurface.HeightRequest = width * 4 / 3;
    }

    private void ApplyTemplate(CardTemplate value)
    {
        updating = true;
        try
        {
            template = value;
            TemplateNameEntry.Text = value.Name;
            Set(QrLeftEntry, value.QrLeft * 100); Set(QrTopEntry, value.QrTop * 100); Set(QrWidthEntry, value.QrWidth * 100);
            QrSizeSlider.Value = Math.Clamp(value.QrWidth * 100, 10, 100);
            Set(DateLeftEntry, value.DateLeft * 100); Set(DateTopEntry, value.DateTop * 100);
            Set(DateWidthEntry, value.DateWidth * 100); Set(DateHeightEntry, value.DateHeight * 100);
            Set(FontSizeEntry, value.DateFontSize);
            DateColorEntry.Text = value.DateColor; QrColorEntry.Text = value.QrColor;
            DatePrefixEntry.Text = value.DatePrefix;
            DateFormatPicker.SelectedItem = value.DateFormat;
            AlignmentPicker.SelectedIndex = (int)value.DateAlignment;
            Set(ColumnsEntry, value.Layout.Columns); Set(RowsEntry, value.Layout.Rows);
            Set(CardWidthEntry, value.Layout.CardWidthMm); Set(GapEntry, value.Layout.GapMm);
            Set(OffsetXEntry, value.Layout.BackOffsetXmm); Set(OffsetYEntry, value.Layout.BackOffsetYmm);
            CropMarksCheck.IsChecked = value.Layout.CropMarks;
            FlipPicker.SelectedIndex = (int)value.Layout.FlipEdge;
            FrontFileLabel.Text = value.FrontImage is null ? "No front panel loaded" : "Front panel loaded";
            BackFileLabel.Text = value.BackImage is null ? "No back panel loaded" : "Back panel loaded";
            FontLabel.Text = "Font: " + value.FontName;
        }
        finally { updating = false; }
        QueuePreview();
    }

    private CardTemplate ReadTemplate(bool requireImages)
    {
        var result = template with
        {
            Name = TemplateNameEntry.Text?.Trim() ?? "",
            QrLeft = Number(QrLeftEntry, "QR left") / 100,
            QrTop = Number(QrTopEntry, "QR top") / 100,
            QrWidth = Number(QrWidthEntry, "QR size") / 100,
            DateLeft = Number(DateLeftEntry, "Date left") / 100,
            DateTop = Number(DateTopEntry, "Date top") / 100,
            DateWidth = Number(DateWidthEntry, "Date width") / 100,
            DateHeight = Number(DateHeightEntry, "Date height") / 100,
            DateFontSize = Number(FontSizeEntry, "Font size"),
            DatePrefix = DatePrefixEntry.Text ?? "",
            DateFormat = DateFormatPicker.SelectedItem as string ?? "dd MMM",
            DateColor = DateColorEntry.Text?.Trim() ?? "",
            QrColor = QrColorEntry.Text?.Trim() ?? "",
            DateAlignment = (DateAlignment)AlignmentPicker.SelectedIndex,
            Layout = new PrintLayout
            {
                Columns = Integer(ColumnsEntry, "Columns"), Rows = Integer(RowsEntry, "Rows"),
                CardWidthMm = Number(CardWidthEntry, "Card width"), GapMm = Number(GapEntry, "Gap"),
                FlipEdge = (FlipEdge)FlipPicker.SelectedIndex,
                BackOffsetXmm = Number(OffsetXEntry, "Back shift X"), BackOffsetYmm = Number(OffsetYEntry, "Back shift Y"),
                CropMarks = CropMarksCheck.IsChecked
            }
        };
        result.Validate(requireImages);
        return result;
    }

    private static double Number(Entry entry, string name)
    {
        if (!double.TryParse(entry.Text?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            throw new ArgumentException($"Enter a number for {name}.");
        return value;
    }

    private static int Integer(Entry entry, string name)
    {
        double value = Number(entry, name);
        if (value < 1 || value > 12 || value != Math.Truncate(value))
            throw new ArgumentException($"{name} must be a whole number between 1 and 12.");
        return (int)value;
    }

    private static void Set(Entry entry, double value) => entry.Text = value.ToString("0.###", CultureInfo.InvariantCulture);
    private void OnSettingChanged(object? sender, EventArgs e) => QueuePreview();

    private async void QueuePreview()
    {
        if (!ready || updating || busy || !active) return;
        previewCancellation?.Cancel();
        previewCancellation?.Dispose();
        previewCancellation = new CancellationTokenSource();
        CancellationToken token = previewCancellation.Token;
        try
        {
            await Task.Delay(250, token);
            CardTemplate snapshot = ReadTemplate(false);
            List<string> codes = PromoCodes.Parse(BatchInput.Shared.Codes);
            bool sample = codes.Count == 0;
            if (sample) codes.Add("3AUE8C9XLPCVY");
            List<string> urls = codes.Select(code => PromoCodes.CreateUrl(BatchInput.Shared.UrlTemplate, code)).ToList();
            PdfExportMode mode = (PdfExportMode)ExportModePicker.SelectedIndex;
            IReadOnlyList<PrintPage> pages = snapshot.Layout.CreatePages(codes.Count, mode);
            int index = Math.Clamp(PagePicker.SelectedIndex, 0, pages.Count - 1);
            updating = true;
            try
            {
                PagePicker.ItemsSource = pages.Select((page, i) => mode == PdfExportMode.Duplex
                    ? $"Page {i + 1} · sheet {page.SheetIndex + 1} · {(page.IsBack ? "back" : "front")}" 
                    : page.IsBack ? $"backs.pdf · page {page.SheetIndex + 1}" : "front.pdf · reusable front").ToList();
                PagePicker.SelectedIndex = index;
            }
            finally { updating = false; }
            DateTime expiry = ExpiryPicker.Date;
            bool showBack = CardSidePicker.SelectedIndex == 1;
            var images = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using var renderer = new CardRenderer(snapshot, defaultFont, requireImages: false);
                byte[] card = renderer.RenderCardPreview(urls[0], expiry, showBack);
                token.ThrowIfCancellationRequested();
                byte[] sheet = renderer.RenderPagePreview(pages[index], urls, expiry);
                return (card, sheet);
            }, token);
            token.ThrowIfCancellationRequested();
            CardPreviewImage.Source = ImageSource.FromStream(() => new MemoryStream(images.card));
            SheetPreviewImage.Source = ImageSource.FromStream(() => new MemoryStream(images.sheet));
            int sheets = (codes.Count - 1) / snapshot.Layout.Capacity + 1;
            SummaryLabel.Text = sample ? "Sample preview — enter codes to export." :
                $"{codes.Count} unique codes · {sheets} sheets · {snapshot.Layout.Capacity} cards per sheet\n" +
                $"Card: {snapshot.Layout.CardWidthMm:0.##} × {snapshot.Layout.CardHeightMm:0.##} mm · Expiry: {snapshot.FormatDate(expiry)}";
            ValidationLabel.Text = snapshot.FrontImage is null || snapshot.BackImage is null
                ? "Load both panel images before exporting." : "";
            ExportButton.IsEnabled = !sample && snapshot.FrontImage is not null && snapshot.BackImage is not null;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            ValidationLabel.Text = ex.Message;
            ExportButton.IsEnabled = false;
            SheetPreviewImage.Source = null;
        }
    }

    private void OnQrSizeChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!ready || updating || busy) return;
        try
        {
            double left = Number(QrLeftEntry, "QR left") / 100;
            double top = Number(QrTopEntry, "QR top") / 100;
            double max = Math.Min(1 - left, (1 - top) * 4 / 3) * 100;
            Set(QrWidthEntry, Math.Max(1, Math.Min(e.NewValue, max)));
        }
        catch (ArgumentException) { Set(QrWidthEntry, e.NewValue); }
    }

    private void OnCardPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (!ready || busy || CardSidePicker.SelectedIndex != 1) return;
        bool qr = DragTargetPicker.SelectedIndex == 0;
        try
        {
            if (e.StatusType == GestureStatus.Started)
            {
                CardTemplate current = ReadTemplate(false);
                panX = qr ? current.QrLeft : current.DateLeft;
                panY = qr ? current.QrTop : current.DateTop;
                panWidth = qr ? current.QrWidth : current.DateWidth;
                panHeight = qr ? current.QrWidth * 3 / 4 : current.DateHeight;
            }
            else if (e.StatusType == GestureStatus.Running)
            {
                Set(qr ? QrLeftEntry : DateLeftEntry, Math.Clamp(panX + e.TotalX / CardPreviewSurface.Width, 0, 1 - panWidth) * 100);
                Set(qr ? QrTopEntry : DateTopEntry, Math.Clamp(panY + e.TotalY / CardPreviewSurface.Height, 0, 1 - panHeight) * 100);
            }
        }
        catch (ArgumentException ex) { ValidationLabel.Text = ex.Message; }
    }

    private async Task ReloadTemplatesAsync(string? selectedId = null)
    {
        var catalog = await store.LoadAsync();
        updating = true;
        try
        {
            TemplatePicker.ItemsSource = catalog.Templates;
            TemplatePicker.SelectedItem = catalog.Templates.FirstOrDefault(t => t.Id == selectedId);
        }
        finally { updating = false; }
        if (catalog.Errors.Count > 0)
            StatusLabel.Text = "Some saved templates could not be loaded:\n" + string.Join("\n", catalog.Errors);
    }

    private void OnTemplateSelected(object? sender, EventArgs e)
    {
        if (!updating && TemplatePicker.SelectedItem is CardTemplate selected) ApplyTemplate(selected);
    }

    private async void OnSaveTemplateClicked(object? sender, EventArgs e)
    {
        if (busy) return;
        try
        {
            CardTemplate snapshot = ReadTemplate(true);
            using (var renderer = new CardRenderer(snapshot, defaultFont)) { }
            SetBusy(true);
            await store.SaveAsync(snapshot);
            template = snapshot;
            await ReloadTemplatesAsync(snapshot.Id);
            StatusLabel.Text = $"Saved template: {snapshot.Name}. Images and font are stored with it.";
        }
        catch (Exception ex) { await DisplayAlert("Could not save template", ex.Message, "OK"); }
        finally { SetBusy(false); QueuePreview(); }
    }

    private void OnNewTemplateClicked(object? sender, EventArgs e)
    {
        TemplatePicker.SelectedIndex = -1;
        ApplyTemplate(new CardTemplate { Name = "New template" });
        StatusLabel.Text = "Choose panel images and save the new template.";
    }

    private async void OnPickFrontClicked(object? sender, EventArgs e) => await PickPanelAsync(false);
    private async void OnPickBackClicked(object? sender, EventArgs e) => await PickPanelAsync(true);

    private async Task PickPanelAsync(bool isBack)
    {
        if (busy) return;
        try
        {
            FileResult? file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = isBack ? "Choose the blank back panel" : "Choose the front panel",
                FileTypes = FilePickerFileType.Images
            });
            if (file is null) return;
            byte[] bytes = await ReadFileAsync(file, 20 * 1024 * 1024);
            using SKImage? image = CardRenderer.DecodeImage(bytes);
            template = isBack ? template with { BackImage = bytes } : template with { FrontImage = bytes };
            (isBack ? BackFileLabel : FrontFileLabel).Text = $"{file.FileName} · {image!.Width} × {image.Height} px";
            QueuePreview();
        }
        catch (Exception ex) { await DisplayAlert("Could not load panel", ex.Message, "OK"); }
    }

    private async void OnPickFontClicked(object? sender, EventArgs e)
    {
        try
        {
            FileResult? file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choose a TTF or OTF font",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.WinUI] = [".ttf", ".otf"],
                    [DevicePlatform.Android] = ["font/ttf", "font/otf", "application/octet-stream"],
                    [DevicePlatform.iOS] = ["public.truetype-ttf-font", "public.opentype-font"],
                    [DevicePlatform.MacCatalyst] = ["public.truetype-ttf-font", "public.opentype-font"]
                })
            });
            if (file is null) return;
            byte[] bytes = await ReadFileAsync(file, 10 * 1024 * 1024);
            using SKData data = SKData.CreateCopy(bytes);
            using SKTypeface face = SKTypeface.FromData(data) ?? throw new ArgumentException("Invalid font file.");
            template = template with { FontData = bytes, FontName = file.FileName };
            FontLabel.Text = "Font: " + file.FileName;
            QueuePreview();
        }
        catch (Exception ex) { await DisplayAlert("Could not load font", ex.Message, "OK"); }
    }

    private void OnResetFontClicked(object? sender, EventArgs e)
    {
        template = template with { FontData = null, FontName = "Open Sans" };
        FontLabel.Text = "Font: Open Sans";
        QueuePreview();
    }

    private static async Task<byte[]> ReadFileAsync(FileResult file, int maxBytes)
    {
        using Stream input = await file.OpenReadAsync();
        using var output = new MemoryStream();
        byte[] buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            if (output.Length + read > maxBytes) throw new ArgumentException($"Choose a file smaller than {maxBytes / 1024 / 1024} MB.");
            await output.WriteAsync(buffer.AsMemory(0, read));
        }
        return output.ToArray();
    }

    private async void OnExportClicked(object? sender, EventArgs e)
    {
        if (busy || !ready) return;
        var saved = new List<string>();
        try
        {
            CardTemplate snapshot = ReadTemplate(true);
            List<string> codes = PromoCodes.Parse(BatchInput.Shared.Codes);
            List<string> urls = codes.Select(code => PromoCodes.CreateUrl(BatchInput.Shared.UrlTemplate, code)).ToList();
            PdfExportMode mode = (PdfExportMode)ExportModePicker.SelectedIndex;
            IReadOnlyList<PrintPage> pages = snapshot.Layout.CreatePages(codes.Count, mode);
            DateTime expiry = ExpiryPicker.Date;
            exportCancellation = new CancellationTokenSource();
            CancellationToken token = exportCancellation.Token;
            previewCancellation?.Cancel();
            SetBusy(true);
            ExportProgress.IsVisible = CancelButton.IsVisible = true;
            ExportProgress.Progress = 0;
            StatusLabel.Text = "Generating print PDF…";
            var progress = new Progress<double>(value => ExportProgress.Progress = value);
            var outputs = await Task.Run(() =>
            {
                using var renderer = new CardRenderer(snapshot, defaultFont);
                if (mode == PdfExportMode.Duplex)
                    return new[] { (Name: "cards.pdf", Bytes: renderer.RenderPdf(pages, urls, expiry, token, progress)) };
                byte[] fronts = renderer.RenderPdf(pages.Where(p => !p.IsBack).ToArray(), urls, expiry, token);
                byte[] backs = renderer.RenderPdf(pages.Where(p => p.IsBack).ToArray(), urls, expiry, token, progress);
                return new[] { (Name: "front.pdf", Bytes: fronts), (Name: "backs.pdf", Bytes: backs) };
            }, token);
            foreach (var output in outputs)
            {
                token.ThrowIfCancellationRequested();
                StatusLabel.Text = "Save " + output.Name;
                using var stream = new MemoryStream(output.Bytes);
                FileSaverResult result = await FileSaver.Default.SaveAsync(output.Name, stream, token);
                if (!result.IsSuccessful) throw result.Exception ?? new OperationCanceledException("Saving canceled.");
                saved.Add(result.FilePath ?? output.Name);
            }
            StatusLabel.Text = $"Saved {codes.Count} cards. Print A4 at 100%, 300 dpi or higher, and flip on the {(snapshot.Layout.FlipEdge == FlipEdge.LongEdge ? "long" : "short")} edge.\n" + string.Join("\n", saved);
        }
        catch (OperationCanceledException) { StatusLabel.Text = "Export canceled." + SavedSuffix(saved); }
        catch (Exception ex)
        {
            StatusLabel.Text = "Export failed: " + ex.Message + SavedSuffix(saved);
            await DisplayAlert("Could not export PDF", ex.Message, "OK");
        }
        finally
        {
            exportCancellation?.Dispose(); exportCancellation = null;
            ExportProgress.IsVisible = CancelButton.IsVisible = false;
            SetBusy(false);
            QueuePreview();
        }
    }

    private static string SavedSuffix(List<string> saved) => saved.Count == 0 ? "" : "\nAlready saved:\n" + string.Join("\n", saved);
    private void OnCancelExportClicked(object? sender, EventArgs e) => exportCancellation?.Cancel();
    private void SetBusy(bool value)
    {
        busy = value;
        SettingsPanel.IsEnabled = !value;
        ExportButton.IsEnabled = !value;
        PagePicker.IsEnabled = CardSidePicker.IsEnabled = DragTargetPicker.IsEnabled = !value;
        CardPreviewSurface.InputTransparent = value;
    }
}
