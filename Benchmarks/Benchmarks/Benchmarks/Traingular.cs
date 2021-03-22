using System;
using Microsoft.Coyote.Specifications;
using Microsoft.Coyote.SystematicTesting;
using Microsoft.Coyote.Tasks;

namespace Benchmarks
{
    public class Traingular
    {
        private int i;
        private int j;
        private readonly int num;
        private readonly int limit;
        private AsyncLock mLock;

        public Traingular(int i, int j, int num)
        {
            this.i = i;
            this.j = j;
            this.num = num;
            this.limit = (2 * this.num) + 6;
        }

        public async Task TestRun()
        {
            Task[] ids = new Task[2];

            this.mLock = AsyncLock.Create();

            ids[0] = Task.Run(async () =>
            {
                for (int k = 0; k < this.num; k++)
                {
                    Task.ExploreContextSwitch();

                    using (await this.mLock.AcquireAsync())
                    {
                        this.i = this.j + 1;
                    }
                }
            });

            ids[1] = Task.Run(async () =>
            {
                for (int k = 0; k < this.num; k++)
                {
                    Task.ExploreContextSwitch();

                    using (await this.mLock.AcquireAsync())
                    {
                        this.j = this.i + 1;
                    }
                }
            });

            int temp_i, temp_j;

            Task.ExploreContextSwitch();

            using (await this.mLock.AcquireAsync())
            {
                temp_i = this.i;
            }

            bool condI = temp_i >= this.limit;

            Task.ExploreContextSwitch();

            using (await this.mLock.AcquireAsync())
            {
                temp_j = this.j;
            }

            bool condJ = temp_j >= this.limit;

            if (condI || condJ)
            {
                Specification.Assert(false, "<Triangular-2> Bug found!");
            }

            await Task.WhenAll(ids);
        }
    }
}
