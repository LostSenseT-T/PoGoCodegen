using CommunityToolkit.Maui.Storage;
using QRCoder;
using PoGOQRCodesGenerator.Models;
using PoGOQRCodesGenerator.Printing;

namespace PoGOQRCodesGenerator;

public partial class MainPage
{
    public MainPage()
    {
        InitializeComponent();
        BindingContext = BatchInput.Shared;
    }

    private async void OnSaveQrClicked(object sender, EventArgs e)
    {
        StatusLabel.Text = string.Empty;

        string baseUrlTemplate = BaseUrlEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(baseUrlTemplate))
        {
            await DisplayAlert("Failed", "Base URL empty.", "OK");
            return;
        }

        List<string> codes = PromoCodes.Parse(CodesEditor.Text);

        if (codes.Count == 0)
        {
            await DisplayAlert("Error", "Can't find any codes.", "OK");
            return;
        }

        CancellationTokenSource cts = new();
        CancellationToken token = cts.Token;

        FolderPickerResult folderResult = await FolderPicker.Default.PickAsync(token);
        if (!folderResult.IsSuccessful || folderResult.Folder is null)
        {
            await DisplayAlert("Canceled", "Select the folder.", "OK");
            return;
        }

        string folderPath = folderResult.Folder.Path;

        int success = 0;
        int fail = 0;

        using QRCodeGenerator qrGenerator = new();

        foreach (string code in codes)
        {
            string url = baseUrlTemplate.Replace("{code}", Uri.EscapeDataString(code));

            try
            {
                using QRCodeData qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
                PngByteQRCode pngQr = new(qrData);
                byte[] pngBytes = pngQr.GetGraphic(
                    20,
                    darkColor: System.Drawing.Color.FromArgb(0x26, 0x13, 0x3A),
                    lightColor: System.Drawing.Color.FromArgb(0xFF, 0xFF, 0xFF),
                    drawQuietZones: true
                );

                string fileName = $"{code}.png";
                string filePath = Path.Combine(folderPath, fileName);

                await File.WriteAllBytesAsync(filePath, pngBytes, token);

                success++;
            }
            catch
            {
                fail++;
            }
        }

        StatusLabel.Text =
            $"Done. Saved: {success}, errors: {fail}.\nFolder: {folderPath}";
    }
}
