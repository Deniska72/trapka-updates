using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
class PipelineTests {
    static int latestX, captured;
    static byte[] background,sprite;
    static void Capture(Rectangle area,Bitmap image){
        int index=Interlocked.Increment(ref captured),x=80+index*2;
        // A long run re-enters from the left; the tracker should safely reacquire.
        if(x>300){Interlocked.Exchange(ref captured,0);x=80;}
        byte[] rgb=(byte[])background.Clone();
        for(int y=0;y<90;y++)for(int xx=0;xx<48;xx++)for(int c=0;c<3;c++)rgb[((65+y)*416+x+xx)*3+c]=sprite[(y*48+xx)*3+c];
        var bits=image.LockBits(new Rectangle(0,0,416,234),ImageLockMode.WriteOnly,PixelFormat.Format24bppRgb);
        try{Marshal.Copy(rgb,0,bits.Scan0,rgb.Length);}finally{image.UnlockBits(bits);}
        image.Tag=new RectangleF(x/416f,65/234f,48/416f,90/234f);
        Volatile.Write(ref latestX,x);
    }
    static Detection[] Recognize(Bitmap image,float threshold){
        object tag=image.Tag;Thread.Sleep(90);
        if(!Object.ReferenceEquals(tag,image.Tag))throw new Exception("Inference frame overwritten before completion");
        return new[]{new Detection{Box=(RectangleF)tag,Score=.9f}};
    }
    static int Main(){try{
        var random=new Random(80);background=new byte[416*234*3];sprite=new byte[48*90*3];random.NextBytes(background);random.NextBytes(sprite);
        using(var worker=new VisionWorker(Capture,Recognize)){
            var area=new Rectangle(0,0,416,234);worker.SetTarget(area,true,.35f);
            int observed=0;double maxError=0;
            for(int i=0;i<90;i++){
                Thread.Sleep(16);var boxes=worker.Snapshot();
                if(boxes.Length>0){observed++;maxError=Math.Max(maxError,Math.Abs(boxes[0].Box.X*416-Volatile.Read(ref latestX)));}
            }
            if(observed<30)throw new Exception("Too few tracking results: "+observed+" "+worker.Diagnostics);
            if(maxError>6)throw new Exception("Slow recognition stalled motion tracking: "+maxError);
            if(worker.CapturedFrames<worker.RecognizedFrames*2)throw new Exception("Capture still follows inference rate");
            worker.SetTarget(area,false,.35f);if(worker.Snapshot().Length!=0)throw new Exception("Inactive results not cleared");
            Thread.Sleep(120);worker.SetTarget(area,true,.35f);
            for(int i=0;i<20;i++)Thread.Sleep(16);
            if(worker.Status.IndexOf("error",StringComparison.OrdinalIgnoreCase)>=0)throw new Exception(worker.Status);
            Console.WriteLine("PASS: 90 ms asynchronous recognizer; current-frame tracking max error="+maxError.ToString("F2")+" pixels; stable frame ownership; hide/reset/reacquire. "+worker.Diagnostics);
        }
        return 0;
    }catch(Exception ex){Console.WriteLine(ex);return 1;}}
}
