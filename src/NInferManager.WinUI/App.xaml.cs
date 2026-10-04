using Microsoft.UI.Xaml;
using System.IO.Pipes;

namespace NInferManager.WinUI;

public partial class App : Application
{
    private const string InstanceMutexName=@"Local\NInferEZ.Manager.SingleInstance.v1";
    private const string ActivationPipeName="NInferEZ.Manager.Activation.v1";
    private Window? _window;
    private Mutex? _instanceMutex;
    private readonly CancellationTokenSource _activationLifetime=new();
    public App()
    {
        UnhandledException += (_, e) =>
        {
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "NInferEZ-Manager-crash.txt"), e.Exception.ToString()); }
            catch { }
        };
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _instanceMutex=new Mutex(true,InstanceMutexName,out var isPrimaryInstance);
            if(!isPrimaryInstance)
            {
                NotifyPrimaryInstance();
                _instanceMutex.Dispose();
                _instanceMutex=null;
                Exit();
                return;
            }
            _window = new MainWindow();
            _window.Closed+=(_,_)=>{_activationLifetime.Cancel();try{_instanceMutex?.ReleaseMutex();}catch(ApplicationException){}finally{_instanceMutex?.Dispose();_instanceMutex=null;}};
            _=ListenForActivationAsync();
            _window.Activate();
        }
        catch (Exception exception)
        {
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "NInferEZ-Manager-crash.txt"), exception.ToString()); }
            catch { }
            throw;
        }
    }

    private static void NotifyPrimaryInstance()
    {
        for(var attempt=0;attempt<12;attempt++)
        {
            try
            {
                using var client=new NamedPipeClientStream(".",ActivationPipeName,PipeDirection.Out);
                client.Connect(150);
                return;
            }
            catch(TimeoutException){Thread.Sleep(50);}
            catch(IOException){Thread.Sleep(50);}
        }
    }

    private async Task ListenForActivationAsync()
    {
        while(!_activationLifetime.IsCancellationRequested)
        {
            try
            {
                await using var server=new NamedPipeServerStream(ActivationPipeName,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_activationLifetime.Token);
                if(_window is MainWindow mainWindow)
                    mainWindow.DispatcherQueue.TryEnqueue(mainWindow.ActivateFromSecondaryInstance);
            }
            catch(ObjectDisposedException){return;}
            catch(OperationCanceledException){return;}
            catch
            {
                try{await Task.Delay(250,_activationLifetime.Token);}catch(OperationCanceledException){return;}
            }
        }
    }
}
