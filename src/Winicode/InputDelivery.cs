namespace Winicode;

internal enum DeliveryState { None, Complete, Partial }

internal readonly record struct InputDeliveryResult(int RequestedEvents,int SentEvents,int ErrorCode)
{
    internal DeliveryState State=>SentEvents<=0?DeliveryState.None:SentEvents==RequestedEvents?DeliveryState.Complete:DeliveryState.Partial;
    internal bool MayRetry=>State==DeliveryState.None;
}
