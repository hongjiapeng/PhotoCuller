namespace PhotoCuller.Core.Interfaces.Platform;

/// <summary>In-app notifications (Toast / SnackBar); implemented by the WinUI layer.</summary>
public interface INotificationService
{
    void ShowSuccess(string message);
    void ShowWarning(string message);
    void ShowError(string message);
}
