using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;


namespace Common;

public class Status {
    static int messageIndex = 0;
    public static int defaultDelay = 3000;
    public static TextBlock statusBar = null;
    public static DispatcherQueue dispatcherQueue = null;

    static public void AddMessage(string message, int? delay = null){
        delay ??= defaultDelay;
        Task.Run(async() => {
            messageIndex++;
            int messageIdx = messageIndex;
            SetStatusBar(message);
            await Task.Delay((int)delay);
            if(messageIdx == messageIndex){
                SetStatusBar("");
            }
        });
    }

    // UI コントロール部
    public static void SetStatusBar(string message){
        if (dispatcherQueue.HasThreadAccess)
        {
            statusBar.Text = message;
        }
        else
        {
            dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () => {
                statusBar.Text = message;
            });
        }
    }
}