// ReSharper disable CheckNamespace

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Protoinject;
using System.Diagnostics;

namespace Protogame
{
    public abstract class CoreGame<TInitialWorld> : CoreGame<TInitialWorld, RenderPipelineWorldManager> where TInitialWorld : IWorld
    {
        private readonly IKernel _kernel;

        protected CoreGame(IKernel kernel) : base(kernel)
        {
            _kernel = kernel;
        }

        protected abstract void ConfigureRenderPipeline(IRenderPipeline pipeline, IKernel kernel);

        protected sealed override void InternalConfigureRenderPipeline(IRenderPipeline pipeline)
        {
            ConfigureRenderPipeline(pipeline, _kernel);
        }

    }


    public abstract class CoreGame<TInitialWorld, TWorldManager> : ICoreGame
        where TInitialWorld : IWorld where TWorldManager : IWorldManager
    {
        private readonly IKernel _kernel;
        private readonly IProfiler _profiler;
        private readonly IAnalyticsEngine _analyticsEngine;
        private int _totalFrames;
        private float _elapsedTime;
        private IEngineHook[] _engineHooks;
        private readonly INode _node;
        private Task _loadContentTask;
        private ICoroutine _coroutine;
        private ICoroutineScheduler _coroutineScheduler;
        private ILoadingScreen _loadingScreen;
        private IConsoleHandle _consoleHandle;
        private bool _hasDoneEarlyRender;
        private bool _isReadyForMainRenderTakeover;
        private bool _hasDoneInitialLoadContent;
        private IHostGame _hostGame;
        public IGameContext GameContext { get; private set; }
        public IUpdateContext UpdateContext { get; private set; }
        public IRenderContext RenderContext { get; private set; }

        public bool IsMouseVisible
        {
            get { return _hostGame?.IsMouseVisible ?? false; }
            set { if (_hostGame != null) { _hostGame.IsMouseVisible = value; } }
        }

        public bool IsActive
        {
            get
            {
                return _hostGame?.IsActive ?? false;
            }
        }

        protected CoreGame(IKernel kernel)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

#if PLATFORM_MACOS
            // On Mac, the MonoGame launcher changes the current working
            // directory which means we can't find any assets.  Change it
            // back to where Protogame is located (because this is usually
            // beside the game).
            var directory = new System.IO.FileInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).Directory;
            if (directory != null)
            {
                System.Environment.CurrentDirectory = directory.FullName;
            }
#endif

            _kernel = kernel;
            _node = _kernel.CreateEmptyNode("Game");

            _profiler = kernel.TryGet<IProfiler>(_node);
            if (_profiler == null)
            {
                kernel.Bind<IProfiler>().To<NullProfiler>();
                _profiler = kernel.Get<IProfiler>(_node);
            }

            _analyticsEngine = kernel.TryGet<IAnalyticsEngine>(_node);
            if (_analyticsEngine == null)
            {
                kernel.Bind<IAnalyticsEngine>().To<NullAnalyticsEngine>();
                _analyticsEngine = kernel.Get<IAnalyticsEngine>(_node);
            }

            var analyticsInitializer = kernel.TryGet<IAnalyticsInitializer>(_node);
            if (analyticsInitializer == null)
            {
                kernel.Bind<IAnalyticsInitializer>().To<NullAnalyticsInitializer>();
                analyticsInitializer = kernel.Get<IAnalyticsInitializer>(_node);
            }

            _consoleHandle = kernel.TryGet<IConsoleHandle>(_node);

            StartupTrace.TimingEntries["constructOptionalGameDependencies"] = stopwatch.Elapsed;
            stopwatch.Restart();

            analyticsInitializer.Initialize(_analyticsEngine);

            StartupTrace.TimingEntries["initializeAnalytics"] = stopwatch.Elapsed;
            stopwatch.Restart();

            _coroutine = _kernel.Get<ICoroutine>();

            StartupTrace.TimingEntries["constructCoroutine"] = stopwatch.Elapsed;
            stopwatch.Restart();

            _coroutineScheduler = _kernel.Get<ICoroutineScheduler>();

