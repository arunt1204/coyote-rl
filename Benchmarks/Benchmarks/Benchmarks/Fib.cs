using System;
using Microsoft.Coyote.Specifications;
using Microsoft.Coyote.SystematicTesting;
using Microsoft.Coyote.Tasks;

namespace Benchmarks
{
    public class Fib
    {
        private int i;
        private int j;
        private readonly int num;
        private AsyncLock mlock;

        public Fib(int i, int j, int num)
        {
            this.i = i;
            this.j = j;
            this.num = num;
        }

        public async Task TestRun()
        {
            this.mlock = AsyncLock.Create();
            Task[] ids = new Task[2];

            ids[0] = Task.Run(async () =>
            {
                for (int k = 0; k < this.num; k++)
                {
                    Task.ExploreContextSwitch();

                    using (await this.mlock.AcquireAsync())
                    {
                        this.i += this.j;
                    }
                }
            });

            ids[1] = Task.Run(async () =>
            {
                for (int k = 0; k < this.num; k++)
                {
                    Task.ExploreContextSwitch();

                    using (await this.mlock.AcquireAsync())
                    {
                        this.j += this.i;
                    }
                }
            });

            await Task.WhenAll(ids);

            if (this.num == 11 && (this.i >= 46368 || this.j >= 46368))
            {
                Specification.Assert(false, "<Fib_Bench_Larger> Bug found!");
            }

            if (this.num == 5 && (this.i >= 144 || this.j >= 144))
            {
                Specification.Assert(false, "<Fib_Bench> Bug found!");
            }
        }
    }
}
