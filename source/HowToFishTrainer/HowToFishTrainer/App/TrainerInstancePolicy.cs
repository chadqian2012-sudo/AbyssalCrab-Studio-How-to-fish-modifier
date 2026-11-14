using System.Diagnostics;
using System.Threading;

namespace HowToFishTrainer.App;

internal static class TrainerInstancePolicy
{
    internal static EventWaitHandle? OpenOwnerEvent(string eventPrefix, out int ownerPid)
    {
        ownerPid = 0;
        EventWaitHandle? activation = null;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (activation == null && EventWaitHandle.TryOpenExisting($"{eventPrefix}.{process.Id}", out activation))
                    ownerPid = process.Id;
            }
        }
        return activation;
    }
}
