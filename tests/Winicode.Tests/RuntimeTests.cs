namespace Winicode;

internal static class RuntimeTests
{
    internal static IReadOnlyList<string> Run()
    {
        var failures=new List<string>();
        if(new InputDeliveryResult(4,0,5).State!=DeliveryState.None)failures.Add("Zero events must be None");
        if(new InputDeliveryResult(4,2,0).State!=DeliveryState.Partial)failures.Add("Partial events must not be complete");
        if(new InputDeliveryResult(4,2,0).MayRetry)failures.Add("Partial input must not be automatically retried");
        if(new InputDeliveryResult(4,4,0).State!=DeliveryState.Complete)failures.Add("All events must be complete");
        if(string.IsNullOrWhiteSpace(InstanceChannel.MutexName))failures.Add("Single instance must have a user-scoped identity");
        return failures;
    }
}
