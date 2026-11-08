using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

static class CheckNativeIcon
{
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadImage(IntPtr instance,string name,uint type,int width,int height,uint flags);
    [DllImport("user32.dll", SetLastError=true)] static extern bool DrawIconEx(IntPtr dc,int x,int y,IntPtr icon,int width,int height,uint step,IntPtr brush,uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct ShellInfo {
        public IntPtr Icon;public int Index;public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Name;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)]public string Type;
    }
    [ComImport,Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ImageList {
        [PreserveSig]int Add(IntPtr image,IntPtr mask,out int index);
        [PreserveSig]int ReplaceIcon(int index,IntPtr icon,out int result);
        [PreserveSig]int SetOverlayImage(int image,int overlay);
        [PreserveSig]int Replace(int index,IntPtr image,IntPtr mask);
        [PreserveSig]int AddMasked(IntPtr image,uint color,out int result);
        [PreserveSig]int Draw(IntPtr parameters);
        [PreserveSig]int Remove(int index);
        [PreserveSig]int GetIcon(int index,uint flags,out IntPtr icon);
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern IntPtr SHGetFileInfo(string path,uint attributes,out ShellInfo info,uint size,uint flags);
    [DllImport("shell32.dll")]static extern int SHGetImageList(int list,ref Guid id,out ImageList images);
    static IntPtr ShellIcon(string path,int size) {
        ShellInfo info;
        if(SHGetFileInfo(path,0,out info,(uint)Marshal.SizeOf(typeof(ShellInfo)),0x4000)==IntPtr.Zero)throw new Exception("SHGetFileInfo failed");
        Guid id=new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");ImageList images;
        int list=size==16?1:size==32?0:size==48?2:4;
        Marshal.ThrowExceptionForHR(SHGetImageList(list,ref id,out images));
        try{IntPtr icon;Marshal.ThrowExceptionForHR(images.GetIcon(info.Index,1,out icon));return icon;}
        finally{Marshal.FinalReleaseComObject(images);}
    }
    static int Main(string[] args)
    {
        bool shell=args.Length==3 && args[0]=="--shell";
        if(args.Length!=2 && !shell)return 2;
        string path=args[shell?1:0],output=args[shell?2:1];
        bool failed=false;
        Color background=Color.FromArgb(128,235,235);
        foreach(int size in new int[]{16,24,32,48,64,128,256})
        {
            if(shell && size!=16 && size!=32 && size!=48 && size!=256)continue;
            IntPtr icon=shell?ShellIcon(path,size):LoadImage(IntPtr.Zero,path,1,size,size,0x10);
            if(icon==IntPtr.Zero){Console.WriteLine("FAIL load "+size+" error="+Marshal.GetLastWin32Error());failed=true;continue;}
            try
            {
                using(Bitmap canvas=new Bitmap(size,size,PixelFormat.Format24bppRgb))
                {
                    using(Graphics graphics=Graphics.FromImage(canvas))
                    {
                        graphics.Clear(background);IntPtr dc=graphics.GetHdc();
                        try{if(!DrawIconEx(dc,0,0,icon,size,size,0,IntPtr.Zero,3))throw new Exception("DrawIconEx failed");}
                        finally{graphics.ReleaseHdc(dc);}
                    }
                    bool corners=true;
                    foreach(Point point in new Point[]{new Point(0,0),new Point(size-1,0),new Point(0,size-1),new Point(size-1,size-1)})
                        if(canvas.GetPixel(point.X,point.Y).ToArgb()!=background.ToArgb())corners=false;
                    canvas.Save(Path.Combine(output,(shell?"shell-":"native-")+size+".png"),ImageFormat.Png);
                    Console.WriteLine((corners?"PASS":"FAIL")+" "+size+"px native corner="+canvas.GetPixel(0,0).ToArgb().ToString("X8")+" expected="+background.ToArgb().ToString("X8"));
                    if(!corners)failed=true;
                }
            }
            finally{DestroyIcon(icon);}
        }
        return failed?1:0;
    }
}
