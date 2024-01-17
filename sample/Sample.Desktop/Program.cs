using System.Numerics;
using System.Runtime.InteropServices;
using Bgfx;
using SDL2;

namespace Sample.Desktop;

public static unsafe class Program
{
    public const int WindowWidth = 1280;
    public const int WindowHeight = 720;
    public static void Main(string[] args)
    {
        // Initilizes SDL.
        if (SDL.SDL_Init(SDL.SDL_INIT_VIDEO) < 0)
        {
            Console.WriteLine($"There was an issue initilizing SDL. {SDL.SDL_GetError()}");
        }

        // Create a new window given a title, size, and passes it a flag indicating it should be shown.
        nint window = SDL.SDL_CreateWindow("SDL Bgfx sample",
                                          SDL.SDL_WINDOWPOS_UNDEFINED,
                                          SDL.SDL_WINDOWPOS_UNDEFINED,
                                          WindowWidth,
                                          WindowHeight,
                                          SDL.SDL_WindowFlags.SDL_WINDOW_SHOWN);

        if (window == IntPtr.Zero)
        {
            Console.WriteLine($"There was an issue creating the window. {SDL.SDL_GetError()}");
        }

        // Create a new bgfx platform data object
        var pd = new bgfx.PlatformData();
        SDL.SDL_SysWMinfo wmi = default;
        SDL.SDL_VERSION(out wmi.version);
        if (SDL.SDL_GetWindowWMInfo(window, ref wmi) == SDL.SDL_bool.SDL_FALSE)
        {
            Console.WriteLine($"Could not get window information {SDL.SDL_GetError()}");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            pd.ndt = wmi.info.x11.display.ToPointer();
            pd.nwh = wmi.info.x11.window.ToPointer();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            pd.ndt = null;
            pd.nwh = wmi.info.cocoa.window.ToPointer();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            pd.ndt = null;
            pd.nwh = wmi.info.win.window.ToPointer();
        }

        bgfx.set_platform_data(&pd);

        // Initialize the bgfx library
        bgfx.Init bgfxInit;
        bgfx.init_ctor(&bgfxInit);
        bgfxInit.type = bgfx.RendererType.Count;
        bgfxInit.vendorId = (ushort)bgfx.PciIdFlags.None;
        bgfxInit.resolution.width = WindowWidth;
        bgfxInit.resolution.height = WindowHeight;
        bgfxInit.platformData.nwh = pd.nwh;
        bgfxInit.platformData.ndt = pd.ndt;
        bgfxInit.limits.transientVbSize = 1024 * 1024 * 24;
        bgfx.init(&bgfxInit);
        bgfx.reset(
            (uint)WindowWidth,
            (uint)WindowHeight,
            (uint)(bgfx.ResetFlags.Vsync | bgfx.ResetFlags.Maxanisotropy | bgfx.ResetFlags.MsaaX16),
            bgfxInit.resolution.format);
        bgfx.set_debug((uint)(bgfx.DebugFlags.Stats | bgfx.DebugFlags.Text));

        // Set the view clear color and depth values
        bgfx.set_view_clear(0, (ushort)(bgfx.ClearFlags.Color | bgfx.ClearFlags.Depth), 0x443355FF, 1.0f, 0);
        // Set the view rect
        bgfx.set_view_rect(0, 0, 0, WindowWidth, WindowHeight);

        bgfx.VertexLayout vertexLayout;
        bgfx.vertex_layout_begin(&vertexLayout, bgfxInit.type);
        bgfx.vertex_layout_add(&vertexLayout, bgfx.Attrib.Position, 3, bgfx.AttribType.Float, false, false);
        bgfx.vertex_layout_add(&vertexLayout, bgfx.Attrib.Color0, 4, bgfx.AttribType.Uint8, true, false);
        bgfx.vertex_layout_end(&vertexLayout);

        bgfx.VertexBufferHandle vbh;
        bgfx.IndexBufferHandle ibh;
        bgfx.ShaderHandle vsh = LoadShader("vs_cubes");
        bgfx.ShaderHandle fsh = LoadShader("fs_cubes");
        bgfx.ProgramHandle program = bgfx.create_program(vsh, fsh, true);

        fixed (void* cubeVerticesPtr = cubeVertices)
        {
            vbh = bgfx.create_vertex_buffer(
                bgfx.make_ref(cubeVerticesPtr, (uint)(cubeVertices.Length * vertexLayout.stride)),
                &vertexLayout,
                (ushort)bgfx.BufferFlags.None);
        }
        fixed (uint* cubeTriPtr = cubeTriList)
        {
            ibh = bgfx.create_index_buffer(
                bgfx.make_ref(cubeTriPtr, (uint)(cubeTriList.Length * sizeof(uint))),
                (ushort)bgfx.BufferFlags.Index32);
        }


        // Main game loop
        var running = true;

        uint counter = 0;
        // Main loop for the program
        while (running)
        {
            // Check to see if there are any events and continue to do so until the queue is empty.
            while (SDL.SDL_PollEvent(out SDL.SDL_Event e) == 1)
            {
                switch (e.type)
                {
                    case SDL.SDL_EventType.SDL_QUIT:
                        running = false;
                        break;
                }
            }
            // Update the bgfx frame
            Vector3 at = new(0.0f, 0.0f, 0.0f);
            Vector3 eye = new(0.0f, 0.0f, 10.0f);
            Matrix4x4 view = Matrix4x4.CreateLookAt(eye, at, new(0, 1, 0));
            Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(
                60f * (float)Math.PI / 180f,
                (float)WindowWidth / WindowHeight,
                0.01f,
                100.0f);
            bgfx.set_view_transform(0, &view, &proj);

            bgfx.set_vertex_buffer(0, vbh, 0, (uint)cubeVertices.Length);
            bgfx.set_index_buffer(ibh, 0, (uint)cubeTriList.Length);

            Matrix4x4 transform = Matrix4x4.CreateRotationX(counter * 0.01f) * Matrix4x4.CreateRotationY(counter * 0.01f);
            bgfx.set_transform(&transform, 1);

            var flags = bgfx.DiscardFlags.None;
            bgfx.submit(0, program, 1000, (byte)flags);
            counter = bgfx.frame(false);
        }

        // Destroy the vertex buffer and shutdown bgfx
        // bgfx.destroy_vertex_buffer(vb);
        bgfx.shutdown();
        SDL.SDL_DestroyWindow(window);
        SDL.SDL_Quit();
    }

