using Microsoft.Coyote.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Benchmarks
{
    public class BoundedBuffer
    {
        private readonly bool pulseAll;

        public BoundedBuffer(bool pulseAll)
        {
            this.pulseAll = pulseAll;
        }

        public void Put(object x)
        {
            using (var monitor = SynchronizedBlock.Lock(this.syncObject))
            {
                while (this.occupied == this.buffer.Length)
                {
                    monitor.Wait();
                }

                ++this.occupied;
                this.putAt %= this.buffer.Length;
                this.buffer[this.putAt++] = x;

                if (this.pulseAll)
                {
                    monitor.PulseAll();
                }
                else
                {
                    monitor.Pulse();
                }
            }
        }

        public object Take()
        {
            object result = null;

            using (var monitor = SynchronizedBlock.Lock(this.syncObject))
            {
                while (this.occupied is 0)
                {
                    monitor.Wait();
                }

                --this.occupied;
                this.takeAt %= this.buffer.Length;
                result = this.buffer[this.takeAt++];

                if (this.pulseAll)
                {
                    monitor.PulseAll();
                }
                else
                {
                    monitor.Pulse();
                }
            }

            return result;
        }

        private readonly object syncObject = new object();
        private readonly object[] buffer = new object[1];
        private int putAt;
        private int takeAt;
        private int occupied;

        public static void Reader(BoundedBuffer buffer)
        {
            for (int i = 0; i < 10; i++)
            {
                buffer.Take();
            }
        }

        public static void Writer(BoundedBuffer buffer)
        {
            for (int i = 0; i < 20; i++)
            {
                buffer.Put("hello " + i);
            }
        }
    }
}
