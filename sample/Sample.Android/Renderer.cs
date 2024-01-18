using System.Numerics;
using System.Runtime.InteropServices;
using Android.Util;
using Bgfx;
using Javax.Security.Auth;

namespace Sample.Android;

public unsafe class Renderer
{
    private uint counter = 0;
    private bool initialized = false;
    private bgfx.VertexBufferHandle vbh = new() { idx = ushort.MaxValue };
    private bgfx.IndexBufferHandle ibh = new() { idx = ushort.MaxValue };
    private bgfx.ProgramHandle program = new() { idx = ushort.MaxValue };
    public int ScreenWidth { get; set; }

    public int ScreenHeight { get; set; }

    public nint WindowHandle { get; set; }

    public bool Available { get; set; }

    public void Initialize()
    {
        OnDestroy();
        var pd = new bgfx.PlatformData();
        pd.ndt = null;
        pd.nwh = WindowHandle.ToPointer();
        bgfx.set_platform_data(&pd);

        bgfx.Init bgfxInit;
        bgfx.init_ctor(&bgfxInit);
        bgfxInit.type = bgfx.RendererType.Count;
        bgfxInit.vendorId = (ushort)bgfx.PciIdFlags.None;
        bgfxInit.resolution.width = (uint)ScreenWidth;
        bgfxInit.resolution.height = (uint)ScreenHeight;
        bgfxInit.platformData.nwh = pd.nwh;
        bgfxInit.platformData.ndt = pd.ndt;
        bgfxInit.limits.transientVbSize = 1024 * 1024 * 24;
        bgfx.reset(
            (uint)ScreenWidth,
            (uint)ScreenHeight,
            (uint)(bgfx.ResetFlags.Vsync | bgfx.ResetFlags.Maxanisotropy | bgfx.ResetFlags.MsaaX16),
            bgfxInit.resolution.format);
        bgfx.set_debug((uint)(bgfx.DebugFlags.Stats | bgfx.DebugFlags.Text));

        // Set the view clear color and depth values
        bgfx.set_view_clear(0, (ushort)(bgfx.ClearFlags.Color | bgfx.ClearFlags.Depth), 0x443355FF, 1.0f, 0);
        // Set the view rect
        bgfx.set_view_rect(0, 0, 0, (ushort)ScreenWidth, (ushort)ScreenHeight);

        // Create the vertex layout
        bgfx.VertexLayout vertexLayout;
        bgfx.vertex_layout_begin(&vertexLayout, bgfxInit.type);
        bgfx.vertex_layout_add(&vertexLayout, bgfx.Attrib.Position, 3, bgfx.AttribType.Float, false, false);
        bgfx.vertex_layout_add(&vertexLayout, bgfx.Attrib.Color0, 4, bgfx.AttribType.Uint8, true, false);
        bgfx.vertex_layout_end(&vertexLayout);

        // Load the shader
        bgfx.ShaderHandle vsh = LoadShader("vs_cubes");
        bgfx.ShaderHandle fsh = LoadShader("fs_cubes");
        program = bgfx.create_program(vsh, fsh, true);
        bgfx.destroy_shader(vsh);
        bgfx.destroy_shader(fsh);

        // Create buffers
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

        initialized = true;
    }

    public void Update(long nanoTime)
    {
        if (!Available)
        {
            return;
        }

        Vector3 at = new(0.0f, 0.0f, 0.0f);
        Vector3 eye = new(0.0f, 0.0f, 10.0f);
        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, at, new(0, 1, 0));
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(
            60f * (float)Math.PI / 180f,
            (float)ScreenWidth / ScreenHeight,
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

    public void OnPause()
    {
    }

    public void OnResume()
    {
    }

    public void OnDestroy()
    {
        if (vbh.Valid)
        {
            bgfx.destroy_vertex_buffer(vbh);
        }

        if (ibh.Valid)
        {
            bgfx.destroy_index_buffer(ibh);
        }

        if (program.Valid)
        {
            bgfx.destroy_program(program);
        }

        if (initialized)
        {
            bgfx.shutdown();
        }
    }

    private bgfx.ShaderHandle LoadShader(string name)
    {
        string path = string.Empty;
        string shaderPath = @$"shaders";
        name = $"{name}.bin";

        bgfx.ShaderHandle invalid = new bgfx.ShaderHandle { idx = ushort.MaxValue };

        path = Path.Combine(shaderPath, name);

        Stream stream = MainActivity.Instance!.GetAssetStream(path);
        using (BinaryReader reader = new BinaryReader(stream))
        {
            uint streamLength = (uint)stream.Length;
            byte[] shaderBytes = reader.ReadBytes((int)streamLength);
            unsafe
            {
                fixed (void* ptr = shaderBytes)
                {
                    bgfx.Memory* memory = bgfx.copy(ptr, streamLength);

                    bgfx.ShaderHandle handle = bgfx.create_shader(memory);

                    if (!handle.Valid)
                    {
                        Log.Error("SO", $"Shader model not supported for {name}");
                        return invalid;
                    }

                    return handle;
                }
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