using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Unity.Collections;
using Unity.Profiling;
using UnityEngine;
using UnityWebBrowser.Communication;
using UnityWebBrowser.Core.Engines;
using UnityWebBrowser.Events;
using UnityWebBrowser.Helper;
using UnityWebBrowser.Logging;
using UnityWebBrowser.Shared;
using UnityWebBrowser.Shared.Events;
using Object = UnityEngine.Object;
using Resolution = UnityWebBrowser.Shared.Resolution;

namespace UnityWebBrowser.Core
{
    /// <summary>
    ///     The main object responsible for UWB.
    ///     <para>
    ///         This class handles:
    ///         <list type="bullet">
    ///             <item>UWB process setup</item>
    ///             <item>Texture setup and rendering</item>
    ///             <item>Wrapper for invoking methods on the UWB process</item>
    ///             <item>Shutdown</item>
    ///         </list>
    ///         If you need to do something with UWB, its probably here.
    ///     </para>
    /// </summary>
    [Serializable]
    public class WebBrowserClient : IDisposable
    {
        #region Profile Markers

        internal static ProfilerMarker markerGetPixels = new("UWB.GetPixels");
        internal static ProfilerMarker markerGetPixelsRpc = new("UWB.GetPixels.RPC");
        
        internal static ProfilerMarker markerLoadTextureApply = new("UWB.LoadTexture.Apply");

        #endregion

        /// <summary>
        ///     The active browser engine this instance is using
        /// </summary>
        [Header("Browser Settings")] [Tooltip("The active browser engine this instance is using")]
        public Engine engine;

        /// <summary>
        ///     The initial URl the browser will start at
        /// </summary>
        [Tooltip("The initial URl the browser will start at")]
        public string initialUrl = "https://voltstro.dev";

        #region Resoltuion

        [SerializeField] private Resolution resolution = new(1920, 1080);

        /// <summary>
        ///     The resolution of the browser.
        ///     <para>There is a chance that resizing the screen causes UWB to crash Unity, use carefully!</para>
        ///     <para>Resizing in performance mode is not supported!</para>
        /// </summary>
        public Resolution Resolution
        {
            get => resolution;
            set
            {
                resolution = value;
                Resize(value);
            }
        }

        #endregion

        /// <summary>
        ///     The background <see cref="UnityEngine.Color32" /> of the webpage
        /// </summary>
        [Tooltip("The background color of the webpage")]
        public Color32 backgroundColor = new(255, 255, 255, 255);

        /// <summary>
        ///     Enable or disable JavaScript
        /// </summary>
        [Tooltip("Enable or disable JavaScript")]
        public bool javascript = true;

        /// <summary>
        ///     Enable or disable the cache
        /// </summary>
        [Tooltip("Enable or disable the cache")]
        public bool cache = true;

        /// <summary>
        ///     Enable or disable WebRTC
        /// </summary>
        [Tooltip("Enable or disable WebRTC")] public bool webRtc;

        /// <summary>
        ///     Proxy Settings
        /// </summary>
        [Tooltip("Proxy settings")] public ProxySettings proxySettings;

        /// <summary>
        ///     Enable or disable remote debugging
        /// </summary>
        [Tooltip("Enable or disable remote debugging")]
        public bool remoteDebugging;

        /// <summary>
        ///     The port to use for remote debugging
        /// </summary>
        [Tooltip("The port to use for remote debugging")] [Range(1024, 65353)]
        public uint remoteDebuggingPort = 9022;

        /// <summary>
        ///     The <see cref="CommunicationLayer" /> to use
        /// </summary>
        [Header("IPC Settings")] [Tooltip("The communication layer to use")]
        public CommunicationLayer communicationLayer;

        /// <summary>
        ///     Timeout time for waiting for the engine to start (in milliseconds)
        /// </summary>
        [Tooltip("Timeout time for waiting for the engine to start (in milliseconds)")]
        public int engineStartupTimeout = 4000;

        /// <summary>
        ///     The log severity. Only messages of this severity level or higher will be logged
        /// </summary>
        [Tooltip("The log severity. Only messages of this severity level or higher will be logged")]
        public LogSeverity logSeverity = LogSeverity.Info;

        /// <summary>
        ///     Has higher performance with a trade-off of screen tearing
        /// </summary>
        [Tooltip("Has higher performance with a trade-off of screen tearing")]
        public bool performanceMode;

        /// <summary>
        ///     Texture that the browser will paint to
        /// </summary>
        public Texture2D BrowserTexture { get; private set; }

