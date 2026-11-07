using System;
namespace AbyssalCrab.Runtime
{
    // Match local hits to later host RPCs without assuming synchronous networking.
    public sealed class CombatHitContext
    {
        private readonly int[] _ids = new int[64];
        private readonly bool[] _head = new bool[64], _used = new bool[64];
        private readonly float[] _x = new float[64], _y = new float[64], _z = new float[64], _time = new float[64];
        private int _next;
        public void Track(int id,float x,float y,float z,float now,bool head)
        {
            int i=_next++%64;_ids[i]=id;_x[i]=x;_y[i]=y;_z[i]=z;_time[i]=now;_head[i]=head;_used[i]=true;
        }
        public bool Consume(int id,float x,float y,float z,float now)
        {
            int selected=-1;float earliest=float.MaxValue;
            for(int i=0;i<64;i++)
                if(_used[i]&&_ids[i]==id&&now>=_time[i]&&now-_time[i]<=2&&_time[i]<earliest)
                {float dx=x-_x[i],dy=y-_y[i],dz=z-_z[i];if(dx*dx+dy*dy+dz*dz<=.01f){selected=i;earliest=_time[i];}}
            if(selected<0)return false;_used[selected]=false;return _head[selected];
        }
    }
}
