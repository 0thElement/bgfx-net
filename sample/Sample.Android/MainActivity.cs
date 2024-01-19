// CREDIT: https://github.com/LittleCodingFox/StapleEngine/blob/master/Engine/Core/Player/Android/StapleActivity.cs
using Android.Views;
using Android.OS;
using Android.Util;
using System.Runtime.InteropServices;
using Android.Runtime;
using Android.Graphics;

namespace Sample.Android;

[Activity(Label = "@string/app_name", MainLauncher = true)]
public partial class MainActivity : Activity, ISurfaceHolderCallback
{
    public static MainActivity? Instance { get; private set; }
    private SurfaceView? surfaceView;
    private FrameCallback? frameCallback;
    private TouchCallback? touchCallback;
    private KeyCallback? keyCallback;
    private Renderer renderer = new();

    [LibraryImport("android")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial nint ANativeWindow_fromSurface(nint env, nint surface);

    [LibraryImport("nativewindow")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial nint ANativeWindow_release(nint window);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Instance = this;

        Java.Lang.JavaSystem.LoadLibrary("android");
        Java.Lang.JavaSystem.LoadLibrary("nativewindow");
        Java.Lang.JavaSystem.LoadLibrary("log");
        Java.Lang.JavaSystem.LoadLibrary("bgfx");

#pragma warning disable CA1416, CA1422
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            WindowManagerLayoutParams p = Window?.Attributes!;

            var modes = Display?.GetSupportedModes();
            if (modes == null) return;

            var maxMode = 0;
            var maxHZ = 60.0f;

            foreach (var mode in modes)
            {
                if (maxHZ < mode.RefreshRate)
                {
                    maxHZ = mode.RefreshRate;
                    maxMode = mode.ModeId;
                }
            }

            p.PreferredDisplayModeId = maxMode;
            if (Window != null)
            {
                Window.Attributes = p;
            }
        }
#pragma warning restore CA1416, CA1422

        surfaceView = new SurfaceView(this);
        SetContentView(surfaceView);
        SetupWindowDisplay();

        touchCallback = new();
        keyCallback = new();

        surfaceView.Holder?.SetKeepScreenOn(true);
        surfaceView.Holder?.AddCallback(this);
        surfaceView.SetOnTouchListener(touchCallback);
        surfaceView.SetOnKeyListener(keyCallback);
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
        {
            SetupWindowDisplay();
        }
    }

    protected override void OnPause()
    {
        base.OnPause();
        renderer.OnPause();
    }

    protected override void OnResume()
    {
        base.OnResume();
        renderer.OnResume();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        renderer.OnDestroy();
    }

    public (int width, int height) GetScreenSize()
    {
#pragma warning disable CA1416, CA1422
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            DisplayMetrics dm = new();
            WindowManager!.DefaultDisplay!.GetMetrics(dm);
            return (dm.WidthPixels, dm.HeightPixels);
        }
        else
        {
            var metrics = WindowManager!.CurrentWindowMetrics;
            var bounds = metrics!.Bounds;
            return (bounds.Width(), bounds.Height());
        }
#pragma warning restore CA1416, CA1422
    }

    public Stream GetAssetStream(string path)
    {
        return Assets?.Open(path) ?? throw new Exception("Could not obtain an instance of asset manager");
    }

    private void SetupWindowDisplay()
    {
#pragma warning disable CA1416, CA1422
        if (Window == null)
        {
            return;
        }

        if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
        {
            if (Window.Attributes != null)
            {
                Window.Attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.Always;
            }
        }

        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            var controller = Window.DecorView.WindowInsetsController!;

            // fit any windows
            Window.SetDecorFitsSystemWindows(false);

            // immersive sticky
            controller.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;

            // hide system bars
            controller.Hide(WindowInsets.Type.SystemBars());

            // hide navigation bars
            controller.Hide(WindowInsets.Type.NavigationBars());
        }
        else
        {
            Window.DecorView.SystemUiFlags =
                SystemUiFlags.LayoutStable |
                SystemUiFlags.LayoutHideNavigation |
                SystemUiFlags.LayoutFullscreen |
                SystemUiFlags.HideNavigation |
                SystemUiFlags.Fullscreen |
                SystemUiFlags.ImmersiveSticky;
        }
#pragma warning restore CA1416, CA1422
    }

    public void SurfaceChanged(ISurfaceHolder holder, [GeneratedEnum] Format format, int width, int height)
    {
        void Finish()
        {
            nint nativeWindow = ANativeWindow_fromSurface(JNIEnv.Handle, holder.Surface!.Handle);
            if (renderer.WindowHandle != nint.Zero)
            {
                ANativeWindow_release(renderer.WindowHandle);
            }

            renderer.ScreenWidth = width;
            renderer.ScreenHeight = height;
            renderer.WindowHandle = nativeWindow;
            renderer.Available = true;

            new Handler(Looper.MainLooper!).Post(() =>
            {
                try
                {
                    Log.Debug("SO", $"Initializing");
                    Initialize();
                }
                catch (Exception e)
                {
                    Log.Error("SO", $"Exception: {e}");
                }
            });

            Log.Debug("SO", $"Surface Changed - Screen size: {width}x{height}. Is creating: {holder.IsCreating}. nativeWindow: {nativeWindow:X}, format: {format}");
        }

        void Delay()
        {
            if (holder.IsCreating)
            {
                Task.Delay(TimeSpan.FromMilliseconds(100)).ContinueWith((_) => Finish());
            }
            else
            {
                Finish();
            }
        }

        Delay();
    }

    public void SurfaceCreated(ISurfaceHolder holder)
    {
        Log.Debug("SO", "Surface created");
    }

    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        Log.Debug("SO", "Surface destroyed");
        renderer.Available = false;
    }

    private void Initialize()
    {
        renderer.Initialize();
        frameCallback = new FrameCallback()
        {
            Callback = Update,
        };

        Choreographer.Instance!.PostFrameCallback(frameCallback);
    }

    private void Update(long nanoTime)
    {
        Choreographer.Instance!.PostFrameCallback(frameCallback);
        renderer.Update(nanoTime);
    }

    class FrameCallback : Java.Lang.Object, Choreographer.IFrameCallback
    {
        public Action<long>? Callback;
        public void DoFrame(long frameTimeNanos)
        {
            Callback?.Invoke(frameTimeNanos);
        }
    }

    class TouchCallback : Java.Lang.Object, View.IOnTouchListener
    {
        public bool OnTouch(View? v, MotionEvent? e)
        {
            return true;
        }
    }

    class KeyCallback : Java.Lang.Object, View.IOnKeyListener
    {
        public bool OnKey(View? v, [GeneratedEnum] Keycode keyCode, KeyEvent? e)
        {
            return true;
        }
    }
}
