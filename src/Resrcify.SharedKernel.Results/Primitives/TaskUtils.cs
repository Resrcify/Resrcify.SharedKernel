using System.Threading.Tasks;

namespace Resrcify.SharedKernel.Results.Primitives;

internal static class TaskUtils
{
    public static bool TryGetResult<T>(Task<T> task, out T result)
    {
        if (task.IsCompletedSuccessfully)
        {
            result = task.GetAwaiter().GetResult();
            return true;
        }

        result = default!;
        return false;
    }
}
