$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    $iconWindowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $iconWorker = Start-Process -FilePath $iconWindowsPowerShell -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $PSCommandPath + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($iconWorker.ExitCode -ne 0) { throw '圖示產生失敗。' }
    return
}
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
public static class VictusFanArtwork {
    public static Bitmap Draw(int size) {
        Bitmap bitmap = new Bitmap(size,size,PixelFormat.Format32bppArgb);
        using(Graphics g=Graphics.FromImage(bitmap)) {
            g.Clear(Color.Transparent); g.SmoothingMode=SmoothingMode.AntiAlias;
            g.ScaleTransform(size/256f,size/256f);
            using(Brush background=new SolidBrush(Color.FromArgb(17,17,17)))g.FillEllipse(background,8,8,240,240);
            for(int i=0;i<3;i++) {
                GraphicsState state=g.Save();g.TranslateTransform(128,128);g.RotateTransform(i*120);g.TranslateTransform(-128,-128);
                using(GraphicsPath blade=new GraphicsPath()) {
                    blade.AddBezier(139,118,150,98,178,94,186,72);
                    blade.AddBezier(186,72,177,51,151,47,139,66);
                    blade.AddBezier(139,66,127,84,130,100,126,112);blade.CloseFigure();
                    using(Brush fill=new SolidBrush(Color.FromArgb(245,245,245)))g.FillPath(fill,blade);
                }
                g.Restore(state);
            }
            using(Brush hub=new SolidBrush(Color.FromArgb(245,245,245)))g.FillEllipse(hub,110,110,36,36);
            using(Brush center=new SolidBrush(Color.FromArgb(17,17,17)))g.FillEllipse(center,123,123,10,10);
        }
        return bitmap;
    }
    public static void Save(string iconPath,string previewPath) {
        int[] sizes={16,24,32,48,64,128,256};byte[][] frames=new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++)using(Bitmap bitmap=Draw(sizes[i]))using(MemoryStream stream=new MemoryStream()) {
            if(sizes[i]==256) { bitmap.Save(stream,ImageFormat.Png);frames[i]=stream.ToArray(); }
            else {
                // Preserve true-color alpha and a DWORD-padded transparency mask.
                // Handle-based Icon.Save converted these frames to indexed color and
                // rendered opaque corners at 16, 24 and 48 pixels on Windows.
                int size=sizes[i], maskStride=((size+31)/32)*4;
                using(BinaryWriter frame=new BinaryWriter(stream)) {
                    frame.Write((uint)40);frame.Write(size);frame.Write(size*2);
                    frame.Write((ushort)1);frame.Write((ushort)32);
                    frame.Write((uint)0);frame.Write((uint)(size*size*4));
                    frame.Write(0);frame.Write(0);frame.Write((uint)0);frame.Write((uint)0);
                    for(int y=size-1;y>=0;y--)for(int x=0;x<size;x++) {
                        Color pixel=bitmap.GetPixel(x,y);
                        frame.Write((byte)(pixel.B*pixel.A/255));
                        frame.Write((byte)(pixel.G*pixel.A/255));
                        frame.Write((byte)(pixel.R*pixel.A/255));
                        frame.Write(pixel.A);
                    }
                    for(int y=size-1;y>=0;y--) {
                        byte[] mask=new byte[maskStride];
                        for(int x=0;x<size;x++)if(bitmap.GetPixel(x,y).A==0)mask[x/8]|=(byte)(0x80>>(x%8));
                        frame.Write(mask);
                    }
                    frame.Flush();frames[i]=stream.ToArray();
                }
            }
        }
        using(BinaryWriter writer=new BinaryWriter(File.Create(iconPath))) {
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);
            uint offset=(uint)(6+16*sizes.Length);
            for(int i=0;i<sizes.Length;i++) {
                writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));
                writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);
                writer.Write((uint)frames[i].Length);writer.Write(offset);offset+=(uint)frames[i].Length;
            }
            foreach(byte[] frame in frames)writer.Write(frame);
        }
        using(Bitmap preview=Draw(256))preview.Save(previewPath,ImageFormat.Png);
    }
}
'@ -ReferencedAssemblies System.Drawing
$fanAssetDirectory = Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) 'assets'
[VictusFanArtwork]::Save((Join-Path $fanAssetDirectory 'app.ico'), (Join-Path $fanAssetDirectory 'icon.png'))
Write-Output '已建立多尺寸風扇圖示。'
