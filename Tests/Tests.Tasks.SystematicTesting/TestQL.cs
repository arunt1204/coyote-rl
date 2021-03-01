// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.Coyote.Specifications;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Coyote.Tasks.SystematicTesting.Tests
{
    public class TestQL : BaseTaskTest
    {
        public TestQL(ITestOutputHelper output)
            : base(output)
        {
        }

        public class TestFib
        {
            private int i;
            private int j;
            private readonly int num;
            private AsyncLock mlock;

            public TestFib(int i, int j, int num)
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

        [Fact(Timeout = 5000)]
        public void TestFibBenchQLStrategy()
        {
            this.Test(async () =>
            {
                TestFib tf = new TestFib(1, 1, 5);
                await tf.TestRun();
            },
            configuration: GetConfiguration().WithTestingIterations(1000).WithQLearningStrategy());
        }

        [Fact(Timeout = 5000)]
        public void TestFibBenchLargestQLStrategy()
        {
            this.Test(async () =>
            {
                TestFib tf = new TestFib(1, 1, 11);
                await tf.TestRun();
            },
            configuration: GetConfiguration().WithTestingIterations(1000).WithQLearningStrategy());
        }

        public class TestTraingular
        {
            private int i;
            private int j;
            private readonly int num;
            private readonly int limit;
            private AsyncLock mLock;

            public TestTraingular(int i, int j, int num)
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
                        // Task.ExploreContextSwitch();

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
                        // Task.ExploreContextSwitch();

                        using (await this.mLock.AcquireAsync())
                        {
                            this.j = this.i + 1;
                        }
                    }
                });

                int temp_i, temp_j;

                // Task.ExploreContextSwitch();

                using (await this.mLock.AcquireAsync())
                {
                    temp_i = this.i;
                }

                bool condI = temp_i >= this.limit;

                // Task.ExploreContextSwitch();

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

        [Fact(Timeout = 5000)]
        public void TestTriBenchQLStrategy()
        {
            this.Test(async () =>
            {
                TestTraingular tt = new TestTraingular(3, 6, 5);
                await tt.TestRun();
            },
            configuration: GetConfiguration().WithTestingIterations(2).WithQLearningStrategy());
        }

        [Fact(Timeout = 5000)]
        public void TestTriBenchLargestQLStrategy()
        {
            this.Test(async () =>
            {
                TestTraingular tt = new TestTraingular(3, 6, 20);
                await tt.TestRun();
            },
            configuration: GetConfiguration().WithTestingIterations(1000).WithQLearningStrategy());
        }
    }
}
