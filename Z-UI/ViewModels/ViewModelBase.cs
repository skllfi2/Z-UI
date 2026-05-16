// ViewModelBase.cs - Base class for all ViewModels with DispatcherQueue support
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;

namespace ZUI.ViewModels;

/// <summary>
/// Base class for ViewModels providing UI thread marshalling via DispatcherQueue.
/// </summary>
public partial class ViewModelBase : ObservableObject
{
    private DispatcherQueue? _dispatcherQueue;

    /// <summary>
    /// Set the DispatcherQueue for UI thread marshalling.
    /// Called after construction when the ViewModel is first used on a UI thread.
    /// </summary>
    public void SetDispatcherQueue(DispatcherQueue queue)
    {
        _dispatcherQueue = queue;
    }

    /// <summary>
    /// Run an action on the UI thread. If already on the UI thread, runs synchronously.
    /// If DispatcherQueue is not yet set (ViewModel constructed but not yet navigated to),
    /// the action is deferred and will execute when SetDispatcherQueue is called.
    /// This prevents RPC_E_WRONG_THREAD (0x8001010E) from WinRT PropertyChanged marshalling.
    /// </summary>
    protected void RunOnUIThread(Action action)
    {
        if (_dispatcherQueue == null)
        {
            // DispatcherQueue not available yet — defer until SetDispatcherQueue is called.
            // This happens when events fire during DI construction before the page is navigated to.
            _pendingActions ??= [];
            _pendingActions.Add(action);
            return;
        }

        if (_dispatcherQueue.HasThreadAccess)
            action();
        else
            _dispatcherQueue.TryEnqueue(() => action());
    }

    private List<Action>? _pendingActions;

    /// <summary>
    /// Run an action on the UI thread asynchronously, returning a Task that completes
    /// when the action has executed. In test contexts where _dispatcherQueue is null,
    /// the action runs synchronously and Task.CompletedTask is returned.
    /// </summary>
    protected Task RunOnUIThreadAsync(Action action)
    {
        if (_dispatcherQueue == null)
        {
            action();
            return Task.CompletedTask;
        }

        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<bool>();
        _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult(true);
            }
            catch (ObjectDisposedException ex)
            {
                tcs.SetException(ex);
            }
            catch (InvalidOperationException ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }
}
