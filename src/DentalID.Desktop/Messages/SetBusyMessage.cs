using CommunityToolkit.Mvvm.Messaging.Messages;

namespace DentalID.Desktop.Messages;

public class SetBusyMessage : ValueChangedMessage<bool>
{
    public string Message { get; }

    public SetBusyMessage(bool isBusy, string message = "Loading...") : base(isBusy)
    {
        Message = message;
    }
}
