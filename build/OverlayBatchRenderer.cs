using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using UnityEngine;

namespace AbyssalCrab.Runtime
{
    public delegate int OverlayBuildDelegate(IntPtr input,int count,IntPtr output,int capacity);
    public delegate int OverlayAbiDelegate();
    public delegate int ProjectionDelegate(IntPtr camera, IntPtr input, int count, IntPtr output, int capacity);
    public delegate int SelectionDelegate(IntPtr input, int count, float maximumSquared, float minimumAlignment, IntPtr output, int capacity);
    public static class OverlayBatchRenderer
    {
        private static float[] _input=new float[13*512],_output=new float[12*512];
        private static int _count;
        private static Material _material;
        private static IntPtr _library;
        private static OverlayBuildDelegate _build;
        private static ProjectionDelegate _project;
        private static SelectionDelegate _select;
        public static bool RustProjection, RustSelection;
        public static int Backend;
        public static bool Collecting;
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        private static extern IntPtr LoadLibraryExW(string path,IntPtr file,uint flags);
        [DllImport("kernel32.dll",CharSet=CharSet.Ansi,ExactSpelling=true,SetLastError=true)]
        private static extern IntPtr GetProcAddress(IntPtr library,string name);
        [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr library);
        public static void Configure(int backend,string path)
            => ConfigureKernels(backend, path, false, false);

        public static void ConfigureKernels(int backend, string path, bool rustProjection, bool rustSelection)
        {
            if(backend<0||backend>2)throw new ArgumentOutOfRangeException("backend");
            if((backend == 2 || rustProjection || rustSelection) && _build == null)
            {
                if(string.IsNullOrEmpty(path)||!System.IO.Path.IsPathRooted(path))throw new ArgumentException("Rust 标注库路径无效");
                IntPtr library=LoadLibraryExW(path,IntPtr.Zero,0x100|0x1000);
                if(library==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    IntPtr abi=GetProcAddress(library,"overlay_abi"),build=GetProcAddress(library,"overlay_build");
                    if(abi==IntPtr.Zero||build==IntPtr.Zero||((OverlayAbiDelegate)Marshal.GetDelegateForFunctionPointer(abi,typeof(OverlayAbiDelegate)))()!=1)
                        throw new InvalidOperationException("Rust 标注库版本不匹配");
                    IntPtr project = GetProcAddress(library, "overlay_project");
                    IntPtr select = GetProcAddress(library, "overlay_select");
                    if (project == IntPtr.Zero || select == IntPtr.Zero)
                        throw new InvalidOperationException("Rust 库缺少投影或选敌接口，请核对版本");
                    var projectCall = (ProjectionDelegate)Marshal.GetDelegateForFunctionPointer(project, typeof(ProjectionDelegate));
                    var selectCall = (SelectionDelegate)Marshal.GetDelegateForFunctionPointer(select, typeof(SelectionDelegate));
                    _build = (OverlayBuildDelegate)Marshal.GetDelegateForFunctionPointer(build, typeof(OverlayBuildDelegate));
                    _project = projectCall;
                    _select = selectCall;
                    _library = library;
                }
                catch { FreeLibrary(library); throw; }
            }
            Backend = backend;
            RustProjection = rustProjection;
            RustSelection = rustSelection;
        }
        public static void NativeProject(float[] camera, float[] input, int count, float[] output)
        {
            if (camera.Length != 37 || count < 0 || count > input.Length / 7 || count > output.Length / 5)
                throw new ArgumentException("无效投影缓冲区");
            if (count == 0) return;
            GCHandle cameraPin = default(GCHandle), inputPin = default(GCHandle), outputPin = default(GCHandle);
            try
            {
                cameraPin = GCHandle.Alloc(camera, GCHandleType.Pinned);
                inputPin = GCHandle.Alloc(input, GCHandleType.Pinned);
                outputPin = GCHandle.Alloc(output, GCHandleType.Pinned);
                int written = _project(cameraPin.AddrOfPinnedObject(), inputPin.AddrOfPinnedObject(), count,
                    outputPin.AddrOfPinnedObject(), output.Length / 5);
                if (written != count) throw new InvalidOperationException("Rust 投影输出数量不符");
            }
            finally
            {
                if (outputPin.IsAllocated) outputPin.Free();
                if (inputPin.IsAllocated) inputPin.Free();
                if (cameraPin.IsAllocated) cameraPin.Free();
            }
        }

