using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Get Premium on About, and the Premium popup from a premium key such as E,
/// must keep the prompt open and revert the picker when Billing does not start.
/// </summary>
public class PremiumBillingFlashRegressionTests
{
    [Fact]
    public void PremiumProductId_IsMusicMatePremium()
    {
        Assert.Equal("music_mate_premium", PremiumProduct.Id);
    }

    [Theory]
    [InlineData(true, false, null, false)]
    [InlineData(false, true, null, false)]
    [InlineData(false, false, "Google Play Billing is not ready.", true)]
    [InlineData(false, false, null, false)]
    public void StorePurchaseResult_ShouldShowError_OnlyForFailedPurchasesWithMessage(
        bool succeeded, bool canceled, string? message, bool expected)
    {
        var result = new StorePurchaseResult(succeeded, canceled, message);
        Assert.Equal(expected, result.ShouldShowError);
    }

    [Fact]
    public void MusicPage_TreatsEAsPremiumKey()
    {
        string text = File.ReadAllText(FindRepoFile(Path.Combine("Pages", "MusicPage.xaml.cs")));

        Assert.Contains("private static readonly HashSet<string> FreeKeys = new() { \"C\", \"F\", \"Bb\", \"G\", \"D\" };", text);
        Assert.Contains("private bool IsPremiumKey(string item) => !FreeKeys.Contains(item);", text);
        Assert.Contains("OnKeyPickerChangedWithPrompt", text);
        Assert.Contains("PremiumPromptHelper.ShowAsync", text);
    }

    [Fact]
    public void KeyPickerChange_RevertsSelection_WhenPurchaseFails()
    {
        string text = File.ReadAllText(FindRepoFile(Path.Combine("Pages", "MusicPage.xaml.cs")));
        int prompt = text.IndexOf("async void OnKeyPickerChangedWithPrompt", StringComparison.Ordinal);
        Assert.True(prompt >= 0);
        string method = text[prompt..Math.Min(text.Length, prompt + 2000)];

        Assert.Contains("onDecline: () => KeyPicker.SelectedIndex = _lastFreeKeyIndex", method);
        int failedPurchase = method.IndexOf("if (!purchased)", StringComparison.Ordinal);
        Assert.True(failedPurchase > 0);
        Assert.Contains("KeyPicker.SelectedIndex = _lastFreeKeyIndex", method[failedPurchase..]);
    }

    [Fact]
    public void PremiumPopup_KeepsOpen_WhenPurchaseReturnsFalse()
    {
        string text = File.ReadAllText(FindRepoFile(Path.Combine("Views", "PremiumPopup.cs")));
        int buy = text.IndexOf("private async void OnBuyPremiumClicked", StringComparison.Ordinal);
        int next = text.IndexOf("private async void OnDeclineClicked", buy + 1, StringComparison.Ordinal);
        Assert.True(buy >= 0 && next > buy);
        string method = text[buy..next];

        Assert.Contains("PurchaseAsync(PremiumProduct.Id)", method);
        Assert.Contains("if (result.Succeeded)", method);
        Assert.Contains("ShowError", method);
        int succeededIf = method.IndexOf("if (result.Succeeded)", StringComparison.Ordinal);
        int close = method.IndexOf("await SafeCloseAsync();", StringComparison.Ordinal);
        Assert.True(succeededIf >= 0 && close > succeededIf);
        Assert.Equal(close, method.LastIndexOf("await SafeCloseAsync();", StringComparison.Ordinal));
    }

    [Fact]
    public void PremiumPromptHelper_DismissesPickerBeforePopup()
    {
        string text = File.ReadAllText(FindRepoFile(Path.Combine("Utilities", "PremiumPromptHelper.cs")));

        Assert.Contains("DismissBlockingPickerAsync", text);
        Assert.Contains("PopModalAsync", text);
        Assert.Contains("await store.CheckPremiumStatusAsync()", text);
        Assert.Contains("await page.ShowPopupAsync(popup);", text);
    }

    [Fact]
    public void AboutGetPremium_SurfacesBillingFailure()
    {
        string vm = File.ReadAllText(FindRepoFile(Path.Combine("ViewModels", "AboutPageViewModel.cs")));
        string xaml = File.ReadAllText(FindRepoFile(Path.Combine("Pages", "AboutPage.xaml")));

        Assert.Contains("Text=\"Get Premium\"", xaml);
        Assert.Contains("Command=\"{Binding BuyPremiumCommand}\"", xaml);
        Assert.Contains("CheckPremiumStatusAsync()", vm);
        Assert.Contains("PurchaseAsync(PremiumProduct.Id)", vm);
        Assert.Contains("ShowPurchaseAlertAsync", vm);
        Assert.Contains("DisplayAlertAsync", vm);
        Assert.Contains("result.ShouldShowError", vm);
        Assert.Contains("BillingUiThread.InvokeAsync", vm);
    }

    [Fact]
    public void PlayRelease_RegistersGooglePlayBilling_NotLocalStub()
    {
        string text = File.ReadAllText(FindRepoFile("MauiProgram.cs"));

        Assert.Contains("builder.Services.AddSingleton<IStoreService, musicmate.Platforms.Android.GooglePlayStoreService>();", text);
        Assert.Contains("#if DEBUG || LOCAL_RELEASE", text);
        Assert.Contains("AddSingleton<IStoreService, LocalStoreService>()", text);
        Assert.Contains("#else", text);
        Assert.Contains("#if ANDROID", text);
    }

    [Fact]
    public void PlayRelease_DoesNotExposeDebugOnlyPremiumControls()
    {
        string vm = File.ReadAllText(FindRepoFile(Path.Combine("ViewModels", "AboutPageViewModel.cs")));
        string aboutXaml = File.ReadAllText(FindRepoFile(Path.Combine("Pages", "AboutPage.xaml")));
        string popup = File.ReadAllText(FindRepoFile(Path.Combine("Views", "PremiumPopup.cs")));

        Assert.Contains("IsVisible=\"{Binding IsDebugRestoreVisible}\"", aboutXaml);
        Assert.Contains("Text=\"🔄 Remove Premium (debug)\"", aboutXaml);
        Assert.Contains("public bool IsDebugRestoreVisible =>", vm);
        Assert.Contains("#if DEBUG || LOCAL_RELEASE", vm);
        Assert.Contains("false;", vm.Split("public bool IsDebugRestoreVisible =>")[1].Split("#else")[1]);

        Assert.Contains("#if DEBUG || LOCAL_RELEASE", popup);
        Assert.Contains("Restore Purchases (debug)", popup);
        Assert.DoesNotContain("IsDebugRestoreVisible => true", vm);
    }

    [Fact]
    public void GooglePlayStoreService_ReturnsFailedResult_WhenProductDetailsMissingOrActivityNull()
    {
        string text = File.ReadAllText(FindRepoFile(Path.Combine("Platforms", "Android", "GooglePlayStoreService.cs")));

        Assert.Contains("if (products.Count == 0)", text);
        Assert.Contains("StorePurchaseResult.Failed", text);
        Assert.Contains("if (activity == null)", text);
        Assert.Contains("LaunchBillingFlow(activity, flowParams)", text);
        Assert.Contains("BillingUiThread.InvokeAsync", text);
        Assert.Contains("already owned — granting Premium without billing flow", text);
        Assert.Contains("StorePurchaseResult.UserCanceled()", text);
        Assert.Contains("BillingClientConnectionGate", text);
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relativePath} from {AppContext.BaseDirectory}");
    }
}
