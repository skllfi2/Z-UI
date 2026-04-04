using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;

namespace ZUI.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    private DispatcherQueue? _dispatcherQueue;

    public void SetDispatcherQueue(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    protected void RunOnUIThread(Action action)
    {
        if (_dispatcherQueue != null)
            _dispatcherQueue.TryEnqueue(() => action());
        else
            action();
    }

    protected async Task RunOnUIThreadAsync(Func<Task> action)
    {
        var tcs = new TaskCompletionSource();
        if (_dispatcherQueue != null)
        {
            _dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
        }
        else
        {
            await action();
        }
        await tcs.Task;
    }
}
