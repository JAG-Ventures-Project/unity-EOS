using System;

namespace EOS
{
    public enum NotificationType
    {
        Normal,
        Warning,
        Error
    }

    internal interface INotification
    {
        void ShowNotification(string message, NotificationType type = NotificationType.Normal);
    }
}