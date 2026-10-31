# Copilot Instructions

## Project Guidelines
- Automatically confirm ColorPickerDialog OK on first appearance of MusicPage by waiting for StaffGraphicsView.Width > 0 (up to 1s), setting _isProgrammaticColorConfirm to true, calling Show() and Confirm(), then awaiting RegenerateNotesAsync() and StaffGraphicsView.Invalidate(), and finally clearing the flag.
- Automatically apply saved or default background color at app start in the MusicPage constructor before bindings: call DeploySavedPanelBackground to ensure the saved/default panel background is applied before the Practice page appears. If 'StaffPanelColor' exists, use it to set ThemeService.PanelBackgroundColor; otherwise, call ColorPickerDialog.ResetToDefaults(), read ColorPickerDialog.PreviewColor, set ThemeService.PanelBackgroundColor to that color, persist it to Preferences as 'StaffPanelColor', and set StaffBorder.Background accordingly. Regenerate notes on the UI thread and invalidate the staff view.
- Remove programmatic left margin and ScrollView padding from MusicPage.xaml.cs; prefer explicit gutter Grid column in XAML instead of programmatic padding.
- When searching in AboutPage, pause audio capture by calling IAudioCaptureService.StopCapture directly from AboutPage; do not use MessagingCenter or CommunityToolkit messenger; no automatic restart required. Note that playback silence may occur due to volume settings during rehearsal; do not assume missing audio always indicates playback failure.
- Approve automatic single-file replacement to remove duplicate method definitions in Pages\MusicPage.xaml.cs; prefer automated fixes applied when asked.
- Do not put passwords or IDs into the code; avoid storing credentials or secrets in source files.

## Development Environment
- Preferred terminal shell: pwsh.exe
- IDE: Microsoft Visual Studio Professional 2026 (18.4.3)