using System;
using System.Drawing;
using System.Diagnostics;
class TrackerTests {
    static void Assert(bool ok,string error){if(!ok)throw new Exception(error);}
    static byte[] Scene(int w,int h,byte[] background,byte[] sprite,int sx,int sy,int sw,int sh){
        var result=(byte[])background.Clone();for(int y=0;y<sh;y++)for(int x=0;x<sw;x++)if(sx+x>=0&&sx+x<w&&sy+y>=0&&sy+y<h)result[(sy+y)*w+sx+x]=sprite[y*sw+x];return result;
    }
    static int Main(){
        try{
            const int w=416,h=234,sw=48,sh=100;var random=new Random(72);var background=new byte[w*h];var sprite=new byte[sw*sh];random.NextBytes(background);random.NextBytes(sprite);
            var initial=Scene(w,h,background,sprite,90,70,sw,sh);var frame=initial;
            var tracked=new[]{new Detection{Box=new RectangleF(90f/w,70f/h,(float)sw/w,(float)sh/h),Score=.8f,ConfirmedAt=1000}};
            var stationary=PixelTracker.Advance(frame,frame,w,h,tracked,16);
            Assert(stationary.Length==1&&Math.Abs(stationary[0].Box.X*w-90)<.5,"Stationary tracking jitter");
            var watch=Stopwatch.StartNew();
            for(int i=1;i<=10;i++){
                var next=Scene(w,h,background,sprite,90+2*i,70+i,sw,sh);
                tracked=PixelTracker.Advance(frame,next,w,h,tracked,16);
                Assert(tracked.Length==1,"Moving character lost at frame "+i);
                Assert(Math.Abs(tracked[0].Box.X*w-(90+2*i))<1.5&&Math.Abs(tracked[0].Box.Y*h-(70+i))<1.5,"Motion lag at frame "+i);
                frame=next;
            }
            watch.Stop();
            var raw=new[]{new Detection{Box=new RectangleF(90f/w,70f/h,(float)sw/w,(float)sh/h),Score=.8f}};
            var caughtUp=PixelTracker.Advance(initial,frame,w,h,raw,100);
            Assert(caughtUp.Length==1&&Math.Abs(caughtUp[0].Box.X*w-110)<1.5,"Delayed recognition did not catch up to current frame");
            var merged=PixelTracker.Reconcile(tracked,caughtUp,1000,1160);
            Assert(merged.Length==1&&Math.Abs(merged[0].Box.X*w-110)<1.5,"Late CNN result pulled box backwards");
            var unrelated=new byte[w*h];random.NextBytes(unrelated);
            Assert(PixelTracker.Advance(frame,unrelated,w,h,tracked,16).Length==0,"Unrelated frame retained a false box");
            Assert(PixelTracker.Advance(initial,frame,w,h,raw,151).Length==0,"Stale recognition accepted");
            Assert(PixelTracker.Expire(merged,1201).Length==0,"Unconfirmed track did not expire");
            Console.WriteLine("PASS: stationary stability; 10 frames of independently moving character without CNN refresh (<1.5 px error); delayed CNN catch-up; no backwards reset; scene-cut rejection; stale-result expiry. Tracking ms/frame="+(watch.Elapsed.TotalMilliseconds/10).ToString("F2"));return 0;
        }catch(Exception ex){Console.WriteLine(ex);return 1;}
    }
}