    private static bgfx.ShaderHandle LoadShader(string name)
    {
        string filePath = string.Empty;
        string shaderPath = @$"{AppContext.BaseDirectory}/shaders";
        name = $"{name}.bin";

        bgfx.ShaderHandle invalid = new bgfx.ShaderHandle { idx = ushort.MaxValue };

        switch (bgfx.get_renderer_type())
        {
            case bgfx.RendererType.Noop:
                break;

            case bgfx.RendererType.Direct3D11:
            case bgfx.RendererType.Direct3D12:
                shaderPath = $"{shaderPath}/dx11/";
                break;

            case bgfx.RendererType.Gnm:
                shaderPath = $"{shaderPath}/pssl/";
                break;

            case bgfx.RendererType.Metal:
                shaderPath = $"{shaderPath}/metal/";
                break;

            case bgfx.RendererType.OpenGL:
                shaderPath = $"{shaderPath}/glsl/";
                break;

            case bgfx.RendererType.OpenGLES:
                shaderPath = $"{shaderPath}/essl/";
                break;

            case bgfx.RendererType.Vulkan:
                shaderPath = $"{shaderPath}/spirv/";
                break;

            default:
                return invalid;
        };


        filePath = Path.Combine(shaderPath, name);

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Shader file {name} not found.");
            return invalid;
        }

        long fileSize = new FileInfo(filePath).Length;

        if (fileSize == 0)
        {
            return invalid;
        }

        byte[] shaderBytes = File.ReadAllBytes(filePath);

        unsafe
        {
            fixed (void* ptr = shaderBytes)
            {
                bgfx.Memory* memory = bgfx.copy(ptr, (uint)fileSize);

                bgfx.ShaderHandle handle = bgfx.create_shader(memory);

                if (!handle.Valid)
                {
                    Console.Error.WriteLine($"Shader model not supported for {name}");
                    return invalid;
                }

                return handle;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PosColorVertex
    {
        float x;
        float y;
        float z;
        uint abgr;

        public PosColorVertex(float x, float y, float z, uint abgr) : this()
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.abgr = abgr;
        }
    };

    private static PosColorVertex[] cubeVertices =
    {
        new(-1.0f,  1.0f,  1.0f, 0xff000000 ),
        new( 1.0f,  1.0f,  1.0f, 0xff0000ff ),
        new(-1.0f, -1.0f,  1.0f, 0xff00ff00 ),
        new( 1.0f, -1.0f,  1.0f, 0xff00ffff ),
        new(-1.0f,  1.0f, -1.0f, 0xffff0000 ),
        new( 1.0f,  1.0f, -1.0f, 0xffff00ff ),
        new(-1.0f, -1.0f, -1.0f, 0xffffff00 ),
        new( 1.0f, -1.0f, -1.0f, 0xffffffff ),
    };

    private static uint[] cubeTriList =
        new uint[] {
            2, 1, 0,
            2, 3, 1,
            5, 6, 4,
            7, 6, 5,
            4, 2, 0,
            6, 2, 4,
            3, 5, 1,
            3, 7, 5,
            1, 4, 0,
            1, 5, 4,
            6, 3, 2,
            7, 3, 6,
        };
}