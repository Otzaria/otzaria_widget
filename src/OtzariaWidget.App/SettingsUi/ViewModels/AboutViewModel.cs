using System.IO;
using System.Windows.Input;
using OtzariaWidget.App.SettingsUi.Infrastructure;
using OtzariaWidget.App.SettingsUi.Localization;

namespace OtzariaWidget.App.SettingsUi.ViewModels;

/// <summary>Page 5 - "אודות".</summary>
public sealed class AboutViewModel : ObservableObject
{
    public const string TestLinkUri = "otzaria://open/history";

    private readonly SettingsServices _svc;
    private StatusMessage? _message;

    public AboutViewModel(SettingsServices svc)
    {
        _svc = svc;
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
        TestLinkCommand = new RelayCommand(TestLink);
    }

    public string Title => Loc.T("אודות");
    public string Version => AppEnvironment.Version;
    public string LogFolder => AppEnvironment.RealDataFolder;

    public string SettingsFilePath
    {
        get
        {
            try { return _svc.Store.SettingsFilePath; }
            catch { return Loc.T("לא זמין"); }
        }
    }

    public ICommand OpenLogFolderCommand { get; }
    public ICommand TestLinkCommand { get; }

    public StatusMessage? Message { get => _message; private set => Set(ref _message, value); }

    private void OpenLogFolder()
    {
        try
        {
            var dir = AppEnvironment.RealDataFolder;
            Directory.CreateDirectory(dir);
            _svc.Shell.OpenFolder(dir);
            Message = null;
        }
        catch (Exception ex)
        {
            SettingsLog.Error("Open log folder failed", ex);
            Message = new StatusMessage(Loc.F("לא ניתן לפתוח את התיקייה: {0}", ex.Message), StateKind.Error);
        }
    }

    private void TestLink()
    {
        try
        {
            _svc.Shell.OpenUri(TestLinkUri);
            Message = new StatusMessage(Loc.T("הקישור נשלח. אם אוצריא נפתחה (בדף ההיסטוריה), הקישורים מהווידג'טים יעבדו."), StateKind.Ok);
        }
        catch (Exception ex)
        {
            SettingsLog.Error("Test link failed", ex);
            Message = new StatusMessage(Loc.T("לא נמצאה תוכנה שמטפלת בקישורי otzaria://. ייתכן שאוצריא אינה מותקנת או לא הופעלה אף פעם."), StateKind.Error);
        }
    }
}
