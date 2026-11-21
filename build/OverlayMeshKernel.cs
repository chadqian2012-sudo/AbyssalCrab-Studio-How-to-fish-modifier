using System;

namespace AbyssalCrab.Runtime
{
    // Shared flat ABI: 13 floats per segment; 12 per quad. No Unity objects cross the boundary.
    public static class OverlayMeshKernel
    {
        public static int Build(float[] input, int count, float[] output)
        {
            if(count<0 || count>input.Length/13 || count>output.Length/12)throw new ArgumentOutOfRangeException("count");
            int written=0;
            for(int i=0;i<count;i++)
            {
                int s=i*13; float x0=input[s],y0=input[s+1],x1=input[s+2],y1=input[s+3],width=input[s+12];
                if(!EntityOverlayGeometry.Finite(width)||width<=0||
                    !EntityOverlayGeometry.ClipLine(ref x0,ref y0,ref x1,ref y1,input[s+4],input[s+5],input[s+6],input[s+7]))continue;
                double dx=(double)x1-x0,dy=(double)y1-y0,len=Math.Sqrt(dx*dx+dy*dy);
                if(len<.1)continue;
                float nx=(float)(-dy/len*width*.5),ny=(float)(dx/len*width*.5);int d=written++*12;
                output[d]=x0+nx;output[d+1]=y0+ny;output[d+2]=x1+nx;output[d+3]=y1+ny;
                output[d+4]=x1-nx;output[d+5]=y1-ny;output[d+6]=x0-nx;output[d+7]=y0-ny;
                for(int c=0;c<4;c++)output[d+8+c]=input[s+8+c];
            }
            return written;
        }
    }
}