            StartupTrace.TimingEntries["constructCoroutineScheduler"] = stopwatch.Elapsed;
            stopwatch.Restart();
        }

        public IGameWindow Window => _hostGame?.ProtogameWindow;
        public GraphicsDevice GraphicsDevice => _hostGame?.GraphicsDevice;
        public int SkipFrames { get; set; }
        public IHostGame HostGame => _hostGame;
        public bool HasLoadedContent => _isReadyForMainRenderTakeover;
        public void AssignHost(IHostGame hostGame)
        {
            _hostGame = hostGame;
            _hostGame.Exiting += _hostGame_Exiting;

            var assetContentManager = new AssetContentManager(_hostGame.Services);
            _hostGame.Content = assetContentManager;
            _kernel.Bind<IAssetContentManager>().ToMethod(x => assetContentManager);

            // We can't load the loading screen until we have access to MonoGame's asset content manager.
            _loadingScreen = _kernel.Get<ILoadingScreen>();
        }

        public void EnableImmediateStartFromHost()
        {
            _hasDoneEarlyRender = true;
        }

        private void _hostGame_Exiting(object sender, EventArgs e)
        {
            Exiting?.Invoke(sender, e);
        }

        public void LoadContent()
        {
            _consoleHandle.LogDebug("LoadContent called");

            _loadContentTask = _coroutine.Run(async () =>
            {
                await LoadContentAsync();
            });
        }

        public void UnloadContent()
        {
            _consoleHandle.LogDebug("UnloadContent called");
        }

        public void DeviceLost()
        {
            _consoleHandle.LogDebug("DeviceLost called");
        }

        public void DeviceResetting()
        {
            _consoleHandle.LogDebug("DeviceResetting called");
        }

        public void DeviceReset()
        {
            _consoleHandle.LogDebug("DeviceReset called");
        }

        public void ResourceCreated(object resource)
        {
            _consoleHandle.LogDebug("ResourceCreated called ({0})", resource);
        }

        public void ResourceDestroyed(string name, object tag)
        {
            _consoleHandle.LogDebug("ResourceDestroyed called ({0}, {1})", name, tag);
        }

        public event EventHandler Exiting;

        public void Exit()
        {
#if !PLATFORM_IOS
            _hostGame?.Exit();
#endif
        }

        protected virtual async Task LoadContentAsync()
        {
            if (!_hasDoneInitialLoadContent)
            {
                _hasDoneInitialLoadContent = true;

                // Allow the user to configure the game window now.
                PrepareGameWindow(Window);

                // Construct the world manager.
                var worldManager = await _kernel.GetAsync<TWorldManager>(_node, null, null, new IInjectionAttribute[0], new IConstructorArgument[0], null);

                // Create the game context.
                GameContext = await _kernel.GetAsync<IGameContext>(
                    _node,
                    null,
                    null,
                    new IInjectionAttribute[0],
                    new IConstructorArgument[]
                    {
                        new NamedConstructorArgument("game", this),
                        new NamedConstructorArgument("world", null),
                        new NamedConstructorArgument("worldManager", worldManager),
                        new NamedConstructorArgument("window", _hostGame.ProtogameWindow)
                    }, null);

                // If we are using the new rendering pipeline, we need to ensure that
                // the rendering context and the render pipeline world manager share
                // the same render pipeline.
                var renderPipelineWorldManager = worldManager as RenderPipelineWorldManager;
                IRenderPipeline renderPipeline = null;
                if (renderPipelineWorldManager != null)
                {
                    renderPipeline = renderPipelineWorldManager.RenderPipeline;
                }

                UpdateContext = await _kernel.GetAsync<IUpdateContext>(_node, null, null, new IInjectionAttribute[0], new IConstructorArgument[0], null);
                RenderContext = await _kernel.GetAsync<IRenderContext>(
                    _node, null, null, new IInjectionAttribute[0], new IConstructorArgument[]
                    {
                        new NamedConstructorArgument("renderPipeline", renderPipeline)
                    },
                    null);

                if (renderPipeline != null)
                {
                    InternalConfigureRenderPipeline(renderPipeline);
                }

                // Retrieve all engine hooks.  These can be set up by additional modules
                // to change runtime behaviour.
                _engineHooks =
                    (await _kernel.GetAllAsync<IEngineHook>(_node, null, null,
                        new IInjectionAttribute[] { new FromGameAttribute() }, new IConstructorArgument[0], null)).ToArray();

                // Now we're ready to enable the main loop and turn off
                // early loading screen rendering.
                _isReadyForMainRenderTakeover = true;

                GameContext.SwitchWorld<TInitialWorld>();
                _analyticsEngine.LogGameplayEvent("Game:Start");
            }
        }

        protected virtual void InternalConfigureRenderPipeline(IRenderPipeline pipeline)
        {
            throw new NotSupportedException();
        }

        public void Dispose(bool disposing)
        {
            GameContext?.World?.Dispose();

            if (_analyticsEngine != null)
            {
                _analyticsEngine.LogGameplayEvent("Game:Stop");

                _analyticsEngine.FlushAndStop();
            }
        }

        public virtual void Update(GameTime gameTime)
        {
            if (!_isReadyForMainRenderTakeover)
            {
                if (_loadContentTask != null && _loadContentTask.IsFaulted)
                {
                    throw new AggregateException(_loadContentTask.Exception);
                }

                // LoadContent hasn't finished running yet.  At this point, we don't even have
                // the engine hooks loaded, so manually update the coroutine scheduler.
                if (_hasDoneEarlyRender)
                {
                    _coroutineScheduler.Update((IGameContext)null, null);
                }
                return;
            }

            if (_consoleHandle != null && !StartupTrace.EmittedTimingEntries)
            {
                foreach (var kv in StartupTrace.TimingEntries)
                {
                    _consoleHandle.LogDebug("{0}: {1}ms", kv.Key, Math.Round(kv.Value.TotalMilliseconds, 2));
                }

                StartupTrace.EmittedTimingEntries = true;
            }

            using (_profiler.Measure("update", GameContext.FrameCount.ToString()))
            {
                // Measure FPS.
                using (_profiler.Measure("measure_fps"))
                {
                    GameContext.FrameCount += 1;
                    _elapsedTime += (float)gameTime.ElapsedGameTime.TotalMilliseconds;
                    if (_elapsedTime >= 1000f)
                    {
                        GameContext.FPS = _totalFrames;
                        _totalFrames = 0;
                        _elapsedTime = 0;
                    }
                }

                // This can be used in case MonoGame does not initialize correctly before the first frame.
                if (GameContext.FrameCount < SkipFrames)
                {
                    return;
                }

                // Update the game.
                using (_profiler.Measure("main"))
                {
                    GameContext.GameTime = gameTime;
                    GameContext.Begin();
                    foreach (var hook in _engineHooks)
                    {
                        hook.Update(GameContext, UpdateContext);
                    }

                    GameContext.WorldManager.Update(this);
                }
            }
        }

        public virtual void Draw(GameTime gameTime)
        {
            if (!_isReadyForMainRenderTakeover)
            {
                // LoadContent hasn't finished running yet.  Use the early game loading screen.
                _loadingScreen.RenderEarly(this, _hostGame.SplashScreenSpriteBatch, _hostGame.SplashScreenTexture);
                _hasDoneEarlyRender = true;
                return;
            }

            using (_profiler.Measure("render", (GameContext.FrameCount - 1).ToString()))
            {
                RenderContext.IsRendering = true;

                _totalFrames++;

                // This can be used in case MonoGame does not initialize correctly before the first frame.
                if (GameContext.FrameCount < SkipFrames)
                {
                    _hostGame.GraphicsDevice.Clear(Color.Black);
                    return;
                }

                // Render the game.
                using (_profiler.Measure("main"))
                {
                    GameContext.GameTime = gameTime;
                    if (typeof(TWorldManager) != typeof(RenderPipelineWorldManager))
                    {
                        foreach (var hook in _engineHooks)
                        {
                            hook.Render(GameContext, RenderContext);
                        }
                    }

                    GameContext.WorldManager.Render(this);
                }

                #if PLATFORM_ANDROID
                // Recorrect the viewport on Android, which seems to be completely bogus by default.
                _hostGame.GraphicsDevice.Viewport = new Viewport(
                    this.Window.ClientBounds.Left,
                    this.Window.ClientBounds.Top,
                    this.Window.ClientBounds.Width,
                    this.Window.ClientBounds.Height);
                #endif

                RenderContext.IsRendering = false;
            }
        }


        public virtual void CloseRequested(out bool cancel)
        {
            cancel = false;
        }

        public virtual void PrepareGraphicsDeviceManager(GraphicsDeviceManager graphicsDeviceManager)
        {
        }

        public virtual void PrepareGameWindow(IGameWindow window)
        {
        }

        public virtual void PrepareDeviceSettings(GraphicsDeviceInformation deviceInformation)
        {
            deviceInformation.PresentationParameters.RenderTargetUsage =
                RenderTargetUsage.PreserveContents;

            #if PLATFORM_WINDOWS
            // This will select the highest available multisampling.
            deviceInformation.PresentationParameters.MultiSampleCount = 32;
            #else
            // On non-Windows platforms, MonoGame's support for multisampling is
            // just totally broken.  Even if we ask for it here, the maximum
            // allowable multisampling for the platform won't be detected, which
            // causes render target corruption later on if we try and create
            // render targets with the presentation parameter's multisample
            // count.  This is because on Windows, the property of MultiSampleCount
            // is adjusted down from 32 to whatever multisampling value is actually
            // available, but this does not occur for OpenGL platforms, and so
            // the render targets on OpenGL platforms aren't initialised to a valid
            // state for the GPU to use.
            deviceInformation.PresentationParameters.MultiSampleCount = 0;
            #endif
        }
    }
}
