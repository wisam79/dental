using CommunityToolkit.Mvvm.Messaging.Messages;
using DentalID.Desktop.Services;

namespace DentalID.Desktop.Messages;

public class ShowToastMessage : ValueChangedMessage<(string Title, string Message, ToastType Type)>
{
    public ShowToastMessage(string title, string message, ToastType type) : base((title, message, type))
    {
    }
}
