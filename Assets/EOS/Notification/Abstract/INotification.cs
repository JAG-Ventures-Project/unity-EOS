using System;

namespace EOS.Notification
{
    public enum NotificationType
    {
        Normal,
        Warning,
        Error
    }

    public interface INotification
    {
        void ShowNotification(string message, NotificationType type = NotificationType.Normal, Action<bool> onResult = null);
    }
}