        public static bool TryNativeSelection(float[] input, int count, float maximumSquared,
            float minimumAlignment, int[] output, out int selectedCount)
        {
            selectedCount = 0;
            if (_select == null || count < 0 || count > input.Length / 3 || output.Length < count) return false;
            if (count == 0) return true;
            GCHandle inputPin = default(GCHandle), outputPin = default(GCHandle);
            try
            {
                inputPin = GCHandle.Alloc(input, GCHandleType.Pinned);
                outputPin = GCHandle.Alloc(output, GCHandleType.Pinned);
                int result = _select(inputPin.AddrOfPinnedObject(), count, maximumSquared, minimumAlignment,
                    outputPin.AddrOfPinnedObject(), output.Length);
                if (result < 0 || result > count) return false;
                selectedCount = result;
                return true;
            }
            finally
            {
                if (outputPin.IsAllocated) outputPin.Free();
                if (inputPin.IsAllocated) inputPin.Free();
            }
        }

        public static void Begin() { _count=0; Collecting=Backend!=0; }
        public static void Add(Vector2 from,Vector2 to,Rect clip,Color color,float width)
        {
            if(_count==_input.Length/13)Array.Resize(ref _input,_input.Length*2);
            int s=_count++*13;
            _input[s]=from.x;_input[s+1]=from.y;_input[s+2]=to.x;_input[s+3]=to.y;
            _input[s+4]=clip.xMin;_input[s+5]=clip.yMin;_input[s+6]=clip.xMax;_input[s+7]=clip.yMax;
            _input[s+8]=color.r;_input[s+9]=color.g;_input[s+10]=color.b;_input[s+11]=color.a;_input[s+12]=width;
        }
        public static int NativeBuild(float[] input,int count,float[] output)
        {
            if(_build==null)throw new InvalidOperationException("Rust 标注库未加载");
            if(count<0||count>input.Length/13||count>output.Length/12)throw new ArgumentOutOfRangeException("count");
            var a=GCHandle.Alloc(input,GCHandleType.Pinned);var b=GCHandle.Alloc(output,GCHandleType.Pinned);
            try { int result=_build(a.AddrOfPinnedObject(),count,b.AddrOfPinnedObject(),output.Length/12);
                if(result<0||result>count)throw new InvalidOperationException("Rust 标注输出无效");return result; }
            finally { b.Free();a.Free(); }
        }
        public static void Flush()
        {
            Collecting=false;if(Backend==0||_count==0)return;
            if(_output.Length/12<_count)Array.Resize(ref _output,(_input.Length/13)*12);
            int count=Backend==2?NativeBuild(_input,_count,_output):OverlayMeshKernel.Build(_input,_count,_output);
            if(!_material)
            {
                var shader=Shader.Find("Hidden/Internal-Colored");if(!shader)throw new NotSupportedException("当前游戏不提供线条绘制材质");
                _material=new Material(shader);_material.hideFlags=HideFlags.HideAndDontSave;
                _material.SetInt("_SrcBlend",5);_material.SetInt("_DstBlend",10);_material.SetInt("_Cull",0);_material.SetInt("_ZWrite",0);_material.SetInt("_ZTest",8);
            }
            if(!_material.SetPass(0))throw new InvalidOperationException("标注材质不可用");
            GL.PushMatrix();
            try
            {
                GL.LoadPixelMatrix(0,Screen.width,0,Screen.height);GL.Begin(GL.QUADS);
                try { for(int i=0;i<count;i++){int s=i*12;GL.Color(new Color(_output[s+8],_output[s+9],_output[s+10],_output[s+11]));
                    for(int v=0;v<8;v+=2)GL.Vertex3(_output[s+v],_output[s+v+1],0);} }
                finally { GL.End(); }
            }
            finally { GL.PopMatrix(); }
        }
        public static void ReleaseVisuals()
        { Collecting=false;_count=0;if(_material)UnityEngine.Object.Destroy(_material);_material=null; }
    }
}