        /// <summary>
        ///     Are we connected to the UW engine process
        /// </summary>
        public bool IsConnected => communicationsManager is {IsConnected: true};
        
        /// <summary>
        ///     The UWB engine has signaled that it is ready
        /// </summary>
        public bool ReadySignalReceived { get; internal set; }

        #region Log Path

        private FileInfo logPath;

        /// <summary>
        ///     The path that UWB engine will log to
        /// </summary>
        /// <exception cref="UwbIsConnectedException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public FileInfo LogPath
        {
            get => logPath;
            set
            {
                if (IsConnected)
                    throw new UwbIsConnectedException(
                        "You cannot change the log path once the browser engine is connected");

                logPath = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        #endregion

        #region Cache Path

        private FileInfo cachePath;

        /// <summary>
        ///     The path to the cache
        /// </summary>
        /// <exception cref="UwbIsConnectedException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public FileInfo CachePath
        {
            get => cachePath;
            set
            {
                if (IsConnected)
                    throw new UwbIsConnectedException(
                        "You cannot change the cache path once the browser engine is connected");

                if (!cache)
                    throw new ArgumentException("The cache is disabled!");

                cachePath = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        #endregion

        #region Logger

        public ProcessLogHandler processLogHandler;
        
        /// <summary>
        ///     Internal usage of <see cref="IWebBrowserLogger" />
        /// </summary>
        internal IWebBrowserLogger logger = new DefaultUnityWebBrowserLogger();

        /// <summary>
        ///     Gets the <see cref="IWebBrowserLogger" /> to use for logging
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public IWebBrowserLogger Logger
        {
            get => logger;
            set => logger = value ?? throw new ArgumentNullException(nameof(value));
        }

        #endregion

        private Process engineProcess;
        private WebBrowserCommunicationsManager communicationsManager;
        private CancellationTokenSource cancellationToken;
        internal NativeArray<byte> textureData;

        /// <summary>
        ///     Inits the browser client
        /// </summary>
        /// <exception cref="FileNotFoundException"></exception>
        internal void Init()
        {
            //Get the path to the UWB process we are using and make sure it exists
            string browserEnginePath = WebBrowserUtils.GetBrowserEngineProcessPath(engine);
            logger.Debug($"Starting browser engine process from '{browserEnginePath}'...");

            if (!File.Exists(browserEnginePath))
            {
                logger.Error("The engine process could not be found!");
                throw new FileNotFoundException("The engine process could not be found!");
            }

            //Check communication layer
            if (communicationLayer.IsInUse)
                throw new InitializationException("The communication layer is already in use!");
            communicationLayer.IsInUse = true;

            //Setup texture
            BrowserTexture = new Texture2D((int) resolution.Width, (int) resolution.Height, TextureFormat.BGRA32, false,
                false);
            WebBrowserUtils.SetAllTextureColorToOne(BrowserTexture, backgroundColor);
            textureData = BrowserTexture.GetRawTextureData<byte>();
            
            pixelDataLock = new object();
            pixelData = new NativeArray<byte>(new byte[(int) resolution.Width * (int) resolution.Height * 4], Allocator.Persistent);

            string browserEngineMainDir = WebBrowserUtils.GetAdditionFilesDirectory();

            //Start to build our arguments
            WebBrowserArgsBuilder argsBuilder = new();

            //Initial URL
            argsBuilder.AppendArgument("initial-url", initialUrl, true);

            //Width & Height
            argsBuilder.AppendArgument("width", resolution.Width);
            argsBuilder.AppendArgument("height", resolution.Height);

            //Javascript
            argsBuilder.AppendArgument("javascript", javascript);

            //Background color
            argsBuilder.AppendArgument("background-color", WebBrowserUtils.ColorToHex(backgroundColor));

            //Logging
            LogPath ??= new FileInfo($"{browserEngineMainDir}/{engine.GetEngineExecutableName()}.log");
            argsBuilder.AppendArgument("log-path", LogPath.FullName, true);
            argsBuilder.AppendArgument("log-severity", logSeverity);

            //IPC settings
            communicationLayer.GetIpcSettings(out object outLocation, out object inLocation,
                out string assemblyLocation);
            if (assemblyLocation != null)
            {
                if (!File.Exists(assemblyLocation))
                {
                    logger.Error("Failed to find provided communication layer assembly!");
                    throw new FileNotFoundException("Failed to find provided communication layer assembly!");
                }

                argsBuilder.AppendArgument("comms-layer-path", assemblyLocation, true);
                logger.Debug($"Using communication layer assembly at '{assemblyLocation}'.");
            }

            argsBuilder.AppendArgument("in-location", inLocation, true);
            argsBuilder.AppendArgument("out-location", outLocation, true);

            //If we have a cache, set the cache path
            if (cache)
            {
                cachePath ??= new FileInfo($"{browserEngineMainDir}/UWBCache");
                argsBuilder.AppendArgument("cache-path", cachePath.FullName, true);
            }

            //Setup web RTC
            if (webRtc)
                argsBuilder.AppendArgument("web-rtc", webRtc);

            //Setup remote debugging
            if (remoteDebugging)
                argsBuilder.AppendArgument("remote-debugging", remoteDebuggingPort);

            //Setup proxy
            argsBuilder.AppendArgument("proxy-server", proxySettings.ProxyServer);
            if (!string.IsNullOrWhiteSpace(proxySettings.Username))
                argsBuilder.AppendArgument("proxy-username", proxySettings.Username, true);

            if (!string.IsNullOrWhiteSpace(proxySettings.Password))
                argsBuilder.AppendArgument("proxy-password", proxySettings.Password, true);
            
            //Make sure not to include this, its for testing
#if UWB_ENGINE_PRJ //Define for backup, cause I am dumb as fuck and gonna accidentally include this in a release build one day 
            //argsBuilder.AppendArgument("start-delay", 2000);
#endif

            //Final built arguments
            string arguments = argsBuilder.ToString();

            //Setup communication manager
            communicationsManager = new WebBrowserCommunicationsManager(this);
            communicationsManager.Listen();
            
            cancellationToken = new CancellationTokenSource();

            //Start the engine process
            UniTask.Create(() => StartEngineProcess(arguments)).ContinueWith(WaitForEngineReadyTask).Forget();
        }
        
        #region Starting

        private UniTask StartEngineProcess(string engineProcessArguments)
        {
            try
            {
                processLogHandler = new ProcessLogHandler(this);
                engineProcess = WebBrowserUtils.CreateEngineProcess(logger, engine, engineProcessArguments,
                    processLogHandler.HandleOutputProcessLog, processLogHandler.HandleErrorProcessLog);
            }
            catch (Exception ex)
            {
                logger.Error($"An error occured while setting up the engine process! {ex}");
                throw;
            }
            
            return UniTask.CompletedTask;
        }

        /// <summary>
        ///     Will wait for <see cref="ReadySignalReceived" /> to be true
        /// </summary>
        internal async UniTask WaitForEngineReadyTask()
        {
            try
            {
                await UniTask.WaitUntil(() => ReadySignalReceived)
                    .Timeout(TimeSpan.FromMilliseconds(engineStartupTimeout));
            }
            catch (TimeoutException)
            {
                logger.Error(engineProcess.HasExited
                    ? $"The engine did not get ready within engine startup timeout! The engine process is not even running! Exit code: {engineProcess.ExitCode}."
                    : "The engine did not get ready within engine startup timeout!");
                await using (UniTask.ReturnToMainThread())
                {
                    Dispose();
                }
            }
            catch (Exception ex)
            {
                logger.Error($"An unknown error occured while waiting for engine to get ready! {ex}");
                await using (UniTask.ReturnToMainThread())
                {
                    Dispose();
                }
            }
        }

        /// <summary>
        ///     Called when the engine sends the ready signal
        /// </summary>
        internal async UniTaskVoid EngineReady()
        {
            ReadySignalReceived = true;

            try
            {
                logger.Debug("UWB startup success, connecting...");
                communicationsManager.Connect();
                _ = Task.Run(PixelDataLoop);
            }
            catch (Exception ex)
            {
                logger.Error($"An error occured while waiting to connect to the UWB engine process! {ex}");
                await using (UniTask.ReturnToMainThread())
                {
                    Dispose();
                }
            }
        }

        #endregion

        #region Main Loop
        
        private NativeArray<byte> pixelData;
        private object pixelDataLock;

        internal async Task PixelDataLoop()
        {
            CancellationToken token = cancellationToken.Token;
            while (!token.IsCancellationRequested)
                try
                {
                    if (!IsConnected)
                        continue;
                    
                    await Task.Delay(25, token);

                    if (token.IsCancellationRequested)
                        return;
                    
                    markerGetPixels.Begin();
                    {
                        markerGetPixelsRpc.Begin();
                        RunPixelDataLockAction(() =>
                        {
                            communicationsManager.GetPixels();
                        });
                        markerGetPixelsRpc.End();
                    }
                    markerGetPixels.End();
                }
                catch (TaskCanceledException)
                {
                    //Do nothing
                }
                catch (Exception ex)
                {
                    logger.Error($"Error in data loop! {ex}");
                }
        }

        /// <summary>
        ///     Loads the pixel data into the <see cref="BrowserTexture" />
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LoadTextureData()
        {
            if (!IsConnected)
                return;

            if (!pixelData.IsCreated || pixelData.Length == 0)
                return;
                
            Texture2D texture = BrowserTexture;
 
            markerLoadTextureApply.Begin();
            RunPixelDataLockAction(() =>
            {
                texture.Apply(false);
            });
            markerLoadTextureApply.End();
        }

        private void RunPixelDataLockAction(Action action)
        {
            if (!performanceMode)
            {
                lock (pixelDataLock)
                {
                    action();
                }
                return;
            }

            action();
        }

        #endregion

        #region Browser Events

        /// <summary>
        ///     Invoked when the url changes
        /// </summary>
        public event OnUrlChangeDelegate OnUrlChanged;

        internal void InvokeUrlChanged(string url)
        {
            OnUrlChanged?.Invoke(url);
        }

        /// <summary>
        ///     Invoked when the page starts to load
        /// </summary>
        public event OnLoadStartDelegate OnLoadStart;

        internal void InvokeLoadStart(string url)
        {
            OnLoadStart?.Invoke(url);
        }

        /// <summary>
        ///     Invoked when the page finishes loading
        /// </summary>
        public event OnLoadFinishDelegate OnLoadFinish;

        internal void InvokeLoadFinish(string url)
        {
            OnLoadFinish?.Invoke(url);
        }

        /// <summary>
        ///     Invoked when the title changes
        /// </summary>
        public event OnTitleChange OnTitleChange;

        internal void InvokeTitleChange(string title)
        {
            OnTitleChange?.Invoke(title);
        }

        /// <summary>
        ///     Invoked when the loading progress changes
        ///     <para>Progress goes from 0 to 1</para>
        /// </summary>
        public event OnLoadingProgressChange OnLoadProgressChange;

        internal void InvokeLoadProgressChange(double progress)
        {
            OnLoadProgressChange?.Invoke(progress);
        }

        /// <summary>
        ///     Invoked when the browser goes in or out of fullscreen
        /// </summary>
        public event OnFullscreenChange OnFullscreen;

        internal void InvokeFullscreen(bool fullscreen)
        {
            OnFullscreen?.Invoke(fullscreen);
        }

        #endregion

        #region Browser Controls

        /// <summary>
        ///     Sends a keyboard event
        /// </summary>
        /// <param name="keysDown"></param>
        /// <param name="keysUp"></param>
        /// <param name="chars"></param>
        public void SendKeyboardControls(WindowsKey[] keysDown, WindowsKey[] keysUp, string chars)
        {
            CheckIfIsReadyAndConnected();

            communicationsManager.SendKeyboardEvent(new KeyboardEvent
            {
                KeysDown = keysDown,
                KeysUp = keysUp,
                Chars = chars
            });
        }

        /// <summary>
        ///     Sends a mouse event
        /// </summary>
        /// <param name="mousePos"></param>
        public void SendMouseMove(Vector2 mousePos)
        {
            CheckIfIsReadyAndConnected();

            communicationsManager.SendMouseMoveEvent(new MouseMoveEvent
            {
                MouseX = (int) mousePos.x,
                MouseY = (int) mousePos.y
            });
        }

        /// <summary>
        ///     Sends a mouse click event
        /// </summary>
        /// <param name="mousePos"></param>
        /// <param name="clickCount"></param>
        /// <param name="clickType"></param>
        /// <param name="eventType"></param>
        public void SendMouseClick(Vector2 mousePos, int clickCount, MouseClickType clickType,
            MouseEventType eventType)
        {
            CheckIfIsReadyAndConnected();

            communicationsManager.SendMouseClickEvent(new MouseClickEvent
            {
                MouseX = (int) mousePos.x,
                MouseY = (int) mousePos.y,
                MouseClickCount = clickCount,
                MouseClickType = clickType,
                MouseEventType = eventType
            });
        }

        /// <summary>
        ///     Sends a mouse scroll event
        /// </summary>
        /// <param name="mousePos"></param>
        /// <param name="mouseScroll"></param>
        public void SendMouseScroll(Vector2 mousePos, int mouseScroll)
        {
            CheckIfIsReadyAndConnected();

            communicationsManager.SendMouseScrollEvent(new MouseScrollEvent
            {
                MouseX = (int) mousePos.x,
                MouseY = (int) mousePos.y,
                MouseScroll = mouseScroll
            });
        }

        /// <summary>
        ///     Tells the browser to load a URL
        /// </summary>
        /// <param name="url"></param>
        public void LoadUrl(string url)
        {
            CheckIfIsReadyAndConnected();

            communicationsManager.LoadUrl(url);
        }
        
        /// <summary>
        ///     Gets the mouse scroll position
        ///     <para>THIS IS INVOKED ON THE THREAD THAT IS CALLING THIS AND IS BLOCKING</para>
        /// </summary>
        /// <returns>Returns the mouse scroll position as a <see cref="Vector2"/></returns>
        public Vector2 GetScrollPosition()
        {
            CheckIfIsReadyAndConnected();
            
            //Gotta convert it to a Unity vector2
            System.Numerics.Vector2 position = communicationsManager.GetScrollPosition();
            return new Vector2(position.X, position.Y);
        }

        /// <summary>
        ///     Tells the browser to go forward
        /// </summary>
        public void GoForward()
        {
            CheckIfIsReadyAndConnected();

            communicationsManager.GoForward();
        }

        /// <summary>
        ///     Tells the browser to go back
        /// </summary>
        public void GoBack()
        {
            CheckIfIsReadyAndConnected();

            communicationsManager.GoBack();
        }

        /// <summary>
        ///     Refreshes the browser
        /// </summary>
        public void Refresh()
        {
            CheckIfIsReadyAndConnected();

            communicationsManager.Refresh();
        }

        /// <summary>
        ///     Makes the browser load html
        /// </summary>
        /// <param name="html"></param>
        public void LoadHtml(string html)
        {
            CheckIfIsReadyAndConnected();

            communicationsManager.LoadHtml(html);
        }

        /// <summary>
        ///     Executes JS in the browser
        /// </summary>
        /// <param name="js"></param>
        public void ExecuteJs(string js)
        {
            CheckIfIsReadyAndConnected();

            communicationsManager.ExecuteJs(js);
        }

        /// <summary>
        ///     Resizes the screen.
        ///     <para>There is a chance that resizing the screen causes UWB to crash Unity, use carefully!</para>
        ///     <para>Resizing in performance mode is not supported!</para>
        /// </summary>
        /// <param name="newResolution"></param>
        /// <exception cref="UwbIsNotConnectedException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        public void Resize(Resolution newResolution)
        {
            CheckIfIsReadyAndConnected();

            if (performanceMode)
                throw new NotSupportedException("Resizing is not allowed in performance mode!");

            BrowserTexture.Reinitialize((int) newResolution.Width, (int) newResolution.Height);
            communicationsManager.Resize(newResolution);
            textureData = BrowserTexture.GetRawTextureData<byte>();
            communicationsManager.pixelsEventTypeReader.SetPixelDataArray(textureData);
            

            logger.Debug($"Resized to {newResolution}.");
        }

        [DebuggerStepThrough]
        private void CheckIfIsReadyAndConnected()
        {
            if (!ReadySignalReceived)
                throw new UwbIsNotReadyException("UWB is not currently ready!");

            if (!IsConnected)
                throw new UwbIsNotConnectedException("UWB is not currently connected!");
        }

        #endregion

        #region Destroying

#if !UNITY_EDITOR
        ~WebBrowserClient()
        {
            ReleaseResources();
        }
#endif

        /// <summary>
        ///     Has this object been disposed
        /// </summary>
        public bool HasDisposed { get; private set; }

        /// <summary>
        ///     Destroys this <see cref="WebBrowserClient" /> instance
        /// </summary>
        public void Dispose()
        {
            if (HasDisposed)
                return;

            ReleaseResources();
            GC.SuppressFinalize(this);
        }

        private void ReleaseResources()
        {
            if (HasDisposed)
                return;

            HasDisposed = true;
            logger.Debug("UWB shutdown...");

            cancellationToken?.Cancel();
            if (BrowserTexture != null)
                Object.Destroy(BrowserTexture);

            //The pixel data will be null as well if the pixel data lock is null
            if(pixelDataLock != null)
                lock (pixelDataLock)
                    pixelData.Dispose();

            if (ReadySignalReceived && IsConnected)
                communicationsManager.Shutdown();

            try
            {
                communicationsManager?.Dispose();
            }
            catch (Exception ex)
            {
                logger.Error($"Some error occured while destroying the communications manager! {ex}");
            }

            if(communicationLayer != null)
                communicationLayer.IsInUse = false;

            if (engineProcess != null)
            {
                engineProcess.KillTree();

                engineProcess.Dispose();
                engineProcess = null;
            }
        }

        #endregion
    }
}